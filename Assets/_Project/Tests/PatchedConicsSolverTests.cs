using System.Collections.Generic;
using NUnit.Framework;
using Orbital.Physics;
using UnityEngine;

namespace Orbital.Tests
{
    /// <summary>
    /// Unit tests for PatchedConicsSolver.
    /// Pure-data tests: no MonoBehaviour, no scene, no Unity lifecycle needed.
    /// Run via Window > General > Test Runner > Edit Mode.
    /// </summary>
    public class PatchedConicsSolverTests
    {
        private const float G = 1.0f;
        private const float Dt = 0.02f;

        // -------------------------------------------------------------------------
        //  Helpers
        // -------------------------------------------------------------------------

        private static CelestialBody MakeBody(int id, string name, Vector2 pos, float mass, float radius, float soi)
            => new CelestialBody { Id = id, Name = name, Position = pos, Mass = mass, Radius = radius, SoiRadius = soi };

        private static RocketState MakeRocket(Vector2 pos, Vector2 vel)
            => new RocketState
            {
                Position = pos,
                Velocity = vel,
                Mass = 0.1f,
                Fuel = 50f,
                CurrentBodyId = 0,
                Status = RocketStatus.InFlight,
                TimeInFlight = 0f
            };

        // -------------------------------------------------------------------------
        //  1. Single-body circular orbit roughly conserves specific orbital energy
        // -------------------------------------------------------------------------

        [Test]
        public void CircularOrbit_SpecificOrbitalEnergy_RemainsNearConstant()
        {
            // Circular orbit around a body at the origin:
            // v_circular = sqrt(G * M / r)
            float mass = 100f;
            float r = 10f;
            float vCircular = Mathf.Sqrt(G * mass / r);

            CelestialBody sun = MakeBody(0, "Sun", Vector2.zero, mass, 1.5f, 50f);
            List<CelestialBody> bodies = new List<CelestialBody> { sun };

            RocketState rocket = MakeRocket(new Vector2(r, 0f), new Vector2(0f, vCircular));

            float initialEnergy = PatchedConicsSolver.SpecificOrbitalEnergy(
                rocket.Position, rocket.Velocity, sun, G);

            // Simulate one full orbit (~2π r / v seconds)
            float period = 2f * Mathf.PI * r / vCircular;
            int steps = Mathf.RoundToInt(period / Dt);

            for (int i = 0; i < steps; i++)
                rocket = PatchedConicsSolver.Step(rocket, bodies, Dt, G);

            float finalEnergy = PatchedConicsSolver.SpecificOrbitalEnergy(
                rocket.Position, rocket.Velocity, sun, G);

            // Semi-implicit Euler has small energy drift; allow 5% over one orbit
            Assert.AreEqual(initialEnergy, finalEnergy,
                Mathf.Abs(initialEnergy) * 0.05f,
                "Specific orbital energy should be roughly conserved over one circular orbit.");

            // Also confirm it's a bound orbit
            Assert.Less(finalEnergy, 0f, "Circular orbit must have negative (bound) orbital energy.");
        }

        // -------------------------------------------------------------------------
        //  2. FindDominantBody returns the planet when inside its SOI
        // -------------------------------------------------------------------------

        [Test]
        public void FindDominantBody_ReturnsSmallestContainingSOI()
        {
            CelestialBody sun    = MakeBody(0, "Sun",    Vector2.zero,        100f, 1.5f, 30f);
            CelestialBody planet = MakeBody(1, "Planet", new Vector2(10f, 0f), 5f,  0.8f, 4f);
            List<CelestialBody> bodies = new List<CelestialBody> { sun, planet };

            // A point 2 units from the planet — inside planet SOI (4) AND sun SOI (30)
            Vector2 insidePlanetSoi = new Vector2(12f, 0f);
            int domId = PatchedConicsSolver.FindDominantBody(insidePlanetSoi, bodies);
            Assert.AreEqual(1, domId, "Planet should be dominant when inside its smaller SOI.");

            // A point 6 units from the planet — outside planet SOI, inside sun SOI
            Vector2 outsidePlanetSoi = new Vector2(16f, 0f);
            domId = PatchedConicsSolver.FindDominantBody(outsidePlanetSoi, bodies);
            Assert.AreEqual(0, domId, "Sun should be dominant when outside all planet SOIs.");
        }

