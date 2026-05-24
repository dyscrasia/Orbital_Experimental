# Orbital (Strategy variant) — Phase 4, Jump 5: Combat, unified population, slider fix

## Goal

Four changes that together complete the Strategy core loop:

1. **Single population per planet.** `Population[bodyId]` is now the only place a
   count is stored. `Colonisation` keeps the timer and which player is
   colonising, but the count moves out of it into `Population`.

2. **One label per planet.** `ColonisationView` is folded into
   `PlanetPopulationView`. Format:
   - Owned planet:           `Pop: N`
   - Planet being colonised: `Pop: N · T turns`
   - Contested planet:       both players' counts shown stacked
     (`Pop A: X` over `Pop B: Y` in each player's colour)
   - Unowned, not colonising: hidden

3. **Combat (contest).** When an opposing-player rocket arrives at an owned
   or being-colonised planet, a `Contest` begins. Each turn handover both
   sides lose `ceil(other side / 5)` people (floored at 1). Whichever side
   reaches 0 first loses; the survivor owns the planet at its remaining
   count. While contested, the planet is **not a launch site**.

4. **Bug fix + UX:** the cargo slider is shown whenever the active launch
   site is in `state.Ownership` (i.e. the player owns it), even when its
   pop is 0. Removes the surprising "slider just disappears" symptom and
   makes the launch state consistently legible.

Plus: the win condition is restored. `WinConditionChecker.CheckForWin`
returns the original "owns enemy home" logic. Combat is what makes capturing
the enemy home possible again.

## Scope

**In scope:**
- `Colonisation` data class: remove `ColonistCount`. Keep `PlayerId` and
  `TurnsRemaining`. The count lives in `Population[bodyId]` exclusively.
- `GameState.Contests`: new `Dictionary<int, Contest>` keyed by body ID.
- `Contest` data class: `int InvaderPlayerId`, `int InvaderCount`.
- `ColonisationResolver` extended outcomes: `StartContest`,
  `ReinforceContest_Invader`, `ReinforceContest_Defender`.
- New `Scripts/Combat/ContestTicker.cs`: per-turn damage tick + resolution.
- `LaunchSiteCalculator` excludes any body that appears in `state.Contests`.
- `PlanetPopulationView` replaces `ColonisationView` entirely (the view is
  retired). One label per planet, format depends on state.
- `LoadingUI` always visible when the active site is owned (Ownership entry
  exists), even if pop is 0.
- `WinConditionChecker.CheckForWin` reverts to the original "owns enemy home"
  logic.

**Out of scope (later jumps — DO NOT build now):**
- Build queues, tech trees, multi-resource economy.
- Body-type-specific yields (Lava → Metals, etc.).
- Defender bonus (e.g. home planets defend better).
- Tie-break resource gain (when both pops hit 0 simultaneously).
- Combat animations beyond the population numbers changing.

## Architectural rules

Same commitments. The new resolver and ticker stay pure-static, no Unity
deps. All `Population`, `Colonisations`, `Contests`, and `Ownership` writes
happen only inside `TurnManager`.

## Data model changes

### `Scripts/Strategy/Colonisation.cs`

Remove `ColonistCount`. The class becomes:

```csharp
namespace Orbital.Strategy
{
    public class Colonisation
    {
        public int PlayerId;
        public int TurnsRemaining;
    }
}
```

Any code that reads `col.ColonistCount` is rewritten to read
`state.Population[bodyId]` (or pass it through Completion).

### New file: `Scripts/Strategy/Contest.cs`

```csharp
namespace Orbital.Strategy
{
    /// <summary>
    /// An active combat on a single planet. Defender count lives in
    /// state.Population[bodyId]; defender player is the planet's current
    /// owner (or, if the planet is still being colonised, the colonising
    /// player). Invader count and player live here.
    /// </summary>
    public class Contest
    {
        public int InvaderPlayerId;
        public int InvaderCount;
    }
}
```

### `Scripts/Strategy/GameState.cs`

Add:

```csharp
public Dictionary<int, Contest> Contests { get; }
    = new Dictionary<int, Contest>();
```

## ColonisationResolver: extend the outcome matrix

Add three new `ColonisationOutcome` values:

```csharp
public enum ColonisationOutcome
{
    NoOp,
    Started,
    Reinforced,
    Blocked,                  // kept for genuine no-op cases (firing onto own owned planet → use Reinforced_Defender instead; Blocked now only fires when passengers == 0 on a planet with same-player ownership and no contest, i.e. nothing meaningful to do)
    StartContest,             // opposing-player rocket lands on owned or colonising planet, no existing contest
    ReinforceContest_Invader, // contest exists; rocket belongs to the invader → invader count grows
    ReinforceContest_Defender // contest exists; rocket belongs to the defender → Population[bodyId] grows
}
```

Updated `ColonisationChange`:

```csharp
public class ColonisationChange
{
    public ColonisationOutcome Outcome;
    public int BodyId;
    public int PlayerId;            // firing player
    public int PassengersDeployed;  // 0 when NoOp/Blocked
    public int NewColonistCount;    // post-op count: Population[bodyId] if Started/Reinforced, defender count if Reinforce_Defender, invader count if StartContest/Reinforce_Invader
    public int NewTurnsRemaining;   // only relevant for Started/Reinforced colonisation
}
```

`ColonisationResolver.Resolve` rule matrix:

| Planet state                | Firing = defender side  | Firing = opposing side |
|-----------------------------|-------------------------|------------------------|
| Unowned, no colonisation    | Started                 | Started                |
| Colonising (no contest)     | Reinforced              | **StartContest** (passengers become invader; existing colonisation pop stays as defender count) |
| Owned (no contest)          | Reinforce_Defender (Population[bodyId] += passengers; ownership unchanged) | **StartContest** |
| Colonising **and** contested| Reinforce_Defender if firing == colonising player; Reinforce_Invader if firing == existing invader; Blocked if neither (only 2 players so this is unreachable in practice) | as left |
| Owned **and** contested     | Reinforce_Defender if firing == owner; Reinforce_Invader if firing == existing invader | as left |
| Firing player's own home    | Reinforce_Defender if applicable; **NoOp** if no Ownership entry yet (game start) | n/a |

Notes:
- "Defender side" means: the current owner of the planet (if owned), OR the
  current colonising player (if being colonised).
- A `Reinforce_Defender` on an owned-no-contest planet means a player adds
  their own rocket's cargo to their own population on a planet they already
  own. Useful and intentional — players should be able to "resupply"
  themselves.

Where `Reinforce_Defender` happens with passengers > 0:
- If the planet is being colonised by you: `Population[bodyId] += passengers`,
  recompute turns from new pop using the existing formula.
- If the planet is owned by you (no colonisation): `Population[bodyId] += passengers`.
- If you have a contest on an owned planet of yours: `Population[bodyId] += passengers`.

Where `Reinforce_Invader` happens: `Contests[bodyId].InvaderCount += passengers`.

Where `StartContest` happens:
- A new `Contests[bodyId]` is created with `InvaderPlayerId = firingPlayerId`
  and `InvaderCount = passengers`.
- The defender side stays exactly as it was; defender count is read from
  `Population[bodyId]`. (For a planet that was being colonised but never
  owned, the colonising player is the defender; the Colonisation entry is
  kept so the timer continues — though see "Open question" below for an
  alternative.)

## New file: `Scripts/Combat/ContestTicker.cs`

Pure static, mirrors `ColonisationTicker` in shape:

```csharp
using System.Collections.Generic;
using Orbital.Strategy;

namespace Orbital.Combat
{
    public static class ContestTicker
    {
        public enum Resolution { Ongoing, DefenderWins, InvaderWins, MutualAnnihilation }

        public class Result
        {
            public int BodyId;
            public Resolution Resolution;
            public int DefenderPlayerId;     // the player who held the planet pre-contest
            public int InvaderPlayerId;
            public int FinalDefenderCount;   // post-tick Population[bodyId]
            public int FinalInvaderCount;    // post-tick Contests[bodyId].InvaderCount
        }

        public static List<Result> Tick(
            GameState state, int damageDivisor, int minDamage)
        {
            // For each Contests entry:
            //   defenderPlayerId =
            //     state.Ownership[bodyId].OwnerPlayerId if Owned,
            //     else state.Colonisations[bodyId].PlayerId
            //   defenderCount = state.Population.TryGetValue(bodyId, out v) ? v : 0
            //   invaderCount  = contest.InvaderCount
            //
            //   defenderLoss = Math.Max(minDamage, ceil(invaderCount / damageDivisor))
            //   invaderLoss  = Math.Max(minDamage, ceil(defenderCount / damageDivisor))
            //
            //   defenderCount -= defenderLoss
            //   invaderCount  -= invaderLoss
            //
            //   Apply: state.Population[bodyId] = max(0, defenderCount)
            //          contest.InvaderCount     = max(0, invaderCount)
            //
            //   Resolution:
            //     if defenderCount <= 0 && invaderCount <= 0: MutualAnnihilation
            //     elif defenderCount <= 0:                     InvaderWins
            //     elif invaderCount  <= 0:                     DefenderWins
            //     else:                                        Ongoing
            //
            //   Collect Result; if not Ongoing, remove from Contests.
        }
    }
}
```

`damageDivisor` and `minDamage` come from `StrategyParameters` (new fields,
defaults 5 and 1).

## TurnManager changes

1. **`AdvanceToNextPlayer()`** — extend the existing tick sequence:
   ```csharp
   // Existing colonisation tick.
   List<ColonisationTicker.Completion> completions = ColonisationTicker.Tick(_gameState);
   foreach (ColonisationTicker.Completion c in completions)
       ApplyColonisationCompletion(c);

   // New contest tick.
   int dmgDivisor = _strategyParams != null ? _strategyParams.ContestDamageDivisor : 5;
   int minDmg     = _strategyParams != null ? _strategyParams.ContestMinDamage : 1;
   List<ContestTicker.Result> results = ContestTicker.Tick(_gameState, dmgDivisor, minDmg);
   foreach (ContestTicker.Result r in results)
       ApplyContestResult(r);

   // Refresh views if anything changed.
   if (completions.Count > 0 || results.Count > 0)
   {
       RefreshOwnershipViews();
       RefreshPlanetPopulationViews();
   }

   // Existing win check, now active again.
   int? winner = WinConditionChecker.CheckForWin(_gameState);
   if (winner.HasValue) { EndGame(winner.Value); return; }

   // Existing flip + grow.
   _gameState.CurrentPlayerId = _gameState.CurrentPlayerId == 1 ? 2 : 1;
   _gameState.TurnNumber++;
   GrowOwnedPlanetPopulations(_gameState.CurrentPlayerId);
   RefreshPlanetPopulationViews();

   _loadingUI?.Hide();
   _gameState.Phase = GamePhase.BetweenTurns;
   _turnUI.Show(_gameState);
   ```

   `ApplyContestResult(ContestTicker.Result r)`:
   - On `DefenderWins`: do nothing extra. `state.Population[bodyId]` already
     holds the survivor's count. The defender's `Ownership` (if any) is
     unchanged.
   - On `InvaderWins`: write `Ownership[bodyId] = new PlanetOwnership { OwnerPlayerId = r.InvaderPlayerId, ... }`
     using existing OrbitingRocketView orbit params (or fallback defaults).
     Set `state.Population[bodyId] = r.FinalInvaderCount`. Remove any
     `Colonisation` entry on this body (defender was being colonised; that's
     cancelled now). Replace the OrbitingRocketView with the invader's
     colour.
   - On `MutualAnnihilation`: remove `Ownership[bodyId]` if present, remove
     `Colonisation[bodyId]` if present, set `state.Population[bodyId] = 0`
     (or remove the entry). Destroy the OrbitingRocketView. Planet becomes
     unowned.

2. **`HandleRocketResolved()`** — the existing `ColonisationResolver.Resolve`
   call covers more outcomes now. Branch on `change.Outcome`:
   - `Started`: write `Colonisation[bodyId] = { PlayerId, TurnsRemaining }`,
     set `Population[bodyId] = passengers`.
   - `Reinforced`: update `Colonisation[bodyId].TurnsRemaining`, set
     `Population[bodyId] += passengers`.
   - `Reinforce_Defender`: `Population[bodyId] += passengers`.
   - `Reinforce_Invader`: `Contests[bodyId].InvaderCount += passengers`.
   - `StartContest`: write `Contests[bodyId] = { InvaderPlayerId = current player, InvaderCount = passengers }`.
     If the planet was being colonised, leave the Colonisation entry intact —
     the contest tick will sort it out once the defender hits 0 or wins.
   - `Blocked` / `NoOp`: do nothing.

3. **`ColonisationTicker.Completion`** — now needs to read the count from
   `state.Population[bodyId]`, not from `Colonisation.ColonistCount` (which
   no longer exists). The Completion record's `FinalColonistCount` field is
   renamed to `FinalCount` and is set from `state.Population[bodyId]` at the
   moment of completion. `ApplyColonisationCompletion` no longer writes
   `state.Population[c.BodyId] = c.FinalCount` (it's already correct);
   instead it just writes the `Ownership` entry and spawns the rocket view.

4. **`SelectLaunchSite()`** — change the slider visibility check:
   ```csharp
   bool isOwned = _gameState.Ownership.ContainsKey(bodyId);
   bool isContested = _gameState.Contests.ContainsKey(bodyId);
   if (_gameState.Phase == GamePhase.WaitingForLaunch && isOwned && !isContested)
   {
       int available = _gameState.Population.TryGetValue(bodyId, out int v) ? v : 0;
       _loadingUI?.Show(available, current.Color);
   }
   else
   {
       _loadingUI?.Hide();
   }
   ```
   This is the **slider fix**: any owned, non-contested launch site shows
   the slider, even when its pop is 0 (the slider will display 0/0).

5. **`HandleRocketLaunched()`** — no functional change; the existing
   "subtract `load` from `Population[siteId]`" continues to work for the
   merged data model.

6. **Removed:** all references to `ColonisationView`. Delete the file.

## `LaunchSiteCalculator.Calculate` — exclude contested planets

Append a filter at the end:

```csharp
sites.RemoveAll(id => state.Contests.ContainsKey(id));
return sites;
```

Contested planets cannot fire rockets during the contest.

## PlanetPopulationView — single merged label

Replace the existing single-line label rendering with a state-aware renderer
that picks one of three layouts:

- **Owned, not contested, not being colonised:** one line, `Pop: N` in
  owner colour.
- **Being colonised (no contest):** one line, `Pop: N · T turns` in
  colonising player colour. Read N from `Population[bodyId]` and T from
  `Colonisation[bodyId].TurnsRemaining`.
- **Contested (regardless of colonisation):** two stacked lines:
  - Top line: defender colour, `Defender: X`.
  - Bottom line: invader colour, `Invader: Y`.
  Defender count is `Population[bodyId]`. Invader count is
  `Contests[bodyId].InvaderCount`.

Build the view as a vertical TMP stack (one TMP per line, both children of
the same canvas/positioning anchor). Show / hide the second line based on
contest presence.

A PlanetPopulationView is created for every planet that appears in
`Ownership` OR `Colonisation` OR `Contest` — i.e., any planet with state to
display. Updating the population-view dictionary based on the union of all
three keysets:

```csharp
private HashSet<int> CollectBodiesWithState()
{
    HashSet<int> ids = new HashSet<int>();
    foreach (int id in _gameState.Ownership.Keys)     ids.Add(id);
    foreach (int id in _gameState.Colonisations.Keys) ids.Add(id);
    foreach (int id in _gameState.Contests.Keys)      ids.Add(id);
    return ids;
}
```

`RefreshPlanetPopulationViews()` creates/destroys based on this set.

## StrategyParameters — new fields

Add under a new `[Header("Combat")]`:

```csharp
[Header("Combat")]
[Tooltip("Per turn each side loses ceil(other / divisor) people, floored at MinDamage.")]
[Min(1)]
public int ContestDamageDivisor = 5;

[Tooltip("Floor on damage per turn so combat always progresses.")]
[Min(0)]
public int ContestMinDamage = 1;
```

## Restoring the win condition

`Scripts/Combat/WinConditionChecker.cs`:

```csharp
public static int? CheckForWin(GameState state)
{
    foreach (Player player in state.Players)
        foreach (Player other in state.Players)
        {
            if (other.Id == player.Id) continue;
            if (state.Ownership.TryGetValue(other.HomeBodyId, out PlanetOwnership o)
                && o.OwnerPlayerId == player.Id)
                return player.Id;
        }
    return null;
}
```

(Identical to the original Classic check. It now actually fires because
combat allows enemy-home capture.)

`WinConditionChecker.CheckForWin` is called both at the end of
`HandleRocketResolved` (already wired) and after `ContestTicker` runs in
`AdvanceToNextPlayer` (newly added — see above).

## Tunables

`StrategyParameters` total list after Jump 5:

- `PopulationGrowthPerTurn` (default 10)
- `StartingPopulation` (default 0)
- `CapturedPlanetGrowthDivisor` (default 2)
- `ColonisationBaseDuration` (default 20)
- `MinColonisationTurns` (default 1)
- `ContestDamageDivisor` (default 5)         ← new
- `ContestMinDamage` (default 1)              ← new

## Determinism

- `ContestTicker.Tick` iterates `Contests` in whatever dictionary order
  — but each entry is independent, so order doesn't change outcomes.
- No randomness anywhere in combat. Same inputs produce same outputs.
- The slider value is still player input recorded at launch (eventually
  serialised in `TurnAction`).

## Success criteria

- Slider is visible on every owned-non-contested launch site, even when
  the planet's pop is 0. Sliding it does nothing when max=0; this is
  intended.
- A captured planet shows `Pop: N · T turns` during colonisation, then
  `Pop: N` after capture — one label, no separate colonist count.
- Firing 10 colonists onto an unowned planet displays `Pop: 10 · 2 turns`.
  Two turns later the planet is owned and the label simplifies to `Pop: 10`.
- Firing an opposing-player rocket onto an owned planet starts a Contest.
  Two stacked labels appear: defender count over invader count, each in the
  appropriate colour. The planet stops being a launch site (no rocket
  marker on it).
- The contest tick reduces both sides each turn handover by
  `ceil(other / 5)` (floored at 1). Eventually one side hits 0; the
  survivor owns the planet at its remaining count and the planet becomes a
  launch site again.
- Capturing the enemy home via contest immediately ends the game and shows
  the win screen.
- Firing your own rocket onto your own owned planet increments that
  planet's population by the rocket's cargo (defender reinforce on an
  uncontested planet works as a resupply).
- Pressing N at any time resets ownership, colonisation, contest, and
  population state cleanly.

## How to hand this to Claude Code

1. Confirm the spec lives at `docs/Phase4_Jump5_Spec.md`.
2. In a terminal:
   ```
   cd "C:\Users\leigh\Documents\Claude\Projects\Video games\Orbital_Experimental"
   claude
   ```
3. Use this prompt:

> Read CLAUDE.md and every existing doc under docs/ (Phase4 Jumps 1–4 specs and implementation notes, and docs/Phase4_Jump5_Spec.md). Implement Jump 5. Pay particular attention to:
> - Removing `ColonistCount` from `Colonisation`; the count is now only in `Population[bodyId]`.
> - Adding the new `Contest` data class and the `state.Contests` dictionary.
> - Extending `ColonisationOutcome` with `StartContest`, `ReinforceContest_Invader`, `ReinforceContest_Defender`.
> - Building `ContestTicker` parallel to `ColonisationTicker` and calling both inside `AdvanceToNextPlayer`.
> - Excluding contested planets from `LaunchSiteCalculator`.
> - Replacing `ColonisationView` with a state-aware `PlanetPopulationView` that shows: one line for owned, one line with `· T turns` suffix for colonising, two stacked lines for contested.
> - Slider visibility: show whenever the active launch site is in `state.Ownership` and not in `state.Contests`, even when pop is 0.
> - Restoring `WinConditionChecker.CheckForWin` to the original logic. Call it after the contest tick as well as after rocket resolution.
>
> Test plan you should consider while implementing:
> - Capture an unowned planet by colonising it: confirm single-label display, slider on subsequent launches.
> - Send an opposing rocket to a captured planet: confirm contest begins, two labels, no launch marker.
> - Resolve contest: confirm ownership flips correctly when invader wins.
> - Capture enemy home via contest: confirm win screen fires.
>
> When done, write `docs/Phase4_Jump5_Implementation_Notes.md` covering what changed, decisions beyond the spec, and any open questions.

After Claude Code reports done:
- Let Unity import.
- Resolve any compile errors in-session.
- Drag `StrategyParameters.asset` onto PSC again if Unity drops the reference
  (it sometimes does after data-class field changes).
- Play. Verify the success criteria. Tune `ContestDamageDivisor` if combat
  feels too fast (raise to 8 or 10) or too slow (lower to 3).
