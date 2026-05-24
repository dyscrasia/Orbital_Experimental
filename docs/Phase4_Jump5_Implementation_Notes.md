# Phase 4 Jump 5 — Implementation Notes

## What was built

### New files
- **`Scripts/Strategy/Contest.cs`** — Data class: `InvaderPlayerId`, `InvaderCount`.
  Lives in `GameState.Contests` keyed by body ID. Defender count stays in
  `Population[bodyId]`; defender player is resolved from `Ownership` or
  `Colonisations` at runtime.
- **`Scripts/Combat/ContestTicker.cs`** — Pure static ticker. For each contest,
  computes mutual casualties (`ceil(otherSide / damageDivisor)` floored at
  `minDamage`), writes updated counts back to `Population[bodyId]` and
  `contest.InvaderCount`, then returns a `Result` per resolved contest.
  Resolved contests are removed from `state.Contests` before returning.
  `CeilDiv` is a private static helper.

### Modified files
- **`Scripts/Strategy/Colonisation.cs`** — `ColonistCount` field removed. The
  count now lives exclusively in `Population[bodyId]`.
- **`Scripts/Strategy/GameState.cs`** — Added `Dictionary<int, Contest> Contests`.
- **`Scripts/Strategy/StrategyParameters.cs`** — Added `[Header("Combat")]` section
  with `ContestDamageDivisor = 5` and `ContestMinDamage = 1`.
- **`Scripts/Strategy/LaunchSiteCalculator.cs`** — Appended
  `sites.RemoveAll(id => state.Contests.ContainsKey(id))` so contested planets
  do not appear as launch sites.
- **`Scripts/Combat/ColonisationResolver.cs`** — Full rewrite of the outcome enum
  (adding `StartContest`, `ReinforceContest_Invader`, `ReinforceContest_Defender`)
  and the full rule matrix. The special "own home → NoOp" guard was removed;
  home planets are in `Ownership` from game start, so firing at own home follows
  the "Owned, same player → `ReinforceContest_Defender`" path (resupply). Firing
  with 0 passengers still returns `NoOp`. The `Blocked` case is now only
  reachable for a third-party player in a contest (unreachable in 2-player).
- **`Scripts/Combat/ColonisationTicker.cs`** — `FinalColonistCount` renamed to
  `FinalCount`; set from `state.Population[bodyId]` at completion time instead
  of from the (now-removed) `col.ColonistCount`.
- **`Scripts/Combat/WinConditionChecker.cs`** — Restored to original
  "owns enemy home" logic. The Jump 3 stub returning `null` is replaced.
- **`Scripts/Presentation/ColonisationView.cs`** — Body emptied; class marked
  `[Obsolete]`. The file is kept to prevent Unity meta-file orphaning; no
  instances are created anywhere in the codebase.
- **`Scripts/Presentation/PlanetPopulationView.cs`** — Completely rewritten.
  Now manages two TMP labels (`_lowerLabel` / `_upperLabel`) parented to the
  shared canvas. `Initialize` no longer takes `playerColor` (colours are
  resolved from `GameState` in `LateUpdate`). Display logic:
  - **Contested**: `_upperLabel` = "Defender: X" (defender colour),
    `_lowerLabel` = "Invader: Y" (invader colour).
  - **Colonising (no contest)**: `_lowerLabel` only = "Pop: N · T turns"
    (colonising player colour).
  - **Owned (no contest)**: `_lowerLabel` only = "Pop: N" (owner colour).
  - **None of the above**: both labels hidden.
  Both label GOs are destroyed in `OnDestroy`.

### TurnManager changes
- **`_colonisationViews` dict removed**, along with `RefreshColonisationViews`
  and `ClearColonisationViews` methods and all their call sites. Colonisation
  state is now rendered by the existing `PlanetPopulationView` instances.
- **`BeginGame`**: removed `ClearColonisationViews()` call; added
  `_gameState.Contests.Clear()`.
- **`EndGame`**: removed `ClearColonisationViews()` call.
- **`ApplyColonisationChange`**: rewritten switch to handle all seven outcomes:
  - `Started` — writes `Colonisation` entry, sets `Population[bodyId] = passengers`.
  - `Reinforced` — updates `TurnsRemaining`, sets `Population[bodyId] = newCount`.
  - `ReinforceContest_Defender` — sets `Population[bodyId] = newCount`.
  - `ReinforceContest_Invader` — sets `contest.InvaderCount = newCount`.
  - `StartContest` — writes `Contests[bodyId]`.
  - `Blocked` / `NoOp` — no-op.
- **`ApplyColonisationCompletion`**: removed `Population[c.BodyId] = c.FinalCount`
  line — population is already correct because it was written at `Started` and
  kept current at every `Reinforced`. Updated field reference to `c.FinalCount`.
