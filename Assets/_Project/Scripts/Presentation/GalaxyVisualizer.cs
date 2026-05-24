using Orbital.Galaxy;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Orbital.Presentation
{
    /// <summary>
    /// Developer tool: seed input, hotkey regeneration, and on-screen evaluation display.
    ///
    /// Attach to any GameObject in the Phase 1 prototype scene alongside the
    /// PrototypeScenarioController. Assign a GalaxyParameters ScriptableObject in the Inspector.
    ///
    /// Hotkeys (active when the game is running):
    ///   G — regenerate with a new random seed
    ///   B — regenerate with the current seed (verifies a seed is stable)
    /// </summary>
    public class GalaxyVisualizer : MonoBehaviour
    {
        [Tooltip("ScriptableObject with all generator tunables. " +
                 "Create via Assets > Create > Orbital > Galaxy Parameters.")]
        public GalaxyParameters GalaxyParams;

        [Tooltip("Starting seed. Changed each time G is pressed.")]
        public int CurrentSeed = 12345;

        // -------------------------------------------------------------------------
        //  Private state
        // -------------------------------------------------------------------------

        private PrototypeScenarioController _psc;
        private TextMeshProUGUI _infoText;

        // -------------------------------------------------------------------------
        //  Unity messages
        // -------------------------------------------------------------------------

        private void Awake()
        {
            _psc = FindAnyObjectByType<PrototypeScenarioController>();
            CreateInfoText();
        }

        private void Start()
        {
            // PSC has already generated its galaxy in Awake; just refresh the text.
            RefreshText();
        }

        private void Update()
        {
            if (Keyboard.current == null) return;

            if (Keyboard.current.gKey.wasPressedThisFrame)
                RegenerateWithRandomSeed();
            else if (Keyboard.current.bKey.wasPressedThisFrame)
                Regenerate();
        }

        // -------------------------------------------------------------------------
        //  Public API
        // -------------------------------------------------------------------------

        /// <summary>Regenerate the current seed (B hotkey / inspector button).</summary>
        public void Regenerate()
        {
            if (_psc == null || GalaxyParams == null)
            {
                Debug.LogWarning("[GalaxyVisualizer] No PrototypeScenarioController or GalaxyParams assigned.");
                return;
            }
            _psc.RegenerateGalaxy(CurrentSeed, GalaxyParams);
            RefreshText();
        }

        /// <summary>Pick a new random seed and regenerate (G hotkey / inspector button).</summary>
        public void RegenerateWithRandomSeed()
        {
            // Presentation-layer seed picking — UnityEngine.Random is fine here.
            CurrentSeed = UnityEngine.Random.Range(1, int.MaxValue);
            Regenerate();
        }

        // -------------------------------------------------------------------------
        //  Private helpers
        // -------------------------------------------------------------------------

        private void RefreshText()
        {
            if (_infoText == null || _psc == null) return;

            GalaxyData galaxy = _psc.CurrentGalaxy;
            if (galaxy == null)
            {
                _infoText.text = "[GalaxyVisualizer] No procedural galaxy (UseProceduralGalaxy=false or no params).";
                return;
            }

            _infoText.text =
                $"Seed: {galaxy.Seed}   Bodies: {galaxy.Bodies.Count}\n" +
                $"{galaxy.Evaluation.Summary}";
        }

        private void CreateInfoText()
        {
            GameObject canvasGo = new GameObject("GalaxyVisualizerCanvas");
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>();
            canvasGo.AddComponent<UnityEngine.UI.GraphicRaycaster>();

            GameObject textGo = new GameObject("InfoText");
            textGo.transform.SetParent(canvasGo.transform, false);

            _infoText = textGo.AddComponent<TextMeshProUGUI>();
            _infoText.fontSize = 14;
            _infoText.color = new Color(1f, 1f, 0.8f, 0.9f);

            RectTransform rt = _infoText.rectTransform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot     = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(10f, -10f);
            rt.sizeDelta        = new Vector2(-20f, 80f);
        }
    }
}
