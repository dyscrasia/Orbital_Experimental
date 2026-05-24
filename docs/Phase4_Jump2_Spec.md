# Orbital (Strategy variant) — Phase 4, Jump 2: Loading people onto rockets

## Goal

Let the player choose how many people to load into a rocket before firing it
from their home planet. Loaded people are subtracted from the home planet's
population. The rocket displays its cargo count while in flight.

People on the rocket have **no gameplay effect on resolution yet** — capture,
crash, and escape behave exactly as they do today. Jump 3 will introduce
colonist deposit and change the capture rule. This jump is purely "people get
on the rocket and visibly travel with it."

## Scope

**In scope:**
- `RocketState` gains a `PassengerCount` field (preserved on `Clone`).
- A slider UI to set the cargo count between 0 and the player's available
  population (the value living in `GameState.Population[currentPlayerId]`).
- On launch, the slider value is copied to the rocket and subtracted from the
  player's population. The home-population label from Jump 1 updates
  immediately.
- A small floating label next to the in-flight rocket showing the passenger
  count, coloured for the current player.
- The slider is hidden when the active launch site is not the player's home
  (bonus rockets from captured planets fire with `PassengerCount = 0`).
- The slider is hidden during `RocketInFlight` and `BetweenTurns` / `GameOver`.

**Out of scope (later jumps — DO NOT build now):**
- Any change to the capture mechanic (orbit still = instant capture).
- Colonist deposit, colonisation timer, surface population, combat.
- Population growing on captured planets.
- Per-rocket capacity caps (max cargo = whatever population the player has).
- Population on captured planets ⇒ ability to load from non-home sites.
- Refunding passengers on a failed launch (if the rocket crashes, the people
  are simply lost for now — a sensible placeholder until Jump 3).

## Architectural rules

Same commitments in `CLAUDE.md` apply:
- `RocketState` stays a pure data class with no Unity dependencies.
- Population mutation happens in `TurnManager` (game-state layer), not in UI.
- The slider is a view: it reads from `GameState`, emits a value, but never
  writes to `GameState` directly. `TurnManager` is the only writer.
- No `Time.deltaTime` involved (the change is event-driven on launch).
- No `UnityEngine.Random` involved.

## Data model changes

### `Scripts/Physics/RocketState.cs`

Add one field:

```csharp
/// <summary>People loaded onto this rocket at launch. Set by TurnManager
/// just before HandleRocketLaunched runs. Has no effect on physics; consumed
/// by the resolution step in a later jump.</summary>
public int PassengerCount;
```

Include it in `Clone()`:

```csharp
PassengerCount = PassengerCount,
```

No other data classes change.

## New file: `Scripts/Presentation/LoadingUI.cs`

A `MonoBehaviour` that owns one `UnityEngine.UI.Slider` and one
`TextMeshProUGUI` label, both inside a shared Screen-Space-Overlay canvas
(reuse the convention used by `OutcomeDisplay` / `TurnUI`; create a single
canvas if needed and parent both UI elements under it).

Layout: bottom-centre of the screen. Slider ~300 px wide. Label sits above
the slider and reads e.g. `Cargo: 12 / 47 available` in the active player's
colour.

Behaviour:
- Public method `Show(int max, Color playerColor)` — set slider's max,
  reset value to 0, recolour the label, make the canvas/group visible.
- Public method `Hide()` — make the canvas/group invisible.
- Public method `SetMax(int max)` — change the slider's max (used when the
  active launch site changes). Clamp current value to `[0, max]`.
- Public property `int CurrentLoad` — returns `(int)slider.value`.
- Public event `Action<int> OnLoadChanged` — fired whenever the slider
  value changes (used to update the home-population label preview if we want
  to dim/highlight the "post-launch" count; **for Jump 2 this event exists
  but TurnManager does NOT need to subscribe — leave the wire for Jump 3+**).
- In `Awake`, build the canvas, slider, and label programmatically (same
  pattern as `OutcomeDisplay`). The slider uses whole-number steps
  (`wholeNumbers = true`) so `slider.value` is always an integer.
