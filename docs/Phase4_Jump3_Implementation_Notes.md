# Phase 4 Jump 3 — Implementation Notes

## What was built

### New files
- **`Scripts/Strategy/Colonisation.cs`** — Data record: `PlayerId`, `ColonistCount`,
  `TurnsRemaining`. Lives in `GameState.Colonisations` keyed by body ID.
- **`Scripts/Combat/ColonisationResolver.cs`** — Pure static resolver. Returns a
  `ColonisationChange` (outcome + new counts/timers) given the current `GameState`,
  the firing player, the target body, passenger count, and tunable parameters. Never
  mutates state. `ComputeTurns` uses integer ceiling: `(base + n - 1) / n`.
- **`Scripts/Combat/ColonisationTicker.cs`** — Pure static ticker. Decrements every
  `Colonisation.TurnsRemaining` by 1, collects completions (those hitting 0), removes
  them from `state.Colonisations`, and returns `List<Completion>`. Does NOT write to
  `state.Ownership` — that is TurnManager's responsibility so it can source orbit
  parameters from the existing `OrbitingRocketView`.
- **`Scripts/Presentation/ColonisationView.cs`** — MonoBehaviour per colonising planet.
  Each view owns its own Screen-Space-Overlay canvas (sorting order 7). In `LateUpdate`
  it looks up `state.Colonisations[body.Id]`, positions the label via world→screen
  projection (+56 px), and renders `"N colonists · T turns"` in the colonising player's
  colour. Hides itself if the entry is absent (handles removal gracefully without being
  destroyed immediately).

### Modified files
- **`OrbitingRocketView.cs`** — Added four public property getters (`OrbitRadius`,
  `OrbitAngle`, `OrbitAngularSpeed`, `OrbitDirection`) over the existing private fields.
  Used by `TurnManager.ApplyColonisationCompletion` to reconstruct `PlanetOwnership`
  without a separate parallel dict.
- **`GameState.cs`** — Added `Dictionary<int, Colonisation> Colonisations`.
- **`StrategyParameters.cs`** — Added `ColonisationBaseDuration = 20` and
  `MinColonisationTurns = 1` under a new `[Header("Colonisation")]`.
- **`OwnershipResolver.cs`** — `ResolveCapture` marked `[System.Obsolete]` with a
  note pointing to `ColonisationResolver`. Method body unchanged; `OwnershipChange`
  class retained (no callers but removing it is not required per spec).
- **`WinConditionChecker.cs`** — `CheckForWin` returns `null` with a comment. Original
  logic body removed but method signature preserved for Jump 4.

### TurnManager changes
- `_colonisationViews` dictionary added.
- `EndGame` calls `ClearColonisationViews()`.
- `BeginGame` calls `ClearColonisationViews()` and `_gameState.Colonisations.Clear()`.
- `HandleRocketResolved` — the `OwnershipResolver.ResolveCapture` path replaced with
  `ColonisationResolver.Resolve` → `ApplyColonisationChange` →
  `SpawnOrReplaceOrbitingRocketView` → `RefreshColonisationViews`. Win-check call
  preserved with a comment noting it is a no-op until Jump 4.
- `AdvanceToNextPlayer` — `ColonisationTicker.Tick` runs first (before player flip);
  completions are applied by `ApplyColonisationCompletion`; ownership and colonisation
  views are refreshed if anything completed.
- New helpers: `ApplyColonisationChange`, `ApplyColonisationCompletion`,
  `SpawnOrReplaceOrbitingRocketView`, `RefreshColonisationViews`, `ClearColonisationViews`.

## Decisions beyond the spec

- **`ColonisationTicker` does not write to `state.Ownership`** — the spec's text
  fluctuates between "the ticker adds to Ownership" and "TurnManager constructs
  PlanetOwnership". I chose the latter so the ticker stays a pure-data function with no
  knowledge of orbit parameters, which are view-layer data. TurnManager's
  `ApplyColonisationCompletion` reads from the `OrbitingRocketView` (or falls back to
  computed defaults) and writes the `PlanetOwnership` entry.
- **Fallback orbit params** when no `OrbitingRocketView` exists at completion time:
  `OrbitRadius = body.Radius * 2`, speed computed from `sqrt(G * mass / radius)`,
  `OrbitDirection = 1`. This mirrors the spec's suggested defaults.
- **`OrbitingRocketId = 0`** in the fallback-free ownership record built by
  `ApplyColonisationCompletion` (when a view is present). Any non-negative value serves
  as the "view exists" sentinel; the actual integer is no longer used for lookups since
  `ApplyOwnershipChange` (the only consumer of the specific ID) is retired in Jump 3.
- **`ColonisationView` canvas per view** (not a shared canvas) — simplifies lifecycle:
  destroying the view's `GameObject` automatically destroys the canvas, so
  `ClearColonisationViews` just calls `Destroy(v.gameObject)` with no separate canvas
  bookkeeping.
- **`ScreenOffsetY = 56f`** for `ColonisationView` — placed above the `HomePopulationView`
  offset (40 px) so both labels can appear simultaneously without overlapping when a home
  planet is being colonised (which can't happen yet in Jump 3, but is defensive).
- **`RefreshOwnershipViews` also called** after colonisation completions even though the
  `PlanetOwnershipView` colour should already reflect the new owner (since `Ownership`
  was just written). Belt-and-braces; the call is cheap.

## Open questions

- **Player count in `RefreshColonisationViews`** — the current view lookup uses
  `_state.GetPlayer(col.PlayerId)?.Color`. If a player ID is somehow stale, the label
  falls back to white. Worth a debug log if this ever happens in practice.
- **Multiple rockets from the same player landing on the same colonising planet in the
  same turn** — the second rocket also goes through `ColonisationResolver` and returns
  `Reinforced`, which is correct. The `SpawnOrReplaceOrbitingRocketView` call on the
  second rocket destroys the first orbit view and places a new one, so only the last
  rocket's orbit is visible. This is fine for now.
- **Bonus-rocket launch sites include colonising planets** — `LaunchSiteCalculator`
  reads only from `state.Ownership`, so colonising (but not yet owned) planets do not
  grant bonus rockets. This is intentional and correct for Jump 3.
- **Win condition removed** — games play indefinitely. Players can colonise every
  unowned planet but cannot win. Jump 4 restores a win condition via combat. Tune
  `ColonisationBaseDuration` (currently 20) to match the pace you want: with
  `PopulationGrowthPerTurn = 10`, a single-rocket 10-colonist landing takes
  `ceil(20/10) = 2` turns to complete.
- **`OwnershipChange` and `OwnershipResolver`** — both retained but `ResolveCapture` is
  `[Obsolete]`. If Jump 4 has no use for them either, both can be deleted then.
