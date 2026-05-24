# Phase 4 Jump 6 — Implementation Notes

## What was built

### Modified files

- **`Scripts/Presentation/LaunchSiteView.cs`** — Added per-site cargo slider.
  - Stores `_body` (for LateUpdate projection) and `_playerColor`.
  - `BuildCargoSlider(Color)` creates a Screen-Space-Overlay canvas (sortingOrder 9)
    as a child of the LaunchSiteView GO, a `TextMeshProUGUI` cargo label, and a
    full Unity Slider hierarchy via the new static `BuildSlider(GameObject)` helper.
    Slider hierarchy is identical to `LoadingUI.BuildSlider()`: Background image,
    Fill Area → Fill image, Handle Slide Area → Handle image, all wired to
    `slider.fillRect`, `slider.handleRect`, and `slider.targetGraphic`.
  - `LateUpdate` projects `_body.Position` to screen space and sets
    `slider.GetComponent<RectTransform>().anchoredPosition` and
    `_cargoLabel.rectTransform.anchoredPosition` each frame to keep both
    elements tracking the planet. Offsets: label top at `screenPos.y − 5 px`,
    slider top at `screenPos.y − 30 px`. Both use `pivot = (0.5, 1)` (top-anchored)
    so the anchor point drives the top edge.
  - New public surface: `CurrentLoad`, `SetMax(int)`, `ResetLoad()`.
  - Canvas is a child of the LaunchSiteView GO, so it is destroyed automatically
    when `Destroy(view.gameObject)` is called; no `OnDestroy` override needed.

- **`Scripts/Presentation/TurnManager.cs`**
  - **`StartTurn()`** — after the `foreach` loop that spawns `LaunchSiteView`
    instances, a second pass calls `v.SetMax(pop)` and `v.ResetLoad()` for every
    newly created view, where `pop` is `state.Population[siteId]` (0 if absent).
  - **`HandleRocketLaunched()`** — reads `load` from
    `_launchSiteViews[siteId].CurrentLoad` instead of `_loadingUI.CurrentLoad`.
    Defensive clamp against `siteAvailable` is kept.
  - **`SelectLaunchSite()`** — the entire `isOwned / isContested` slider show/hide
    block is replaced with a single `_loadingUI?.Hide()`. The per-site sliders are
    independent of which site is "active"; all sliders accept input at all times.

### Unchanged files

- **`Scripts/Presentation/LoadingUI.cs`** — kept in code, never `Show()`d. The
  `Hide()` calls in `EndTurn`, `AdvanceToNextPlayer`, `EndGame`, and
  `HandleRocketLaunched` are now redundant but harmless.

## Decisions beyond the spec

- **Pivot `(0.5, 1)` on both slider and label** — The spec says "offset downward
  by ~30 px". Using a top-anchored pivot means `anchoredPosition.y` directly
  controls where the top edge lands, making the offset arithmetic unambiguous.
  The pop label (in `PlanetPopulationView`) uses pivot `(0.5, 0)` (bottom-anchored)
  because it sits above the planet; cargo elements sit below, so the inverse pivot
  is more natural.
- **`BuildSlider` is `private static`** — same visibility as `LoadingUI.BuildSlider`.
  No cross-class sharing is needed; the two classes build identical hierarchies
  independently.
- **Canvas as child of LaunchSiteView GO** — Screen-Space-Overlay canvases render
  in screen space regardless of their position in the scene hierarchy. Parenting to
  the LaunchSiteView GO gives free lifecycle management: `Destroy(view.gameObject)`
  also destroys the canvas, with no `OnDestroy` override required.
- **All sliders visible simultaneously** — The spec says "all sliders accept input".
  No visibility toggling on site selection; only the active site's slider value is
  read at fire time (`HandleRocketLaunched` uses `_gameState.ActiveLaunchSiteId`).

## Open questions

- **Label / slider overlap with clustered planets** — The spec acknowledges this.
  Offsets (`LabelOffsetY = -5`, `SliderOffsetY = -30`) are the first guess; they
  may need tuning once the galaxy layout is played with. A future jump could apply
  a column-avoidance pass similar to bubble charts.
- **`LoadingUI` deletion** — `LoadingUI.cs` is kept. If per-site sliders feel good
  after a few games, a follow-up trivial commit can delete `LoadingUI.cs`,
  `_loadingUI` in TurnManager, and the `Hide()` calls scattered through `EndTurn`,
  `AdvanceToNextPlayer`, and `EndGame`.
- **Slider input versus click detection** — Both the `Slider` component and
  `LaunchSiteView.Update` use the `GraphicRaycaster` / `EventSystem` path for
  input. There is potential for a drag on the slider to also register as a click
  on the LaunchSiteView. In practice the click-detection radius (`ClickRadius =
  1.2` world units) is small and the slider is in screen space, so accidental
  site-switching during a drag is unlikely; no guard has been added yet.
