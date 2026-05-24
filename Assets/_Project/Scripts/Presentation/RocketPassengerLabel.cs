using TMPro;
using Orbital.Physics;
using UnityEngine;
using UnityEngine.UI;

namespace Orbital.Presentation
{
    /// <summary>
    /// Displays the rocket's PassengerCount as a small floating label just above
    /// the rocket sprite while it is in flight or on the launch pad with cargo loaded.
    ///
    /// Created once by TurnManager.BeginGame() and kept alive across launches.
    /// Re-initialized in TurnManager.SelectLaunchSite() each time a new rocket is
    /// prepared, so the label always holds a fresh RocketState reference.
    ///
    /// Mirrors the screen-space projection pattern from HomePopulationView.
    /// </summary>
    public class RocketPassengerLabel : MonoBehaviour
    {
        private RocketState _rocket;
        private TextMeshProUGUI _label;
        private Camera _cam;

        private const float ScreenOffsetY = 28f;

        // -------------------------------------------------------------------------
        //  Public API
        // -------------------------------------------------------------------------

        /// <summary>
        /// Set the rocket to track and recolour the label.
        /// Safe to call multiple times (canvas is created only on the first call).
        /// </summary>
        public void Initialize(RocketState rocket, Color playerColor)
        {
            _rocket = rocket;

            if (_label == null)
            {
                _cam = Camera.main;
                BuildCanvas();
            }

            _label.color = playerColor;
        }

        // -------------------------------------------------------------------------
        //  Unity messages
        // -------------------------------------------------------------------------

        private void LateUpdate()
        {
            if (_label == null || _rocket == null) return;

            bool resolved = _rocket.Status == RocketStatus.Crashed
                         || _rocket.Status == RocketStatus.Orbited
                         || _rocket.Status == RocketStatus.Escaped;

            if (resolved || _rocket.PassengerCount == 0)
            {
                _label.gameObject.SetActive(false);
                return;
            }

            _label.gameObject.SetActive(true);
            _label.text = _rocket.PassengerCount.ToString();

            Vector3 screenPos = _cam.WorldToScreenPoint(
                new Vector3(_rocket.Position.x, _rocket.Position.y, 0f));
            _label.rectTransform.anchoredPosition =
                new Vector2(screenPos.x, screenPos.y + ScreenOffsetY);
        }

        // -------------------------------------------------------------------------
        //  Canvas construction (mirrors HomePopulationView)
        // -------------------------------------------------------------------------

        private void BuildCanvas()
        {
            GameObject canvasGo = new GameObject("RocketPassengerLabelCanvas");
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 6;
            canvasGo.AddComponent<CanvasScaler>();

            GameObject labelGo = new GameObject("PassengerCount");
            labelGo.transform.SetParent(canvasGo.transform, false);

            _label = labelGo.AddComponent<TextMeshProUGUI>();
            _label.fontSize     = 16;
            _label.alignment    = TextAlignmentOptions.Center;
            _label.outlineColor = Color.black;
            _label.outlineWidth = 0.25f;

            RectTransform rt = _label.rectTransform;
            rt.anchorMin  = Vector2.zero;
            rt.anchorMax  = Vector2.zero;
            rt.pivot      = new Vector2(0.5f, 0f);
            rt.sizeDelta  = new Vector2(60f, 25f);

            _label.gameObject.SetActive(false);
        }
    }
}
