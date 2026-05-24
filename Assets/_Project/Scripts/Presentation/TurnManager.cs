using System.Collections.Generic;
using Orbital.Combat;
using Orbital.Physics;
using Orbital.Strategy;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Orbital.Presentation
{
    /// <summary>
    /// MonoBehaviour coordinator for the Phase 3 two-player hot-seat game loop.
    ///
    /// Created programmatically by PrototypeScenarioController.Start().
    /// Do NOT add manually to the scene — PSC bootstraps it.
    ///
    /// Rocket production (Classic mode):
    ///   Each turn the active player receives launch sites calculated by
    ///   LaunchSiteCalculator: 1 home rocket + floor(nonHomeCaptured / 2) bonus
    ///   rockets on randomly selected captured planets (seeded deterministically).
    ///   After each rocket resolves the player fires the next one; pressing Enter
    ///   (or the End Turn button) forfeits remaining rockets and passes the turn.
    ///
    /// Flow:
    ///   Initialize / NewGame
    ///     → BeginGame  →  BetweenTurns
    ///     → [Space]    →  WaitingForLaunch  (StartTurn: sites calculated, views spawned)
    ///     → [click site] → SelectLaunchSite (updates rocket + AimController)
    ///     → [fire]     →  RocketInFlight
    ///     → [resolve]  →  if more sites: WaitingForLaunch (next rocket)
    ///                     else: AdvanceToNextPlayer → BetweenTurns
    ///     → [Enter / button] EndTurn → AdvanceToNextPlayer
    /// </summary>
    public class TurnManager : MonoBehaviour
    {
        // -------------------------------------------------------------------------
        //  Strategy parameters
        // -------------------------------------------------------------------------

        [Header("Strategy")]
        [SerializeField] private StrategyParameters _strategyParams;
        public StrategyParameters StrategyParams
        {
            get => _strategyParams;
            set => _strategyParams = value;
        }

        // -------------------------------------------------------------------------
        //  Player default colors
        // -------------------------------------------------------------------------

        private static readonly Color Player1Color = new Color(0.25f, 0.55f, 1f);
        private static readonly Color Player2Color = new Color(1f, 0.25f, 0.25f);

        // -------------------------------------------------------------------------
        //  State exposed to AimController
        // -------------------------------------------------------------------------

        public GameState GameState => _gameState;

        // -------------------------------------------------------------------------
        //  Private state
        // -------------------------------------------------------------------------

        private PrototypeScenarioController _psc;
        private GameState _gameState;
        private TurnUI _turnUI;
        private WinScreenUI _winScreen;

        private readonly Dictionary<int, PlanetOwnershipView> _ownershipViews
            = new Dictionary<int, PlanetOwnershipView>();

        private readonly Dictionary<int, OrbitingRocketView> _orbitingRockets
            = new Dictionary<int, OrbitingRocketView>();

        private readonly Dictionary<int, LaunchSiteView> _launchSiteViews
            = new Dictionary<int, LaunchSiteView>();

        private readonly Dictionary<int, PlanetPopulationView> _planetPopulationViews
            = new Dictionary<int, PlanetPopulationView>();

        private Canvas _popLabelCanvas;

        private LoadingUI _loadingUI;
        private RocketPassengerLabel _rocketPassengerLabel;



        // -------------------------------------------------------------------------
        //  Unity messages
        // -------------------------------------------------------------------------

        private void Awake()
        {
            // Create UI elements immediately so they exist before any game state arrives.
            // Everything else is wired in Initialize().
            GameObject uiGo = new GameObject("TurnUI");
            _turnUI = uiGo.AddComponent<TurnUI>();
            _turnUI.OnEndTurn += EndTurn;

            GameObject winGo = new GameObject("WinScreenUI");
            _winScreen = winGo.AddComponent<WinScreenUI>();
            _winScreen.OnNewGame += NewGame;

            GameObject loadGo = new GameObject("LoadingUI");
            _loadingUI = loadGo.AddComponent<LoadingUI>();
            _loadingUI.Hide();
        }

        // Start() intentionally omitted — PSC drives startup via Initialize().

        private void Update()
        {
            if (_gameState == null || Keyboard.current == null) return;

            if (_gameState.Phase == GamePhase.BetweenTurns
                && Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                StartTurn();
            }

            if (_gameState.Phase == GamePhase.WaitingForLaunch
                && Keyboard.current.enterKey.wasPressedThisFrame)
            {
                EndTurn();
            }

            if (Keyboard.current.nKey.wasPressedThisFrame)
                NewGame();
        }

        // -------------------------------------------------------------------------
        //  Bootstrap entry point — called by PSC.Start()
        // -------------------------------------------------------------------------

        /// <summary>
        /// Wire PSC events and begin the first game using the galaxy PSC already built.
        /// Must be called exactly once, immediately after AddComponent.
        /// </summary>
        public void Initialize(PrototypeScenarioController psc)
        {
            _psc = psc;
            _psc.RocketLaunched    += HandleRocketLaunched;
            _psc.RocketResolved    += HandleRocketResolved;
            // GalaxyRegenerated fires whenever PSC.RegenerateGalaxy() completes,
            // regardless of who called it (TurnManager.NewGame OR GalaxyVisualizer G/B).
            // This is the single point that resets all per-galaxy game state.
            _psc.GalaxyRegenerated += OnGalaxyRegenerated;

            // First boot: PSC already built a galaxy in Awake; no regen event fires,
            // so we initialise game state directly here.
            BeginGame();
        }

        // -------------------------------------------------------------------------
        //  Public game flow
        // -------------------------------------------------------------------------

        /// <summary>
        /// Regenerate the galaxy with a fresh seed and restart the game.
        /// Called by the New Game button and the N hotkey.
        /// BeginGame() is triggered automatically via the GalaxyRegenerated event —
        /// do NOT call it here as well, or it would run twice.
        /// </summary>
        public void NewGame()
        {
            if (_psc == null) return;

            if (_psc.GalaxyParams != null)
            {
                // UnityEngine.Random acceptable here (presentation-layer seed selection).
                int seed = UnityEngine.Random.Range(1, int.MaxValue);
                _psc.RegenerateGalaxy(seed, _psc.GalaxyParams);
                // BeginGame() fires via GalaxyRegenerated event inside RegenerateGalaxy.
            }
            else
            {
                // No GalaxyParams → can't regenerate; still reset state with current galaxy.
                BeginGame();
            }
        }

        private void OnGalaxyRegenerated()
        {
            BeginGame();
        }

        /// <summary>
        /// Calculate this turn's launch sites, spawn their views, position the rocket
        /// at the home site, and enter WaitingForLaunch. Called when the player presses Space.
        /// </summary>
        public void StartTurn()
        {
            if (_gameState == null) return;

            _gameState.Phase = GamePhase.WaitingForLaunch;

            Player current = _gameState.CurrentPlayer;

            // Calculate available launch sites for this turn.
            _gameState.AvailableLaunchSites = LaunchSiteCalculator.Calculate(_gameState, current.Id);

            // Spawn a LaunchSiteView on every available site.
            ClearLaunchSiteViews();
            foreach (int siteId in _gameState.AvailableLaunchSites)
            {
                CelestialBody body = _psc.GetBodyById(siteId);
                if (body == null) continue;

                GameObject go = new GameObject($"LaunchSiteView_{siteId}");
                LaunchSiteView view = go.AddComponent<LaunchSiteView>();
                view.Initialize(body, current.Color);
                view.Selected += HandleSiteSelected;
                _launchSiteViews[siteId] = view;
            }

            // Set each site's slider max to the planet's current population, reset to 0.
            foreach (int siteId in _gameState.AvailableLaunchSites)
            {
                if (_launchSiteViews.TryGetValue(siteId, out LaunchSiteView v) && v != null)
                {
                    int pop = _gameState.Population.TryGetValue(siteId, out int p) ? p : 0;
                    v.SetMax(pop);
                    v.ResetLoad();
                }
            }

            // Default active site is home.
            SelectLaunchSite(current.HomeBodyId);

            _turnUI.Show(_gameState);
            _turnUI.ShowPositioningHint();
        }

        /// <summary>
        /// Forfeit remaining rockets for this turn and pass to the next player.
        /// Called by the End Turn button, the Enter hotkey, or internally when
        /// all rockets have been fired.
        /// </summary>
        public void EndTurn()
        {
            if (_gameState == null || _gameState.Phase != GamePhase.WaitingForLaunch) return;
            _gameState.AvailableLaunchSites.Clear();
            ClearLaunchSiteViews();
            _loadingUI?.Hide();
            AdvanceToNextPlayer();
        }

        public void EndGame(int winnerId)
        {
            _gameState.Phase    = GamePhase.GameOver;
            _gameState.WinnerId = winnerId;
            ClearLaunchSiteViews();
            _loadingUI?.Hide();
            _winScreen.Show(_gameState.GetPlayer(winnerId));
            _turnUI.Show(_gameState);
        }

        // -------------------------------------------------------------------------
        //  Event handlers (fired by PSC)
        // -------------------------------------------------------------------------

        private void HandleRocketLaunched()
        {
            if (_gameState == null) return;

            int siteId = _gameState.ActiveLaunchSiteId;

            int load = 0;
            if (_launchSiteViews.TryGetValue(siteId, out LaunchSiteView siteView) && siteView != null)
                load = siteView.CurrentLoad;

            int siteAvailable = _gameState.Population.TryGetValue(siteId, out int sitePop) ? sitePop : 0;
            if (load > siteAvailable) load = siteAvailable;

            _psc.Rocket.PassengerCount = load;
            if (load > 0)
                _gameState.Population[siteId] = siteAvailable - load;

            _loadingUI?.Hide();
            RefreshPlanetPopulationViews();

            _gameState.Phase = GamePhase.RocketInFlight;
            _turnUI.Show(_gameState);
        }

        private void HandleRocketResolved(Outcome outcome, int capturedBodyId)
        {
            if (_gameState == null) return;

            if (outcome == Outcome.Orbited && capturedBodyId >= 0)
            {
                int passengers = _psc.Rocket.PassengerCount;
                Player current = _gameState.CurrentPlayer;

                int baseDur  = _strategyParams != null ? _strategyParams.ColonisationBaseDuration : 20;
                int minTurns = _strategyParams != null ? _strategyParams.MinColonisationTurns : 1;

                ColonisationChange change = ColonisationResolver.Resolve(
                    _gameState, current.Id, capturedBodyId, passengers, baseDur, minTurns);

                ApplyColonisationChange(change);

                // OrbitingRocketView is spawned at colonisation completion, not here.
                RefreshPlanetPopulationViews();
            }

            int? winnerId = WinConditionChecker.CheckForWin(_gameState);
            if (winnerId.HasValue)
            {
                EndGame(winnerId.Value);
                return;
            }

            // Remove the launch site that was just used.
            int usedSite = _gameState.ActiveLaunchSiteId;
            _gameState.AvailableLaunchSites.Remove(usedSite);
            DestroyLaunchSiteView(usedSite);

            if (_gameState.AvailableLaunchSites.Count > 0)
            {
                // More rockets remain — stay on this player's turn, auto-select first.
                int nextSite = _gameState.AvailableLaunchSites[0];
                SelectLaunchSite(nextSite);
                _gameState.Phase = GamePhase.WaitingForLaunch;
                _turnUI.Show(_gameState);
                // Hint text: show a brief "select or fire" instruction.
                _turnUI.ShowPositioningHint();
            }
            else
            {
                // All rockets used — pass to next player.
                ClearLaunchSiteViews();
                AdvanceToNextPlayer();
            }
        }

        // -------------------------------------------------------------------------
        //  Private helpers — launch site management
        // -------------------------------------------------------------------------

        /// <summary>
        /// Make bodyId the active launch site: update PSC skip flags, reposition
        /// rocket, update AimController, and refresh highlight on all site views.
        /// </summary>
        private void SelectLaunchSite(int bodyId)
        {
            _gameState.ActiveLaunchSiteId = bodyId;

            Player current = _gameState.CurrentPlayer;

            // PSC: skip the launch site for capture detection (prevents immediate re-capture).
            _psc.SetActiveLaunchSite(bodyId);

            // Position the rocket at the selected site.
            _psc.PrepareRocketForPlayer(bodyId);

            // Update AimController for the new surface reference planet.
            CelestialBody siteBody = _psc.GetBodyById(bodyId);
            _psc.AimController?.SetPlayerColor(current.Color);
            _psc.AimController?.SetHomePlanet(siteBody);
            _psc.AimController?.CancelDrag();

            // Refresh highlights: active site gets white ring, others are dim.
            foreach (KeyValuePair<int, LaunchSiteView> kv in _launchSiteViews)
                kv.Value.SetActive(kv.Key == bodyId);

            // Per-site sliders are always visible via LaunchSiteView; shared LoadingUI stays hidden.
            _loadingUI?.Hide();

            // Re-initialize the in-flight label so it tracks the freshly prepared rocket.
            _rocketPassengerLabel?.Initialize(_psc.Rocket, current.Color);
        }

        private void HandleSiteSelected(int bodyId)
        {
            if (_gameState?.Phase != GamePhase.WaitingForLaunch) return;
            if (!_gameState.AvailableLaunchSites.Contains(bodyId)) return;
            SelectLaunchSite(bodyId);
        }

        private void DestroyLaunchSiteView(int bodyId)
        {
            if (_launchSiteViews.TryGetValue(bodyId, out LaunchSiteView view))
            {
                if (view != null) Destroy(view.gameObject);
                _launchSiteViews.Remove(bodyId);
            }
        }

        private void ClearLaunchSiteViews()
        {
            foreach (LaunchSiteView v in _launchSiteViews.Values)
                if (v != null) Destroy(v.gameObject);
            _launchSiteViews.Clear();
        }

        // -------------------------------------------------------------------------
        //  Private helpers — player advancement
        // -------------------------------------------------------------------------

        private void AdvanceToNextPlayer()
        {
            // 1. Tick colonisations (before player flip).
            List<ColonisationTicker.Completion> completions = ColonisationTicker.Tick(_gameState);
            foreach (ColonisationTicker.Completion c in completions)
                ApplyColonisationCompletion(c);

            // 2. Tick contests.
            int dmgDivisor = _strategyParams != null ? _strategyParams.ContestDamageDivisor : 5;
            int minDmg     = _strategyParams != null ? _strategyParams.ContestMinDamage : 1;
            List<ContestTicker.Result> contestResults = ContestTicker.Tick(_gameState, dmgDivisor, minDmg);
            foreach (ContestTicker.Result r in contestResults)
                ApplyContestResult(r);

            // 3. Refresh views if anything changed.
            if (completions.Count > 0 || contestResults.Count > 0)
            {
                RefreshOwnershipViews();
                RefreshPlanetPopulationViews();
            }

            // 4. Win check (now active — enemy home can be captured via contest).
            int? winnerId = WinConditionChecker.CheckForWin(_gameState);
            if (winnerId.HasValue) { EndGame(winnerId.Value); return; }

            // 5. Flip player and increment turn.
            _gameState.CurrentPlayerId = _gameState.CurrentPlayerId == 1 ? 2 : 1;
            _gameState.TurnNumber++;

            // 6. Grow population on every planet the new current player owns.
            GrowOwnedPlanetPopulations(_gameState.CurrentPlayerId);
            RefreshPlanetPopulationViews();

            _loadingUI?.Hide();
            _gameState.Phase = GamePhase.BetweenTurns;
            _turnUI.Show(_gameState);
        }

        // -------------------------------------------------------------------------
        //  Private helpers — game initialisation
        // -------------------------------------------------------------------------

        /// <summary>
        /// Initialise game state from the galaxy PSC currently holds.
        /// Does NOT regenerate the galaxy — call after PSC has a valid CurrentGalaxy.
        /// </summary>
        private void BeginGame()
        {
            if (_psc?.CurrentGalaxy == null)
            {
                Debug.LogError("[TurnManager] BeginGame: no galaxy available on PSC.");
                return;
            }

            ClearLaunchSiteViews();
            ClearOrbitingRocketViews();
            ClearOwnershipViews();
            ClearPlanetPopulationViews();

            int p1Home = _psc.CurrentGalaxy.Player1HomeId;
            int p2Home = _psc.CurrentGalaxy.Player2HomeId;

            Player p1 = new Player(1, "Player 1", Player1Color, p1Home);
            Player p2 = new Player(2, "Player 2", Player2Color, p2Home);

            _gameState = new GameState(new List<Player> { p1, p2 });
            _gameState.TurnNumber      = 1;
            _gameState.CurrentPlayerId = p1.Id;

            int start = _strategyParams != null ? _strategyParams.StartingPopulation : 0;
            _gameState.Population.Clear();
            _gameState.Population[p1Home] = start;
            _gameState.Population[p2Home] = start;

            _gameState.Ownership[p1Home] = new PlanetOwnership
                { OwnerPlayerId = p1.Id, OrbitingRocketId = -1 };
            _gameState.Ownership[p2Home] = new PlanetOwnership
                { OwnerPlayerId = p2.Id, OrbitingRocketId = -1 };
            _gameState.Colonisations.Clear();
            _gameState.Contests.Clear();

            foreach (CelestialBody body in _psc.Bodies)
            {
                GameObject go = new GameObject($"OwnershipView_{body.Id}");
                PlanetOwnershipView view = go.AddComponent<PlanetOwnershipView>();
                view.Initialize(body);
                _ownershipViews[body.Id] = view;
            }
            RefreshOwnershipViews();

            // Create the shared canvas for population labels.
            GameObject canvasGo = new GameObject("PopLabelCanvas");
            Canvas popCanvas = canvasGo.AddComponent<Canvas>();
            popCanvas.renderMode  = RenderMode.ScreenSpaceOverlay;
            popCanvas.sortingOrder = 5;
            canvasGo.AddComponent<CanvasScaler>();
            _popLabelCanvas = popCanvas;

            RefreshPlanetPopulationViews();

            // Create the passenger label once; re-initialize each BeginGame to pick up
            // the fresh rocket reference after galaxy regeneration.
            if (_rocketPassengerLabel == null)
            {
                GameObject labelGo = new GameObject("RocketPassengerLabel");
                _rocketPassengerLabel = labelGo.AddComponent<RocketPassengerLabel>();
            }
            _rocketPassengerLabel.Initialize(_psc.Rocket, Color.white);

            _psc.SetTurnManagedMode(true);
            _winScreen.Hide();

            // Start in BetweenTurns so Space must be pressed before Player 1 can aim.
            _gameState.Phase = GamePhase.BetweenTurns;
            _turnUI.Show(_gameState);
        }

        // -------------------------------------------------------------------------
        //  Private helpers — colonisation
        // -------------------------------------------------------------------------

        private void ApplyColonisationChange(ColonisationChange change)
        {
            switch (change.Outcome)
            {
                case ColonisationOutcome.Started:
                    _gameState.Colonisations[change.BodyId] = new Colonisation
                    {
                        PlayerId       = change.PlayerId,
                        TurnsRemaining = change.NewTurnsRemaining
                    };
                    _gameState.Population[change.BodyId] = change.NewColonistCount;
                    break;

                case ColonisationOutcome.Reinforced:
                    if (_gameState.Colonisations.TryGetValue(change.BodyId, out Colonisation col))
                        col.TurnsRemaining = change.NewTurnsRemaining;
                    _gameState.Population[change.BodyId] = change.NewColonistCount;
                    break;

                case ColonisationOutcome.ReinforceContest_Defender:
                    _gameState.Population[change.BodyId] = change.NewColonistCount;
                    break;

                case ColonisationOutcome.ReinforceContest_Invader:
                    if (_gameState.Contests.TryGetValue(change.BodyId, out Contest contest))
                        contest.InvaderCount = change.NewColonistCount;
                    break;

                case ColonisationOutcome.StartContest:
                    _gameState.Contests[change.BodyId] = new Contest
                    {
                        InvaderPlayerId = change.PlayerId,
                        InvaderCount    = change.PassengersDeployed
                    };
                    break;

                case ColonisationOutcome.Blocked:
                case ColonisationOutcome.NoOp:
                default:
                    break;
            }
        }

        /// <summary>
        /// Called by AdvanceToNextPlayer after ColonisationTicker removes a completed
        /// entry. Builds a PlanetOwnership using the stored orbit params from the
        /// existing OrbitingRocketView (or sensible fallback defaults).
        /// </summary>
        private void ApplyColonisationCompletion(ColonisationTicker.Completion c)
        {
            PlanetOwnership ownership;

            // In Jump 4 the OrbitingRocketView is no longer spawned during colonisation,
            // so the TryGetValue branch is a safety fallback for any edge cases.
            if (_orbitingRockets.TryGetValue(c.BodyId, out OrbitingRocketView existingView)
                && existingView != null)
            {
                ownership = new PlanetOwnership
                {
                    OwnerPlayerId    = c.PlayerId,
                    OrbitingRocketId = 0,
                    OrbitRadius      = existingView.OrbitRadius,
                    OrbitAngle       = existingView.OrbitAngle,
                    OrbitAngularSpeed = existingView.OrbitAngularSpeed,
                    OrbitDirection   = existingView.OrbitDirection
                };
            }
            else
            {
                // No orbiting rocket view — generate fallback circular orbit params.
                CelestialBody body = _psc.GetBodyById(c.BodyId);
                float radius = body != null ? body.Radius * 2f : 2f;
                float mass   = body != null ? body.Mass : 1f;
                float speed  = Mathf.Sqrt(_psc.G * mass / radius);
                ownership = new PlanetOwnership
                {
                    OwnerPlayerId    = c.PlayerId,
                    OrbitingRocketId = 0,
                    OrbitRadius      = radius,
                    OrbitAngle       = 0f,
                    OrbitAngularSpeed = speed / radius,
                    OrbitDirection   = 1
                };
            }

            _gameState.Ownership[c.BodyId] = ownership;
            // Population[c.BodyId] already holds the correct count (set at Started/Reinforced).

            Player owner = _gameState.GetPlayer(c.PlayerId);
            Color ownerColor = owner?.Color ?? Color.white;
            SpawnOrReplaceOrbitingRocketView(c.BodyId, ownership, ownerColor);
            RefreshPlanetPopulationViews();
        }

        /// <summary>
        /// Destroy any existing OrbitingRocketView on this body and spawn a new one
        /// using the supplied orbit parameters. Only called at colonisation completion.
        /// </summary>
        private void SpawnOrReplaceOrbitingRocketView(int bodyId, PlanetOwnership orbitParams,
                                                      Color playerColor)
        {
            if (_orbitingRockets.TryGetValue(bodyId, out OrbitingRocketView old) && old != null)
            {
                Destroy(old.gameObject);
                _orbitingRockets.Remove(bodyId);
            }

            CelestialBody body = _psc.GetBodyById(bodyId);
            if (body == null) return;

            GameObject go = new GameObject($"OrbitingRocket_{bodyId}");
            OrbitingRocketView view = go.AddComponent<OrbitingRocketView>();
            view.Initialize(body, orbitParams, playerColor);
            _orbitingRockets[bodyId] = view;
        }

        // -------------------------------------------------------------------------
        //  Private helpers — ownership
        // -------------------------------------------------------------------------

        private void RefreshOwnershipViews()
        {
            foreach (KeyValuePair<int, PlanetOwnershipView> kv in _ownershipViews)
            {
                if (_gameState.Ownership.TryGetValue(kv.Key, out PlanetOwnership ownership))
                    kv.Value.SetOwner(_gameState.GetPlayer(ownership.OwnerPlayerId)?.Color);
                else
                    kv.Value.SetOwner(null);
            }
        }

        private void ClearOrbitingRocketViews()
        {
            foreach (OrbitingRocketView v in _orbitingRockets.Values)
                if (v != null) Destroy(v.gameObject);
            _orbitingRockets.Clear();
        }

        private void ClearOwnershipViews()
        {
            foreach (PlanetOwnershipView v in _ownershipViews.Values)
                if (v != null) Destroy(v.gameObject);
            _ownershipViews.Clear();
        }

        private void ClearPlanetPopulationViews()
        {
            foreach (PlanetPopulationView v in _planetPopulationViews.Values)
                if (v != null) Destroy(v.gameObject);
            _planetPopulationViews.Clear();

            if (_popLabelCanvas != null)
            {
                Destroy(_popLabelCanvas.gameObject);
                _popLabelCanvas = null;
            }
        }

        private void RefreshPlanetPopulationViews()
        {
            if (_popLabelCanvas == null || _gameState == null) return;

            // Create views for any body that has displayable state (owned, colonising, or contested).
            HashSet<int> activeIds = CollectBodiesWithState();
            foreach (int bodyId in activeIds)
            {
                if (_planetPopulationViews.ContainsKey(bodyId)) continue;

                CelestialBody body = _psc.GetBodyById(bodyId);
                if (body == null) continue;

                GameObject go = new GameObject($"PlanetPopulationView_{bodyId}");
                PlanetPopulationView view = go.AddComponent<PlanetPopulationView>();
                view.Initialize(body, _popLabelCanvas, this);
                _planetPopulationViews[bodyId] = view;
            }

            // Destroy views for bodies that no longer have any displayable state.
            List<int> toRemove = new List<int>();
            foreach (int bodyId in _planetPopulationViews.Keys)
            {
                if (!activeIds.Contains(bodyId))
                    toRemove.Add(bodyId);
            }
            foreach (int bodyId in toRemove)
            {
                if (_planetPopulationViews.TryGetValue(bodyId, out PlanetPopulationView v) && v != null)
                    Destroy(v.gameObject);
                _planetPopulationViews.Remove(bodyId);
            }
        }

        private HashSet<int> CollectBodiesWithState()
        {
            HashSet<int> ids = new HashSet<int>();
            foreach (int id in _gameState.Ownership.Keys)     ids.Add(id);
            foreach (int id in _gameState.Colonisations.Keys) ids.Add(id);
            foreach (int id in _gameState.Contests.Keys)      ids.Add(id);
            return ids;
        }

        private void ApplyContestResult(ContestTicker.Result r)
        {
            switch (r.Resolution)
            {
                case ContestTicker.Resolution.DefenderWins:
                    // Population[bodyId] already updated by ContestTicker.
                    // Ownership is unchanged. No view changes needed.
                    break;

                case ContestTicker.Resolution.InvaderWins:
                {
                    // Build new ownership for the invader using current orbit params (or fallback).
                    PlanetOwnership ownership;
                    if (_orbitingRockets.TryGetValue(r.BodyId, out OrbitingRocketView existingView)
                        && existingView != null)
                    {
                        ownership = new PlanetOwnership
                        {
                            OwnerPlayerId    = r.InvaderPlayerId,
                            OrbitingRocketId = 0,
                            OrbitRadius      = existingView.OrbitRadius,
                            OrbitAngle       = existingView.OrbitAngle,
                            OrbitAngularSpeed = existingView.OrbitAngularSpeed,
                            OrbitDirection   = existingView.OrbitDirection
                        };
                    }
                    else
                    {
                        CelestialBody body = _psc.GetBodyById(r.BodyId);
                        float radius = body != null ? body.Radius * 2f : 2f;
                        float mass   = body != null ? body.Mass : 1f;
                        float speed  = Mathf.Sqrt(_psc.G * mass / radius);
                        ownership = new PlanetOwnership
                        {
                            OwnerPlayerId    = r.InvaderPlayerId,
                            OrbitingRocketId = 0,
                            OrbitRadius      = radius,
                            OrbitAngle       = 0f,
                            OrbitAngularSpeed = speed / radius,
                            OrbitDirection   = 1
                        };
                    }

                    _gameState.Ownership[r.BodyId]    = ownership;
                    _gameState.Population[r.BodyId]   = r.FinalInvaderCount;
                    _gameState.Colonisations.Remove(r.BodyId); // cancel any in-progress colonisation

                    Player invader     = _gameState.GetPlayer(r.InvaderPlayerId);
                    Color  invaderColor = invader?.Color ?? Color.white;
                    SpawnOrReplaceOrbitingRocketView(r.BodyId, ownership, invaderColor);
                    break;
                }

                case ContestTicker.Resolution.MutualAnnihilation:
                {
                    _gameState.Ownership.Remove(r.BodyId);
                    _gameState.Colonisations.Remove(r.BodyId);
                    _gameState.Population.Remove(r.BodyId);

                    if (_orbitingRockets.TryGetValue(r.BodyId, out OrbitingRocketView view)
                        && view != null)
                    {
                        Destroy(view.gameObject);
                        _orbitingRockets.Remove(r.BodyId);
                    }
                    break;
                }
            }
        }

        private void GrowOwnedPlanetPopulations(int playerId)
        {
            if (_strategyParams == null) return;
            int homeRate     = _strategyParams.PopulationGrowthPerTurn;
            int divisor      = Mathf.Max(1, _strategyParams.CapturedPlanetGrowthDivisor);
            int capturedRate = homeRate / divisor;

            Player p = _gameState.GetPlayer(playerId);
            if (p == null) return;

            foreach (KeyValuePair<int, PlanetOwnership> kv in _gameState.Ownership)
            {
                if (kv.Value.OwnerPlayerId != playerId) continue;

                bool isHome  = kv.Key == p.HomeBodyId;
                int  amount  = isHome ? homeRate : capturedRate;
                int existing = _gameState.Population.TryGetValue(kv.Key, out int v) ? v : 0;
                _gameState.Population[kv.Key] = existing + amount;
            }
        }

    }
}