- `LateUpdate` keeps the label text in sync with the slider value
  (`Cargo: {value} / {max} available`).

## New file: `Scripts/Presentation/RocketPassengerLabel.cs`

A `MonoBehaviour` that displays the rocket's `PassengerCount` floating just
above the rocket sprite while it's in flight (or on the launch pad with a
non-zero cargo).

Behaviour:
- Holds references to: the `RocketState`, a `Camera` (Camera.main), and an
  internal `TextMeshProUGUI` on a small Screen-Space-Overlay canvas (mirror
  the `HomePopulationView` pattern from Jump 1).
- Public method `Initialize(RocketState rocket, Color playerColor)`.
- `LateUpdate` reads `rocket.PassengerCount`. If `> 0`, position the label at
  the rocket's world position projected to screen space (offset a few pixels
  upward) and set the text. If `== 0`, hide the label.
- Hidden entirely while `rocket.Status == Crashed | Orbited | Escaped`
  (cargo is no longer meaningful once the flight resolves in Jump 2).

The class is reasonably small — share canvas with `HomePopulationView` if
practical, or use its own; either is fine.

## Modifications to `Scripts/Presentation/TurnManager.cs`

Add private fields:

```csharp
private LoadingUI _loadingUI;
private RocketPassengerLabel _rocketPassengerLabel;
```

In `Awake()`, create the LoadingUI alongside TurnUI / WinScreenUI:

```csharp
GameObject loadGo = new GameObject("LoadingUI");
_loadingUI = loadGo.AddComponent<LoadingUI>();
_loadingUI.Hide();
```

In `BeginGame()`, create the RocketPassengerLabel and initialise it against
PSC's rocket (the label persists across launches; it just hides itself when
the cargo is zero or the rocket is resolved):

```csharp
if (_rocketPassengerLabel == null)
{
    GameObject labelGo = new GameObject("RocketPassengerLabel");
    _rocketPassengerLabel = labelGo.AddComponent<RocketPassengerLabel>();
}
_rocketPassengerLabel.Initialize(_psc.Rocket, /* default colour — set per turn */ Color.white);
```

(Re-`Initialize` in `BeginGame` so it picks up a fresh `_psc.Rocket` after
galaxy regeneration. Colour is updated each turn — see below.)

In `SelectLaunchSite(int bodyId)`, after the existing logic, update the
LoadingUI's visibility and max based on whether the selected site is the
current player's home:

```csharp
Player current = _gameState.CurrentPlayer;
bool isHomeSite = bodyId == current.HomeBodyId;
if (_gameState.Phase == GamePhase.WaitingForLaunch && isHomeSite)
{
    int available = _gameState.Population.TryGetValue(current.Id, out int p) ? p : 0;
    _loadingUI.Show(available, current.Color);
}
else
{
    _loadingUI.Hide();
}
// Also: refresh the in-flight label colour each turn.
_rocketPassengerLabel?.Initialize(_psc.Rocket, current.Color);
```

In `HandleRocketLaunched()`, copy the slider value onto the rocket and
subtract from population **before** the rest of the method runs:

```csharp
int load = _loadingUI != null ? _loadingUI.CurrentLoad : 0;

// Only home launches consume population. Bonus rockets always launch with 0.
Player current = _gameState.CurrentPlayer;
bool isHomeSite = _gameState.ActiveLaunchSiteId == current.HomeBodyId;
if (!isHomeSite) load = 0;

_psc.Rocket.PassengerCount = load;
if (load > 0 && _gameState.Population.ContainsKey(current.Id))
    _gameState.Population[current.Id] -= load;

_loadingUI.Hide();

// existing body of HandleRocketLaunched continues here
```

After a rocket resolves and the next site is auto-selected (the
`if (_gameState.AvailableLaunchSites.Count > 0) { ... SelectLaunchSite(nextSite); }`
branch already calls `SelectLaunchSite`), the LoadingUI will be re-shown by
that call. No extra code needed.

In `EndTurn()` and `AdvanceToNextPlayer()`, hide the slider for cleanliness:

```csharp
_loadingUI?.Hide();
```

