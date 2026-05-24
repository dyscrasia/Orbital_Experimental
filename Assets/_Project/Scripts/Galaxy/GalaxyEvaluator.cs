using System.Collections.Generic;
using Orbital.Physics;
using UnityEngine;

namespace Orbital.Galaxy
{
    /// <summary>
    /// Pure static class — no MonoBehaviour, no Unity lifecycle.
    /// Scores a generated body layout on four criteria and returns an overall pass/fail.
    /// </summary>
    public static class GalaxyEvaluator
    {
        private const int ShotAngles = 8;
        private const int MaxEvalSimSteps = 1500; // 30 s at 50 Hz

        /// <summary>
        /// Evaluate a body layout and return per-criterion scores.
        /// </summary>
        /// <param name="bodies">All bodies in the galaxy.</param>
        /// <param name="p1HomeId">ID of Player 1's home body.</param>
        /// <param name="p2HomeId">ID of Player 2's home body.</param>
        /// <param name="playArea">Simulation boundary rectangle.</param>
        /// <param name="G">Gravitational constant (should match runtime value).</param>
        /// <param name="dt">Simulation timestep in seconds.</param>
        /// <param name="maxLaunchSpeed">Maximum rocket launch speed for PathViability shots.</param>
        /// <param name="maxSimTime">Maximum seconds to simulate each PathViability shot.</param>
        /// <param name="parameters">Optional — provides per-criterion thresholds.</param>
        public static GalaxyEvaluation Evaluate(
            IReadOnlyList<CelestialBody> bodies,
            int p1HomeId, int p2HomeId,
            Rect playArea,
            float G = 1f,
            float dt = 0.02f,
            float maxLaunchSpeed = 16f,
            float maxSimTime = 30f,
            GalaxyParameters parameters = null)
        {
            List<CelestialBody> nonHome = CollectNonHome(bodies, p1HomeId, p2HomeId);

            float pvLaunchMult  = parameters?.PathViabilityLaunchMultiplier ?? 1.5f;
            float pathViability = ScorePathViability(
                bodies, nonHome, p1HomeId, p2HomeId, playArea, G, dt, maxLaunchSpeed, maxSimTime, pvLaunchMult);
            float regionBalance = ScoreRegionBalance(bodies, nonHome, p1HomeId, p2HomeId);
            float spread        = ScoreSpread(bodies, playArea, parameters?.MinBodySeparation ?? 2.5f);
            float symmetry      = ScoreSymmetry(nonHome, playArea);

            float pvThresh  = parameters?.PathViabilityThreshold ?? 0.4f;
            float rbThresh  = parameters?.RegionBalanceThreshold  ?? 0.6f;
            float spThresh  = parameters?.SpreadThreshold         ?? 0.3f;
            float symThresh = parameters?.SymmetryThreshold       ?? 0.5f;

            bool isAcceptable = pathViability >= pvThresh
                             && regionBalance  >= rbThresh
                             && spread         >= spThresh
                             && symmetry       >= symThresh;

            string status = isAcceptable ? "ACCEPTABLE" : "REJECTED";
            string summary =
                $"PathViability: {pathViability:F2} (>={pvThresh:F2}), " +
                $"Balance: {regionBalance:F2} (>={rbThresh:F2}), " +
                $"Spread: {spread:F2} (>={spThresh:F2}), " +
                $"Symmetry: {symmetry:F2} (>={symThresh:F2})  —  {status}";

            return new GalaxyEvaluation
            {
                PathViability = pathViability,
                RegionBalance = regionBalance,
                Spread        = spread,
                Symmetry      = symmetry,
                IsAcceptable  = isAcceptable,
                Summary       = summary
            };
        }

        // -------------------------------------------------------------------------
        //  Criterion 1 — PathViability
        //  Simulate 8 shots from each home; score = fraction of non-home bodies
        //  reachable (rocket passes within CaptureRingRadius) averaged over both homes.
        // -------------------------------------------------------------------------

        private static float ScorePathViability(
            IReadOnlyList<CelestialBody> bodies,
            List<CelestialBody> nonHome,
            int p1HomeId, int p2HomeId,
            Rect playArea,
            float G, float dt, float maxLaunchSpeed, float maxSimTime,
            float launchMultiplier)
        {
            if (nonHome.Count == 0) return 1f;

            int simSteps = Mathf.Min(
                MaxEvalSimSteps,
                Mathf.RoundToInt(maxSimTime / dt));

            CelestialBody p1 = FindBody(p1HomeId, bodies);
            CelestialBody p2 = FindBody(p2HomeId, bodies);

            float evalLaunchSpeed = maxLaunchSpeed * launchMultiplier;

            float score1 = SimulatePathViabilityFromHome(p1, nonHome, bodies, playArea, G, dt, evalLaunchSpeed, simSteps);
            float score2 = SimulatePathViabilityFromHome(p2, nonHome, bodies, playArea, G, dt, evalLaunchSpeed, simSteps);

            return (score1 + score2) * 0.5f;
        }

