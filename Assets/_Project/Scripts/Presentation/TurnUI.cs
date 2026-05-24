using System.Collections;
using Orbital.Strategy;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Orbital.Presentation
{
    /// <summary>
    /// HUD overlay for the two-player turn loop.
    /// Creates its own Canvas programmatically; call Show(state) after any state change.
    /// Call ShowPositioningHint() at the start of each WaitingForLaunch phase to display
    /// the position-then-aim instruction, which fades out automatically after 4 seconds.
    /// </summary>
    public class TurnUI : MonoBehaviour
    {
        /// <summary>Fired when the player clicks the End Turn button.</summary>
        public event System.Action OnEndTurn;

        private TextMeshProUGUI _turnHeader;    // top center: "Player N's Turn  —  Turn 17"
        private TextMeshProUGUI _p1Info;        // top left: name + planet count
        private TextMeshProUGUI _p2Info;        // top right: name + planet count
        private TextMeshProUGUI _prompt;        // bottom center: phase-specific help
        private TextMeshProUGUI _rocketCounter; // bottom left: "Rockets: N"
        private Button          _endTurnButton; // bottom right: "End Turn [Enter]"

        private Coroutine _hintFade;
        private const float HintFadeDuration = 4f;
        private static readonly Color PromptColor  = new Color(1f, 1f, 0.8f, 0.85f);
        private static readonly Color CounterColor = new Color(0.8f, 0.8f, 0.8f, 0.80f);

        private void Awake()
        {
            BuildCanvas();
        }

        public void Show(GameState state)
        {
            if (state == null) return;

            // Any phase change stops a running hint fade and restores full prompt opacity.
            StopHintFade();

            Player current = state.CurrentPlayer;

            // Turn header
            if (current != null)
            {
                _turnHeader.text  = $"{current.Name}'s Turn\n<size=60%>Turn {state.TurnNumber}</size>";
                _turnHeader.color = current.Color;
            }

            // Player info panels
            foreach (Player p in state.Players)
            {
                int count = state.GetPlayerPlanetCount(p.Id);
                string info = $"{p.Name}\n<size=80%>{count} planet{(count == 1 ? "" : "s")}</size>";

                if (p.Id == 1)
                {
                    _p1Info.text  = info;
                    _p1Info.color = p.Color;
                }
                else
                {
                    _p2Info.text  = info;
                    _p2Info.color = p.Color;
                }
            }

            // Rocket counter and End Turn button — only meaningful during WaitingForLaunch
            bool showRocketUI = state.Phase == GamePhase.WaitingForLaunch;
            _rocketCounter.enabled       = showRocketUI;
            _endTurnButton.gameObject.SetActive(showRocketUI);

            if (showRocketUI)
            {
                int remaining = state.AvailableLaunchSites.Count;
                _rocketCounter.text = $"Rockets: {remaining}";
            }

            // Bottom prompt
            switch (state.Phase)
            {
                case GamePhase.BetweenTurns:
                    _prompt.text    = current != null
                        ? $"{current.Name} — Press Space to begin your turn"
                        : "Press Space to begin";
                    _prompt.color   = PromptColor;
                    _prompt.enabled = true;
                    break;
                case GamePhase.WaitingForLaunch:
                    // Generic fallback; ShowPositioningHint() overrides this immediately after.
                    _prompt.text    = "Drag to position, then outward to aim and fire";
                    _prompt.color   = PromptColor;
                    _prompt.enabled = true;
                    break;
                case GamePhase.RocketInFlight:
                    _prompt.text    = "";
                    _prompt.enabled = false;
                    break;
                case GamePhase.GameOver:
                    _prompt.enabled = false;
                    break;
            }
        }

        /// <summary>
        /// Show the position-then-aim instruction and fade it out over 4 seconds.
        /// Call immediately after Show(state) when entering WaitingForLaunch.
        /// </summary>
        public void ShowPositioningHint()
        {
            StopHintFade();
            _prompt.text    = "Drag rocket around home to position, then drag outward to aim and launch";
            _prompt.color   = PromptColor;
            _prompt.enabled = true;
            _hintFade = StartCoroutine(FadePrompt(HintFadeDuration));
        }

        public void Hide()
        {
            StopHintFade();
            gameObject.SetActive(false);
        }

        // -------------------------------------------------------------------------
        //  Hint fade helpers
        // -------------------------------------------------------------------------

        private void StopHintFade()
        {
            if (_hintFade == null) return;
            StopCoroutine(_hintFade);
            _hintFade = null;
            // Restore full opacity so the next Show() call renders cleanly.
            Color c = _prompt.color;
            c.a = PromptColor.a;
            _prompt.color = c;
        }

        private IEnumerator FadePrompt(float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                Color c = _prompt.color;
                c.a = Mathf.Lerp(PromptColor.a, 0f, t);
                _prompt.color = c;
                yield return null;
            }
            _prompt.enabled = false;
            _hintFade = null;
        }

        // -------------------------------------------------------------------------
        //  Canvas construction
        // -------------------------------------------------------------------------

        private void BuildCanvas()
        {
            GameObject canvasGo = new GameObject("TurnUICanvas");
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();

            // --- Turn header: top center ---
            _turnHeader = MakeText(canvasGo, "TurnHeader",
                anchorMin: new Vector2(0.25f, 1f),
                anchorMax: new Vector2(0.75f, 1f),
                pivot:      new Vector2(0.5f, 1f),
                anchoredPos: new Vector2(0f, -12f),
                sizeDelta: new Vector2(0f, 80f),
                fontSize: 26,
                alignment: TextAlignmentOptions.Top);

            // --- Player 1 info: top left ---
            _p1Info = MakeText(canvasGo, "P1Info",
                anchorMin: new Vector2(0f, 1f),
                anchorMax: new Vector2(0.22f, 1f),
                pivot:      new Vector2(0f, 1f),
                anchoredPos: new Vector2(14f, -12f),
                sizeDelta: new Vector2(0f, 70f),
                fontSize: 18,
                alignment: TextAlignmentOptions.TopLeft);

            // --- Player 2 info: top right ---
            _p2Info = MakeText(canvasGo, "P2Info",
                anchorMin: new Vector2(0.78f, 1f),
                anchorMax: new Vector2(1f, 1f),
                pivot:      new Vector2(1f, 1f),
                anchoredPos: new Vector2(-14f, -12f),
                sizeDelta: new Vector2(0f, 70f),
                fontSize: 18,
                alignment: TextAlignmentOptions.TopRight);

            // --- Prompt: bottom center ---
            _prompt = MakeText(canvasGo, "Prompt",
                anchorMin: new Vector2(0.15f, 0f),
                anchorMax: new Vector2(0.85f, 0f),
                pivot:      new Vector2(0.5f, 0f),
                anchoredPos: new Vector2(0f, 18f),
                sizeDelta: new Vector2(0f, 40f),
                fontSize: 17,
                alignment: TextAlignmentOptions.Bottom);
            _prompt.color = PromptColor;

            // --- Rocket counter: bottom left ---
            _rocketCounter = MakeText(canvasGo, "RocketCounter",
                anchorMin: new Vector2(0f, 0f),
                anchorMax: new Vector2(0.18f, 0f),
                pivot:      new Vector2(0f, 0f),
                anchoredPos: new Vector2(14f, 18f),
                sizeDelta: new Vector2(0f, 36f),
                fontSize: 15,
                alignment: TextAlignmentOptions.BottomLeft);
            _rocketCounter.color   = CounterColor;
            _rocketCounter.enabled = false;

            // --- End Turn button: bottom right ---
            _endTurnButton = MakeEndTurnButton(canvasGo);
            _endTurnButton.gameObject.SetActive(false);
        }

        private Button MakeEndTurnButton(GameObject parent)
        {
            // Container
            GameObject go = new GameObject("EndTurnButton");
            go.transform.SetParent(parent.transform, false);

            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin        = new Vector2(1f, 0f);
            rt.anchorMax        = new Vector2(1f, 0f);
            rt.pivot            = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(-14f, 14f);
            rt.sizeDelta        = new Vector2(130f, 36f);

            // Background image
            Image img = go.AddComponent<Image>();
            img.color = new Color(0.15f, 0.15f, 0.2f, 0.85f);

            // Button component
            Button btn = go.AddComponent<Button>();
            ColorBlock colors = btn.colors;
            colors.normalColor      = new Color(0.15f, 0.15f, 0.2f, 0.85f);
            colors.highlightedColor = new Color(0.25f, 0.25f, 0.35f, 0.95f);
            colors.pressedColor     = new Color(0.35f, 0.35f, 0.5f, 1.00f);
            btn.colors = colors;
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => OnEndTurn?.Invoke());

            // Label
            GameObject labelGo = new GameObject("Label");
            labelGo.transform.SetParent(go.transform, false);
            TextMeshProUGUI label = labelGo.AddComponent<TextMeshProUGUI>();
            label.text      = "End Turn  [Enter]";
            label.fontSize  = 13;
            label.alignment = TextAlignmentOptions.Center;
            label.color     = new Color(0.9f, 0.9f, 0.9f, 1f);
            label.textWrappingMode = TextWrappingModes.NoWrap;

            RectTransform lrt = label.rectTransform;
            lrt.anchorMin        = Vector2.zero;
            lrt.anchorMax        = Vector2.one;
            lrt.offsetMin        = Vector2.zero;
            lrt.offsetMax        = Vector2.zero;

            return btn;
        }

        private static TextMeshProUGUI MakeText(
            GameObject parent,
            string name,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 anchoredPos, Vector2 sizeDelta,
            float fontSize, TextAlignmentOptions alignment)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);

            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.fontSize  = fontSize;
            tmp.alignment = alignment;
            tmp.color = Color.white;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;

            RectTransform rt = tmp.rectTransform;
            rt.anchorMin        = anchorMin;
            rt.anchorMax        = anchorMax;
            rt.pivot            = pivot;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta        = sizeDelta;

            return tmp;
        }
    }
}
