# Phase 4 Jump 1 — Implementation Notes

## What was built

### New files
- **`Scripts/Strategy/StrategyParameters.cs`** — ScriptableObject with two fields:
  `StartingPopulation` (default 0) and `PopulationGrowthPerTurn` (default 10).
  Create the asset via `Assets > Create > Orbital > Strategy Parameters`.
- **`Scripts/Presentation/HomePopulationView.cs`** — MonoBehaviour that displays a
  `Pop: N` label near one player's home planet. Positions itself each `LateUpdate`
  by projecting the home body's world position to screen space and offsetting 40 px
  upward. Reads `GameState.Population[playerId]`; hides itself if the entry is absent.

### Modified files
- **`GameState.cs`** — Added `public Dictionary<int, int> Population`.
- **`TurnManager.cs`** — Added `_strategyParams` serialized field with public getter/setter;
  population init in `BeginGame()`; growth tick in `AdvanceToNextPlayer()`;
  `_homePopulationViews` dictionary and `_popLabelCanvas`; `ClearHomePopulationViews()` helper.
- **`PrototypeScenarioController.cs`** — Added `public StrategyParameters StrategyParams`
  inspector field; assigns it onto `TurnManager.StrategyParams` before calling `Initialize()`.

## Decisions beyond the spec

- **Canvas sorting order 5** for the population label canvas. This places it below the
  TurnUI / WinScreen (which inherit whatever order they use), high enough to be visible
  above world geometry.
- **40 px screen-space offset** above the planet centre for the label. Chosen to clear
  the planet sprite at typical zoom without overlapping the player-colour ring drawn by
  `PlanetOwnershipView`. Adjust `HomePopulationView.ScreenOffsetY` if needed.
- **`StrategyParams` is assigned before `Initialize()`** (not after) so that `BeginGame()`
  inside `Initialize()` already has a valid reference. The spec snippet shows assignment
  "immediately after AddComponent" — placing it before `Initialize()` satisfies that and
  avoids a null-params first game.
- `TurnManager.StrategyParams` is a full get/set property rather than just a getter over
  a `[SerializeField]` because the value must be writable by PSC at runtime (TurnManager
  is added via `AddComponent`, so the Inspector-drag path is unavailable).

## Open questions

- **Growth timing for Player 1 turn 1** — per the spec, Player 1 starts with
  `StartingPopulation` and receives no growth until the start of their *second* turn
  (i.e. after Player 2 has had turn 1). This is symmetric — both players see their
  first growth tick at the same turn count — but means the first round shows `Pop: 0`
  for both. Confirm this is the intended feel once Jump 2 (loading rockets) lands and
  population actually matters.
- **Label placement on small screens / unusual zoom** — the fixed 40 px offset may
  clip off-screen at extreme zoom-out or overlap the planet name at extreme zoom-in.
  A world-space TMP (as mentioned in the spec as an alternative) would scale naturally
  but would require a world-space canvas per label. Worth revisiting if visual polish
  becomes a priority.
- **`StrategyParameters.asset` must be created manually** — Unity won't auto-create it.
  After importing, go to `Assets > Create > Orbital > Strategy Parameters`, save it
  under `Assets/_Project/Data/`, and drag it onto the `PrototypeScenarioController`
  `Strategy Params` field in the Inspector. Without it the game still runs (all null-
  checks default to 0 start / 10 growth) but the values won't be tunable at runtime.
