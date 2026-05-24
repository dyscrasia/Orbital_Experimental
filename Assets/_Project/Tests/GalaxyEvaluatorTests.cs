using System.Collections.Generic;
using NUnit.Framework;
using Orbital.Galaxy;
using Orbital.Physics;
using UnityEngine;

namespace Orbital.Tests
{
    /// <summary>
    /// Unit tests for GalaxyEvaluator.
    /// Run via Window > General > Test Runner > Edit Mode.
    /// </summary>
    public class GalaxyEvaluatorTests
    {
        private static readonly Rect PlayArea = new Rect(-40f, -20f, 80f, 40f);
        private const int P1Id = 0;
        private const int P2Id = 1;

        // -------------------------------------------------------------------------
        //  Helpers
        // -------------------------------------------------------------------------

        private static CelestialBody MakeHome(int id, Vector2 pos)
            => new CelestialBody
            {
                Id = id, Name = $"Home{id}",
                Position = pos, Mass = 200f, Radius = 0.8f, SoiRadius = 8f,
                CaptureRingRadius = 0f
            };

        private static CelestialBody MakePlanet(int id, Vector2 pos, float captureRing = 4f)
            => new CelestialBody
            {
                Id = id, Name = "Rocky",
                Position = pos, Mass = 100f, Radius = 0.6f, SoiRadius = 5f,
                CaptureRingRadius = captureRing,
                CaptureMinSpeed = 1f, CaptureMaxSpeed = 30f,
                CaptureAngleToleranceDegrees = 60f
            };

        // -------------------------------------------------------------------------
        //  1. RegionBalance — all bodies on one side scores below threshold
        // -------------------------------------------------------------------------

        [Test]
        public void RegionBalance_FailsWhenAllBodiesOnOneSide()
        {
            // P1 home at far left, P2 home at far right.
            // All non-home bodies clustered near P2 → heavily imbalanced.
            List<CelestialBody> bodies = new List<CelestialBody>
            {
                MakeHome(P1Id, new Vector2(-35f, 0f)),
                MakeHome(P2Id, new Vector2( 35f, 0f)),
                MakePlanet(2, new Vector2(25f,  5f)),
                MakePlanet(3, new Vector2(28f, -3f)),
                MakePlanet(4, new Vector2(30f,  8f)),
                MakePlanet(5, new Vector2(32f, -6f)),
            };

            GalaxyEvaluation eval = GalaxyEvaluator.Evaluate(
                bodies, P1Id, P2Id, PlayArea);

            Assert.Less(eval.RegionBalance, 0.6f,
                "RegionBalance should be below threshold when all bodies are on one side.");
            Assert.IsFalse(eval.IsAcceptable,
                "Unbalanced layout should not be acceptable.");
        }

        [Test]
        public void RegionBalance_PassesWhenBodiesEvenlyDistributed()
        {
            // Symmetric layout: equal count on each side.
            List<CelestialBody> bodies = new List<CelestialBody>
            {
                MakeHome(P1Id, new Vector2(-35f, 0f)),
                MakeHome(P2Id, new Vector2( 35f, 0f)),
                MakePlanet(2, new Vector2(-15f,  5f)),
                MakePlanet(3, new Vector2(-15f, -5f)),
                MakePlanet(4, new Vector2( 15f,  5f)),
                MakePlanet(5, new Vector2( 15f, -5f)),
            };

            GalaxyEvaluation eval = GalaxyEvaluator.Evaluate(
                bodies, P1Id, P2Id, PlayArea);

            Assert.GreaterOrEqual(eval.RegionBalance, 0.6f,
                "RegionBalance should pass for an evenly distributed layout.");
        }

        // -------------------------------------------------------------------------
        //  2. Symmetry — perfect mirror layout scores high; one-sided scores low
        // -------------------------------------------------------------------------

        [Test]
        public void Symmetry_ScoresHighForMirrorLayout()
        {
            List<CelestialBody> bodies = new List<CelestialBody>
            {
                MakeHome(P1Id, new Vector2(-35f, 0f)),
                MakeHome(P2Id, new Vector2( 35f, 0f)),
                // Perfect mirror pairs around x = 0
                MakePlanet(2, new Vector2(-12f,  6f)),
                MakePlanet(3, new Vector2(-12f, -6f)),
                MakePlanet(4, new Vector2( 12f,  6f)),
                MakePlanet(5, new Vector2( 12f, -6f)),
                MakePlanet(6, new Vector2( -5f,  0f)),
                MakePlanet(7, new Vector2(  5f,  0f)),
            };

            GalaxyEvaluation eval = GalaxyEvaluator.Evaluate(
                bodies, P1Id, P2Id, PlayArea);

            Assert.GreaterOrEqual(eval.Symmetry, 0.5f,
                "Symmetry should be high for a near-perfect mirror layout.");
        }

