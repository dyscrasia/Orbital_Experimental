# Orbital — Phase 3 Implementation Notes

## What was built

### New files

| File | Purpose |
|---|---|
| `Scripts/Strategy/Player.cs` | Pure data class: `int Id`, `string Name`, `Color Color`, `int HomeBodyId` |
| `Scripts/Strategy/PlanetOwnership.cs` | Pure data class: owner ID, orbit params, and view-lookup `OrbitingRocketId` |
| `Scripts/Strategy/GameState.cs` | Pure data class with `GamePhase` enum, Ownership dictionary, helpers `CurrentPlayer` / `GetPlayer` / `GetPlayerPlanetCount` |
| `Scripts/Combat/OwnershipChange.cs` | Result type returned by `OwnershipResolver` |
| `Scripts/Combat/OwnershipResolver.cs` | Pure static resolver; returns null for own-home no-op, otherwise returns who owns what after a capture |
| `Scripts/Combat/WinConditionChecker.cs` | Pure static checker; returns winning player ID if any player owns the enemy home |
| `Scripts/Presentation/PlanetOwnershipView.cs` | `MonoBehaviour` — colored LineRenderer ring halo drawn around an owned planet |
| `Scripts/Presentation/OrbitingRocketView.cs` | `MonoBehaviour` — small triangle that advances kinematically (Time.deltaTime) around a captured body |
| `Scripts/Presentation/TurnUI.cs` | `MonoBehaviour` — procedural Canvas with top-center turn header, top-left/right player info, bottom-center phase prompt |
| `Scripts/Presentation/WinScreenUI.cs` | `MonoBehaviour` — full-screen semi-transparent overlay with win text and New Game button |
| `Scripts/Presentation/TurnManager.cs` | `MonoBehaviour` coordinator; bootstrapped by PSC.Start() via `AddComponent` + `Initialize(psc)` |
| `Tests/OwnershipResolverTests.cs` | NUnit tests: fresh capture, enemy dislodge, same-player re-capture, own-home no-op, enemy-home capture, invalid player |
| `Tests/WinConditionCheckerTests.cs` | NUnit tests: no win, P1 wins on P2 home, P2 wins on P1 home, neutral ownership, unowned enemy home |

### Modified files

**`PrototypeScenarioController.cs`**
- Added `public event System.Action RocketLaunched` — fires from `LaunchRocket()`
- Added `public event System.Action<Outcome, int> RocketResolved` — fires from `HandleOutcome()`
- Added `_turnManagedMode` bool; when true: suppresses `OutcomeDisplay` messages and R-to-reset
- Added `_activeHomeBodyId` int; the currently-active-player's home body. Capture detection skips this body instead of the hard-coded index.
- Added `public AimController AimController` property for TurnManager
- Added `SetTurnManagedMode(bool)`, `SetActiveHome(int)`, `PrepareRocketForPlayer(int)`, `GetBodyById(int)` public methods
- Changed capture skip from `i == _homeBodyId` to `body.Id == _activeHomeBodyId` so TurnManager can dynamically change which home is skipped per turn

**`AimController.cs`**
- Added `SetPlayerColor(Color)` — updates aim arrow `startColor` / `endColor` to the active player's tint
- Added `private TurnManager _turnManager` and `SetTurnManager(TurnManager)` — called by PSC.Start() after creating TurnManager
- Added Phase guard in `Update()`: when TurnManager is present, input is blocked unless `GameState.Phase == WaitingForLaunch`. Without this guard, AimController would accept clicks during BetweenTurns (the rocket starts as Prelaunch from PSC.Awake before TurnManager positions it).

---

## Architectural decisions

### Self-bootstrapping: no scene changes needed

The original TurnManager was a standalone MonoBehaviour the user had to add to the scene, and its `Start()` called `NewGame()` which regenerated the galaxy — wrong on first boot because PSC.Awake had already built one. The fixed design:

- PSC.Start() creates TurnManager via `AddComponent` (TurnManager.Awake fires synchronously, creating UI)
- PSC.Start() calls `TurnManager.Initialize(this)` which wires events and calls `BeginGame()` — using the galaxy PSC already holds, with no regeneration
- The New Game button / N key call `NewGame()` which does regenerate, then calls `BeginGame()`
- `BeginGame()` is the shared "reset game state with current galaxy" path

AimController needed a TurnManager reference for the Phase gate. PSC wires this in Start() after creating TurnManager: `_aimController.SetTurnManager(_turnManager)`.

### Home capture enabled by TurnManager directly

