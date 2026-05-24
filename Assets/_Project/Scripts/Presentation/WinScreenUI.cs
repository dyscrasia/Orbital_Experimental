using Orbital.Strategy;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Orbital.Presentation
{
    /// <summary>
    /// Full-screen overlay shown when the game ends.
    /// Displays "Player N Wins!" in the winner's color and a New Game button.
    /// </summary>
    public class WinScreenUI : MonoBehaviour
    {
        public event System.Action OnNewGame;

        private GameObject _overlay;
        private TextMeshProUGUI _winText;

        private void Awake()
        {
            BuildOverlay();
            _overlay.SetActive(false);
        }

        public void Show(Player winner)
        {
            if (winner == null) return;

            _winText.text  = $"{winner.Name} Wins!";
            _winText.color = winner.Color;
            _overlay.SetActive(true);
        }

        public void Hide()
        {
            if (_overlay != null)
                _overlay.SetActive(false);
        }

        // -------------------------------------------------------------------------
        //  Construction
        // -------------------------------------------------------------------------

        private void BuildOverlay()
        {
            GameObject canvasGo = new GameObject("WinScreenCanvas");
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();

            // Semi-transparent black full-screen panel
            _overlay = new GameObject("Overlay");
            _overlay.transform.SetParent(canvasGo.transform, false);

            Image bg = _overlay.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.75f);

            RectTransform bgRt = bg.rectTransform;
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.sizeDelta = Vector2.zero;

            // Win text
            GameObject textGo = new GameObject("WinText");
            textGo.transform.SetParent(_overlay.transform, false);

            _winText = textGo.AddComponent<TextMeshProUGUI>();
            _winText.fontSize  = 64;
            _winText.alignment = TextAlignmentOptions.Center;
            _winText.fontStyle = FontStyles.Bold;

            RectTransform textRt = _winText.rectTransform;
            textRt.anchorMin        = new Vector2(0.1f, 0.55f);
            textRt.anchorMax        = new Vector2(0.9f, 0.85f);
            textRt.anchoredPosition = Vector2.zero;
            textRt.sizeDelta        = Vector2.zero;

            // New Game button
            GameObject btnGo = new GameObject("NewGameButton");
            btnGo.transform.SetParent(_overlay.transform, false);

            Image btnImg = btnGo.AddComponent<Image>();
            btnImg.color = new Color(0.15f, 0.15f, 0.2f, 1f);

            Button btn = btnGo.AddComponent<Button>();
            btn.targetGraphic = btnImg;

            ColorBlock colors = btn.colors;
            colors.highlightedColor = new Color(0.3f, 0.3f, 0.4f, 1f);
            colors.pressedColor     = new Color(0.1f, 0.1f, 0.15f, 1f);
            btn.colors = colors;

            btn.onClick.AddListener(() => OnNewGame?.Invoke());

            RectTransform btnRt = btnImg.rectTransform;
            btnRt.anchorMin        = new Vector2(0.35f, 0.35f);
            btnRt.anchorMax        = new Vector2(0.65f, 0.5f);
            btnRt.anchoredPosition = Vector2.zero;
            btnRt.sizeDelta        = Vector2.zero;

            // Button label
            GameObject labelGo = new GameObject("Label");
            labelGo.transform.SetParent(btnGo.transform, false);

            TextMeshProUGUI label = labelGo.AddComponent<TextMeshProUGUI>();
            label.text      = "New Game";
            label.fontSize  = 28;
            label.alignment = TextAlignmentOptions.Center;
            label.color     = Color.white;

            RectTransform labelRt = label.rectTransform;
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.sizeDelta = Vector2.zero;
            labelRt.anchoredPosition = Vector2.zero;

            // Press N hint
            GameObject hintGo = new GameObject("HintText");
            hintGo.transform.SetParent(_overlay.transform, false);

            TextMeshProUGUI hint = hintGo.AddComponent<TextMeshProUGUI>();
            hint.text      = "or press N";
            hint.fontSize  = 18;
            hint.alignment = TextAlignmentOptions.Center;
            hint.color     = new Color(1f, 1f, 1f, 0.5f);

            RectTransform hintRt = hint.rectTransform;
            hintRt.anchorMin        = new Vector2(0.25f, 0.28f);
            hintRt.anchorMax        = new Vector2(0.75f, 0.36f);
            hintRt.anchoredPosition = Vector2.zero;
            hintRt.sizeDelta        = Vector2.zero;
        }
    }
}