        private static float SimulatePathViabilityFromHome(
            CelestialBody home,
            List<CelestialBody> nonHome,
            IReadOnlyList<CelestialBody> allBodies,
            Rect playArea,
            float G, float dt, float maxLaunchSpeed, int simSteps)
        {
            if (home == null) return 0f;

            HashSet<int> reachable = new HashSet<int>();

            for (int s = 0; s < ShotAngles; s++)
            {
                float angle = s / (float)ShotAngles * Mathf.PI * 2f;
                Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

                // Start at the same offset the game uses (PSC.BuildRocket: radius + 0.7)
                Vector2 startPos = home.Position + dir * (home.Radius + 0.7f);
                Vector2 startVel = dir * maxLaunchSpeed;

                RocketState state = new RocketState
                {
                    Position     = startPos,
                    Velocity     = startVel,
                    Mass         = 0.1f,
                    Fuel         = 50f,
                    CurrentBodyId = home.Id,
                    Status       = RocketStatus.InFlight,
                    TimeInFlight = 0f
                };

                for (int step = 0; step < simSteps; step++)
                {
                    state = PatchedConicsSolver.Step(state, allBodies, dt, G);

                    // Check proximity to each non-home body
                    foreach (CelestialBody body in nonHome)
                    {
                        float detectionRadius = body.CaptureRingRadius > 0f
                            ? body.CaptureRingRadius
                            : body.SoiRadius * 0.3f;

                        if ((state.Position - body.Position).magnitude <= detectionRadius)
                            reachable.Add(body.Id);
                    }

                    // Stop if rocket crashes into any body
                    bool crashed = false;
                    foreach (CelestialBody body in allBodies)
                    {
                        if ((state.Position - body.Position).magnitude <= body.Radius)
                        {
                            crashed = true;
                            break;
                        }
                    }
                    if (crashed) break;

                    // Stop if rocket leaves the play area
                    if (!playArea.Contains(state.Position)) break;
                }
            }

            return (float)reachable.Count / nonHome.Count;
        }

        // -------------------------------------------------------------------------
        //  Criterion 2 — RegionBalance
        //  Partition non-home bodies by which home is nearer; score imbalance.
        // -------------------------------------------------------------------------

        private static float ScoreRegionBalance(
            IReadOnlyList<CelestialBody> bodies,
            List<CelestialBody> nonHome,
            int p1HomeId, int p2HomeId)
        {
            if (nonHome.Count == 0) return 1f;

            CelestialBody p1 = FindBody(p1HomeId, bodies);
            CelestialBody p2 = FindBody(p2HomeId, bodies);
            if (p1 == null || p2 == null) return 0f;

            int p1Count = 0;
            int p2Count = 0;
            foreach (CelestialBody body in nonHome)
            {
                float d1 = (body.Position - p1.Position).magnitude;
                float d2 = (body.Position - p2.Position).magnitude;
                if (d1 <= d2) p1Count++;
                else          p2Count++;
            }

            return 1f - Mathf.Abs(p1Count - p2Count) / (float)nonHome.Count;
        }

        // -------------------------------------------------------------------------
        //  Criterion 3 — Spread
        //  Average pairwise distance between all bodies, normalised by play-area
        //  diagonal. Also a safety check: any pair violating MinBodySeparation → 0.
        // -------------------------------------------------------------------------

        private static float ScoreSpread(
            IReadOnlyList<CelestialBody> bodies, Rect playArea, float minBodySep)
        {
            if (bodies.Count < 2) return 0f;

            float diag = Mathf.Sqrt(
                playArea.width  * playArea.width +
                playArea.height * playArea.height);

            float totalDist = 0f;
            int pairs = 0;

            for (int i = 0; i < bodies.Count; i++)
            {
                for (int j = i + 1; j < bodies.Count; j++)
                {
                    float d = (bodies[i].Position - bodies[j].Position).magnitude;

                    // Safety check — generator enforces this; fail loudly if violated
                    if (d < minBodySep)
                        return 0f;

                    totalDist += d;
                    pairs++;
                }
            }

            if (pairs == 0) return 0f;
            return Mathf.Clamp01((totalDist / pairs) / diag);
        }

        // -------------------------------------------------------------------------
        //  Criterion 4 — Symmetry
        //  For each non-home body, reflect it across x = 0 and find the nearest
        //  other non-home body. Score = closeness of that match, averaged.
        // -------------------------------------------------------------------------

        private static float ScoreSymmetry(List<CelestialBody> nonHome, Rect playArea)
        {
            if (nonHome.Count < 2) return 0f;

            float matchTolerance = playArea.width * 0.15f;
            float totalScore = 0f;

            foreach (CelestialBody body in nonHome)
            {
                Vector2 reflected = new Vector2(-body.Position.x, body.Position.y);
                float nearestDist = float.MaxValue;

                foreach (CelestialBody other in nonHome)
                {
                    if (other == body) continue;
                    float d = (other.Position - reflected).magnitude;
                    if (d < nearestDist) nearestDist = d;
                }

                float matchScore = Mathf.Max(0f, 1f - nearestDist / matchTolerance);
                totalScore += matchScore;
            }

            return totalScore / nonHome.Count;
        }

        // -------------------------------------------------------------------------
        //  Helpers
        // -------------------------------------------------------------------------

        private static List<CelestialBody> CollectNonHome(
            IReadOnlyList<CelestialBody> bodies, int p1HomeId, int p2HomeId)
        {
            List<CelestialBody> result = new List<CelestialBody>();
            foreach (CelestialBody body in bodies)
                if (body.Id != p1HomeId && body.Id != p2HomeId)
                    result.Add(body);
            return result;
        }

        private static CelestialBody FindBody(int id, IReadOnlyList<CelestialBody> bodies)
        {
            foreach (CelestialBody body in bodies)
                if (body.Id == id) return body;
            return null;
        }
    }
}
