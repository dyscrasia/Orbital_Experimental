using System;
using System.Collections.Generic;
using Orbital.Core;
using Orbital.Physics;
using UnityEngine;

namespace Orbital.Galaxy
{
    /// <summary>
    /// Pure static class — no MonoBehaviour, no Unity lifecycle, no UnityEngine.Random.
    /// Produces a deterministic Galaxy from a seed and a GalaxyParameters asset.
    /// </summary>
    public static class GalaxyGenerator
    {
        private const int MaxPlacementAttempts = 30;
        private const int P1HomeId = 0;
        private const int P2HomeId = 1;

        public static GalaxyData Generate(int seed, GalaxyParameters parameters)
        {
            if (parameters == null)
                throw new ArgumentNullException(nameof(parameters));

            Rng rng = new Rng(seed);
            GalaxyData best = null;
            float bestScore = float.MinValue;

            for (int attempt = 1; attempt <= parameters.MaxAttempts; attempt++)
            {
                Rng attemptRng = rng.SubStream($"attempt-{attempt}");
                GalaxyData galaxy = TryGenerate(seed, attemptRng, parameters);
                if (galaxy == null) continue;

                if (galaxy.Evaluation.IsAcceptable)
                    return galaxy;

                float score = galaxy.Evaluation.PathViability
                            + galaxy.Evaluation.RegionBalance
                            + galaxy.Evaluation.Spread
                            + galaxy.Evaluation.Symmetry;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = galaxy;
                }
            }

            // Return best attempt found even if not fully acceptable
            return best;
        }

        private static GalaxyData TryGenerate(int seed, Rng rng, GalaxyParameters p)
        {
            Rect playArea = new Rect(
                -p.PlayAreaWidth * 0.5f,
                -p.PlayAreaHeight * 0.5f,
                p.PlayAreaWidth,
                p.PlayAreaHeight);

            List<CelestialBody> bodies = new List<CelestialBody>();

            // 1. Place home planets at fixed edge X positions with small Y jitter
            float homeY1 = rng.Range(-3f, 3f);
            float homeY2 = rng.Range(-3f, 3f);
            float p1X = -p.PlayAreaWidth * 0.5f + p.HomePlanetXOffsetFromEdge;
            float p2X =  p.PlayAreaWidth * 0.5f - p.HomePlanetXOffsetFromEdge;

            bodies.Add(new CelestialBody
            {
                Id = P1HomeId,
                Name = "Home1",
                Position = new Vector2(p1X, homeY1),
                Mass = p.HomePlanetMass,
                Radius = p.HomePlanetRadius,
                SoiRadius = p.HomePlanetSoiRadius,
                CaptureRingRadius            = p.HomePlanetCaptureCriteria.CaptureRingRadius,
                CaptureMinSpeed              = p.HomePlanetCaptureCriteria.CaptureMinSpeed,
                CaptureMaxSpeed              = p.HomePlanetCaptureCriteria.CaptureMaxSpeed,
                CaptureAngleToleranceDegrees = p.HomePlanetCaptureCriteria.CaptureAngleToleranceDegrees
            });
            bodies.Add(new CelestialBody
            {
                Id = P2HomeId,
                Name = "Home2",
                Position = new Vector2(p2X, homeY2),
                Mass = p.HomePlanetMass,
                Radius = p.HomePlanetRadius,
                SoiRadius = p.HomePlanetSoiRadius,
                CaptureRingRadius            = p.HomePlanetCaptureCriteria.CaptureRingRadius,
                CaptureMinSpeed              = p.HomePlanetCaptureCriteria.CaptureMinSpeed,
                CaptureMaxSpeed              = p.HomePlanetCaptureCriteria.CaptureMaxSpeed,
                CaptureAngleToleranceDegrees = p.HomePlanetCaptureCriteria.CaptureAngleToleranceDegrees
            });

            // 2. Place cluster centres distributed roughly evenly along X
            int clusterCount = rng.Range(p.MinClusters, p.MaxClusters + 1);
            List<Vector2> clusterCenters = new List<Vector2>(clusterCount);
            for (int i = 0; i < clusterCount; i++)
            {
                float t = (i + 0.5f) / clusterCount;
                float cx = Mathf.Lerp(p.ClusterCenterXMin, p.ClusterCenterXMax, t)
                         + rng.Range(-3f, 3f);
                float cy = rng.Range(-p.ClusterCenterYJitter, p.ClusterCenterYJitter);
                clusterCenters.Add(new Vector2(cx, cy));
            }

            // 3. Populate each cluster
            foreach (Vector2 center in clusterCenters)
            {
                int count = rng.Range(p.MinBodiesPerCluster, p.MaxBodiesPerCluster + 1);
                for (int j = 0; j < count; j++)
                {
                    CelestialBody body = TryPlaceBodyNear(
                        center, p.ClusterRadius, bodies, rng, p, bodies.Count);
                    if (body != null)
                        bodies.Add(body);
                }
            }

            // 4. Add outlier planets
            int outlierCount = rng.Range(p.OutlierCountMin, p.OutlierCountMax + 1);
            for (int k = 0; k < outlierCount; k++)
            {
                CelestialBody body = TryPlaceBodyAsOutlier(
                    bodies, clusterCenters, rng, p, bodies.Count);
                if (body != null)
                    bodies.Add(body);
            }

            // 5. Cap to MaxBodies (remove non-home bodies from the end)
            while (bodies.Count > p.MaxBodies)
                bodies.RemoveAt(bodies.Count - 1);

            // Reject attempts that produced too few bodies
            if (bodies.Count < p.MinBodies)
                return null;

            // 6. Evaluate and return
            GalaxyEvaluation eval = GalaxyEvaluator.Evaluate(
                bodies, P1HomeId, P2HomeId, playArea,
                p.G, p.SimDt, p.MaxLaunchSpeed, p.MaxSimTime, p);

            return new GalaxyData(seed, bodies, P1HomeId, P2HomeId, playArea, eval);
        }

