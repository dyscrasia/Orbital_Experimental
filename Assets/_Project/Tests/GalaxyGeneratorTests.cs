using NUnit.Framework;
using Orbital.Galaxy;
using Orbital.Physics;
using UnityEngine;

namespace Orbital.Tests
{
    /// <summary>
    /// Unit tests for GalaxyGenerator.
    /// Run via Window > General > Test Runner > Edit Mode.
    /// </summary>
    public class GalaxyGeneratorTests
    {
        private GalaxyParameters _params;

        [SetUp]
        public void SetUp()
        {
            _params = ScriptableObject.CreateInstance<GalaxyParameters>();

            // Minimal valid body type set
            _params.BodyTypes = new BodyTypeDefinition[]
            {
                new BodyTypeDefinition
                {
                    TypeName = "Rocky",
                    VisualColor = Color.gray,
                    MinMass = 50f, MaxMass = 150f,
                    MinRadius = 0.5f, MaxRadius = 1.0f,
                    Weight = 1f
                }
            };
            _params.DefaultCaptureCriteria = new CaptureCriteria
            {
                CaptureRingRadius = 4f,
                CaptureMinSpeed = 2f,
                CaptureMaxSpeed = 20f,
                CaptureAngleToleranceDegrees = 45f
            };
            _params.HomePlanetCaptureCriteria = new CaptureCriteria
            {
                CaptureRingRadius = 3f,
                CaptureMinSpeed = 1f,
                CaptureMaxSpeed = 25f,
                CaptureAngleToleranceDegrees = 60f
            };

            _params.MinBodies = 15;
            _params.MaxBodies = 25;
            _params.MinClusters = 3;
            _params.MaxClusters = 5;
            _params.MinBodiesPerCluster = 3;
            _params.MaxBodiesPerCluster = 7;
            _params.PlayAreaWidth = 80f;
            _params.PlayAreaHeight = 40f;
            _params.HomePlanetXOffsetFromEdge = 5f;
            _params.MinBodySeparation = 2.5f;
            _params.ClusterCenterXMin = -25f;
            _params.ClusterCenterXMax = 25f;
            _params.ClusterCenterYJitter = 12f;
            _params.ClusterRadius = 6f;
            _params.OutlierCountMin = 0;
            _params.OutlierCountMax = 3;
            _params.HomePlanetMass = 200f;
            _params.HomePlanetRadius = 0.8f;
            _params.HomePlanetSoiRadius = 8f;
            _params.HomePlanetColor = Color.blue;
            _params.SoiRadiusMultiplier = 6f;
            _params.G = 1f;
            _params.SimDt = 0.02f;
            _params.MaxLaunchSpeed = 16f;
            _params.MaxSimTime = 30f;
            _params.MaxAttempts = 50;
            _params.PathViabilityThreshold = 0.4f;
            _params.RegionBalanceThreshold = 0.6f;
            _params.SpreadThreshold = 0.3f;
            _params.SymmetryThreshold = 0.5f;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_params);
        }

        // -------------------------------------------------------------------------
        //  1. Determinism — same seed produces identical output
        // -------------------------------------------------------------------------

        [Test]
        public void SameSeed_ProducesSameGalaxy()
        {
            const int seed = 42;
            GalaxyData a = GalaxyGenerator.Generate(seed, _params);
            GalaxyData b = GalaxyGenerator.Generate(seed, _params);

            Assert.IsNotNull(a, "Galaxy A should not be null.");
            Assert.IsNotNull(b, "Galaxy B should not be null.");
            Assert.AreEqual(a.Bodies.Count, b.Bodies.Count,
                "Same seed must produce the same body count.");

            for (int i = 0; i < a.Bodies.Count; i++)
            {
                Assert.AreEqual(a.Bodies[i].Position.x, b.Bodies[i].Position.x, 1e-5f,
                    $"Body {i} X position should be identical.");
                Assert.AreEqual(a.Bodies[i].Position.y, b.Bodies[i].Position.y, 1e-5f,
                    $"Body {i} Y position should be identical.");
                Assert.AreEqual(a.Bodies[i].Mass, b.Bodies[i].Mass, 1e-5f,
                    $"Body {i} mass should be identical.");
            }
        }