        [Test]
        public void FindDominantBody_FallsBackToNearestWhenOutsideAllSOIs()
        {
            // Sun with small SOI that doesn't cover the test point
            CelestialBody sun = MakeBody(0, "Sun", Vector2.zero, 100f, 1.5f, 5f);
            List<CelestialBody> bodies = new List<CelestialBody> { sun };

            // Point far outside the sun's SOI
            Vector2 farAway = new Vector2(100f, 0f);
            int domId = PatchedConicsSolver.FindDominantBody(farAway, bodies);
            Assert.AreEqual(0, domId, "Should fall back to the nearest body (sun) when outside all SOIs.");
        }

        // -------------------------------------------------------------------------
        //  3. Rocket crashes when it intersects a body
        // -------------------------------------------------------------------------

        [Test]
        public void CheckOutcome_Crashed_WhenRocketInsideBodyRadius()
        {
            CelestialBody sun = MakeBody(0, "Sun", Vector2.zero, 100f, 1.5f, 30f);
            List<CelestialBody> bodies = new List<CelestialBody> { sun };
            Rect playArea = new Rect(-25f, -15f, 50f, 30f);

            // Place rocket inside the sun's radius
            RocketState rocket = MakeRocket(new Vector2(0.5f, 0f), Vector2.zero);

            Outcome result = PatchedConicsSolver.CheckOutcome(
                rocket, bodies, playArea, G, maxSimTime: 60f, out int outcomeBodyId);

            Assert.AreEqual(Outcome.Crashed, result, "Rocket inside a body's radius must be Crashed.");
            Assert.AreEqual(0, outcomeBodyId, "Crashed body ID should be the sun (0).");
        }

        // -------------------------------------------------------------------------
        //  4. Rocket escapes when energy is positive and it is outside the play area
        // -------------------------------------------------------------------------

        [Test]
        public void CheckOutcome_Escaped_WhenOutsidePlayAreaWithPositiveEnergy()
        {
            CelestialBody sun = MakeBody(0, "Sun", Vector2.zero, 100f, 1.5f, 30f);
            List<CelestialBody> bodies = new List<CelestialBody> { sun };
            Rect playArea = new Rect(-25f, -15f, 50f, 30f);

            // Place rocket far outside the play area moving away fast (positive energy)
            Vector2 pos = new Vector2(100f, 0f);
            Vector2 vel = new Vector2(20f, 0f); // escape velocity at this distance is much less

            RocketState rocket = MakeRocket(pos, vel);

            // Confirm the energy is positive before calling CheckOutcome
            float energy = PatchedConicsSolver.SpecificOrbitalEnergy(pos, vel, sun, G);
            Assert.Greater(energy, 0f, "Pre-condition: rocket should have positive orbital energy.");

            Outcome result = PatchedConicsSolver.CheckOutcome(
                rocket, bodies, playArea, G, maxSimTime: 60f, out int _);

            Assert.AreEqual(Outcome.Escaped, result, "Rocket outside play area with positive energy must be Escaped.");
        }

        // -------------------------------------------------------------------------
        //  5. Escaped when sim time exceeds maxSimTime
        // -------------------------------------------------------------------------

        [Test]
        public void CheckOutcome_Escaped_WhenSimTimeExceedsMax()
        {
            CelestialBody sun = MakeBody(0, "Sun", Vector2.zero, 100f, 1.5f, 30f);
            List<CelestialBody> bodies = new List<CelestialBody> { sun };
            Rect playArea = new Rect(-25f, -15f, 50f, 30f);

            // Rocket inside play area — normally None, but sim time exceeded
            RocketState rocket = MakeRocket(new Vector2(5f, 0f), new Vector2(1f, 0f));
            rocket.TimeInFlight = 60f;

            Outcome result = PatchedConicsSolver.CheckOutcome(
                rocket, bodies, playArea, G, maxSimTime: 60f, out int _);

            Assert.AreEqual(Outcome.Escaped, result, "Should Escape when TimeInFlight >= maxSimTime.");
        }
    }
}