        private static CelestialBody TryPlaceBodyNear(
            Vector2 center, float maxRadius,
            List<CelestialBody> existing, Rng rng,
            GalaxyParameters p, int nextId)
        {
            Rect playArea = new Rect(
                -p.PlayAreaWidth * 0.5f, -p.PlayAreaHeight * 0.5f,
                p.PlayAreaWidth, p.PlayAreaHeight);

            for (int attempt = 0; attempt < MaxPlacementAttempts; attempt++)
            {
                float angle = rng.Range(0f, Mathf.PI * 2f);
                float dist  = rng.Range(0f, maxRadius);
                Vector2 candidate = center + new Vector2(
                    Mathf.Cos(angle) * dist,
                    Mathf.Sin(angle) * dist);

                if (!playArea.Contains(candidate)) continue;
                if (!HasMinSeparation(candidate, existing, p.MinBodySeparation)) continue;

                return MakeBody(nextId, PickBodyType(rng, p.BodyTypes), candidate, rng, p);
            }
            return null;
        }

        private static CelestialBody TryPlaceBodyAsOutlier(
            List<CelestialBody> existing, List<Vector2> clusterCenters,
            Rng rng, GalaxyParameters p, int nextId)
        {
            Rect playArea = new Rect(
                -p.PlayAreaWidth * 0.5f, -p.PlayAreaHeight * 0.5f,
                p.PlayAreaWidth, p.PlayAreaHeight);

            for (int attempt = 0; attempt < MaxPlacementAttempts; attempt++)
            {
                float x = rng.Range(playArea.xMin, playArea.xMax);
                float y = rng.Range(playArea.yMin, playArea.yMax);
                Vector2 candidate = new Vector2(x, y);

                // Outliers must sit outside every cluster's radius
                bool tooCloseToCluster = false;
                foreach (Vector2 cc in clusterCenters)
                {
                    if ((candidate - cc).magnitude < p.ClusterRadius)
                    {
                        tooCloseToCluster = true;
                        break;
                    }
                }
                if (tooCloseToCluster) continue;
                if (!HasMinSeparation(candidate, existing, p.MinBodySeparation)) continue;

                return MakeBody(nextId, PickBodyType(rng, p.BodyTypes), candidate, rng, p);
            }
            return null;
        }

        private static bool HasMinSeparation(
            Vector2 candidate, List<CelestialBody> existing, float minSep)
        {
            foreach (CelestialBody body in existing)
            {
                if ((candidate - body.Position).magnitude < minSep)
                    return false;
            }
            return true;
        }

        private static BodyTypeDefinition PickBodyType(Rng rng, BodyTypeDefinition[] types)
        {
            if (types == null || types.Length == 0)
                throw new InvalidOperationException("GalaxyParameters.BodyTypes must not be empty.");

            float totalWeight = 0f;
            foreach (BodyTypeDefinition t in types)
                totalWeight += t.Weight;

            float roll = rng.Range(0f, totalWeight);
            float accumulated = 0f;
            foreach (BodyTypeDefinition t in types)
            {
                accumulated += t.Weight;
                if (roll < accumulated)
                    return t;
            }
            return types[types.Length - 1];
        }

        private static CelestialBody MakeBody(
            int id, BodyTypeDefinition type, Vector2 position, Rng rng, GalaxyParameters p)
        {
            float mass   = rng.Range(type.MinMass, type.MaxMass);
            float radius = rng.Range(type.MinRadius, type.MaxRadius);
            float soi    = Mathf.Max(
                radius * p.SoiRadiusMultiplier,
                p.DefaultCaptureCriteria.CaptureRingRadius);

            return new CelestialBody
            {
                Id = id,
                Name = type.TypeName,
                Position = position,
                Mass = mass,
                Radius = radius,
                SoiRadius = soi,
                CaptureRingRadius           = p.DefaultCaptureCriteria.CaptureRingRadius,
                CaptureMinSpeed             = p.DefaultCaptureCriteria.CaptureMinSpeed,
                CaptureMaxSpeed             = p.DefaultCaptureCriteria.CaptureMaxSpeed,
                CaptureAngleToleranceDegrees = p.DefaultCaptureCriteria.CaptureAngleToleranceDegrees
            };
        }
    }
}