        [Test]
        public void DifferentSeeds_ProduceDifferentGalaxies()
        {
            GalaxyData a = GalaxyGenerator.Generate(1, _params);
            GalaxyData b = GalaxyGenerator.Generate(2, _params);

            Assert.IsNotNull(a);
            Assert.IsNotNull(b);

            // At least one body position should differ (astronomically unlikely to be equal)
            bool anyDifference = false;
            int compareCount = Mathf.Min(a.Bodies.Count, b.Bodies.Count);
            for (int i = 0; i < compareCount; i++)
            {
                if (Mathf.Abs(a.Bodies[i].Position.x - b.Bodies[i].Position.x) > 0.01f)
                {
                    anyDifference = true;
                    break;
                }
            }
            Assert.IsTrue(anyDifference, "Different seeds should produce different galaxies.");
        }

        // -------------------------------------------------------------------------
        //  2. MinBodySeparation is never violated
        // -------------------------------------------------------------------------

        [Test]
        public void AllBodies_SatisfyMinBodySeparation()
        {
            GalaxyData galaxy = GalaxyGenerator.Generate(99, _params);
            Assert.IsNotNull(galaxy);

            for (int i = 0; i < galaxy.Bodies.Count; i++)
            {
                for (int j = i + 1; j < galaxy.Bodies.Count; j++)
                {
                    float dist = (galaxy.Bodies[i].Position - galaxy.Bodies[j].Position).magnitude;
                    Assert.GreaterOrEqual(dist, _params.MinBodySeparation - 1e-4f,
                        $"Bodies {i} ({galaxy.Bodies[i].Name}) and {j} ({galaxy.Bodies[j].Name}) " +
                        $"are only {dist:F3} apart, violating MinBodySeparation={_params.MinBodySeparation}.");
                }
            }
        }

        // -------------------------------------------------------------------------
        //  3. Body count stays within [MinBodies, MaxBodies]
        // -------------------------------------------------------------------------

        [Test]
        public void BodyCount_WithinParameterRange()
        {
            // Test several seeds for robustness
            int[] seeds = { 1, 2, 3, 42, 100, 999 };
            foreach (int seed in seeds)
            {
                GalaxyData galaxy = GalaxyGenerator.Generate(seed, _params);
                Assert.IsNotNull(galaxy, $"Galaxy for seed {seed} should not be null.");
                Assert.GreaterOrEqual(galaxy.Bodies.Count, _params.MinBodies,
                    $"Seed {seed}: body count {galaxy.Bodies.Count} < MinBodies {_params.MinBodies}.");
                Assert.LessOrEqual(galaxy.Bodies.Count, _params.MaxBodies,
                    $"Seed {seed}: body count {galaxy.Bodies.Count} > MaxBodies {_params.MaxBodies}.");
            }
        }

        // -------------------------------------------------------------------------
        //  4. Home planets are at the configured edge X positions
        // -------------------------------------------------------------------------

        [Test]
        public void HomePlanets_AtConfiguredEdgePositions()
        {
            GalaxyData galaxy = GalaxyGenerator.Generate(7, _params);
            Assert.IsNotNull(galaxy);

            CelestialBody p1 = null;
            CelestialBody p2 = null;
            foreach (CelestialBody body in galaxy.Bodies)
            {
                if (body.Id == galaxy.Player1HomeId) p1 = body;
                if (body.Id == galaxy.Player2HomeId) p2 = body;
            }

            Assert.IsNotNull(p1, "Player 1 home body not found.");
            Assert.IsNotNull(p2, "Player 2 home body not found.");

            float expectedP1X = -_params.PlayAreaWidth * 0.5f + _params.HomePlanetXOffsetFromEdge;
            float expectedP2X =  _params.PlayAreaWidth * 0.5f - _params.HomePlanetXOffsetFromEdge;

            Assert.AreEqual(expectedP1X, p1.Position.x, 1e-4f,
                "Player 1 home X should be at the left edge offset.");
            Assert.AreEqual(expectedP2X, p2.Position.x, 1e-4f,
                "Player 2 home X should be at the right edge offset.");

            // Y jitter is in [-3, 3]
            Assert.LessOrEqual(Mathf.Abs(p1.Position.y), 3f + 1e-4f,
                "Player 1 home Y jitter should be within [-3, 3].");
            Assert.LessOrEqual(Mathf.Abs(p2.Position.y), 3f + 1e-4f,
                "Player 2 home Y jitter should be within [-3, 3].");
        }

