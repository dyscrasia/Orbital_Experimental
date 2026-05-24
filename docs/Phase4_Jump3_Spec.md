# Orbital (Strategy variant) — Phase 4, Jump 3: Colonisation replaces instant capture

## Goal

Orbital capture no longer immediately flips planet ownership. Instead, when a
rocket carrying passengers orbits a non-home planet, those passengers
**deposit to the surface** and a **colonisation timer** begins. The planet is
captured (ownership flips) when the timer expires. Larger landings finish
sooner. Rockets keep orbiting visually after dropping their cargo.

This is the largest jump in Phase 4. It changes the *defining* rule of the
game — capture itself.

## Scope

**In scope:**
- New `Colonisation` data record on `GameState`, one entry per planet
  currently being colonised.
- `ColonisationResolver` (pure static): given an orbital capture, returns a
  description of what happens (start / reinforce / blocked / no-op).
- `ColonisationTicker` (pure static): called once per turn handover,
  decrements every colonisation by 1 and converts any that hit 0 into actual
  ownership.
- Visual indicator (`ColonisationView`) above each colonising planet showing
  the colonising player's colour, current colonist count, and turns remaining.
- The `OrbitingRocketView` is now spawned on **every** orbital capture (so the
  rocket stays in orbit as a visual beacon), regardless of whether ownership
  or colonisation state changed.
- Two new tunables on `StrategyParameters`:
  `ColonisationBaseDuration` (default 20) and `MinColonisationTurns` (default 1).
- **Win condition is temporarily disabled** — see the dedicated section below.

**Out of scope (Jump 4 — DO NOT build now):**
- Any form of combat between opposing colonist groups.
- Population growing on captured planets.
- Decay of colonisation progress when a colonisation is interrupted or
  abandoned.
- A win condition for the Strategy variant. Jump 4 restores winning via
  combat against the enemy home.
- Cargo refund on rocket crash/escape (passengers still simply vanish on
  non-orbit outcomes, same as Jump 2).

## Architectural rules

- `Colonisation`, `GameState`, `ColonisationResolver`, and `ColonisationTicker`
  are pure data / pure static — no Unity dependencies.
- `ColonisationTicker` is deterministic from `GameState` alone; no time, no
  randomness.
- Mutations to `GameState.Colonisations` and `GameState.Ownership` happen
  exclusively in `TurnManager`. Resolvers return descriptions; the manager
  applies them.
- `ColonisationView` is read-only — it observes `GameState` and never mutates it.

## Data model changes

### New file: `Scripts/Strategy/Colonisation.cs`

```csharp
namespace Orbital.Strategy
{
    /// <summary>
    /// One in-progress colonisation. Lives in GameState.Colonisations keyed by body ID.
    /// Removed when the timer reaches 0 (the planet becomes owned)
    /// or when the colonisation is cancelled by some future mechanic.
    /// </summary>
    public class Colonisation
    {
        public int PlayerId;
        public int ColonistCount;
        public int TurnsRemaining;
    }
}
```

### `Scripts/Strategy/GameState.cs`

Add a new dictionary alongside `Ownership`:

```csharp
/// <summary>In-progress colonisations keyed by body ID. A planet may appear
/// in Colonisations XOR Ownership (never both — completion moves it).</summary>
public Dictionary<int, Colonisation> Colonisations { get; }
    = new Dictionary<int, Colonisation>();
```

No other GameState changes.

### `Scripts/Strategy/StrategyParameters.cs`

Add two fields under a new `[Header("Colonisation")]`:

```csharp
[Header("Colonisation")]
[Tooltip("Total colonist-turns needed to capture a planet. " +
         "Turns to complete = max(MinColonisationTurns, ceil(BaseDuration / colonists)).")]
public int ColonisationBaseDuration = 20;

[Tooltip("Floor on turns-to-complete, even with very large colonist counts.")]
public int MinColonisationTurns = 1;
```