        [Test]
        public void Symmetry_ScoresLowWhenAllBodiesOnOneSide()
        {
            List<CelestialBody> bodies = new List<CelestialBody>
            {
                MakeHome(P1Id, new Vector2(-35f, 0f)),
                MakeHome(P2Id, new Vector2( 35f, 0f)),
                MakePlanet(2, new Vector2(20f,  5f)),
                MakePlanet(3, new Vector2(22f, -5f)),
                MakePlanet(4, new Vector2(25f,  0f)),
                MakePlanet(5, new Vector2(28f,  3f)),
            };

            GalaxyEvaluation eval = GalaxyEvaluator.Evaluate(
                bodies, P1Id, P2Id, PlayArea);

            Assert.Less(eval.Symmetry, 0.5f,
                "Symmetry should be low when all non-home bodies are on one side.");
        }

        // -------------------------------------------------------------------------
        //  3. PathViability — bodies on a direct straight path are reachable
        //     Use G = 0 so rockets travel in straight lines, making it deterministic.
        // -------------------------------------------------------------------------

        [Test]
        public void PathViability_BodyOnDirectPath_IsReachable()
        {
            // P1 at far left, one target body directly to the right.
            // With G = 0 a shot straight right will reach the capture ring.
            List<CelestialBody> bodies = new List<CelestialBody>
            {
                MakeHome(P1Id, new Vector2(-35f, 0f)),
                MakeHome(P2Id, new Vector2( 35f, 0f)),
                MakePlanet(2, new Vector2(0f, 0f), captureRing: 5f),
            };

            GalaxyEvaluation eval = GalaxyEvaluator.Evaluate(
                bodies, P1Id, P2Id, PlayArea,
                G: 0f, dt: 0.02f, maxLaunchSpeed: 20f, maxSimTime: 30f);

            Assert.Greater(eval.PathViability, 0f,
                "PathViability should be > 0 when there is a body on a direct shot path.");
        }

        // -------------------------------------------------------------------------
        //  4. Spread — bodies packed into a tiny area score below threshold
        // -------------------------------------------------------------------------

        [Test]
        public void Spread_FailsForTightlyPackedLayout()
        {
            // All bodies crammed into a 3×3 square — average pairwise distance << diagonal
            List<CelestialBody> bodies = new List<CelestialBody>
            {
                MakeHome(P1Id, new Vector2(-35f, 0f)),
                MakeHome(P2Id, new Vector2( 35f, 0f)),
                MakePlanet(2, new Vector2(0f,   0f)),
                MakePlanet(3, new Vector2(2.6f, 0f)),
                MakePlanet(4, new Vector2(0f,   2.6f)),
                MakePlanet(5, new Vector2(2.6f, 2.6f)),
            };

            GalaxyEvaluation eval = GalaxyEvaluator.Evaluate(
                bodies, P1Id, P2Id, PlayArea);

            Assert.Less(eval.Spread, 0.3f,
                "Spread should be below threshold for a tightly packed layout.");
        }

        [Test]
        public void Spread_PassesForWidelyDistributedLayout()
        {
            List<CelestialBody> bodies = new List<CelestialBody>
            {
                MakeHome(P1Id, new Vector2(-35f,  0f)),
                MakeHome(P2Id, new Vector2( 35f,  0f)),
                MakePlanet(2, new Vector2(-20f, -10f)),
                MakePlanet(3, new Vector2(-10f,  12f)),
                MakePlanet(4, new Vector2(  0f,  -8f)),
                MakePlanet(5, new Vector2( 10f,  10f)),
                MakePlanet(6, new Vector2( 20f, -12f)),
            };

            GalaxyEvaluation eval = GalaxyEvaluator.Evaluate(
                bodies, P1Id, P2Id, PlayArea);

            Assert.GreaterOrEqual(eval.Spread, 0.3f,
                "Spread should pass for a well-distributed layout.");
        }

        // -------------------------------------------------------------------------
        //  5. Overall acceptability on a fully balanced layout
        // -------------------------------------------------------------------------

        [Test]
        public void WellBalancedLayout_IsAcceptable()
        {
            // Mirror layout, spread well, G=0 so PathViability is deterministic
            List<CelestialBody> bodies = new List<CelestialBody>
            {
                MakeHome(P1Id, new Vector2(-35f,  0f)),
                MakeHome(P2Id, new Vector2( 35f,  0f)),
                MakePlanet( 2, new Vector2(-20f, -8f),  captureRing: 5f),
                MakePlanet( 3, new Vector2(-20f,  8f),  captureRing: 5f),
                MakePlanet( 4, new Vector2(-10f,  0f),  captureRing: 5f),
                MakePlanet( 5, new Vector2(  0f, -10f), captureRing: 5f),
                MakePlanet( 6, new Vector2(  0f,  10f), captureRing: 5f),
                MakePlanet( 7, new Vector2( 10f,  0f),  captureRing: 5f),
                MakePlanet( 8, new Vector2( 20f, -8f),  captureRing: 5f),
                MakePlanet( 9, new Vector2( 20f,  8f),  captureRing: 5f),
            };

            GalaxyEvaluation eval = GalaxyEvaluator.Evaluate(
                bodies, P1Id, P2Id, PlayArea,
                G: 0f, dt: 0.02f, maxLaunchSpeed: 20f, maxSimTime: 30f);

            Assert.IsTrue(eval.IsAcceptable,
                $"Well-balanced layout should be acceptable. Evaluation: {eval.Summary}");
        }
    }
}
