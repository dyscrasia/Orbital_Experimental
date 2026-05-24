# Orbital — Phase 3.5 Implementation Notes

## What was built

### New files

| File | Purpose |
|---|---|
| `Scripts/Strategy/LaunchSiteCalculator.cs` | Pure static class: `Calculate(state, playerId)` returns `List<int>` of launch site body IDs. Also exposes `RocketCount(int nonHomeCaptured)` for testing without a full GameState. |
| `Scripts/Presentation/LaunchSiteView.cs` | MonoBehaviour — small rocket-shaped triangle at a planet's surface in the player's color; white highlight ring when selected. Fires `Selected(bodyId)` event on click. |
| `Tests/LaunchSiteCalculatorTests.cs` | NUnit tests: rocket count formula (0–8 captures), Calculate() result count and home-first ordering, bonus sites are valid captured planets, no duplicates, Rng determinism. |
| `docs/Phase3_5_Implementation_Notes.md` | This file. |

### Modified files

**`Scripts/Strategy/GameState.cs`**
- Added `public List<int> AvailableLaunchSites { get; set; }` — body IDs the active player can launch from this turn. Populated by TurnManager at StartTurn; entries removed as rockets fire.
- Added `public int ActiveLaunchSiteId { get; set; }` — currently selected site.

**`Scripts/Presentation/PrototypeScenarioController.cs`**
- Added `private int _activeLaunchSiteId` field (default -1).
- Added `public void SetActiveLaunchSite(int bodyId)` — called by TurnManager when a site is selected.
- Modified FixedUpdate capture-skip: now skips `_activeHomeBodyId` **and** `_activeLaunchSiteId`. This prevents the rocket from immediately re-capturing the planet it launched from.
- `LoadProceduralGalaxy()`: now sets `_activeLaunchSiteId = _homeBodyId` alongside `_activeHomeBodyId`.

**`Scripts/Presentation/AimController.cs`**
- Changed `CancelDrag()` from `private` to `public`. TurnManager calls this when switching to a different launch site mid-turn so any in-progress drag is cleanly reset.

**`Scripts/Presentation/TurnManager.cs`**
Major changes:
- Added `_launchSiteViews` dictionary (bodyId → LaunchSiteView).
- `StartTurn()`: calls `LaunchSiteCalculator.Calculate()`, spawns `LaunchSiteView` per site, auto-selects home via `SelectLaunchSite()`.
- New `SelectLaunchSite(int bodyId)`: calls `SetActiveHome`, `SetActiveLaunchSite`, `PrepareRocketForPlayer`, updates AimController's color/home planet/cancels drag, and sets highlight on all views.
- New `HandleSiteSelected(int bodyId)`: event subscriber — ignores clicks unless `Phase == WaitingForLaunch`.
- `HandleRocketResolved()`: removes used site, destroys its view, then either re-enters WaitingForLaunch (if sites remain, auto-selects next) or calls `AdvanceToNextPlayer()`.
- New `AdvanceToNextPlayer()`: switches `CurrentPlayerId`, increments `TurnNumber`, sets `BetweenTurns`.
- New `EndTurn()`: clears remaining sites and views, calls `AdvanceToNextPlayer()`. Guarded on `Phase == WaitingForLaunch`.
- New `DestroyLaunchSiteView(int bodyId)`: destroys a single view and removes from dict.
- New `ClearLaunchSiteViews()`: destroys all, clears dict. Called from `BeginGame()` and `EndGame()`.
- `BeginGame()`: now calls `ClearLaunchSiteViews()` first (guards against stale views from a previous game).
- `Update()`: added Enter key → `EndTurn()`.
- `Awake()`: subscribes `_turnUI.OnEndTurn += EndTurn`.

**`Scripts/Presentation/TurnUI.cs`**
- Added `public event System.Action OnEndTurn` — fired by End Turn button click.
- Added `_rocketCounter` TextMeshProUGUI (bottom-left) showing "Rockets: N".
- Added `_endTurnButton` Unity Button (bottom-right) with label "End Turn  [Enter]".
- `Show(GameState)`: enables/shows the counter and button only during `WaitingForLaunch`; hides them otherwise.
- `BuildCanvas()`: constructs counter and button procedurally.

**`CLAUDE.md`**
- Added "Game variants — scope guard" section: Classic (current), Arcade, Strategy — don't build for Arcade/Strategy.
- Added "Classic rocket production rule" section documenting the formula, seed, classes, and behavior.

---

## Architectural decisions

### LaunchSiteCalculator as pure static