## New file: `Scripts/Combat/ColonisationResolver.cs`

A pure static class that decides what happens when a rocket orbits a planet.
It does NOT mutate state — it returns a description.

```csharp
using Orbital.Strategy;

namespace Orbital.Combat
{
    public enum ColonisationOutcome
    {
        NoOp,            // 0 passengers, or own home, or own already-owned planet
        Started,         // unowned planet, colonisation begins
        Reinforced,      // same-player colonisation extends (passengers added)
        Blocked          // opposing-player colonisation or opposing-owned planet — passengers lost
    }

    public class ColonisationChange
    {
        public ColonisationOutcome Outcome;
        public int BodyId;
        public int PlayerId;       // the firing player
        public int PassengersDeployed; // 0 when Blocked / NoOp
        public int NewColonistCount;   // count on the planet after the operation
        public int NewTurnsRemaining;  // turns remaining after the operation
    }

    public static class ColonisationResolver
    {
        public static ColonisationChange Resolve(
            GameState state,
            int firingPlayerId,
            int bodyId,
            int passengers,
            int baseDuration,
            int minTurns)
        {
            // ...
        }
    }
}
```

The `Resolve` logic (the rule matrix):

| Planet state           | Same firing player                | Different player                          |
|------------------------|-----------------------------------|-------------------------------------------|
| Unowned, not colonising| Start colonisation                | Start colonisation                        |
| Colonising             | Reinforce (add to count + retime) | **Blocked** (passengers lost)             |
| Owned                  | NoOp                              | **Blocked** (Jump 4 will add combat here) |
| Firing player's own home| NoOp                             | n/a                                       |

Edge cases:
- `passengers == 0` → `NoOp` regardless of planet state.
- Home planets (either player's) cannot be colonised at all in Jump 3 — they
  are owned from game start, so Blocked covers it automatically.

The turns formula:
```csharp
int newCount = existingCount + passengers; // or just passengers if new
int turns = System.Math.Max(minTurns, (baseDuration + newCount - 1) / newCount);
```
(`(a + b - 1) / b` is integer ceiling for positive ints.)

## New file: `Scripts/Combat/ColonisationTicker.cs`

```csharp
using System.Collections.Generic;
using Orbital.Strategy;

namespace Orbital.Combat
{
    public static class ColonisationTicker
    {
        /// <summary>Completed colonisations from the most recent tick — used by
        /// TurnManager to spawn ownership views and effects.</summary>
        public class Completion
        {
            public int BodyId;
            public int PlayerId;
            public int FinalColonistCount;
        }

        /// <summary>Decrement every colonisation by 1. Any that hit 0 are
        /// converted into Ownership entries and returned. Mutates state.</summary>
        public static List<Completion> Tick(GameState state)
        {
            // ...
        }
    }
}
```

The tick:
- For each `(bodyId, colonisation)` in `state.Colonisations`:
  - `colonisation.TurnsRemaining -= 1`
  - if `TurnsRemaining <= 0`: collect a `Completion`, remove from
    `Colonisations`, add an entry to `Ownership` with that player as owner.
- Returns the list of completions for the caller (TurnManager) to refresh
  views.

Important: the `Ownership` entry for a completed colonisation needs the
existing kinematic-orbit fields populated. Use the current orbiting rocket
on that planet as the source — `_psc.Rocket` won't work because it's the
*next* rocket. TurnManager keeps `_orbitingRockets` (a Dict<int, OrbitingRocketView>);
read the orbit parameters from the view's stored state. (If no view exists
because the rocket has been replaced and lost its orbit state, fall back to
sensible defaults: `OrbitRadius = body.Radius * 2`, angle 0, speed
`Mathf.Sqrt(G * body.Mass) / radius`, direction +1.)

