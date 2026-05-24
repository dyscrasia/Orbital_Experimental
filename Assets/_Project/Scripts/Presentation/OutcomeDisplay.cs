using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Orbital.Presentation
{
    /// <summary>
    /// Displays shot outcome text (Crashed / Orbited / Escaped) on screen.
    /// Expects a TextMeshProUGUI sibling or child; auto-creates a Canvas if needed.
    /// </summary>
    public class OutcomeDisplay : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI _text;

        private void Awake()
        {
            _text = GetComponentInChildren<TextMeshProUGUI>(includeInactive: true);
            if (_text == null)
                _text = CreateText();
            Hide();
        }

        public void Show(string message)
        {
            _text.text = message;
            _text.gameObject.SetActive(true);
        }

        public void Hide()
        {
            if (_text != null)
                _text.gameObject.SetActive(false);
        }

        private TextMeshProUGUI CreateText()
        {
            // Build a full-screen overlay canvas on this GameObject
            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            gameObject.AddComponent<CanvasScaler>();
            gameObject.AddComponent<GraphicRaycaster>();

            GameObject textGo = new GameObject("OutcomeText");
            textGo.transform.SetParent(transform, false);

            RectTransform rt = textGo.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            TextMeshProUGUI tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = 36;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.outlineColor = Color.black;
            tmp.outlineWidth = 0.2f;

            return tmp;
        }
    }
}