The rocket count formula is deterministic from game state. Making it a pure static class (no Unity, no MonoBehaviour) keeps it testable in isolation and consistent with `OwnershipResolver` / `WinConditionChecker`. The only Unity dependency in the whole feature is `LaunchSiteView` (MonoBehaviour) and the TurnUI additions (Unity UI).

### Rng seed: `TurnNumber * 31 + playerId`

The spec mandates determinism from TurnNumber and CurrentPlayerId. The `* 31` is a prime multiplier that prevents trivially similar seeds for different (turn, player) combinations (e.g. turn 2 player 1 ≠ turn 1 player 2). Same seed formula used in `Rng.SubStream` for consistency.

The Rng is constructed fresh each time `Calculate()` is called — it's not stored anywhere. This means calling `Calculate` twice with identical state always returns identical results.

### Shuffle-then-take-first-N for "without replacement" selection

`Rng.Shuffle(nonHomeCaptured)` followed by taking `bonusCount` elements is the standard Fisher-Yates without-replacement selection. It's O(n) and unbiased. The home body is excluded from the candidate list before shuffling.

### `_activeLaunchSiteId` separate from `_activeHomeBodyId`

Phase 2 only ever launched from the player's home, so one skip ID was enough. Phase 3.5 can launch from any captured planet. We now skip **two** bodies:

| Skip | Why |
|---|---|
| `_activeHomeBodyId` | Player's home — can't meaningfully capture it (OwnershipResolver returns null); skip prevents "orbited own home" wasting the turn |
| `_activeLaunchSiteId` | Launch site — rocket would immediately re-capture the body it just left |

When launching from home both IDs are equal (`==`), so the check still fires exactly once for that body. -1 is used as the initial/unset value since no body has Id == -1.

### LaunchSiteView vs OrbitingRocketView — visual distinction

| | `LaunchSiteView` | `OrbitingRocketView` |
|---|---|---|
| Meaning | "I have a rocket ready to fire here" | "This planet is mine — I have a rocket in orbit" |
| Motion | Static (surface) | Kinematic orbit (moves each frame) |
| Shape | Small upward triangle | Small triangle pointing in direction of travel |
| Selection | White highlight ring on active | No selection state |
| Lifetime | Spawned at StartTurn, destroyed when used or on EndTurn | Spawned on capture, destroyed on dislodge or BeginGame |

### Auto-select first remaining site after resolution

After a rocket resolves with more sites left, TurnManager auto-selects `AvailableLaunchSites[0]` and re-enters WaitingForLaunch with a positioning hint. This avoids requiring the player to always manually click a site before they can drag. They can still click a different site to switch before firing.

### EndTurn guard: `Phase == WaitingForLaunch`

`EndTurn()` is a no-op unless the phase is WaitingForLaunch. This prevents accidental double-calls from the button and the Enter key if both fire in the same frame, and prevents misfire during RocketInFlight.

### `AdvanceToNextPlayer()` factored out