Cleanest: have the ticker take an optional callback or return enough info
that TurnManager can construct the PlanetOwnership itself. The simplest
shape is: ticker returns `Completion` records; TurnManager builds the
`PlanetOwnership` using either the existing OrbitingRocketView's data or
the fallback defaults.

## Modifications to `Scripts/Combat/OwnershipResolver.cs`

The old `ResolveCapture` method is no longer called from
`HandleRocketResolved`. We have two options:

**Recommended:** delete the method (no callers remain after this jump) or
mark it `[System.Obsolete]` with a note pointing to ColonisationResolver.
Either approach is fine — deletion is cleaner.

OwnershipChange itself can stay; nothing references it after this jump but
removing it is not required.

## Modifications to `Scripts/Combat/WinConditionChecker.cs`

Temporarily disable the win check. Two options, equally valid:

```csharp
public static int? CheckForWin(GameState state)
{
    // Jump 3: capture rule has changed and the enemy home can no longer be
    // taken (no combat yet). Disabling the win check until Jump 4 restores
    // enemy-home capture via combat.
    return null;
}
```

Leave the rest of the file intact and the comment in place so it's obvious
this is temporary.

## Modifications to `Scripts/Presentation/TurnManager.cs`

This is the biggest set of edits. Outline:

1. **Field additions:**
   ```csharp
   private readonly Dictionary<int, ColonisationView> _colonisationViews
       = new Dictionary<int, ColonisationView>();
   ```

2. **`HandleRocketResolved(Outcome outcome, int capturedBodyId)`** — replace
   the OwnershipResolver call with a ColonisationResolver call. Outline:

   ```csharp
   if (outcome == Outcome.Orbited && capturedBodyId >= 0)
   {
       int passengers = _psc.Rocket.PassengerCount;
       Player current = _gameState.CurrentPlayer;

       int baseDur = _strategyParams != null ? _strategyParams.ColonisationBaseDuration : 20;
       int minTurns = _strategyParams != null ? _strategyParams.MinColonisationTurns : 1;

       ColonisationChange change = ColonisationResolver.Resolve(
           _gameState, current.Id, capturedBodyId, passengers, baseDur, minTurns);

       ApplyColonisationChange(change);

       // Always spawn / replace the OrbitingRocketView on this body so the
       // rocket is visible as it orbits, regardless of whether colonisation
       // started or was blocked.
       SpawnOrReplaceOrbitingRocketView(capturedBodyId, current.Color);

       RefreshColonisationViews();
   }

   // Win check intentionally still called — it's now a no-op (returns null).
   int? winnerId = WinConditionChecker.CheckForWin(_gameState);
   // ... (existing code continues unchanged)
   ```

   `ApplyColonisationChange` mutates `_gameState.Colonisations` according to
   the `Outcome`:
   - `Started`: add a new entry with `(PlayerId, PassengersDeployed, NewTurnsRemaining)`.
   - `Reinforced`: update existing entry's count and turns to the values in
     the change.
   - `Blocked` / `NoOp`: do nothing.

   `SpawnOrReplaceOrbitingRocketView` factors out the existing pattern (in
   the current `ApplyOwnershipChange`) of destroying any existing
   `OrbitingRocketView` on the body and spawning a new one for the current
   player. **Important:** it should use the *current* `_psc.Rocket`'s orbit
   parameters at the moment of capture so the visual rocket continues from
   where the physical one stopped.

3. **`AdvanceToNextPlayer()`** — tick colonisations *before* flipping the
   player. The tick belongs to the player who is ending their turn (their
   colonisations have just had a full turn-cycle to progress).

   Actually, simpler and more symmetric: tick once per turn handover, regardless
   of which player is up next. Insert at the top of `AdvanceToNextPlayer`:

   ```csharp
   List<ColonisationTicker.Completion> completions = ColonisationTicker.Tick(_gameState);
   foreach (ColonisationTicker.Completion c in completions)
   {
       // Build PlanetOwnership using the current orbiting view's data, or
       // fallback defaults if no view is present.
       ApplyColonisationCompletion(c);
   }

   if (completions.Count > 0)
   {
       RefreshOwnershipViews();
       RefreshColonisationViews();
   }
   ```

   `ApplyColonisationCompletion(c)` reads the existing `OrbitingRocketView` on
   `c.BodyId` (if any), constructs a `PlanetOwnership` with that view's
   orbit parameters, and stores it in `_gameState.Ownership`. The view itself
   is then re-coloured to the new owner (the ticker has already added the
   ownership entry so `RefreshOwnershipViews` will pick this up).

