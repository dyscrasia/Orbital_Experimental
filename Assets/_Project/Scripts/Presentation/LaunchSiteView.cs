using TMPro;
using Orbital.Physics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Orbital.Presentation
{
    /// <summary>
    /// Visual marker for a planet that the active player can launch from this turn.
    /// Shows a small rocket-shaped triangle in the player's color, plus a highlight
    /// ring when this site is the currently selected one.
    ///
    /// Also owns a per-site cargo slider that lets the player independently set how
    /// many people to load onto this rocket before firing. The slider is built as a
    /// Screen-Space-Overlay canvas child of this GameObject and positions itself below
    /// the planet via world-to-screen projection in LateUpdate.
    ///
    /// Visually distinct from OrbitingRocketView (which orbits captured planets):
    ///   • OrbitingRocketView: moves continuously around the planet, marks ownership.
    ///   • LaunchSiteView: static on the surface, marks a ready-to-fire rocket.
    ///
    /// Fires Selected(bodyId) when clicked. TurnManager ignores this event if the
    /// current phase is not WaitingForLaunch.
    /// </summary>
    public class LaunchSiteView : MonoBehaviour
    {
        /// <summary>Fired when the player clicks this launch site.</summary>
        public event System.Action<int> Selected;

        private const float ClickRadius      = 1.2f;
        private const float RocketTipY       =  0.35f;
        private const float RocketBaseHalfW  = 0.20f;
        private const float RocketBaseY      = -0.25f;
        private const float HighlightRadius  = 0.42f;
        private const int   HighlightSegments = 20;

        // Screen-space offsets from the planet centre (pixels, Y-up).
        // Label top 5 px below planet; slider top 30 px below planet.
        private const float LabelOffsetY  = -5f;
        private const float SliderOffsetY = -30f;

        private int _bodyId;
        private CelestialBody _body;
        private LineRenderer _rocketShape;
        private LineRenderer _highlight;
        private Camera _cam;

        // Cargo slider canvas (Screen-Space-Overlay, sortingOrder 9).
        private Slider _slider;
        private TextMeshProUGUI _cargoLabel;

        // -------------------------------------------------------------------------
        //  Public API — cargo
        // -------------------------------------------------------------------------

        /// <summary>Current slider value (whole number, 0 … max).</summary>
        public int CurrentLoad => _slider != null ? (int)_slider.value : 0;

        /// <summary>Set the slider maximum. Clamps current value if needed.</summary>
        public void SetMax(int max)
        {
            if (_slider == null) return;
            _slider.maxValue = Mathf.Max(0, max);
            if (_slider.value > _slider.maxValue) _slider.value = _slider.maxValue;
        }

        /// <summary>Reset the slider to 0 without changing the max.</summary>
        public void ResetLoad()
        {
            if (_slider == null) return;
            _slider.value = 0;
        }

        // -------------------------------------------------------------------------
        //  Public API — visual
        // -------------------------------------------------------------------------

        /// <summary>
        /// Position this view on the planet's surface and build its renderers and
        /// cargo slider. Default placement: directly above the planet (top of surface).
        /// </summary>
        public void Initialize(CelestialBody body, Color playerColor, float surfaceOffset = 0.7f)
        {
            _bodyId = body.Id;
            _body   = body;
            _cam    = Camera.main;

            // Place at the top of the planet surface.
            Vector2 surfacePos = body.Position + new Vector2(0f, body.Radius + surfaceOffset);
            transform.position = new Vector3(surfacePos.x, surfacePos.y, -0.15f);

            _rocketShape = BuildRocketShape(playerColor);
            _highlight   = BuildHighlight(playerColor);

            BuildCargoSlider(playerColor);

            SetActive(false);
        }

        /// <summary>
        /// Highlight this site as the currently selected launch site.
        /// When active: bright white ring + full-opacity rocket shape.
        /// When inactive: no ring + dim rocket shape.
        /// </summary>
        public void SetActive(bool active)
        {
            _highlight.enabled = active;

            Color dim = _rocketShape.startColor;
            dim.a = active ? 1.0f : 0.55f;
            _rocketShape.startColor = dim;
            _rocketShape.endColor   = dim;
        }

        // -------------------------------------------------------------------------
        //  Unity messages
        // -------------------------------------------------------------------------

        private void Update()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || _cam == null) return;
            if (!mouse.leftButton.wasPressedThisFrame) return;

            Vector2 worldPos = _cam.ScreenToWorldPoint(mouse.position.ReadValue());
            if ((worldPos - (Vector2)transform.position).magnitude <= ClickRadius)
                Selected?.Invoke(_bodyId);
        }

        private void LateUpdate()
        {
            if (_body == null || _cam == null || _slider == null) return;

            Vector3 screenPos = _cam.WorldToScreenPoint(
                new Vector3(_body.Position.x, _body.Position.y, 0f));

            // Slider: pivot (0.5, 1) — top of slider at anchoredPosition.
            _slider.GetComponent<RectTransform>().anchoredPosition =
                new Vector2(screenPos.x, screenPos.y + SliderOffsetY);

            // Label: pivot (0.5, 1) — top of label at anchoredPosition.
            _cargoLabel.rectTransform.anchoredPosition =
                new Vector2(screenPos.x, screenPos.y + LabelOffsetY);

            // Refresh label text.
            _cargoLabel.text = $"Cargo: {(int)_slider.value} / {(int)_slider.maxValue}";
        }

        // -------------------------------------------------------------------------
        //  Canvas and slider construction
        // -------------------------------------------------------------------------

        private void BuildCargoSlider(Color playerColor)
        {
            // Screen-Space-Overlay canvas parented to this GO for lifecycle management.
            // Sorting order 9 — above pop labels (5) but below TurnUI HUD (10).
            GameObject canvasGo = new GameObject("CargoSliderCanvas");
            canvasGo.transform.SetParent(transform, false);

            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 9;
            canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();

            // Cargo label — "Cargo: V / M" in player colour, positioned just above
            // the slider in LateUpdate.
            GameObject labelGo = new GameObject("CargoLabel");
            labelGo.transform.SetParent(canvasGo.transform, false);
            _cargoLabel = labelGo.AddComponent<TextMeshProUGUI>();
            _cargoLabel.fontSize         = 14;
            _cargoLabel.alignment        = TextAlignmentOptions.Center;
            _cargoLabel.color            = playerColor;
            _cargoLabel.outlineColor     = Color.black;
            _cargoLabel.outlineWidth     = 0.2f;
            _cargoLabel.textWrappingMode = TextWrappingModes.NoWrap;
            _cargoLabel.text             = "Cargo: 0 / 0";

            RectTransform labelRt = _cargoLabel.rectTransform;
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.zero;
            labelRt.pivot     = new Vector2(0.5f, 1f); // top-anchored: position drives top edge
            labelRt.sizeDelta = new Vector2(160f, 22f);

            // Slider — hierarchy mirrors LoadingUI.BuildSlider().
            _slider = BuildSlider(canvasGo);
            _slider.wholeNumbers = true;
            _slider.minValue     = 0f;
            _slider.maxValue     = 0f;
            _slider.value        = 0f;
        }

        private static Slider BuildSlider(GameObject parent)
        {
            // Root
            GameObject sliderGo = new GameObject("Slider");
            sliderGo.transform.SetParent(parent.transform, false);

            RectTransform sliderRt = sliderGo.AddComponent<RectTransform>();
            sliderRt.anchorMin = Vector2.zero;
            sliderRt.anchorMax = Vector2.zero;
            sliderRt.pivot     = new Vector2(0.5f, 1f); // top-anchored: LateUpdate drives top edge
            sliderRt.sizeDelta = new Vector2(160f, 20f);

            // Background
            GameObject bgGo = new GameObject("Background");
            bgGo.transform.SetParent(sliderGo.transform, false);
            RectTransform bgRt = bgGo.AddComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;
            Image bgImg = bgGo.AddComponent<Image>();
            bgImg.color = new Color(0.15f, 0.15f, 0.2f, 0.85f);

            // Fill Area
            GameObject fillAreaGo = new GameObject("Fill Area");
            fillAreaGo.transform.SetParent(sliderGo.transform, false);
            RectTransform fillAreaRt = fillAreaGo.AddComponent<RectTransform>();
            fillAreaRt.anchorMin = new Vector2(0f, 0f);
            fillAreaRt.anchorMax = new Vector2(1f, 1f);
            fillAreaRt.offsetMin = new Vector2(5f, 2f);
            fillAreaRt.offsetMax = new Vector2(-15f, -2f);

            // Fill
            GameObject fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(fillAreaGo.transform, false);
            RectTransform fillRt = fillGo.AddComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = new Vector2(0f, 1f);
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;
            Image fillImg = fillGo.AddComponent<Image>();
            fillImg.color = new Color(0.3f, 0.65f, 1f, 0.9f);

            // Handle Slide Area
            GameObject handleAreaGo = new GameObject("Handle Slide Area");
            handleAreaGo.transform.SetParent(sliderGo.transform, false);
            RectTransform handleAreaRt = handleAreaGo.AddComponent<RectTransform>();
            handleAreaRt.anchorMin = new Vector2(0f, 0f);
            handleAreaRt.anchorMax = new Vector2(1f, 1f);
            handleAreaRt.offsetMin = new Vector2(10f, 0f);
            handleAreaRt.offsetMax = new Vector2(-10f, 0f);

            // Handle
            GameObject handleGo = new GameObject("Handle");
            handleGo.transform.SetParent(handleAreaGo.transform, false);
            RectTransform handleRt = handleGo.AddComponent<RectTransform>();
            handleRt.sizeDelta = new Vector2(20f, 20f);
            Image handleImg = handleGo.AddComponent<Image>();
            handleImg.color = new Color(0.9f, 0.9f, 0.9f, 1f);

            // Wire up the Slider component
            Slider slider = sliderGo.AddComponent<Slider>();
            slider.fillRect      = fillRt;
            slider.handleRect    = handleRt;
            slider.targetGraphic = handleImg;
            slider.direction     = Slider.Direction.LeftToRight;

            return slider;
        }

        // -------------------------------------------------------------------------
        //  Renderer builders
        // -------------------------------------------------------------------------

        private LineRenderer BuildRocketShape(Color playerColor)
        {
            GameObject go = new GameObject("RocketShape");
            go.transform.SetParent(transform, false);

            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace  = false;
            lr.loop           = true;
            lr.positionCount  = 3;
            lr.startWidth     = 0.06f;
            lr.endWidth       = 0.06f;
            lr.numCapVertices = 2;
            lr.material       = new Material(Shader.Find("Sprites/Default"));

            Color c = new Color(playerColor.r, playerColor.g, playerColor.b, 0.55f);
            lr.startColor = c;
            lr.endColor   = c;

            // Upward-pointing triangle: tip at top, base at bottom
            lr.SetPosition(0, new Vector3(0f,                RocketTipY,  0f));
            lr.SetPosition(1, new Vector3( RocketBaseHalfW,  RocketBaseY, 0f));
            lr.SetPosition(2, new Vector3(-RocketBaseHalfW,  RocketBaseY, 0f));

            return lr;
        }

        private LineRenderer BuildHighlight(Color playerColor)
        {
            GameObject go = new GameObject("Highlight");
            go.transform.SetParent(transform, false);

            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace  = false;
            lr.loop           = true;
            lr.positionCount  = HighlightSegments;
            lr.startWidth     = 0.055f;
            lr.endWidth       = 0.055f;
            lr.numCapVertices = 2;
            lr.material       = new Material(Shader.Find("Sprites/Default"));

            // Bright white ring to indicate selection
            lr.startColor = new Color(1f, 1f, 1f, 0.90f);
            lr.endColor   = new Color(1f, 1f, 1f, 0.90f);

            for (int i = 0; i < HighlightSegments; i++)
            {
                float angle = i / (float)HighlightSegments * Mathf.PI * 2f;
                lr.SetPosition(i, new Vector3(
                    Mathf.Cos(angle) * HighlightRadius,
                    Mathf.Sin(angle) * HighlightRadius,
                    0f));
            }

            lr.enabled = false;
            return lr;
        }
    }
}
