using System.Collections.Generic;
using Orbital.Galaxy;
using Orbital.Physics;
using Orbital.Strategy;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Orbital.Presentation
{
    /// <summary>
    /// Root MonoBehaviour for the Phase 1 / Phase 2 prototype scene.
    ///
    /// When UseProceduralGalaxy is true (default), generates a galaxy via
    /// GalaxyGenerator and uses those bodies as the scenario. When false, falls
    /// back to the hard-coded BodyConfigs array (Phase 1 reference layout).
    ///
    /// GalaxyVisualizer can call RegenerateGalaxy() at runtime to swap in a new layout.
    /// </summary>
    public class PrototypeScenarioController : MonoBehaviour
    {
        // -------------------------------------------------------------------------
        //  Serializable config struct — used only for the hard-coded fallback
        // -------------------------------------------------------------------------

        [System.Serializable]
        public struct BodyConfig
        {
            public string Name;
            public Vector2 Position;
            public float Mass;
            public float Radius;
            public float SoiRadius;
            public Color BodyColor;

            // Capture window — set CaptureRingRadius = 0 to disable for this body
            public float CaptureRingRadius;
            public float CaptureMinSpeed;
            public float CaptureMaxSpeed;
            public float CaptureAngleToleranceDegrees;
        }

        // -------------------------------------------------------------------------
        //  Tunable physics parameters
        // -------------------------------------------------------------------------

        [Header("Physics")]
        [Tooltip("Gravitational constant. Raise to make gravity stronger; lower to weaken it.")]
        public float G = 1.0f;

        [Tooltip("Fixed simulation timestep in seconds (50 Hz = 0.02). Must match Time.fixedDeltaTime.")]
        public float Dt = 0.02f;

        [Tooltip("After this many seconds in flight, the rocket is declared Escaped.")]
        public float MaxSimTime = 60f;

        [Tooltip("Full-drag launch speed in world units per second.")]
        public float MaxLaunchSpeed = 16f;

        [Tooltip("Number of simulation steps to predict for the trajectory preview (3 s at 50 Hz = 150).")]
        public int TrajectorySteps = 150;

        // -------------------------------------------------------------------------
        //  Play area
        // -------------------------------------------------------------------------

        [Header("Play Area")]
        public float PlayAreaWidth = 50f;
        public float PlayAreaHeight = 30f;

        // -------------------------------------------------------------------------
        //  Procedural galaxy
        // -------------------------------------------------------------------------

        [Header("Procedural Galaxy")]
        [Tooltip("When true, generates a galaxy from GalaxyParams instead of using BodyConfigs.")]
        public bool UseProceduralGalaxy = true;

        [Tooltip("ScriptableObject with generator tunables. " +
                 "Create via Assets > Create > Orbital > Galaxy Parameters.")]
        public GalaxyParameters GalaxyParams;

        [Header("Strategy")]
        [Tooltip("ScriptableObject with Strategy-variant tunables. " +
                 "Create via Assets > Create > Orbital > Strategy Parameters.")]
        public StrategyParameters StrategyParams;

        [Tooltip("Seed used for the initial galaxy. GalaxyVisualizer can override this at runtime.")]
        public int InitialSeed = 12345;

        // -------------------------------------------------------------------------
        //  Hard-coded fallback scenario (Phase 1)
        // -------------------------------------------------------------------------

        [Header("Hard-coded Scenario (fallback when UseProceduralGalaxy = false)")]
        public BodyConfig[] BodyConfigs = new BodyConfig[]
        {
            new BodyConfig
            {
                Name = "Sun",
                Position = Vector2.zero,
                Mass = 200f, Radius = 1.5f, SoiRadius = 30f,
                BodyColor = new Color(1f, 0.9f, 0.2f),
                CaptureRingRadius = 0f
            },
            new BodyConfig
            {
                Name = "HomePlanet",
                Position = new Vector2(-20f, 0f),
                Mass = 200f, Radius = 0.8f, SoiRadius = 6f,
                BodyColor = new Color(0.2f, 0.5f, 1f),
                CaptureRingRadius = 0f
            },
            new BodyConfig
            {
                Name = "IcePlanet",
                Position = new Vector2(5f, 8f),
                Mass = 200f, Radius = 0.6f, SoiRadius = 3f,
                BodyColor = new Color(0.75f, 0.92f, 1f),
                CaptureRingRadius = 3f, CaptureMinSpeed = 4f, CaptureMaxSpeed = 12f,
                CaptureAngleToleranceDegrees = 30f
            },
            new BodyConfig
            {
                Name = "LavaPlanet",
                Position = new Vector2(10f, -6f),
                Mass = 200f, Radius = 0.7f, SoiRadius = 4f,
                BodyColor = new Color(1f, 0.3f, 0.1f),
                CaptureRingRadius = 4f, CaptureMinSpeed = 4f, CaptureMaxSpeed = 50f,
                CaptureAngleToleranceDegrees = 50f
            },
            new BodyConfig
            {
                Name = "TargetPlanet",
                Position = new Vector2(20f, 0f),
                Mass = 80f, Radius = 0.8f, SoiRadius = 6f,
                BodyColor = new Color(0.3f, 1f, 0.3f),
                CaptureRingRadius = 6f, CaptureMinSpeed = 4f, CaptureMaxSpeed = 12f,
                CaptureAngleToleranceDegrees = 30f
            },
        };

        // -------------------------------------------------------------------------
        //  Events — raised by PSC, consumed by TurnManager
        // -------------------------------------------------------------------------

        /// <summary>Fired the moment LaunchRocket() is called.</summary>
        public event System.Action RocketLaunched;

        /// <summary>
        /// Fired when an outcome is determined (Crashed / Orbited / Escaped).
        /// Second argument is the body ID involved, or -1 for Escaped.
        /// </summary>
        public event System.Action<Outcome, int> RocketResolved;

        /// <summary>
        /// Fired at the end of RegenerateGalaxy(), after body views and the rocket
        /// have been rebuilt. TurnManager subscribes to this to reset all per-galaxy
        /// game state (ownership views, orbiting rocket views, GameState) so that
        /// any caller of RegenerateGalaxy — including the GalaxyVisualizer G hotkey —
        /// automatically triggers a complete game reset.
        /// </summary>
        public event System.Action GalaxyRegenerated;

        // -------------------------------------------------------------------------
        //  Turn-managed mode — set by TurnManager
        // -------------------------------------------------------------------------

        /// <summary>
        /// When true, PSC suppresses the OutcomeDisplay and the R-to-reset hotkey;
        /// TurnManager handles both.
        /// </summary>
        private bool _turnManagedMode;

        /// <summary>
        /// Body ID of the planet the rocket is launching FROM this shot.
        /// Capture detection skips ONLY this body so the rocket doesn't immediately
        /// re-capture the planet it just left.
        /// All other planets — including the opposing player's home — are valid capture targets.
        /// Set by TurnManager via SetActiveLaunchSite(); defaults to P1 home so Phase 2
        /// behaviour (single rocket from home) is unchanged.
        /// </summary>
        private int _activeLaunchSiteId = -1;

        // -------------------------------------------------------------------------
        //  State accessible by AimController / GalaxyVisualizer / TurnManager
        // -------------------------------------------------------------------------

        public RocketState Rocket => _rocket;
        public IReadOnlyList<CelestialBody> Bodies => _bodies;
        public GalaxyData CurrentGalaxy => _currentGalaxy;
        public AimController AimController => _aimController;

        // -------------------------------------------------------------------------
        //  Private state
        // -------------------------------------------------------------------------

        private List<CelestialBody> _bodies;
        private RocketState _rocket;
        private Vector2 _rocketStartPosition;
        private int _homeBodyId;
        private Rect _playArea;
        private GalaxyData _currentGalaxy;
        private TurnManager _turnManager;

        // Per-body flag: was the rocket inside this body's capture ring last tick?
        private bool[] _wasInsideCaptureRing;

        // View references (auto-created in Awake)
        private CelestialBodyView[] _bodyViews;
        private RocketView _rocketView;
        private AimController _aimController;
        private TrajectoryView _trajectoryView;
        private OutcomeDisplay _outcomeDisplay;

        // Body scene objects (body views + SOI/capture rings) — destroyed on regeneration
        private readonly List<GameObject> _bodySceneObjects = new List<GameObject>();

        // -------------------------------------------------------------------------
        //  Unity messages
        // -------------------------------------------------------------------------

        private void Awake()
        {
            Time.fixedDeltaTime = Dt;

            if (UseProceduralGalaxy && GalaxyParams != null)
            {
                LoadProceduralGalaxy(InitialSeed);
            }
            else
            {
                _playArea = new Rect(
                    -PlayAreaWidth * 0.5f, -PlayAreaHeight * 0.5f,
                    PlayAreaWidth, PlayAreaHeight);
                BuildBodiesFromConfig();
            }

            BuildRocket();
            SetupCamera();
            CreateBodyViews();
            CreatePersistentViews();
        }

        private void Start()
        {
            // Bootstrap Phase 3 turn management.
            // TurnManager.Awake() fires synchronously inside AddComponent, creating UI.
            // Initialize() wires events and starts the first game with the galaxy
            // already generated in Awake().
            GameObject tmGo = new GameObject("TurnManager");
            tmGo.transform.SetParent(transform, false);
            _turnManager = tmGo.AddComponent<TurnManager>();
            _turnManager.StrategyParams = StrategyParams;
            _turnManager.Initialize(this);

            // Wire the aim controller into TurnManager so it only accepts input
            // during WaitingForLaunch.
            _aimController.SetTurnManager(_turnManager);
        }

        private void FixedUpdate()
        {
            if (_rocket == null || _bodies == null) return;

            // Physics continues for Orbited so the rocket visually traces its orbit.
            if (_rocket.Status != RocketStatus.InFlight && _rocket.Status != RocketStatus.Orbited)
                return;

            _rocket = PatchedConicsSolver.Step(_rocket, _bodies, Dt, G);
            _rocketView.SetData(_rocket);

            // Detection only while actively in flight.
            if (_rocket.Status != RocketStatus.InFlight)
                return;

            LogPhysicsStep();

            // --- Capture-window detection ---
            for (int i = 0; i < _bodies.Count; i++)
            {
                CelestialBody body = _bodies[i];
                // Skip only the body the rocket launched FROM (prevents immediate re-capture)
                // and bodies with no capture ring. All other planets — including the
                // opposing player's home — are valid capture targets.
                if (body.Id == _activeLaunchSiteId || body.CaptureRingRadius <= 0f)
                    continue;

                float dist = (_rocket.Position - body.Position).magnitude;
                bool insideNow = dist <= body.CaptureRingRadius;

                if (insideNow && !_wasInsideCaptureRing[i])
                {
                    _wasInsideCaptureRing[i] = true;
                    if (EvaluateCapture(body, dist))
                        return;
                }
                else
                {
                    _wasInsideCaptureRing[i] = insideNow;
                }
            }

            // --- Crash / Escape detection ---
            Outcome outcome = PatchedConicsSolver.CheckOutcome(
                _rocket, _bodies, _playArea, G, MaxSimTime, out int outcomeBodyId);

            if (outcome != Outcome.None)
                HandleOutcome(outcome, outcomeBodyId);
        }

        private void Update()
        {
            if (_turnManagedMode) return;

            bool isTerminal = _rocket.Status == RocketStatus.Crashed
                           || _rocket.Status == RocketStatus.Orbited
                           || _rocket.Status == RocketStatus.Escaped;

            if (isTerminal && Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
                ResetRocket();
        }

        // -------------------------------------------------------------------------
        //  Public API used by AimController and GalaxyVisualizer
        // -------------------------------------------------------------------------

        /// <summary>Apply an initial velocity and start the simulation.</summary>
        public void LaunchRocket(Vector2 initialVelocity)
        {
            _rocket.Velocity = initialVelocity;
            _rocket.Status = RocketStatus.InFlight;
            RocketLaunched?.Invoke();
        }

        // -------------------------------------------------------------------------
        //  Public API used by TurnManager
        // -------------------------------------------------------------------------

        /// <summary>
        /// Enable / disable turn-managed mode. When true, PSC suppresses the
        /// OutcomeDisplay and R-to-reset; TurnManager handles feedback instead.
        /// </summary>
        public void SetTurnManagedMode(bool on) => _turnManagedMode = on;

        /// <summary>
        /// Set the body ID of the planet the rocket is launching FROM this shot.
        /// Capture detection skips this body to prevent immediate re-capture of
        /// the launch site. Called by TurnManager when a site is selected.
        /// </summary>
        public void SetActiveLaunchSite(int bodyId) => _activeLaunchSiteId = bodyId;

        /// <summary>
        /// Position the rocket at the specified home body and reset it to Prelaunch.
        /// </summary>
        public void PrepareRocketForPlayer(int homeBodyId)
        {
            _homeBodyId = homeBodyId;
            BuildRocket();
            _rocket.PassengerCount = 0;
            _rocketView.SetData(_rocket);
            System.Array.Clear(_wasInsideCaptureRing, 0, _wasInsideCaptureRing.Length);
            if (!_turnManagedMode) _outcomeDisplay.Hide();
        }

        /// <summary>
        /// Slide the rocket to a new world position without changing its status.
        /// Used by AimController during the positioning phase so the rocket follows
        /// the cursor around the home planet surface.
        /// </summary>
        public void RepositionRocket(Vector2 worldPos)
        {
            _rocket.Position = worldPos;
            _rocketView.SetData(_rocket);
        }

        /// <summary>Find a body by its ID. Returns null if not found.</summary>
        public CelestialBody GetBodyById(int id)
        {
            foreach (CelestialBody body in _bodies)
                if (body.Id == id) return body;
            return null;
        }

        /// <summary>
        /// Replace the current galaxy with a newly generated one.
        /// Called by GalaxyVisualizer on G/B hotkeys.
        /// </summary>
        public void RegenerateGalaxy(int seed, GalaxyParameters parameters = null)
        {
            if (parameters != null) GalaxyParams = parameters;
            if (GalaxyParams == null)
            {
                Debug.LogWarning("[PSC] RegenerateGalaxy called but GalaxyParams is null.");
                return;
            }

            InitialSeed = seed;
            DestroyBodySceneObjects();
            LoadProceduralGalaxy(seed);
            CreateBodyViews();
            SetupCamera();

            BuildRocket();
            _rocketView.SetData(_rocket);
            System.Array.Clear(_wasInsideCaptureRing, 0, _wasInsideCaptureRing.Length);
            _outcomeDisplay.Hide();

            // Notify TurnManager (and any other subscribers) so they can reset all
            // per-galaxy game state. Must fire after body views and rocket are rebuilt.
            GalaxyRegenerated?.Invoke();
        }

        // -------------------------------------------------------------------------
        //  Private helpers — galaxy loading
        // -------------------------------------------------------------------------

        private void LoadProceduralGalaxy(int seed)
        {
            _currentGalaxy = GalaxyGenerator.Generate(seed, GalaxyParams);

            _bodies = new List<CelestialBody>(_currentGalaxy.Bodies);
            _homeBodyId = _currentGalaxy.Player1HomeId;
            _activeLaunchSiteId = _homeBodyId;
            _playArea = _currentGalaxy.PlayArea;
            PlayAreaWidth  = _currentGalaxy.PlayArea.width;
            PlayAreaHeight = _currentGalaxy.PlayArea.height;
            _wasInsideCaptureRing = new bool[_bodies.Count];
        }

        private void BuildBodiesFromConfig()
        {
            _bodies = new List<CelestialBody>(BodyConfigs.Length);
            for (int i = 0; i < BodyConfigs.Length; i++)
            {
                BodyConfig cfg = BodyConfigs[i];
                _bodies.Add(new CelestialBody
                {
                    Id = i,
                    Name = cfg.Name,
                    Position = cfg.Position,
                    Mass = cfg.Mass,
                    Radius = cfg.Radius,
                    SoiRadius = cfg.SoiRadius,
                    CaptureRingRadius = cfg.CaptureRingRadius,
                    CaptureMinSpeed = cfg.CaptureMinSpeed,
                    CaptureMaxSpeed = cfg.CaptureMaxSpeed,
                    CaptureAngleToleranceDegrees = cfg.CaptureAngleToleranceDegrees
                });

                if (cfg.Name == "HomePlanet")
                    _homeBodyId = i;
            }

            if (_homeBodyId == 0 && BodyConfigs.Length > 1)
                _homeBodyId = 1;

            _wasInsideCaptureRing = new bool[_bodies.Count];
        }

        private void BuildRocket()
        {
            CelestialBody home = _bodies[_homeBodyId];
            _rocketStartPosition = home.Position + new Vector2(home.Radius + 0.7f, 0f);

            _rocket = new RocketState
            {
                Position = _rocketStartPosition,
                Velocity = Vector2.zero,
                Mass = 0.1f,
                Fuel = 50f,
                CurrentBodyId = _homeBodyId,
                Status = RocketStatus.Prelaunch,
                TimeInFlight = 0f,
                CapturedBodyId = -1
            };
        }

        private void SetupCamera()
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            cam.transform.position = new Vector3(0f, 0f, -10f);
            cam.orthographic = true;

            float aspectRatio = (float)Screen.width / Screen.height;
            float halfHeight = PlayAreaHeight * 0.5f;
            float halfWidth  = PlayAreaWidth  * 0.5f;
            cam.orthographicSize = Mathf.Max(halfHeight, halfWidth / aspectRatio) * 1.05f;
            cam.backgroundColor = new Color(0.02f, 0.02f, 0.08f);
        }

        // -------------------------------------------------------------------------
        //  Private helpers — view creation and destruction
        // -------------------------------------------------------------------------

        private void CreateBodyViews()
        {
            _bodyViews = new CelestialBodyView[_bodies.Count];
            for (int i = 0; i < _bodies.Count; i++)
            {
                Color color = ResolveBodyColor(i);
                BodyTypeVisuals visuals = ResolveBodyVisuals(_bodies[i]);
                GameObject go = new GameObject(_bodies[i].Name);
                go.transform.SetParent(transform, false);
                CelestialBodyView view = go.AddComponent<CelestialBodyView>();
                view.Initialize(_bodies[i], color, visuals);
                _bodyViews[i] = view;
                _bodySceneObjects.Add(go);
            }

            DrawSoiRings();
            DrawCaptureRings();
        }

        private void CreatePersistentViews()
        {
            // Rocket view
            GameObject rocketGo = new GameObject("Rocket");
            rocketGo.transform.SetParent(transform, false);
            _rocketView = rocketGo.AddComponent<RocketView>();
            _rocketView.Initialize(_rocket, Color.white);

            // Trajectory view
            GameObject trajectoryGo = new GameObject("TrajectoryPreview");
            trajectoryGo.transform.SetParent(transform, false);
            _trajectoryView = trajectoryGo.AddComponent<TrajectoryView>();

            // AimController
            GameObject aimGo = new GameObject("AimController");
            aimGo.transform.SetParent(transform, false);
            _aimController = aimGo.AddComponent<AimController>();
            _aimController.Initialize(this, _trajectoryView);

            // Outcome display (its own canvas)
            GameObject uiGo = new GameObject("OutcomeDisplay");
            _outcomeDisplay = uiGo.AddComponent<OutcomeDisplay>();
        }

        private void DestroyBodySceneObjects()
        {
            foreach (GameObject go in _bodySceneObjects)
            {
                if (go != null) Destroy(go);
            }
            _bodySceneObjects.Clear();
            _bodyViews = null;
        }

        /// <summary>
        /// Colour lookup: procedural bodies carry type name; hard-coded bodies use BodyConfigs.
        /// </summary>
        private Color ResolveBodyColor(int bodyIndex)
        {
            CelestialBody body = _bodies[bodyIndex];

            if (_currentGalaxy != null && GalaxyParams != null)
            {
                // Home planets
                if (body.Id == _currentGalaxy.Player1HomeId || body.Id == _currentGalaxy.Player2HomeId)
                    return GalaxyParams.HomePlanetColor;

                // Match by type name
                foreach (BodyTypeDefinition type in GalaxyParams.BodyTypes)
                {
                    if (type.TypeName == body.Name)
                        return type.VisualColor;
                }

                return Color.white;
            }

            // Hard-coded fallback: use BodyConfigs colours
            if (bodyIndex < BodyConfigs.Length)
                return BodyConfigs[bodyIndex].BodyColor;

            return Color.white;
        }

        /// <summary>
        /// Returns the BodyTypeVisuals for a body. Home planets return HomeVisuals (if set);
        /// all others are looked up by TypeName in TypeVisuals. Returns null if none configured.
        /// </summary>
        private BodyTypeVisuals ResolveBodyVisuals(CelestialBody body)
        {
            if (GalaxyParams == null) return null;

            if (_currentGalaxy != null && GalaxyParams.HomeVisuals != null)
            {
                if (body.Id == _currentGalaxy.Player1HomeId || body.Id == _currentGalaxy.Player2HomeId)
                    return GalaxyParams.HomeVisuals;
            }

            if (GalaxyParams.TypeVisuals == null) return null;
            foreach (BodyTypeVisuals v in GalaxyParams.TypeVisuals)
            {
                if (v != null && v.TypeName == body.Name) return v;
            }
            return null;
        }

        private void DrawSoiRings()
        {
            foreach (CelestialBody body in _bodies)
            {
                GameObject go = new GameObject($"{body.Name}_SoiRing");
                go.transform.SetParent(transform, false);
                go.transform.position = new Vector3(body.Position.x, body.Position.y, 0.1f);
                _bodySceneObjects.Add(go);

                LineRenderer lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = false;
                lr.loop = true;
                lr.startWidth = 0.04f;
                lr.endWidth = 0.04f;
                lr.material = new Material(Shader.Find("Sprites/Default"));
                lr.startColor = new Color(1f, 1f, 1f, 0.12f);
                lr.endColor   = new Color(1f, 1f, 1f, 0.12f);

                const int segments = 64;
                lr.positionCount = segments;
                for (int i = 0; i < segments; i++)
                {
                    float angle = i / (float)segments * Mathf.PI * 2f;
                    lr.SetPosition(i, new Vector3(
                        Mathf.Cos(angle) * body.SoiRadius,
                        Mathf.Sin(angle) * body.SoiRadius,
                        0f));
                }
            }
        }

        private void DrawCaptureRings()
        {
            foreach (CelestialBody body in _bodies)
            {
                if (body.CaptureRingRadius <= 0f) continue;

                GameObject go = new GameObject($"{body.Name}_CaptureRing");
                go.transform.SetParent(transform, false);
                go.transform.position = new Vector3(body.Position.x, body.Position.y, 0.1f);
                _bodySceneObjects.Add(go);

                LineRenderer lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = false;
                lr.loop = true;
                lr.startWidth = 0.06f;
                lr.endWidth = 0.06f;
                lr.material = new Material(Shader.Find("Sprites/Default"));
                lr.startColor = new Color(1f, 0.85f, 0.1f, 0.35f);
                lr.endColor   = new Color(1f, 0.85f, 0.1f, 0.35f);

                const int segments = 64;
                lr.positionCount = segments;
                for (int i = 0; i < segments; i++)
                {
                    float angle = i / (float)segments * Mathf.PI * 2f;
                    lr.SetPosition(i, new Vector3(
                        Mathf.Cos(angle) * body.CaptureRingRadius,
                        Mathf.Sin(angle) * body.CaptureRingRadius,
                        0f));
                }
            }
        }

        private void HandleOutcome(Outcome outcome, int bodyId)
        {
            string bodyName = bodyId >= 0 && bodyId < _bodies.Count
                ? _bodies[bodyId].Name : "unknown";

            switch (outcome)
            {
                case Outcome.Crashed:
                    _rocket.Status = RocketStatus.Crashed;
                    if (!_turnManagedMode)
                        _outcomeDisplay.Show($"Crashed into {bodyName}\n\nPress R to reset");
                    break;
                case Outcome.Orbited:
                    _rocket.Status = RocketStatus.Orbited;
                    if (!_turnManagedMode)
                        _outcomeDisplay.Show($"Orbited {bodyName}!\n\nPress R to reset");
                    break;
                case Outcome.Escaped:
                    _rocket.Status = RocketStatus.Escaped;
                    if (!_turnManagedMode)
                        _outcomeDisplay.Show("Escaped to deep space\n\nPress R to reset");
                    break;
            }

            RocketResolved?.Invoke(outcome, bodyId);
        }

        private void ResetRocket()
        {
            BuildRocket();
            _rocketView.SetData(_rocket);
            System.Array.Clear(_wasInsideCaptureRing, 0, _wasInsideCaptureRing.Length);
            _outcomeDisplay.Hide();
        }

        /// <summary>
        /// Evaluate whether the rocket qualifies for orbital capture by a body.
        /// Called once, when the rocket crosses inbound through the body's CaptureRingRadius.
        /// Returns true if capture succeeded.
        /// </summary>
        private bool EvaluateCapture(CelestialBody body, float dist)
        {
            Vector2 relVelocity = _rocket.Velocity;
            float speed = relVelocity.magnitude;

            Vector2 radialDir = (_rocket.Position - body.Position) / dist;

            float radialDot = Vector2.Dot(relVelocity.normalized, radialDir);
            float angleBetweenVelAndRadial = Mathf.Acos(Mathf.Clamp(radialDot, -1f, 1f)) * Mathf.Rad2Deg;
            float angleFromTangent = Mathf.Abs(90f - angleBetweenVelAndRadial);

            bool speedOk = speed >= body.CaptureMinSpeed && speed <= body.CaptureMaxSpeed;
            bool angleOk = angleFromTangent <= body.CaptureAngleToleranceDegrees;

            Debug.Log($"[Capture {body.Name}] " +
                      $"speed={speed:F2} [{body.CaptureMinSpeed}-{body.CaptureMaxSpeed}] {(speedOk ? "PASS" : "FAIL")} | " +
                      $"angle={angleFromTangent:F1}° (tol {body.CaptureAngleToleranceDegrees}°) {(angleOk ? "PASS" : "FAIL")}");

            if (!speedOk || !angleOk) return false;

            float circularSpeed = Mathf.Sqrt(G * body.Mass / dist);

            Vector2 tangent = new Vector2(-radialDir.y, radialDir.x);
            int orbitDirection = Vector2.Dot(_rocket.Velocity, tangent) >= 0f ? 1 : -1;
            if (orbitDirection < 0) tangent = -tangent;

            _rocket.CapturedBodyId    = body.Id;
            _rocket.OrbitRadius       = dist;
            _rocket.OrbitAngle        = Mathf.Atan2(radialDir.y, radialDir.x);
            _rocket.OrbitAngularSpeed = circularSpeed / dist;
            _rocket.OrbitDirection    = orbitDirection;

            _rocket.Velocity = tangent * circularSpeed;
            HandleOutcome(Outcome.Orbited, body.Id);
            return true;
        }

        private void LogPhysicsStep()
        {
            Vector2 totalForce = Vector2.zero;
            System.Text.StringBuilder perBody = new System.Text.StringBuilder();
            foreach (CelestialBody b in _bodies)
            {
                Vector2 offset = b.Position - _rocket.Position;
                float distSq = offset.sqrMagnitude;
                if (distSq < 0.01f) { perBody.Append($"  {b.Name}=SKIP"); continue; }
                float forceMag = G * b.Mass * _rocket.Mass / distSq;
                totalForce += (offset / Mathf.Sqrt(distSq)) * forceMag;
                perBody.Append($"  {b.Name}={forceMag:F4}");
            }
            Debug.Log($"[Phys t={_rocket.TimeInFlight:F2}] pos={_rocket.Position} totalF={totalForce.magnitude:F4} |{perBody}");
        }
    }
}