4. **`BeginGame()`** — add a cleanup line for the new dictionary:
   ```csharp
   ClearColonisationViews();
   ```
   and after the existing ownership setup, clear `_gameState.Colonisations`
   to be safe:
   ```csharp
   _gameState.Colonisations.Clear();
   ```

5. **`EndGame()`** — call `ClearColonisationViews()` for cleanliness.

6. **Helpers to add:**
   - `ApplyColonisationChange(ColonisationChange change)` — mutates state.
   - `ApplyColonisationCompletion(Completion c)` — builds PlanetOwnership.
   - `RefreshColonisationViews()` — creates / destroys / updates
     `ColonisationView` instances based on `_gameState.Colonisations`.
   - `ClearColonisationViews()` — destroys all views and clears the dict.
   - `SpawnOrReplaceOrbitingRocketView(int bodyId, Color color)` — extracted
     from the existing `ApplyOwnershipChange` logic.

## New file: `Scripts/Presentation/ColonisationView.cs`

A `MonoBehaviour` that floats above a colonising planet. Pattern mirrors
`HomePopulationView` from Jump 1 (Screen-Space-Overlay canvas, TMP label,
worldToScreen each `LateUpdate`).

Behaviour:
- `Initialize(CelestialBody body, GameState state)` — stores references.
- `LateUpdate`:
  - Look up `state.Colonisations[body.Id]`. If absent, hide and return.
  - Position the canvas at `body.Position` projected to screen space, offset
    upward.
  - Text: `"{count} colonists · {turnsRemaining} turns"` in the colonising
    player's colour.
- Destroyed by `TurnManager.RefreshColonisationViews()` when the entry
  disappears from `Colonisations` (either via capture completion or future
  cancellation).

`TurnManager.RefreshColonisationViews()` logic:
- For each entry in `state.Colonisations`, ensure a view exists; create if missing.
- For each view, check whether its body still appears in `Colonisations`; if
  not, destroy and remove from `_colonisationViews`.

## Behaviour walkthrough (sanity check)

1. New game. Both home planets owned, no Colonisations. P1 has Pop:0, P2 has Pop:0.
2. P1 turn 1: slider max=0, fires empty rocket at a nearby unowned planet.
   Orbital capture: `passengers=0` → `NoOp`. Rocket stays in orbit. No
   colonisation begins. Planet remains unowned.
3. P2 turn 1: same — empty rocket, NoOp.
4. P1 turn 2 (after pop tick to 10): loads 10, fires at planet X.
   Orbital capture: `Started`, 10 colonists, turns = ceil(20/10) = 2. Planet
   X shows ColonisationView "10 colonists · 2 turns" in P1's colour. Pop
   drops to 0.
5. End P1 turn → AdvanceToNextPlayer → Tick: X goes to 1 turn remaining.
6. P2 turn 2: P2 has pop=10. Loads 5, fires at unowned planet Y. Started, 5
   colonists, turns = ceil(20/5) = 4.
7. End P2 turn → AdvanceToNextPlayer → Tick: X goes to 0 → completion → X
   moves from Colonisations to Ownership for P1. P1's PlanetOwnershipView
   turns blue (or whatever P1 colour is). ColonisationView for X disappears.
   Y ticks from 4 → 3.
