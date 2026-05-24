using TMPro;
using Orbital.Physics;
using Orbital.Strategy;
using UnityEngine;
using UnityEngine.UI;

namespace Orbital.Presentation
{
    /// <summary>
    /// Single floating label above a planet that shows the current state of that
    /// planet's population. One instance per planet with any displayable state;
    /// all share a single Screen-Space-Overlay canvas owned by TurnManager.
    ///
    /// Display format:
    ///   Owned, not contested:         "Pop: N"  (one line, owner colour)
    ///   Being colonised, no contest:  "Pop: N · T turns"  (one line, colonising-player colour)
    ///   Contested:                    "Defender: X"  (upper line, defender colour)
    ///                                 "Invader: Y"   (lower line, invader colour)
    ///
    /// The view reads all state dynamically in LateUpdate; colour and text update
    /// automatically when ownership or contest status changes without needing
    /// recreation.
    /// </summary>
    public class PlanetPopulationView : MonoBehaviour
    {
        private CelestialBody _body;
        private TurnManager   _turnManager;
        private Camera        _cam;

        // Upper label — defender line during contests; hidden otherwise.
        private TextMeshProUGUI _upperLabel;
        private GameObject      _upperLabelGo;

        // Lower label — main population line; also invader line during contests.
        private TextMeshProUGUI _lowerLabel;
        private GameObject      _lowerLabelGo;

        private const float LowerOffsetY = 40f;
        private const float UpperOffsetY = 60f;

        /// <summary>
        /// Wire up this view. Must be called immediately after AddComponent.
        /// </summary>
        public void Initialize(CelestialBody body, Canvas canvas, TurnManager turnManager)
        {
            _body        = body;
            _turnManager = turnManager;
            _cam         = Camera.main;

            _lowerLabelGo = BuildLabel(canvas, $"PopLabel_B{body.Id}_lower",  18, 180f, 30f);
            _lowerLabel   = _lowerLabelGo.GetComponent<TextMeshProUGUI>();

            _upperLabelGo = BuildLabel(canvas, $"PopLabel_B{body.Id}_upper",  15, 180f, 25f);
            _upperLabel   = _upperLabelGo.GetComponent<TextMeshProUGUI>();
        }

        private static GameObject BuildLabel(Canvas canvas, string name,
                                              int fontSize, float width, float height)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(canvas.transform, false);

            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.fontSize         = fontSize;
            tmp.alignment        = TextAlignmentOptions.Center;
            tmp.outlineColor     = Color.black;
            tmp.outlineWidth     = 0.2f;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;

            RectTransform rt = tmp.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot     = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(width, height);

            go.SetActive(false);
            return go;
        }

        private void OnDestroy()
        {
            // Both labels are parented to the shared canvas, not this GO.
            if (_lowerLabelGo != null) Destroy(_lowerLabelGo);
            if (_upperLabelGo != null) Destroy(_upperLabelGo);
        }

        private void LateUpdate()
        {
            if (_lowerLabel == null) return;

            GameState state = _turnManager?.GameState;
            if (state == null)
            {
                _lowerLabelGo.SetActive(false);
                _upperLabelGo.SetActive(false);
                return;
            }

            bool isOwned      = state.Ownership.ContainsKey(_body.Id);
            bool isColonising = state.Colonisations.TryGetValue(_body.Id, out Colonisation col);
            bool isContested  = state.Contests.TryGetValue(_body.Id, out Contest contest);
            bool hasPop       = state.Population.TryGetValue(_body.Id, out int pop);

            Vector3 screenPos = _cam.WorldToScreenPoint(
                new Vector3(_body.Position.x, _body.Position.y, 0f));

            if (isContested)
            {
                // Defender (upper line) + Invader (lower line).
                int defenderPlayerId = isOwned
                    ? state.Ownership[_body.Id].OwnerPlayerId
                    : (isColonising ? col.PlayerId : -1);

                Player defender = defenderPlayerId >= 0 ? state.GetPlayer(defenderPlayerId) : null;
                Player invader  = state.GetPlayer(contest.InvaderPlayerId);

                _upperLabel.color = defender?.Color ?? Color.white;
                _upperLabel.text  = $"Defender: {(hasPop ? pop : 0)}";
                _upperLabelGo.SetActive(true);
                _upperLabel.rectTransform.anchoredPosition =
                    new Vector2(screenPos.x, screenPos.y + UpperOffsetY);

                _lowerLabel.color = invader?.Color ?? Color.white;
                _lowerLabel.text  = $"Invader: {contest.InvaderCount}";
                _lowerLabelGo.SetActive(true);
                _lowerLabel.rectTransform.anchoredPosition =
                    new Vector2(screenPos.x, screenPos.y + LowerOffsetY);
            }
            else if (isColonising)
            {
                // One line: "Pop: N · T turns" in colonising player's colour.
                Player coloniser = state.GetPlayer(col.PlayerId);

                _lowerLabel.color = coloniser?.Color ?? Color.white;
                _lowerLabel.text  = $"Pop: {(hasPop ? pop : 0)} · {col.TurnsRemaining} turns";
                _lowerLabelGo.SetActive(true);
                _lowerLabel.rectTransform.anchoredPosition =
                    new Vector2(screenPos.x, screenPos.y + LowerOffsetY);

                _upperLabelGo.SetActive(false);
            }
            else if (isOwned)
            {
                // One line: "Pop: N" in owner's colour.
                Player owner = state.GetPlayer(state.Ownership[_body.Id].OwnerPlayerId);

                _lowerLabel.color = owner?.Color ?? Color.white;
                _lowerLabel.text  = $"Pop: {(hasPop ? pop : 0)}";
                _lowerLabelGo.SetActive(true);
                _lowerLabel.rectTransform.anchoredPosition =
                    new Vector2(screenPos.x, screenPos.y + LowerOffsetY);

                _upperLabelGo.SetActive(false);
            }
            else
            {
                _lowerLabelGo.SetActive(false);
                _upperLabelGo.SetActive(false);
            }
        }
    }
}
