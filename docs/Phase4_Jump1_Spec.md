# Orbital (Strategy variant) — Phase 4, Jump 1: Home-planet population

## Goal

Introduce per-player population as a counted resource on the home planet.
Population accumulates each turn at a fixed rate. It is **displayed only** in
this jump — nothing in the game spends it yet. This is the smallest possible
foundation for the colonisation mechanic that follows.

After this jump, the game plays exactly as it does today (Classic rocket rule,
orbit = instant capture) — but each home planet now shows a population counter
that grows each turn.

## Scope

**In scope:**
- A per-player population counter stored in `GameState`.
- Population grows by a configurable amount at the start of each player's turn.
- A new on-screen label near each home planet showing that player's current
  population.
- Population resets to its starting value on `NewGame`.
- Tunable growth rate and starting value exposed via a new ScriptableObject
  (`StrategyParameters`).

**Out of scope (later jumps — DO NOT build now):**
- Loading population into rockets.
- Subtracting population on launch.
- Population on captured (non-home) planets.
- Colonist deposits, colonisation timers, combat between groups.
- Any change to the capture/orbit mechanic.
- Any change to the Classic rocket production rule (1 + floor(captures/2)).
- UI for selecting how many people to load.
- Population caps, decay, plagues, etc.

## Architectural rules

The same commitments in `CLAUDE.md` apply:
- Population state lives in `GameState` (pure data, no Unity deps).
- The growth tick runs deterministically once per player turn — no
  `Time.deltaTime`, no `UnityEngine.Random`.
- Tunable values live on a ScriptableObject, not in code.
- Presentation reads population from `GameState`; it never writes to it.

## Data model changes

### `Scripts/Strategy/GameState.cs`

Add a dictionary keyed by player ID:

```csharp
/// <summary>Population owned by each player. Currently lives only on the home planet;
/// future jumps may distribute population to other planets.</summary>
public Dictionary<int, int> Population { get; } = new Dictionary<int, int>();
```

No other GameState changes.

### `Scripts/Strategy/Player.cs`

**No changes.** `Player` stays immutable; population is mutable game state and
belongs in `GameState`.

## New file: `Scripts/Strategy/StrategyParameters.cs`

A new ScriptableObject for Strategy-variant tunables. This jump puts two
fields on it; future jumps will add more (rocket capacity, colonisation rate,
combat rates, etc.).

```csharp
using UnityEngine;

namespace Orbital.Strategy
{
    [CreateAssetMenu(fileName = "StrategyParameters",
                     menuName = "Orbital/Strategy Parameters")]
    public class StrategyParameters : ScriptableObject
    {
        [Header("Population")]
        [Tooltip("People added to a player's home planet at the start of each of " +
                 "their turns.")]
        public int PopulationGrowthPerTurn = 10;

        [Tooltip("Starting population for each player on turn 1.")]
        public int StartingPopulation = 0;
    }
}
```

Create the asset (`StrategyParameters.asset`) in `Assets/_Project/Data/` and
leave fields at their defaults.

## Growth tick

The growth tick fires **at the start of each player's turn**. The natural
hook is inside `TurnManager.AdvanceToNextPlayer()` (or alternatively at the
top of `TurnManager.StartTurn()` — `AdvanceToNextPlayer` is preferred because
it runs once per turn handover regardless of whether the player presses Space).

Modifications to `Scripts/Presentation/TurnManager.cs`:

1. Add a serialized field:
   ```csharp
   [Header("Strategy")]
   [SerializeField] private StrategyParameters _strategyParams;
   public StrategyParameters StrategyParams => _strategyParams;
   ```

2. In `BeginGame()`, after creating the GameState and before setting Phase,
   initialise both players' populations to `StartingPopulation`:
   ```csharp
   int start = _strategyParams != null ? _strategyParams.StartingPopulation : 0;
   _gameState.Population[p1.Id] = start;
   _gameState.Population[p2.Id] = start;
   ```

3. Insert a population grant at the start of every turn. Place it inside
   `AdvanceToNextPlayer()`, after `CurrentPlayerId` flips and `TurnNumber++`,
   but before `Phase = BetweenTurns`:
   ```csharp
   int growth = _strategyParams != null ? _strategyParams.PopulationGrowthPerTurn : 10;
   if (_gameState.Population.ContainsKey(_gameState.CurrentPlayerId))
       _gameState.Population[_gameState.CurrentPlayerId] += growth;
   else
       _gameState.Population[_gameState.CurrentPlayerId] = growth;
   ```

   Note: this means Player 1 receives population at the *start* of their
   second turn onwards. Player 1's turn 1 starts with `StartingPopulation`
   exactly (no growth applied yet). Both players reach the same turn count
   before their first growth tick, so it's symmetric.