Phase 2 generates home bodies with `CaptureRingRadius = 0` (uncapturable). To avoid adding yet more GalaxyParameters fields and risking breakage of the existing ScriptableObject asset, TurnManager mutates the home bodies' capture parameters after galaxy generation:

```csharp
home.CaptureRingRadius           = 4f;
home.CaptureMinSpeed             = 2f;
home.CaptureMaxSpeed             = 20f;
home.CaptureAngleToleranceDegrees = 45f;
```

`CelestialBody` is a plain class (mutable), so this is safe. The capture ring visual is not drawn for home planets (ring drawing happens in `CreateBodyViews` before TurnManager runs) but ownership halos make ownership unmistakable anyway.

If you want capture rings drawn for homes, move `SetHomeCaptureParams` calls earlier — before `CreateBodyViews` runs — by exposing them from a TurnManager bootstrap that runs before PSC.Awake. Not worth the complexity for Phase 3.

### `_activeHomeBodyId` replaces fixed home-skip

The old check `if (i == _homeBodyId || body.CaptureRingRadius <= 0f)` skipped P1's home by list index. Phase 3 needs to skip whichever home is the *current* player's — not always P1's. `_activeHomeBodyId` is set to `_homeBodyId` at init (same Phase 2 behavior), then TurnManager calls `SetActiveHome(current.HomeBodyId)` at the start of each turn.

### OrbitingRocketView uses `Time.deltaTime`

The kinematic orbit advance in `OrbitingRocketView.Update()` uses `Time.deltaTime`. Per CLAUDE.md, `Time.deltaTime` is prohibited in *physics math*, but OrbitingRocketViews are pure presentation — they never feed back into any simulation. The prohibition applies to `PatchedConicsSolver` and game-logic orbit calculations, not cosmetic views.

### TurnManager uses `UnityEngine.Random` for seed selection

Per CLAUDE.md: "No `UnityEngine.Random` in game logic." TurnManager uses it only for picking a new seed on `NewGame()`, which is a presentation-layer decision (same pattern as `GalaxyVisualizer`).

### Own-home re-capture: null return

`OwnershipResolver.ResolveCapture` returns `null` when the active player fires into their own home's orbit. The capture registers as `Orbited` in the physics layer (EvaluateCapture still fires, the rocket enters orbit), but TurnManager treats a null OwnershipChange as "no ownership update needed." The rocket settles into orbit of the home visually but no OrbitingRocketView is spawned (no point marking a home planet that already has a colored halo).

### Same-player re-capture: `DislodgedExistingRocket = true`

When P1 fires into a planet they already own, `OwnershipResolver` returns `DislodgedExistingRocket = true` so the old `OrbitingRocketView` is replaced with a fresh one at the new orbit position. The new owner is still P1, so the ownership halo color is unchanged. This feels correct: the rocket settled into a new orbit, so the visual should update.

### Two-player only

`GameState.Players` is `IReadOnlyList<Player>` so future multi-player support is easy; `WinConditionChecker` and `OwnershipResolver` already iterate instead of hard-coding indices. The current turn toggle `CurrentPlayerId == 1 ? 2 : 1` is the only two-player assumption in `TurnManager.HandleRocketResolved`.

---

## First-time setup in Unity

No manual scene changes required. The bootstrap is fully automatic:

1. Open the scene that has `PrototypeScenarioController` (e.g. `Phase1_Prototype`).
2. Press Play.

On play:
- PSC.Awake() generates the galaxy, builds body views, creates the rocket and AimController.
- PSC.Start() creates a `TurnManager` child GameObject via `AddComponent`, then calls `TurnManager.Initialize(this)`.
- TurnManager.Awake() (which fires synchronously inside AddComponent) creates TurnUI and WinScreenUI.
- TurnManager.Initialize() wires PSC events and calls `BeginGame()`, which sets up players, enables home-planet capture, creates ownership halo views, and enters BetweenTurns phase.
- PSC.Start() then calls `_aimController.SetTurnManager(_turnManager)` so AimController gates on Phase.
- Player 1's turn prompt appears immediately. Space → aim → fire → turn passes automatically.

**What NOT to do:** do not add TurnManager to the scene manually. PSC creates exactly one.

---

## Galaxy regeneration and per-galaxy state leaks (post-initial fix)

### What was leaking

When `GalaxyVisualizer.G` (or `B`) pressed, `PSC.RegenerateGalaxy()` ran and correctly
destroyed and rebuilt all **body view** GameObjects (planets, SOI rings, capture rings).
It did **not** notify TurnManager. Three categories of per-galaxy state were therefore left
stale and pointing at the old galaxy:

