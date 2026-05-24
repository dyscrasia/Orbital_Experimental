using System.Collections.Generic;
using UnityEngine;

namespace Orbital.Physics
{
    public enum Outcome { None, Crashed, Orbited, Escaped }

    /// <summary>
    /// Pure static class for multi-body gravity simulation.
    /// NO MonoBehaviour, NO Time, NO UnityEngine.Random.
    /// The only Unity types used are Vector2 and Rect, both value types.
    /// </summary>
    public static class PatchedConicsSolver
    {
        /// <summary>
        /// Advance one fixed timestep using semi-implicit Euler integration.
        /// Every body contributes gravitational force F = G * M_body * m_rocket / r²;
        /// forces are summed then divided by rocket mass to get acceleration.
        /// No SOI filtering — all bodies are treated identically as gravity sources.
        /// Returns a new RocketState; the input is not mutated.
        /// </summary>
        public static RocketState Step(RocketState rocket, IReadOnlyList<CelestialBody> bodies, float dt, float G)
        {
            // --- Kinematic orbit: bypass gravity entirely ---
            if (rocket.Status == RocketStatus.Orbited)
            {
                RocketState orbitNext = rocket.Clone();
                orbitNext.TimeInFlight = rocket.TimeInFlight + dt;

                CelestialBody capturedBody = FindBody(rocket.CapturedBodyId, bodies);
                if (capturedBody != null)
                {
                    float newAngle = rocket.OrbitAngle + rocket.OrbitAngularSpeed * rocket.OrbitDirection * dt;
                    orbitNext.OrbitAngle = newAngle;
                    Vector2 radial = new Vector2(Mathf.Cos(newAngle), Mathf.Sin(newAngle));
                    orbitNext.Position = capturedBody.Position + radial * rocket.OrbitRadius;
                    Vector2 tangent = new Vector2(-radial.y, radial.x) * rocket.OrbitDirection;
                    orbitNext.Velocity = tangent * (rocket.OrbitAngularSpeed * rocket.OrbitRadius);
                }

                return orbitNext;
            }

            RocketState next = rocket.Clone();
            next.TimeInFlight = rocket.TimeInFlight + dt;

            Vector2 totalForce = Vector2.zero;
            foreach (CelestialBody body in bodies)
            {
                Vector2 offset = body.Position - rocket.Position;
                float distSq = offset.sqrMagnitude;
                if (distSq < 0.01f)
                    continue;
                float dist = Mathf.Sqrt(distSq);
                Vector2 direction = offset / dist;
                float forceMagnitude = G * body.Mass * rocket.Mass / distSq;
                totalForce += direction * forceMagnitude;
            }

            Vector2 acceleration = totalForce / rocket.Mass;
            next.Velocity = rocket.Velocity + acceleration * dt;
            next.Position = rocket.Position + next.Velocity * dt;

            // CurrentBodyId: dominant SOI body, used only for outcome detection
            next.CurrentBodyId = FindDominantBody(rocket.Position, bodies);

            return next;
        }

        /// <summary>
        /// Return the ID of the smallest SOI that contains the position.
        /// If the position is inside no SOI, returns the ID of the nearest body.
        /// </summary>
        public static int FindDominantBody(Vector2 position, IReadOnlyList<CelestialBody> bodies)
        {
            float smallestSoi = float.MaxValue;
            int dominantId = -1;

            foreach (CelestialBody body in bodies)
            {
                float dist = (position - body.Position).magnitude;
                if (dist <= body.SoiRadius && body.SoiRadius < smallestSoi)
                {
                    smallestSoi = body.SoiRadius;
                    dominantId = body.Id;
                }
            }

            if (dominantId != -1)
                return dominantId;

            // Outside all SOIs — return nearest body
            float nearestDist = float.MaxValue;
            int nearestId = -1;
            foreach (CelestialBody body in bodies)
            {
                float dist = (position - body.Position).magnitude;
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearestId = body.Id;
                }
            }
            return nearestId;
        }

        /// <summary>
        /// Check for Crashed and Escaped outcomes only.
        /// Orbited is now handled by the capture-window mechanic in the scenario controller.
        /// </summary>
        /// <param name="outcomeBodyId">Which body triggered the outcome (-1 if none).</param>
        public static Outcome CheckOutcome(
            RocketState rocket,
            IReadOnlyList<CelestialBody> bodies,
            Rect playArea,
            float G,
            float maxSimTime,
            out int outcomeBodyId)
        {
            outcomeBodyId = -1;

            // --- Crash: inside any body's physical radius ---
            foreach (CelestialBody body in bodies)
            {
                if ((rocket.Position - body.Position).magnitude <= body.Radius)
                {
                    outcomeBodyId = body.Id;
                    return Outcome.Crashed;
                }
            }

            // --- Escape: exceeded max sim time ---
            if (rocket.TimeInFlight >= maxSimTime)
                return Outcome.Escaped;

            // --- Escape: outside play area with non-negative energy relative to dominant body ---
            if (!playArea.Contains(rocket.Position))
            {
                CelestialBody dom = FindBody(FindDominantBody(rocket.Position, bodies), bodies);
                if (dom != null)
                {
                    float energy = SpecificOrbitalEnergy(rocket.Position, rocket.Velocity, dom, G);
                    if (energy >= 0f)
                        return Outcome.Escaped;
                }
                else
                {
                    return Outcome.Escaped;
                }
            }

            return Outcome.None;
        }

        /// <summary>
        /// Specific orbital energy: v²/2 − GM/r.
        /// Negative means a closed (bound) orbit; non-negative means escape trajectory.
        /// </summary>
        public static float SpecificOrbitalEnergy(Vector2 position, Vector2 velocity, CelestialBody body, float G)
        {
            float r = (position - body.Position).magnitude;
            float vSq = velocity.sqrMagnitude;
            if (r < 1e-6f)
                return float.MaxValue;
            return vSq * 0.5f - G * body.Mass / r;
        }

        private static CelestialBody FindBody(int id, IReadOnlyList<CelestialBody> bodies)
        {
            foreach (CelestialBody body in bodies)
                if (body.Id == id) return body;
            return null;
        }
    }
}
