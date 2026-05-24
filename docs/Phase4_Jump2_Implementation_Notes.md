# Phase 4 Jump 2 — Implementation Notes

## What was built

### Modified files
- **`RocketState.cs`** — Added `public int PassengerCount` field and included it in
  `Clone()`. No other physics changes.
- **`PrototypeScenarioController.cs`** — Added `_rocket.PassengerCount = 0` in
  `PrepareRocketForPlayer()` immediately after `BuildRocket()`. Explicit reset so each
  launch starts clean (redundant given the new-object default, but semantically clear).
- **`TurnManager.cs`** — Multiple additions (see below).

### New files
- **`Scripts/Presentation/LoadingUI.cs`** — Screen-Space-Overlay canvas (sorting order 8)
  containing a `TextMeshProUGUI` label and a `UnityEngine.UI.Slider`, both created
  programmatically in `Awake()`. The slider is built as a full Unity UI Slider hierarchy
  (Background / Fill Area / Fill / Handle Slide Area / Handle) matching the pattern used
  internally by Unity's default UI.

- **`Scripts/Presentation/RocketPassengerLabel.cs`** — Screen-Space-Overlay canvas
  (sorting order 6) with a single TMP label that follows the rocket in screen space.
  Canvas is created lazily on the first `Initialize()` call (not in `Awake`) so the
  camera is guaranteed to be ready. Re-initialized each `SelectLaunchSite` to pick up
  the fresh `RocketState` reference after `PrepareRocketForPlayer` replaces the object.

### TurnManager additions
- `_loadingUI` field — created in `Awake()` alongside `TurnUI` / `WinScreenUI`.
- `_rocketPassengerLabel` field — created once in `BeginGame()` with a null guard;
  re-initialized on every `BeginGame()` call to stay in sync with the current rocket.
- `SelectLaunchSite()` — appended loading UI visibility logic (home site +
  WaitingForLaunch = show; otherwise hide) and `_rocketPassengerLabel.Initialize(...)`.
- `HandleRocketLaunched()` — reads slider value, zeros it for non-home sites, writes
  `PassengerCount`, subtracts from population, hides slider — all before setting phase.
- `EndTurn()`, `AdvanceToNextPlayer()`, `EndGame()` — each calls `_loadingUI?.Hide()`.

## Decisions beyond the spec

- **Canvas sorting orders**: LoadingUI = 8, RocketPassengerLabel = 6, HomePopulationView
  canvas = 5, TurnUI = 10, OutcomeDisplay = 10. This stacks them below the HUD but above
  world geometry.
- **Slider vertical position**: `anchoredPosition.y = 68` (in screen pixels from bottom).
  This clears the TurnUI prompt text (which occupies approximately y=18–58). Adjust if
  the prompt and slider overlap at a particular screen resolution.
- **Label vertical position**: `anchoredPosition.y = 100`, placing it 32 px above the
  slider track.
- **`RocketPassengerLabel` text** is the raw integer (`_rocket.PassengerCount.ToString()`)
  rather than a longer string — the spec leaves text format open and a bare number is
  least intrusive for a label that floats over the rocket.
- **`LoadingUI.LateUpdate`** keeps the label text live every frame instead of only
  updating on `onValueChanged`. This is consistent with the `HomePopulationView` pattern
  and avoids a stale display on `SetMax` calls that don't move the handle.
- **`_loadingUI?.Hide()` in `AdvanceToNextPlayer()`**: the spec requests it; it is also
  called inside `EndTurn()` before `AdvanceToNextPlayer()`, so this is defensive but
  harmless.

## Open questions

- **Slider reset on site switch**: `Show(max, color)` clamps but does not zero the
  slider when switching from home to home (e.g. if a second home-launch bonus ever
  exists). Currently it preserves the previous value clamped to the new max. Zeroing
  on every `Show()` call would be safer once Jump 3 introduces capacity caps.
- **Population going negative**: `HandleRocketLaunched` subtracts `load` from
  population with no lower-bound guard beyond the slider's max. If the player loads
  the full population and fires, population reaches 0, which is fine. If two rockets
  with cargo were ever allowed in the same turn (not currently possible), a guard
  `Mathf.Max(0, pop - load)` would be needed.
- **Lost passengers on crash/escape**: as noted in the spec, passengers are simply lost
  on non-orbit outcomes — there is no refund. This is the intended placeholder; Jump 3
  will define what happens to colonists when a rocket crashes.
- **TurnAction serialisation**: the chosen `PassengerCount` is not yet recorded in a
  `TurnAction`. Once the serialisation layer is built (needed for replay determinism),
  the slider value at launch must be captured alongside launch direction and thrust.
