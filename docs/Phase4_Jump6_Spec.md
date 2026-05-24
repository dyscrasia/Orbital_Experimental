# Orbital (Strategy variant) — Phase 4, Jump 6: Per-rocket cargo sliders

## Goal

Each launch-site rocket marker gets its own cargo slider, positioned next
to that planet, with its own max (the planet's current pop) and its own
value. The player can set cargo on every rocket independently before firing
them. The shared bottom-of-screen `LoadingUI` is retired.

This makes the relationship between "rocket" and "how many people on it"
literal and physical: each rocket has a slider attached to it.

## Scope

**In scope:**
- A child slider on every `LaunchSiteView`, positioned near the planet
  in screen space (mirrors how `PlanetPopulationView` and `ColonisationView`
  position themselves).
- Each slider's max equals `state.Population[siteId]` at the moment the
  launch site is spawned. Default value 0.
- When a player drags a particular slider, only that site's pending cargo
  changes.
- When a rocket fires, `HandleRocketLaunched` reads the load from the *active*
  `LaunchSiteView`'s slider, not from the shared `LoadingUI`.
- The shared `LoadingUI` is hidden permanently in this jump. (Keep the class
  in code for now in case we revert; just stop showing it.)

**Out of scope:**
- Sliders updating dynamically if pop changes mid-turn (it can't, in
  practice — a site is removed from `AvailableLaunchSites` once its rocket
  fires).
- Reset-to-max buttons, "load all" hotkeys, fine-grained number entry.
- Touching the in-flight `RocketPassengerLabel` — that stays as-is.

## Architectural rules

- The slider is a view-layer widget on `LaunchSiteView`. It does not write
  to `GameState`; values are read at fire time.
- No `Time.deltaTime`, no `UnityEngine.Random`. Slider position updates use
  `LateUpdate` and the camera-space projection of the planet's position,
  same pattern already used in `PlanetPopulationView`.

## Modifications to `Scripts/Presentation/LaunchSiteView.cs`

Add the slider as a sibling of the existing visual marker. Pattern mirrors
the lazily-created canvases used elsewhere:

1. In `Initialize(CelestialBody body, Color playerColor)` (existing signature),
   build a new Screen-Space-Overlay canvas as a child of this GameObject.
   Sorting order above the planet population label but below the TurnUI HUD —
   `sortingOrder = 9` is fine (TurnUI is 10, PlanetPopulationView is 5).

2. Inside the canvas, build a `UnityEngine.UI.Slider` (with the full
   Background / Fill Area / Fill / Handle Slide Area / Handle hierarchy
   Unity expects — copy the construction from `LoadingUI.Awake()` as a
   reference). Set `wholeNumbers = true`, `minValue = 0`, `maxValue = 0`,
   `value = 0`.

3. Add a small `TextMeshProUGUI` label above the slider showing
   `Cargo: V / M` in the player's colour. Update in `LateUpdate` to mirror
   the slider's current value and max.

4. Position the canvas via world-to-screen projection of the body's
   position. Offset downward by ~30 px so the slider sits **below** the
   planet (the population label is above, the slider is below). If multiple
   bodies cluster tightly this will overlap occasionally — fine for now,
   we can tune the offsets later.

5. Public surface to add:
   ```csharp
   public int CurrentLoad => _slider != null ? (int)_slider.value : 0;
   public void SetMax(int max)
   {
       if (_slider == null) return;
       _slider.maxValue = Mathf.Max(0, max);
       if (_slider.value > _slider.maxValue) _slider.value = _slider.maxValue;
   }
   public void ResetLoad()
   {
       if (_slider == null) return;
       _slider.value = 0;
   }
   ```

6. The existing `SetActive(bool)` highlight method stays. The active site's
   slider is the one whose value matters at launch time; we don't visually
   gate the *other* sliders. All sliders accept input. The active site is
   simply where the rocket is currently positioned for aiming.

## Modifications to `Scripts/Presentation/TurnManager.cs`

1. **`StartTurn()`**, immediately after the existing site-view creation loop:
   ```csharp
   foreach (int siteId in _gameState.AvailableLaunchSites)
   {
       if (_launchSiteViews.TryGetValue(siteId, out LaunchSiteView v) && v != null)
       {
           int pop = _gameState.Population.TryGetValue(siteId, out int p) ? p : 0;
           v.SetMax(pop);
           v.ResetLoad();
       }
   }
   ```

2. **`HandleRocketLaunched()`** — replace the `_loadingUI.CurrentLoad` read
   with the active site's slider value:
   ```csharp
   int siteId = _gameState.ActiveLaunchSiteId;

   int load = 0;
   if (_launchSiteViews.TryGetValue(siteId, out LaunchSiteView siteView) && siteView != null)
       load = siteView.CurrentLoad;

   int siteAvailable = _gameState.Population.TryGetValue(siteId, out int sitePop) ? sitePop : 0;
   if (load > siteAvailable) load = siteAvailable; // defensive

   _psc.Rocket.PassengerCount = load;
   if (load > 0)
       _gameState.Population[siteId] = siteAvailable - load;

   // Existing flow continues — hide the (now-permanently-hidden) shared slider,
   // refresh views, advance phase.
   _loadingUI?.Hide();
   RefreshPlanetPopulationViews();
   _gameState.Phase = GamePhase.RocketInFlight;
   _turnUI.Show(_gameState);
   ```

3. **`SelectLaunchSite()`** — remove the slider show/hide code entirely.
   The per-site sliders are independent of selection; clicking a different
   site changes the active rocket for aiming, not which slider is visible.
   The block to remove:
   ```csharp
   // Show slider for owned sites; hide for colonising-only or contested sites.
   bool isOwned     = _gameState.Ownership.ContainsKey(bodyId);
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
   Replace with a single call `_loadingUI?.Hide();` to keep the shared slider
   off-screen.

4. **`EndTurn()`, `AdvanceToNextPlayer()`, `EndGame()`** — these already call
   `_loadingUI?.Hide()`. Leave those calls in place (they're now redundant
   but harmless). The per-site sliders go away when the LaunchSiteViews are
   destroyed, which already happens in `ClearLaunchSiteViews()` and on
   per-rocket `DestroyLaunchSiteView` calls.

## Optional: keep the shared slider hidden but in code

Don't delete `LoadingUI.cs` or its instantiation in `Awake()` for this jump.
Leaving the class wired but never `Show()`n means we can revert with a
one-line change if per-rocket sliders feel cluttered in practice. If they
do feel fine after a few games, a follow-up trivial commit can delete
`LoadingUI` entirely.

## EventSystem reminder

The new per-site sliders depend on the same `EventSystem` GameObject in the
scene that the previous shared slider did. If the experimental project's
scene has the EventSystem (it should, given the earlier shared slider
worked), nothing extra is needed. If sliders don't respond after this jump,
the EventSystem check is the first thing to verify.

## Success criteria

- Press Space at turn start → every launch-site marker has a small slider
  beneath it.
- Each slider's max equals its planet's current population.
- Dragging slider on site X only changes site X's cargo. Site Y's slider is
  unaffected.
- Switching the active site (clicking a different marker) does **not**
  reset any slider value.
- Firing from a site consumes that site's cargo, dropping that site's pop
  by exactly the slider value. Other sites' values are unchanged.
- Contested planets have no LaunchSiteView (already covered by Jump 5
  exclusion) so they have no slider.
- The shared bottom-of-screen LoadingUI is never visible.

## How to hand this to Claude Code

1. Confirm the spec lives at `docs/Phase4_Jump6_Spec.md`.
2. In a terminal:
   ```
   cd "C:\Users\leigh\Documents\Claude\Projects\Video games\Orbital_Experimental"
   claude
   ```
3. Use this prompt:

> Read CLAUDE.md and the most recent Phase 4 docs (specifically Jump 5's spec and implementation notes, and docs/Phase4_Jump6_Spec.md). Implement Jump 6. The change is focused: move the cargo slider from a shared bottom-of-screen LoadingUI to a per-LaunchSiteView widget. Pay particular attention to:
> - Mirror the canvas construction used in LoadingUI.Awake() when building the per-site slider; reuse the standard Unity Slider hierarchy.
> - Position each per-site slider below its planet via world-to-screen projection, similar to how PlanetPopulationView positions itself above.
> - HandleRocketLaunched now reads load from the active LaunchSiteView, not from the shared LoadingUI.
> - SelectLaunchSite no longer shows/hides the shared LoadingUI; just hide it once and leave it.
> - StartTurn calls SetMax and ResetLoad on every newly-spawned LaunchSiteView.
>
> Do not delete LoadingUI.cs — keep it in code (hidden via Hide()) so we can revert easily.
>
> When done, write docs/Phase4_Jump6_Implementation_Notes.md covering what changed, decisions beyond the spec, and any open questions.

After Claude Code reports done:
- Let Unity import.
- Resolve any compile errors in-session.
- Play. Verify the success criteria. Specifically: drag one slider, observe
  the others are unaffected; fire from each in turn, observe pops drop
  correctly per-site.