8. Continue. P1 can reinforce Y (if P2 owned it, no — Blocked) or start
   colonisations on other unowned planets.
9. New Game (N) clears Colonisations and resets all state.

## Tunables (post-jump)

`StrategyParameters`:
- `PopulationGrowthPerTurn` — already exists. Likely needs tuning down or up
  depending on how many planets are reachable.
- `StartingPopulation` — already exists.
- `ColonisationBaseDuration = 20`. Higher = more turns to capture a planet
  with one colonist. With 10 colonists default loading, that's 2 turns to
  capture per landing.
- `MinColonisationTurns = 1`. Could be raised to 2 or 3 if instant captures
  with huge cargos feel cheap.

## Determinism

- `ColonisationResolver.Resolve` is pure: given the same state and inputs,
  it returns the same `ColonisationChange`. No randomness.
- `ColonisationTicker.Tick` iterates the dictionary in whatever order; this
  is fine because the operations on different bodies are independent. No
  randomness.
- The win check is disabled (returns null), so no behaviour depends on it.

## Win condition (deliberately removed)

The Classic win condition — "a player owns the other player's home planet" —
is currently unreachable in Jump 3 because home planets are owned at game
start and `Blocked` covers any attempt to colonise them.

`WinConditionChecker.CheckForWin` returns null. The game continues
indefinitely; players colonise unowned planets but no one wins. **This is
intentional**: it isolates the colonisation mechanic so we can tune it
without interference. Jump 4 introduces combat that allows colonisation of
*owned* planets including the enemy home, which simultaneously restores a
meaningful win condition.

## Success criteria

- Empty rockets fired into orbit → planet shows OrbitingRocketView but no
  Colonisation entry, no ColonisationView, no ownership change.
- 10-colonist rocket into orbit of an unowned planet → ColonisationView
  shows "10 colonists · 2 turns" in player colour. Pop on home dropped by 10.
- Two turns later (after both players each take a turn) the same planet
  becomes owned by that player (colour ring appears, ColonisationView
  disappears).
- Reinforcing your own colonisation: count adds, timer recomputes.
- Firing onto a planet currently being colonised by the opposing player →
  rocket parks in orbit but the existing colonisation is unaffected.
- Firing onto an owned planet (either yours or enemy's) → no colonisation.
- Game never ends; pressing N starts over with all state cleared.
- All views (ColonisationView, OrbitingRocketView, HomePopulationView) clean
  up on New Game.

## How to hand this to Claude Code

1. Confirm the spec lives at `docs/Phase4_Jump3_Spec.md`.
2. In a terminal:
   ```
   cd "C:\Users\leigh\Documents\Claude\Projects\Video games\Orbital_Experimental"
   claude
   ```
3. Use this prompt:

> Read CLAUDE.md, docs/Phase4_Jump1_Spec.md, docs/Phase4_Jump1_Implementation_Notes.md, docs/Phase4_Jump2_Spec.md, docs/Phase4_Jump2_Implementation_Notes.md, and docs/Phase4_Jump3_Spec.md carefully. Implement Jump 3. Pay particular attention to:
> - Keeping ColonisationResolver and ColonisationTicker pure-static, no Unity deps
> - Always spawning the OrbitingRocketView on every orbital capture, not just on ownership changes
> - The temporary win-condition disable (return null with a comment, do not delete the method)
> - Matching the existing view-lifecycle pattern (Refresh/Clear pairs) for the new ColonisationViews dictionary
>
> Do not introduce combat between opposing colonisations — Jump 4 handles that.
>
> When done, write a short summary at docs/Phase4_Jump3_Implementation_Notes.md covering what you built, decisions beyond the spec, and any open questions.

After Claude Code reports done:
- Let Unity import.
- Resolve any compile errors in-session.
- Play. Verify the success criteria. Tune `ColonisationBaseDuration` if
  captures feel too fast or too slow. Remember: the game has no win
  condition in this jump — play continues until N.