| Stale state | Location | Effect |
|---|---|---|
| `_ownershipViews` (dict bodyId → `PlanetOwnershipView` GO) | `TurnManager` | Old halo GOs remained in the scene at the previous galaxy's body positions |
| `_orbitingRockets` (dict bodyId → `OrbitingRocketView` GO) | `TurnManager` | Old orbiting-rocket GOs remained in the scene |
| `_gameState.Ownership` dict | `TurnManager` | Referenced old body IDs; any captured planet could now alias a wrong body in the new galaxy |
| `GameState.CurrentPlayerId`, `TurnNumber`, `WinnerId` | `TurnManager` | Not reset; game logic continued from mid-game state into a fresh galaxy |
| Win screen | `WinScreenUI` | Remained visible if game had just ended |

### What was NOT leaking (confirmed clean)

| State | Location | How it resets |
|---|---|---|
| `_homeBodyId`, `_activeHomeBodyId` | PSC | Reset in `LoadProceduralGalaxy()` (called inside `RegenerateGalaxy`) |
| `_wasInsideCaptureRing` array | PSC | Rebuilt in `LoadProceduralGalaxy()` |
| `AimController._homeBody` | AimController | Re-set by `TurnManager.StartTurn()` via `GetBodyById()` each turn |
| `OrbitingRocketView._capturedBody` ref | OrbitingRocketView | GO is destroyed when `TurnManager.ClearOrbitingRocketViews()` runs |
| `GalaxyVisualizer.CurrentSeed` | GalaxyVisualizer | Just a seed int; no body IDs |

### Fix

Added `public event System.Action GalaxyRegenerated` to PSC. It fires at the **end** of
`RegenerateGalaxy()`, after body views and the rocket are fully rebuilt.

`TurnManager.Initialize()` subscribes: `_psc.GalaxyRegenerated += OnGalaxyRegenerated`.
`OnGalaxyRegenerated()` calls `BeginGame()`, which already:
- Destroys all `OrbitingRocketView` GOs (`ClearOrbitingRocketViews`)
- Destroys all `PlanetOwnershipView` GOs (`ClearOwnershipViews`)
- Creates a new `GameState` with fresh Players (using `CurrentGalaxy.Player1HomeId /
  Player2HomeId`), fresh Ownership dict seeded with home planet entries, `TurnNumber = 1`,
  `CurrentPlayerId = 1`, `WinnerId = null`
- Sets home planet capture parameters on the new bodies
- Creates fresh `PlanetOwnershipView` instances at the new body positions
- Calls `RefreshOwnershipViews()` so home planet halos immediately show correct colors
- Sets `Phase = BetweenTurns` and shows the Space-to-begin prompt
- Calls `_winScreen.Hide()`

Because the event fires from inside `RegenerateGalaxy()`, **any** caller — including
`GalaxyVisualizer` (G/B hotkeys), `TurnManager.NewGame()`, or future callers — gets the
full reset automatically without needing to know about TurnManager.

`TurnManager.NewGame()` was simplified: it no longer calls `BeginGame()` explicitly
after `RegenerateGalaxy()` (that would run `BeginGame` twice). If `GalaxyParams` is null
and no regen is possible, `NewGame()` falls back to calling `BeginGame()` directly.

---

## Known gaps before competitive play is fun

- **No capture ring drawn for home planets.** The capture window is active (4 u radius, tested in physics), but there is no yellow ring visual on home planets. Players learn "orbit the enemy base" through play. Could add by calling `SetHomeCaptureParams` before `CreateBodyViews` (requires TurnManager to initialize earlier, or PSC to expose a hook).
- **Dislodge flash is absent.** The spec mentions "a small visual flash for feedback." Currently the old `OrbitingRocketView` is simply destroyed. A brief color pulse or scale pop would improve feedback but is not in scope for the initial implementation.
- **Planet count includes home planets.** The spec says "Blue: 3 planets" — counting owned home is reasonable (you start with 1), but check whether it feels natural vs. counting only captured non-home bodies.
- **No "give up turn" option.** The spec discussed R as a possible "give up turn." Currently in `TurnManagedMode`, R does nothing. If a rocket is stuck in a low-probability loop (nearly escaped), there is no way to skip; the player must wait for the physics timeout (~60 s, `MaxSimTime`). Consider adding a Give Up button or shorter `MaxSimTime` for the competitive mode.
- **Galaxy not tuned for Phase 3 match length.** The Phase 3 spec target is 10-30 turns per game. Run a few matches and tune `HomeCaptureAngleToleranceDeg`, home body mass, and cluster density accordingly.