4. PSC wiring: `PrototypeScenarioController.cs` instantiates TurnManager via
   `gameObject.AddComponent<TurnManager>()` — the `[SerializeField]` won't be
   wired by drag-and-drop because TurnManager is added programmatically. Add
   a public field on PSC (`StrategyParameters StrategyParams`) that the
   inspector accepts, and assign it onto the TurnManager via a setter or
   public field immediately after AddComponent. Mirror the existing pattern
   used for `GalaxyParameters`.

## UI: home-planet population label

### New file: `Scripts/Presentation/HomePopulationView.cs`

A MonoBehaviour that draws a population count next to one home planet. One
instance per player. Style mirrors `LaunchSiteView` / `OrbitingRocketView`:
created programmatically by `TurnManager.BeginGame()`, stored in a dictionary,
destroyed and re-created on `NewGame`.

Behaviour:
- Holds references to: the home `CelestialBody`, the player's ID, the player's
  colour.
- Spawns a `TextMeshProUGUI` child (a screen-space Canvas works; or a
  world-space TMP — choose whichever matches the existing in-scene UI pattern
  most closely). Existing `OutcomeDisplay` uses `TextMeshProUGUI` in a
  Screen-Space-Overlay canvas; follow that convention.
- Each `LateUpdate`, position the label at the home planet's world position
  projected to screen space, offset a few pixels above the planet sprite.
- Each `LateUpdate`, read `GameState.Population[playerId]` (TurnManager exposes
  this via the `GameState` property) and update the text to `Pop: N` in the
  player's colour.
- Hides itself if `GameState` is null or the player ID is missing from the
  Population dict.

A single shared canvas may be used for both home labels — create it on demand
in `HomePopulationView.Awake()` or, cleaner, have `TurnManager` create one
shared canvas and pass it in via `Initialize(...)`.

### `TurnManager` additions

- Add a dictionary: `Dictionary<int, HomePopulationView> _homePopulationViews`.
- In `BeginGame()`, after creating ownership views, instantiate one
  `HomePopulationView` for each player's home and store it in the dictionary.
- Add a `ClearHomePopulationViews()` helper that destroys and clears the dict.
- Call `ClearHomePopulationViews()` at the top of `BeginGame()` alongside the
  existing `ClearOwnershipViews()` / `ClearOrbitingRocketViews()` / `ClearLaunchSiteViews()` calls.

## Tunable values (final list)

On `StrategyParameters.asset`:
- `PopulationGrowthPerTurn = 10`
- `StartingPopulation = 0`

That's it. No other tunables in this jump.

## Determinism check

Population growth is `+10 per turn`, applied in `AdvanceToNextPlayer` which
already advances `TurnNumber`. There is no randomness anywhere in this jump.
A given seed + sequence of player actions produces the same Population values
on every replay. No `Rng` calls needed.

## Success criteria

- Start a new game. Both home planets show `Pop: 0`.
- Player 1 fires their rocket(s) and ends turn. Player 2's label is still
  `Pop: 0`. (Growth fires for Player 2 at start of their turn.)
- Player 2's turn starts. Player 2's label now reads `Pop: 10`.
- Player 2 ends turn. Player 1's label reads `Pop: 10` (Player 1's second
  turn just started).
- After ten full back-and-forth turns, each player has `Pop: 90` (Player 1
  has had 9 growth ticks, Player 2 has had 9 growth ticks).
- New Game (N) resets both populations to `0`.
- Changing `StartingPopulation` or `PopulationGrowthPerTurn` on the
  ScriptableObject and pressing N produces the new values without recompiling.

## How to hand this to Claude Code

1. Confirm this spec lives at `docs/Phase4_Jump1_Spec.md`.
2. Open Claude Code from the project root.
3. Use this prompt:

> Read `CLAUDE.md` and `docs/Phase4_Jump1_Spec.md` carefully. Implement Jump 1
> of Phase 4. Make only the changes the spec describes — do not refactor
> unrelated code, do not change the capture or rocket-production rules, do
> not add UI beyond the home-population label. Match the existing patterns
> in `TurnManager`, `OrbitingRocketView`, and `OutcomeDisplay`.
>
> When done, write a short summary at `docs/Phase4_Jump1_Implementation_Notes.md`
> covering what you built, any decisions beyond the spec, and any open questions.

After Claude Code reports done:
- Open Unity, let it import.
- Create the `StrategyParameters.asset` via `Assets > Create > Orbital > Strategy Parameters`.
- Wire it onto the `PrototypeScenarioController` in the scene.
- Press Play, verify the success criteria above.

Tune `PopulationGrowthPerTurn` if 10/turn feels wrong — it almost certainly
will once Jump 2 (loading rockets) lands and the value starts to *matter*.