        // -------------------------------------------------------------------------
        //  5. Home planets use HomePlanetCaptureCriteria; non-home bodies use
        //     DefaultCaptureCriteria — all four fields on both groups.
        // -------------------------------------------------------------------------

        [Test]
        public void HomePlanets_UseHomePlanetCaptureCriteria()
        {
            GalaxyData galaxy = GalaxyGenerator.Generate(55, _params);
            Assert.IsNotNull(galaxy);

            foreach (CelestialBody body in galaxy.Bodies)
            {
                if (body.Id != galaxy.Player1HomeId && body.Id != galaxy.Player2HomeId)
                    continue;

                Assert.AreEqual(_params.HomePlanetCaptureCriteria.CaptureRingRadius,
                    body.CaptureRingRadius, 1e-5f,
                    $"Home body '{body.Name}' CaptureRingRadius should come from HomePlanetCaptureCriteria.");
                Assert.AreEqual(_params.HomePlanetCaptureCriteria.CaptureMinSpeed,
                    body.CaptureMinSpeed, 1e-5f,
                    $"Home body '{body.Name}' CaptureMinSpeed should come from HomePlanetCaptureCriteria.");
                Assert.AreEqual(_params.HomePlanetCaptureCriteria.CaptureMaxSpeed,
                    body.CaptureMaxSpeed, 1e-5f,
                    $"Home body '{body.Name}' CaptureMaxSpeed should come from HomePlanetCaptureCriteria.");
                Assert.AreEqual(_params.HomePlanetCaptureCriteria.CaptureAngleToleranceDegrees,
                    body.CaptureAngleToleranceDegrees, 1e-5f,
                    $"Home body '{body.Name}' CaptureAngleToleranceDegrees should come from HomePlanetCaptureCriteria.");
            }
        }

        [Test]
        public void NonHomeBodies_InheritCaptureCriteriaFromDefault()
        {
            GalaxyData galaxy = GalaxyGenerator.Generate(55, _params);
            Assert.IsNotNull(galaxy);

            foreach (CelestialBody body in galaxy.Bodies)
            {
                if (body.Id == galaxy.Player1HomeId || body.Id == galaxy.Player2HomeId)
                    continue;

                Assert.AreEqual(_params.DefaultCaptureCriteria.CaptureRingRadius,
                    body.CaptureRingRadius, 1e-5f,
                    $"Non-home body '{body.Name}' CaptureRingRadius should come from DefaultCaptureCriteria.");
                Assert.AreEqual(_params.DefaultCaptureCriteria.CaptureMinSpeed,
                    body.CaptureMinSpeed, 1e-5f,
                    $"Non-home body '{body.Name}' CaptureMinSpeed should come from DefaultCaptureCriteria.");
                Assert.AreEqual(_params.DefaultCaptureCriteria.CaptureMaxSpeed,
                    body.CaptureMaxSpeed, 1e-5f,
                    $"Non-home body '{body.Name}' CaptureMaxSpeed should come from DefaultCaptureCriteria.");
                Assert.AreEqual(_params.DefaultCaptureCriteria.CaptureAngleToleranceDegrees,
                    body.CaptureAngleToleranceDegrees, 1e-5f,
                    $"Non-home body '{body.Name}' CaptureAngleToleranceDegrees should come from DefaultCaptureCriteria.");
            }
        }
    }
}
