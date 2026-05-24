using Orbital.Physics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Orbital.Presentation
{
    /// <summary>
    /// Visual marker for a planet that the active player can launch from this turn.
    /// Shows a small rocket-shaped triangle in the player's color, plus a highlight
    /// ring when this site is the currently selected one.
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

        private const float ClickRadius   = 1.2f;
        private const float RocketTipY    =  0.35f;
        private const float RocketBaseHalfW = 0.20f;
        private const float RocketBaseY   = -0.25f;
        private const float HighlightRadius = 0.42f;
        private const int   HighlightSegments = 20;

        private int _bodyId;
        private LineRenderer _rocketShape;
        private LineRenderer _highlight;
        private Camera _cam;

        // -------------------------------------------------------------------------
        //  Public API
        // -------------------------------------------------------------------------

        /// <summary>
        /// Position this view on the planet's surface and build its renderers.
        /// Default placement: directly above the planet (top of surface).
        /// </summary>
        public void Initialize(CelestialBody body, Color playerColor, float surfaceOffset = 0.7f)
        {
            _bodyId = body.Id;
            _cam    = Camera.main;

            // Place at the top of the planet surface.
            Vector2 surfacePos = body.Position + new Vector2(0f, body.Radius + surfaceOffset);
            transform.position = new Vector3(surfacePos.x, surfacePos.y, -0.15f);

            _rocketShape = BuildRocketShape(playerColor);
            _highlight   = BuildHighlight(playerColor);

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
