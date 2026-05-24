# Phase 4 Jump 4 — Implementation Notes

## What was built

### New files
- **`Scripts/Presentation/PlanetPopulationView.cs`** — Generalised successor to
  `HomePopulationView`. Initialised with any owned `CelestialBody`; reads
  `state.Population[body.Id]` in `LateUpdate`. Stores `_labelGo` (the TMP label
  parented to the shared canvas) and destroys it in `OnDestroy` so labels are
  cleaned up whether views are removed individually (via `RefreshPlanetPopulationViews`)
  or in bulk (via `ClearPlanetPopulationViews` which destroys the canvas).

### Modified files
- **`Scripts/Strategy/GameState.cs`** — Updated `Population` XML comment: keys are
  now body IDs, not player IDs. Signature unchanged (`Dictionary<int, int>`).
- **`Scripts/Strategy/StrategyParameters.cs`** — Added `CapturedPlanetGrowthDivisor = 2`
  under `[Header("Population")]` (done in prior session; noted here for completeness).
- **`Scripts/Strategy/LaunchSiteCalculator.cs`** — Full replacement. New rule: home
  first, then all other owned planets sorted by ascending body ID. `Rng`, `RocketCount`,
  and the `1 + floor(captures/2)` Classic formula are removed entirely.
- **`Scripts/Presentation/HomePopulationView.cs`** — Marked `[System.Obsolete]`.
  Implementation unchanged; callers in TurnManager have been replaced.
- **`Tests/LaunchSiteCalculatorTests.cs`** — Fully rewritten. All Classic
  `RocketCount`/random-site tests replaced with Strategy-variant tests: all-owned
  returned, home first, captured sorted by ascending ID, determinism, invalid player.

### TurnManager changes
- `_homePopulationViews` (typed `HomePopulationView`, keyed by player ID) replaced by
  `_planetPopulationViews` (typed `PlanetPopulationView`, keyed by body ID).
- **`BeginGame`** — Population now seeded by body ID (`p1Home`, `p2Home`). Explicit
  per-player HomePopulationView creation loop replaced by a single
  `RefreshPlanetPopulationViews()` call.
- **`HandleRocketLaunched`** — `isHomeSite` guard and per-player population subtraction
  removed. Population is now deducted from the active launch site's body ID with a
  defensive clamp (`load > siteAvailable → load = siteAvailable`).
  Calls `RefreshPlanetPopulationViews()` after deducting cargo.
- **`HandleRocketResolved`** — `SpawnOrReplaceOrbitingRocketView` call removed from the
  Orbited branch. OrbitingRocketView is no longer spawned on orbital capture; it appears
  only at colonisation completion. Added `RefreshPlanetPopulationViews()` call.
- **`SelectLaunchSite`** — Slider visibility now based on the active site's population
  (`state.Population[bodyId] > 0`) rather than an `isHomeSite` check. Any owned site
  with population shows the slider.
- **`AdvanceToNextPlayer`** — Single-player growth grant removed. Replaced by:
  1. `RefreshPlanetPopulationViews()` in the colonisation-completions block.
  2. `GrowOwnedPlanetPopulations(_gameState.CurrentPlayerId)` after the player flip.
  3. `RefreshPlanetPopulationViews()` after the growth pass.
- **`ApplyColonisationCompletion`** — After writing `Ownership`, also writes
  `Population[c.BodyId] = c.FinalColonistCount`. Then calls
  `SpawnOrReplaceOrbitingRocketView(c.BodyId, ownership, ownerColor)` — this is now
  the only place that spawns the orbiting-rocket visual. Calls
  `RefreshPlanetPopulationViews()` so the new planet's pop label appears in the same
  frame the ring and rocket appear.
- **`SpawnOrReplaceOrbitingRocketView`** — Signature changed to
  `(int bodyId, PlanetOwnership orbitParams, Color playerColor)`. No longer reads
  `_psc.Rocket`; uses the caller-supplied `orbitParams` directly. The
  `_nextRocketViewId` field is now unused but kept for Jump 5 if needed.
- **`ClearHomePopulationViews` → `ClearPlanetPopulationViews`** — Renamed and re-typed.
  Logic is identical; destroys each view GO and then the shared canvas.
- **New `RefreshPlanetPopulationViews()`** — Creates a `PlanetPopulationView` for every
  body in `state.Ownership` that doesn't have one; destroys views for bodies that are
  no longer in `Ownership`. Guards on `_popLabelCanvas == null` so it's safe to call
  before `BeginGame` completes.
- **New `GrowOwnedPlanetPopulations(int playerId)`** — Iterates `state.Ownership`;
  grants `PopulationGrowthPerTurn` to home planets and
  `PopulationGrowthPerTurn / CapturedPlanetGrowthDivisor` to captured planets.
  Uses `Mathf.Max(1, divisor)` to prevent divide-by-zero.

## Decisions beyond the spec

- **`_nextRocketViewId` removed** — The field was only written (never read) after
  `SpawnOrReplaceOrbitingRocketView` stopped using it, causing a CS0414 warning.
  Removed field and its `= 0` reset in `BeginGame`.
- **`RefreshPlanetPopulationViews` called from both `ApplyColonisationCompletion` and
  the batch block in `AdvanceToNextPlayer`** — The per-completion call ensures the view
  appears in the same frame the ownership entry is written; the batch call at the end
  is belt-and-braces. Both are cheap (no-op if view already exists).
- **`PlanetPopulationView.OnDestroy` destroys `_labelGo`** — The label is a child of
  the shared canvas GO, not of the view's own GO, so Unity's normal destruction cascade
  won't clean it up. Explicit cleanup in `OnDestroy` handles both the selective-removal
  path (`RefreshPlanetPopulationViews`) and the bulk path (`ClearPlanetPopulationViews`,
  which destroys the canvas after the views). Calling `Destroy` on an already-destroyed
  object is safe in Unity via the overloaded null check.
- **`RefreshPlanetPopulationViews` does not update colour when ownership changes** —
  In Jump 4, colonising an already-owned planet is `Blocked`, so a planet's owner can
  only change once (unowned → owned). The colour set at view-creation time is always
  correct. Jump 5 (dislodging) will need a colour-update path.
- **Fallback `OrbitingRocketId = 0` in both branches of `ApplyColonisationCompletion`** —
  Previously the fallback branch used `-1`. Changed to `0` for consistency; the field is
  a "view exists" sentinel and the specific value is never looked up.

## Open questions

- **`_nextRocketViewId` was removed** — field and reset both deleted after the CS0414
  warning. No open action.
- **`HomePopulationView` is marked obsolete but not deleted** — nothing outside
  TurnManager referenced it, so it will compile cleanly. Can be deleted in Jump 5 once
  confirmed no Unity scene references remain.
- **`RefreshPlanetPopulationViews` colour staleness** — If Jump 5 introduces planet
  recapture (dislodging), the view for a recaptured body will still show the old owner's
  colour. The fix is to destroy and recreate the view in `RefreshPlanetPopulationViews`
  when `owner.Color` differs from the existing view's colour, or to expose a
  `SetColor` method on `PlanetPopulationView`.
- **`ColonisationView` canvas leak** (carried over from Jump 3) — `ColonisationView`
  creates its own top-level canvas GO. `Destroy(v.gameObject)` in
  `ClearColonisationViews` does not destroy it. Low priority while game count is small.
