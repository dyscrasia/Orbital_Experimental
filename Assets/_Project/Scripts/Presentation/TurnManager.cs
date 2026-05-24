using System.Collections.Generic;
using Orbital.Combat;
using Orbital.Physics;
using Orbital.Strategy;
using UnityEngine;
using UnityEngine.InputSystem;

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

        private int _nextRocketViewId;

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
            AdvanceToNextPlayer();
        }

        public void EndGame(int winnerId)
        {
            _gameState.Phase    = GamePhase.GameOver;
            _gameState.WinnerId = winnerId;
            ClearLaunchSiteViews();
            _winScreen.Show(_gameState.GetPlayer(winnerId));
            _turnUI.Show(_gameState);
        }

        // -------------------------------------------------------------------------
        //  Event handlers (fired by PSC)
        // -------------------------------------------------------------------------

        private void HandleRocketLaunched()
        {
            if (_gameState == null) return;
            _gameState.Phase = GamePhase.RocketInFlight;
            _turnUI.Show(_gameState);
        }

        private void HandleRocketResolved(Outcome outcome, int capturedBodyId)
        {
            if (_gameState == null) return;

            if (outcome == Outcome.Orbited && capturedBodyId >= 0)
            {
                OwnershipChange change = OwnershipResolver.ResolveCapture(
                    _gameState, _gameState.CurrentPlayerId, capturedBodyId, _psc.Rocket);

                if (change != null)
                {
                    ApplyOwnershipChange(change, capturedBodyId);
                    RefreshOwnershipViews();
                }
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
            _gameState.CurrentPlayerId = _gameState.CurrentPlayerId == 1 ? 2 : 1;
            _gameState.TurnNumber++;
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

            _nextRocketViewId = 0;

            int p1Home = _psc.CurrentGalaxy.Player1HomeId;
            int p2Home = _psc.CurrentGalaxy.Player2HomeId;

            Player p1 = new Player(1, "Player 1", Player1Color, p1Home);
            Player p2 = new Player(2, "Player 2", Player2Color, p2Home);

            _gameState = new GameState(new List<Player> { p1, p2 });
            _gameState.TurnNumber      = 1;
            _gameState.CurrentPlayerId = p1.Id;

            _gameState.Ownership[p1Home] = new PlanetOwnership
                { OwnerPlayerId = p1.Id, OrbitingRocketId = -1 };
            _gameState.Ownership[p2Home] = new PlanetOwnership
                { OwnerPlayerId = p2.Id, OrbitingRocketId = -1 };

            foreach (CelestialBody body in _psc.Bodies)
            {
                GameObject go = new GameObject($"OwnershipView_{body.Id}");
                PlanetOwnershipView view = go.AddComponent<PlanetOwnershipView>();
                view.Initialize(body);
                _ownershipViews[body.Id] = view;
            }
            RefreshOwnershipViews();

            _psc.SetTurnManagedMode(true);
            _winScreen.Hide();

            // Start in BetweenTurns so Space must be pressed before Player 1 can aim.
            _gameState.Phase = GamePhase.BetweenTurns;
            _turnUI.Show(_gameState);
        }

        // -------------------------------------------------------------------------
        //  Private helpers — ownership
        // -------------------------------------------------------------------------

        private void ApplyOwnershipChange(OwnershipChange change, int capturedBodyId)
        {
            if (change.DislodgedExistingRocket
                && _orbitingRockets.TryGetValue(capturedBodyId, out OrbitingRocketView old))
            {
                if (old != null) Destroy(old.gameObject);
                _orbitingRockets.Remove(capturedBodyId);
            }

            RocketState rocket = _psc.Rocket;
            int rocketViewId = _nextRocketViewId++;
            _gameState.Ownership[capturedBodyId] = new PlanetOwnership
            {
                OwnerPlayerId     = change.NewOwnerId,
                OrbitingRocketId  = rocketViewId,
                OrbitRadius       = rocket.OrbitRadius,
                OrbitAngle        = rocket.OrbitAngle,
                OrbitAngularSpeed = rocket.OrbitAngularSpeed,
                OrbitDirection    = rocket.OrbitDirection
            };

            CelestialBody body = _psc.GetBodyById(capturedBodyId);
            Player owner       = _gameState.GetPlayer(change.NewOwnerId);
            if (body != null && owner != null)
            {
                GameObject go = new GameObject($"OrbitingRocket_{capturedBodyId}");
                OrbitingRocketView view = go.AddComponent<OrbitingRocketView>();
                view.Initialize(body, _gameState.Ownership[capturedBodyId], owner.Color);
                _orbitingRockets[capturedBodyId] = view;
            }
        }

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

    }
}