In `EndGame(int winnerId)`, hide the slider too.

## Modifications to `Scripts/Presentation/PrototypeScenarioController.cs`

PSC currently resets the rocket via `PrepareRocketForPlayer(int bodyId)`. The
rocket's `PassengerCount` should be **reset to 0** on every prepare so a
fresh launch starts from a clean slate. Add one line near the existing reset
code in that method:

```csharp
Rocket.PassengerCount = 0;
```

(If `PrepareRocketForPlayer` does not currently reset rocket fields directly,
add the line wherever the rocket's per-launch reset happens — usually next to
`Status = Prelaunch`.)

## Behaviour walkthrough (sanity check)

1. New game. Both home planets show `Pop: 0`. LoadingUI hidden.
2. Player 1 presses Space → `StartTurn` runs → active site is P1 home →
   `SelectLaunchSite(P1.HomeBodyId)` runs → LoadingUI shown with max 0.
3. Player 1 fires the rocket anyway (cargo 0). Rocket flies normally with
   no passenger label. Resolution happens as today. Turn passes to P2.
4. P2's turn starts → `AdvanceToNextPlayer` grants P2 +10 population →
   P2 presses Space → LoadingUI shown with max 10.
5. P2 drags the slider to 5 → label shows `Cargo: 5 / 10 available`.
6. P2 fires → `PassengerCount = 5`, P2 population becomes 5, the in-flight
   rocket shows a small `5` label following it.
7. Rocket orbits a planet → ownership flips (Classic rule unchanged) →
   the `5` label disappears because `Status == Orbited`.
8. P2 ends turn. P1's turn starts → P1 has now received 10 population from
   their second-turn growth tick → LoadingUI max = 10.
9. New Game (N) → populations reset, slider hidden until first
   `WaitingForLaunch`.

## Tunables

None new. Slider min is 0, max is whatever population the current player has
on the active turn. Future jumps will introduce per-rocket capacity caps.

## Determinism

The slider value is player input, recorded as the launch's parameters; the
subtraction is deterministic; the in-flight display is read-only. No new
random calls. The TurnAction layer (eventually) will need to record the
chosen passenger count alongside the launch direction/thrust — but
serialising `TurnAction` is not part of this jump.

## Success criteria

- Fresh game: slider visible during P1's first turn with max 0; firing
  consumes nothing and behaves exactly like today.
- After several turns each player can load any value from 0 to their full
  available population; the home-population label drops by exactly that
  amount at launch.
- The in-flight rocket displays its cargo count above the sprite in the
  active player's colour. The label disappears at resolution.
- Bonus rockets (fired from captured non-home planets via the Classic rule)
  always launch with 0 passengers; the slider is hidden when one of them is
  the active site.
- Switching between launch sites within a turn (clicking a different
  highlighted planet) updates the slider's max correctly: home shows
  available population, bonus shows hidden / 0.
- Pressing N resets all population and hides the slider.

## How to hand this to Claude Code

1. Confirm the spec lives at `docs/Phase4_Jump2_Spec.md`.
2. In a terminal, `cd "C:\Users\leigh\Documents\Claude\Projects\Video games\Orbital_Experimental"`.
3. Run `claude`.
4. Use this prompt:

> Read CLAUDE.md, docs/Phase4_Jump1_Spec.md, docs/Phase4_Jump1_Implementation_Notes.md, and docs/Phase4_Jump2_Spec.md carefully. Implement Jump 2. Do not change the capture rule, do not change rocket production, do not touch trajectory or physics. Only the changes the spec calls out, matching the patterns used in TurnUI, OutcomeDisplay, HomePopulationView, and OrbitingRocketView.
>
> When done, write a short summary at docs/Phase4_Jump2_Implementation_Notes.md covering what you built, decisions beyond the spec, and any open questions.

After Claude Code reports done:
- Let Unity import.
- Resolve any compile errors in-session.
- Play. Verify the success criteria. If the slider's placement / styling is
  off, tweak in the Inspector or note it for a later polish pass — the
  *behaviour* matters more than the look in this jump.
