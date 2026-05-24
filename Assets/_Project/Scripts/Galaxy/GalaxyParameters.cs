using UnityEngine;

namespace Orbital.Galaxy
{
    [CreateAssetMenu(fileName = "GalaxyParameters", menuName = "Orbital/Galaxy Parameters")]
    public class GalaxyParameters : ScriptableObject
    {
        [Header("Body Count")]
        public int MinBodies = 15;
        public int MaxBodies = 25;

        [Header("Clusters")]
        public int MinClusters = 3;
        public int MaxClusters = 5;
        public int MinBodiesPerCluster = 3;
        public int MaxBodiesPerCluster = 7;

        [Header("Play Area")]
        public float PlayAreaWidth = 80f;
        public float PlayAreaHeight = 40f;
        public float HomePlanetXOffsetFromEdge = 5f;

        [Header("Separation and Layout")]
        public float MinBodySeparation = 2.5f;
        public float ClusterCenterXMin = -25f;
        public float ClusterCenterXMax = 25f;
        public float ClusterCenterYJitter = 12f;
        public float ClusterRadius = 6f;

        [Header("Outliers")]
        public int OutlierCountMin = 0;
        public int OutlierCountMax = 3;

        [Header("Home Planet")]
        public float HomePlanetMass = 200f;
        public float HomePlanetRadius = 0.8f;
        public float HomePlanetSoiRadius = 8f;
        [Tooltip("Color shown for both home planets in the visualizer.")]
        public Color HomePlanetColor = new Color(0.2f, 0.5f, 1f);
        [Tooltip("Capture window for home planets. Must match what TurnManager enforces. " +
                 "The rendered capture ring is drawn at CaptureRingRadius.")]
        public CaptureCriteria HomePlanetCaptureCriteria = new CaptureCriteria
        {
            CaptureRingRadius            = 4f,
            CaptureMinSpeed              = 2f,
            CaptureMaxSpeed              = 20f,
            CaptureAngleToleranceDegrees = 45f
        };

        [Header("Body Types (Rocky, Ice, Lava, Gas, Water)")]
        public BodyTypeDefinition[] BodyTypes = new BodyTypeDefinition[]
        {
            new BodyTypeDefinition
            {
                TypeName = "Rocky",
                VisualColor = new Color(0.60f, 0.50f, 0.40f),
                MinMass = 50f, MaxMass = 150f,
                MinRadius = 0.5f, MaxRadius = 1.0f,
                Weight = 3f
            },
            new BodyTypeDefinition
            {
                TypeName = "Ice",
                VisualColor = new Color(0.75f, 0.92f, 1f),
                MinMass = 40f, MaxMass = 120f,
                MinRadius = 0.4f, MaxRadius = 0.9f,
                Weight = 2f
            },
            new BodyTypeDefinition
            {
                TypeName = "Lava",
                VisualColor = new Color(1f, 0.30f, 0.10f),
                MinMass = 60f, MaxMass = 180f,
                MinRadius = 0.5f, MaxRadius = 1.0f,
                Weight = 1f
            },
            new BodyTypeDefinition
            {
                TypeName = "Gas",
                VisualColor = new Color(0.85f, 0.75f, 0.50f),
                MinMass = 100f, MaxMass = 250f,
                MinRadius = 1.0f, MaxRadius = 1.8f,
                Weight = 1f
            },
            new BodyTypeDefinition
            {
                TypeName = "Water",
                VisualColor = new Color(0.20f, 0.50f, 1f),
                MinMass = 50f, MaxMass = 130f,
                MinRadius = 0.5f, MaxRadius = 1.0f,
                Weight = 2f
            },
        };

        [Header("Planet Visuals")]
        [Tooltip("Per-type sprite sets. Match TypeName to a BodyTypeDefinition. " +
                 "Types with no entry here fall back to the colored circle.")]
        public BodyTypeVisuals[] TypeVisuals = new BodyTypeVisuals[0];

        [Tooltip("Visuals used for both home planets, overriding their type-based visuals. Leave null to use the type lookup for home planets too.")]
        public BodyTypeVisuals HomeVisuals;

        [Header("Capture Criteria (applied to all generated non-home bodies)")]
        public CaptureCriteria DefaultCaptureCriteria = new CaptureCriteria
        {
            CaptureRingRadius = 4f,
            CaptureMinSpeed = 2f,
            CaptureMaxSpeed = 20f,
            CaptureAngleToleranceDegrees = 45f
        };

        [Tooltip("soiRadius = body.Radius * SoiRadiusMultiplier, clamped to at least CaptureRingRadius.")]
        public float SoiRadiusMultiplier = 6f;

        [Header("Physics (used by evaluator PathViability simulation)")]
        [Tooltip("Must match PrototypeScenarioController.G.")]
        public float G = 1f;
        public float SimDt = 0.02f;
        public float MaxLaunchSpeed = 16f;
        public float MaxSimTime = 30f;
        [Tooltip("Launch speed multiplier used only in the PathViability headless sim. " +
                 "Needs to be > 1 because at MaxLaunchSpeed the rocket is barely below " +
                 "escape velocity from a HomePlanetMass=200 body; a boost ensures test shots " +
                 "actually escape and reach other bodies.")]
        public float PathViabilityLaunchMultiplier = 1.5f;

        [Header("Generator")]
        public int MaxAttempts = 50;

        [Header("Evaluator Thresholds (must be in [0, 1])")]
        public float PathViabilityThreshold = 0.4f;
        public float RegionBalanceThreshold = 0.6f;
        public float SpreadThreshold = 0.3f;
        public float SymmetryThreshold = 0.5f;

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Scores are bounded [0, 1] so thresholds above 1 make criteria permanently impossible.
            PathViabilityThreshold = UnityEngine.Mathf.Clamp01(PathViabilityThreshold);
            RegionBalanceThreshold = UnityEngine.Mathf.Clamp01(RegionBalanceThreshold);
            SpreadThreshold        = UnityEngine.Mathf.Clamp01(SpreadThreshold);
            SymmetryThreshold      = UnityEngine.Mathf.Clamp01(SymmetryThreshold);
        }
#endif
    }
}
