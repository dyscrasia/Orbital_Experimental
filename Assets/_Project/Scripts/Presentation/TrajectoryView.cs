using System.Collections.Generic;
using UnityEngine;

namespace Orbital.Presentation
{
    /// <summary>
    /// Renders the trajectory preview line during aiming.
    /// Wraps a LineRenderer; show/hide by enabling/disabling it.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class TrajectoryView : MonoBehaviour
    {
        private LineRenderer _lineRenderer;

        private void Awake()
        {
            _lineRenderer = GetComponent<LineRenderer>();
            ConfigureLineRenderer();
            Hide();
        }

        public void Show(List<Vector2> points)
        {
            _lineRenderer.enabled = true;
            _lineRenderer.positionCount = points.Count;
            for (int i = 0; i < points.Count; i++)
                _lineRenderer.SetPosition(i, new Vector3(points[i].x, points[i].y, -0.1f));
        }

        public void Hide()
        {
            _lineRenderer.enabled = false;
        }

        private void ConfigureLineRenderer()
        {
            _lineRenderer.useWorldSpace = true;
            _lineRenderer.startWidth = 0.06f;
            _lineRenderer.endWidth = 0.01f;
            _lineRenderer.numCapVertices = 4;

            // Fade from white to transparent along the prediction
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f)
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(0.8f, 0f),
                    new GradientAlphaKey(0f, 1f)
                }
            );
            _lineRenderer.colorGradient = gradient;

            // Use the default Sprites/Default material so we don't need a special material
            _lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        }
    }
}