Previously the player-switch logic was inlined in `HandleRocketResolved`. Factoring it into `AdvanceToNextPlayer()` lets both "all rockets used" and "End Turn" converge on one path. `EndGame` is a separate path (doesn't advance; instead shows the win screen).

---

## Bug fix — enemy home planet was unreachable (post-Phase 3.5)

**Symptom:** A rocket fired at the enemy's home planet could never enter orbit there. The win condition was unreachable.

**Root cause:** `PSC.FixedUpdate` had a capture-skip check with two exclusions:

```csharp
// WRONG
if (body.Id == _activeHomeBodyId || body.Id == _activeLaunchSiteId || body.CaptureRingRadius <= 0f)
    continue;
```

`_activeHomeBodyId` was set to the *current player's* home at turn start via `SetActiveHome()` in `TurnManager.SelectLaunchSite()`. However, home planets are assigned `CaptureRingRadius = 4f` by `TurnManager.SetHomeCaptureParams()`, so they pass the `<= 0f` guard. That means `_activeHomeBodyId` was the only thing keeping home planets out of capture detection — and it was keyed to the *firing* player's home. When Player 1 fired, Player 1's home was excluded (correct). But Player 2's home was also excluded whenever `_activeHomeBodyId` was stale or set incorrectly (which it sometimes was depending on call order).

The real fix: `_activeHomeBodyId` is entirely redundant. When launching from home, `_activeLaunchSiteId == _activeHomeBodyId`, so the launch-site skip already covers it. When launching from a non-home captured planet, the player's home should be a valid capture target anyway (capturing your own home is a no-op that `OwnershipResolver` safely ignores).

**Fix applied:**
- Removed `_activeHomeBodyId` field and all references from `PSC`.
- Removed `SetActiveHome()` method from `PSC`.
- Removed `_psc.SetActiveHome(current.HomeBodyId)` call from `TurnManager.SelectLaunchSite()`.
- Capture skip is now:

```csharp
// CORRECT
if (body.Id == _activeLaunchSiteId || body.CaptureRingRadius <= 0f)
    continue;
```

**Files changed:** `PrototypeScenarioController.cs` (field removed, condition narrowed, method removed), `TurnManager.cs` (one call removed).

---

## Bug fix — home planet capture ring not rendered (visual mismatch)

**Symptom:** Home planets showed no yellow capture ring (or showed only the faint SOI ring at a completely different radius), but rockets DID capture into orbit around home planets at 4 world units. The rendered visual gave no indication of where capture would occur.

**Root cause — timing:**

`GalaxyGenerator` hardcoded `CaptureRingRadius = 0f` for both home planets. `PSC.Awake()` calls `LoadProceduralGalaxy()` then `CreateBodyViews()` → `DrawCaptureRings()`. `DrawCaptureRings()` has an early-out: `if (body.CaptureRingRadius <= 0f) continue;`. So home planets were always skipped and no yellow ring was drawn.

Later, in `PSC.Start()`, `TurnManager.Initialize()` → `BeginGame()` → `SetHomeCaptureParams()` mutated each home body's `CaptureRingRadius` to `4f`. This made capture detection work (PSC.FixedUpdate uses `body.CaptureRingRadius`), but the ring had already been drawn (or skipped) — there was no mechanism to retroactively draw it.

The same ordering problem existed in `RegenerateGalaxy()`: `CreateBodyViews()` runs before the `GalaxyRegenerated` event fires → `BeginGame()` → `SetHomeCaptureParams()`.

**Root cause — architecture:** Home planet capture criteria lived as hardcoded constants in `TurnManager` (`HomeCaptureRingRadius = 4f`, etc.) rather than as data in `GalaxyParameters`. This made it impossible for `GalaxyGenerator` to populate the correct values at generation time.

**Fix applied:**

1. Added `HomePlanetCaptureCriteria` (`CaptureCriteria` struct) to `GalaxyParameters`, defaulting to `{4f, 2f, 20f, 45°}` — the same values TurnManager used to hardcode.
2. `GalaxyGenerator` now applies `p.HomePlanetCaptureCriteria` to home planet bodies (all four fields) instead of `CaptureRingRadius = 0f`. Home planets now leave the generator with correct, non-zero `CaptureRingRadius`.
3. `DrawCaptureRings()` sees `CaptureRingRadius = 4f` for home planets at draw time and renders the yellow ring correctly.
4. Removed `TurnManager.SetHomeCaptureParams()` method and the four `HomeCaptureXxx` constants — they were the after-the-fact patch that this fix makes unnecessary.

**Orbit radius verification:** `EvaluateCapture()` (PSC) sets `_rocket.OrbitRadius = dist` where `dist` is the rocket's distance from the body at the moment of crossing `CaptureRingRadius`. Because capture is only triggered on the inbound crossing of exactly that ring, `OrbitRadius ≈ CaptureRingRadius`. The rendered orbit (`OrbitingRocketView`) animates at `OrbitRadius`, so orbit visual matches capture ring. No override or special case for home planets changes this.

**Files changed:** `GalaxyParameters.cs` (new field), `GalaxyGenerator.cs` (apply capture criteria to home planets), `TurnManager.cs` (removed `SetHomeCaptureParams` method and 4 constants).

---

## Known gaps

- **No visual rocket in flight indicator for non-home launches.** The rocket view shows at whichever body it was launched from. The LaunchSiteView for the home planet remains visible even when the rocket launched from a non-home site. This is a minor cosmetic quirk — the home view just sits there during non-home launches. Could be hidden when it's not the active site.
- **LaunchSiteViews always placed at the top of the planet (0°).** All sites are initialized at `body.Position + (0, body.Radius + offset)`. If two planets are very close and stacked vertically, views could overlap. A small improvement would be to randomize or evenly distribute placement angle per planet. Not urgent.
- **No hint differentiating "click a site to select" from "drag to aim."** `ShowPositioningHint()` gives the same message for both first launch and mid-turn re-entry. A second message variant like "Select a rocket then drag to aim" would improve clarity when multiple sites remain.
- **Galaxy not yet tuned for multi-rocket turn length.** With more rockets per turn, games may end faster. Tune galaxy density and capture angle tolerance based on playtest results.
