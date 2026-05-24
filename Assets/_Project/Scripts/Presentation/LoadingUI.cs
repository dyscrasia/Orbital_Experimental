using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Orbital.Presentation
{
    /// <summary>
    /// Bottom-centre slider that lets the active player choose how many people
    /// to load into their home-planet rocket before firing.
    ///
    /// Created programmatically by TurnManager.Awake(). Show/Hide are called by
    /// TurnManager; this view never writes to GameState directly.
    /// </summary>
    public class LoadingUI : MonoBehaviour
    {
        /// <summary>Fired whenever the slider value changes.</summary>
        public event System.Action<int> OnLoadChanged;

        /// <summary>Currently selected cargo count (whole number).</summary>
        public int CurrentLoad => _slider != null ? (int)_slider.value : 0;

        private Slider _slider;
        private TextMeshProUGUI _label;
        private GameObject _canvasGo;

        // -------------------------------------------------------------------------
        //  Unity messages
        // -------------------------------------------------------------------------

        private void Awake()
        {
            BuildCanvas();
            Hide();
        }

        private void LateUpdate()
        {
            if (_label == null || _slider == null) return;
            _label.text = $"Cargo: {(int)_slider.value} / {(int)_slider.maxValue} available";
        }

        // -------------------------------------------------------------------------
        //  Public API
        // -------------------------------------------------------------------------

        /// <summary>
        /// Show the slider with the given max and recolour the label for the
        /// active player.
        /// </summary>
        public void Show(int max, Color playerColor)
        {
            _slider.maxValue = max;
            _slider.value    = Mathf.Clamp(_slider.value, 0f, max);
            _label.color     = playerColor;
            _canvasGo.SetActive(true);
        }

        public void Hide()
        {
            if (_canvasGo != null)
                _canvasGo.SetActive(false);
        }

        /// <summary>
        /// Update the slider maximum (e.g. when the active site changes mid-turn).
        /// Clamps the current value to [0, max].
        /// </summary>
        public void SetMax(int max)
        {
            _slider.maxValue = max;
            _slider.value    = Mathf.Clamp(_slider.value, 0f, max);
        }

        // -------------------------------------------------------------------------
        //  Canvas construction (mirrors OutcomeDisplay / TurnUI patterns)
        // -------------------------------------------------------------------------

        private void BuildCanvas()
        {
            _canvasGo = new GameObject("LoadingUICanvas");
            Canvas canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 8;
            _canvasGo.AddComponent<CanvasScaler>();
            _canvasGo.AddComponent<GraphicRaycaster>();

            // Label: "Cargo: N / M available" — sits above the slider
            GameObject labelGo = new GameObject("CargoLabel");
            labelGo.transform.SetParent(_canvasGo.transform, false);
            _label = labelGo.AddComponent<TextMeshProUGUI>();
            _label.fontSize     = 17;
            _label.alignment    = TextAlignmentOptions.Center;
            _label.color        = Color.white;
            _label.outlineColor = Color.black;
            _label.outlineWidth = 0.2f;
            _label.textWrappingMode = TextWrappingModes.NoWrap;

            RectTransform labelRt = _label.rectTransform;
            labelRt.anchorMin        = new Vector2(0.5f, 0f);
            labelRt.anchorMax        = new Vector2(0.5f, 0f);
            labelRt.pivot            = new Vector2(0.5f, 0f);
            labelRt.anchoredPosition = new Vector2(0f, 100f);
            labelRt.sizeDelta        = new Vector2(320f, 25f);

            // Slider — bottom centre, 300 px wide
            _slider = BuildSlider(_canvasGo);
            _slider.wholeNumbers = true;
            _slider.minValue     = 0f;
            _slider.maxValue     = 0f;
            _slider.value        = 0f;
            _slider.onValueChanged.AddListener(v => OnLoadChanged?.Invoke((int)v));
        }

        private static Slider BuildSlider(GameObject parent)
        {
            // Root
            GameObject sliderGo = new GameObject("Slider");
            sliderGo.transform.SetParent(parent.transform, false);

            RectTransform sliderRt = sliderGo.AddComponent<RectTransform>();
            sliderRt.anchorMin        = new Vector2(0.5f, 0f);
            sliderRt.anchorMax        = new Vector2(0.5f, 0f);
            sliderRt.pivot            = new Vector2(0.5f, 0f);
            sliderRt.anchoredPosition = new Vector2(0f, 68f);
            sliderRt.sizeDelta        = new Vector2(300f, 22f);

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
            slider.fillRect    = fillRt;
            slider.handleRect  = handleRt;
            slider.targetGraphic = handleImg;
            slider.direction   = Slider.Direction.LeftToRight;

            return slider;
        }
    }
}