- **`AdvanceToNextPlayer`**: removed `RefreshColonisationViews()`. Added
  `ContestTicker.Tick` + `ApplyContestResult` loop; added win-check call
  (now live) between the tick block and the player flip.
- **`HandleRocketResolved`**: removed `RefreshColonisationViews()`. Win check
  comment updated (no longer a no-op).
- **`SelectLaunchSite`**: slider visibility now based on
  `isOwned && !isContested`. Contested sites never show the slider. Owned sites
  always show it (even pop = 0).
- **`RefreshPlanetPopulationViews`**: refactored to use `CollectBodiesWithState`
  (union of `Ownership`, `Colonisations`, `Contests` key sets). `Initialize`
  call updated to remove the `playerColor` argument.
- **New `CollectBodiesWithState()`**: returns `HashSet<int>` of all body IDs
  that have any displayable state.
- **New `ApplyContestResult(ContestTicker.Result r)`**: handles the three
  resolved outcomes:
  - `DefenderWins` — no additional state changes (population already correct).
  - `InvaderWins` — writes `Ownership[bodyId]` for new owner, sets
    `Population[bodyId] = FinalInvaderCount`, removes any `Colonisation` entry
    (defender was colonising), spawns new OrbitingRocketView in invader colour.
  - `MutualAnnihilation` — removes `Ownership`, `Colonisation`, and `Population`
    entries; destroys OrbitingRocketView.

## Decisions beyond the spec

- **`Blocked` outcome retained but effectively unreachable in 2-player** — The
  spec notes this. Kept for robustness if the game ever supports 3+ players.
- **Own-home "NoOp" guard removed from `ColonisationResolver`** — The spec says
  "Reinforce_Defender if applicable; NoOp if no Ownership entry yet". Since
  `BeginGame` always writes the home to `Ownership`, the "no Ownership entry"
  case is unreachable in practice. Removing the guard lets the home be
  resupplied, which is consistent with the spec's success criterion ("firing
  your own rocket onto your own owned planet increments that planet's population
  by the rocket's cargo").
- **`Population.Remove` on MutualAnnihilation** — Spec says "set to 0 (or
  remove the entry)". Using `Remove` keeps semantics clean: a planet absent from
  `Population` means "no civilian population" (consistent with the data model
  comment). A view for a mutually-annihilated planet will be destroyed by
  `RefreshPlanetPopulationViews` in the same frame since it leaves all three
  dicts.
- **`ApplyContestResult` reuses `SpawnOrReplaceOrbitingRocketView`** — No new
  code path needed for spawning the invader's rocket; the helper already handles
  the "replace" case.
- **`PlanetPopulationView.Initialize` signature simplified** — `playerColor` was
  dropped because the view now reads colours dynamically. This is a breaking
  change to the public interface but `RefreshPlanetPopulationViews` is the only
  caller.
- **`ColonisationView.cs` kept as an empty obsolete stub** — Unity associates
  `.meta` files with every script. Deleting the `.cs` file would orphan the
  `.meta` and produce a harmless but distracting warning on re-import. The empty
  class produces no compiler errors.

## Open questions

- **Resupply onto own home** — Firing at your home now adds cargo to
  `Population[homeId]` via the "Owned, same player" path. This is correct per
  spec, but it means players can "bank" cargo at home by firing an empty-ish
  rocket back. Not a problem for the current tuning but worth noting if
  home-growth rates are ever made much larger.
- **Colonisation timer during a contest** — When a colonising planet is
  contested, `ColonisationTicker` still decrements `TurnsRemaining` each turn.
  If the defender wins the contest before the timer expires, the planet continues
  colonising from its current (reduced) population. If the timer reaches 0
  while the contest is unresolved, `ColonisationTicker` removes the `Colonisation`
  entry and returns a `Completion`, which `ApplyColonisationCompletion` uses to
  write an `Ownership` entry — but the contest is still active! The planet would
  then appear in both `Ownership` and `Contests`, which is a supported state
  (the defender is now the owner). This is probably the intended behaviour; a
  future jump may wish to pause the colonisation timer during a contest.
- **`FinalCount` on `Completion` is no longer used in `ApplyColonisationCompletion`**
  — The field is still computed and stored in the `Completion` struct (useful for
  debugging) but TurnManager no longer reads it. Can be removed in a future
  cleanup pass if desired; being a public field on a nested class, there is no
  compiler warning for it.
- **Contest on a planet being colonised by a third player** — In 2-player games
  this is unreachable. In a future N-player extension, `ColonisationResolver`
  returns `Blocked` for a third-party firing player, which is correct.
- **`WinConditionCheckerTests`** — The existing tests were written for the
  restored logic and pass without modification. They were already correct when
  the checker was re-enabled.
