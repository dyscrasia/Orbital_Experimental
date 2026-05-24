using TMPro;
using Orbital.Physics;
using Orbital.Strategy;
using UnityEngine;

namespace Orbital.Presentation
{
    /// <summary>
    /// Displays a population counter near one player's home planet.
    /// One instance per player; both share a single Screen-Space-Overlay canvas
    /// created and owned by TurnManager.
    ///
    /// The label is positioned in screen space each LateUpdate by projecting the
    /// home planet's world position and applying a fixed pixel offset above it.
    /// Text and colour read from GameState; this view never writes to game state.
    /// </summary>
    [System.Obsolete("Replaced by PlanetPopulationView in Jump 4. Remove once all references are gone.")]
    public class HomePopulationView : MonoBehaviour
    {
        private CelestialBody _homeBody;
        private int _playerId;
        private TurnManager _turnManager;
        private TextMeshProUGUI _label;
        private Camera _cam;

        private const float ScreenOffsetY = 40f;

        /// <summary>
        /// Wire up this view. Must be called immediately after AddComponent.
        /// </summary>
        /// <param name="homeBody">The player's home planet (provides world position).</param>
        /// <param name="playerId">Player whose Population entry to display.</param>
        /// <param name="playerColor">Text colour.</param>
        /// <param name="canvas">Shared Screen-Space-Overlay canvas created by TurnManager.</param>
        /// <param name="turnManager">Source of GameState.Population reads.</param>
        public void Initialize(CelestialBody homeBody, int playerId, Color playerColor,
                               Canvas canvas, TurnManager turnManager)
        {
            _homeBody    = homeBody;
            _playerId    = playerId;
            _turnManager = turnManager;
            _cam         = Camera.main;

            GameObject labelGo = new GameObject($"PopLabel_P{playerId}");
            labelGo.transform.SetParent(canvas.transform, false);

            _label = labelGo.AddComponent<TextMeshProUGUI>();
            _label.fontSize     = 18;
            _label.alignment    = TextAlignmentOptions.Center;
            _label.color        = playerColor;
            _label.outlineColor = Color.black;
            _label.outlineWidth = 0.2f;

            // Anchor at bottom-left so anchoredPosition maps 1:1 to screen pixels.
            RectTransform rt = _label.rectTransform;
            rt.anchorMin  = Vector2.zero;
            rt.anchorMax  = Vector2.zero;
            rt.pivot      = new Vector2(0.5f, 0f);
            rt.sizeDelta  = new Vector2(120f, 30f);
        }

        private void LateUpdate()
        {
            if (_label == null) return;

            GameState state = _turnManager?.GameState;
            if (state == null || !state.Population.ContainsKey(_playerId))
            {
                _label.gameObject.SetActive(false);
                return;
            }

            _label.gameObject.SetActive(true);
            _label.text = $"Pop: {state.Population[_playerId]}";

            // Project home-planet centre to screen space and offset upward.
            Vector3 screenPos = _cam.WorldToScreenPoint(
                new Vector3(_homeBody.Position.x, _homeBody.Position.y, 0f));
            _label.rectTransform.anchoredPosition =
                new Vector2(screenPos.x, screenPos.y + ScreenOffsetY);
        }
    }
}
