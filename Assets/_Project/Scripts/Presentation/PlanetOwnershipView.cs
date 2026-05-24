using Orbital.Physics;
using UnityEngine;

namespace Orbital.Presentation
{
    /// <summary>
    /// Visual halo ring drawn around a planet to indicate its owner.
    /// Created by TurnManager and positioned at the body's world position.
    /// Call SetOwner(color) to show; SetOwner(null) to hide.
    /// </summary>
    public class PlanetOwnershipView : MonoBehaviour
    {
        private LineRenderer _ring;
        private const int Segments = 64;

        public void Initialize(CelestialBody body)
        {
            transform.position = new Vector3(body.Position.x, body.Position.y, 0f);

            GameObject ringGo = new GameObject("OwnershipRing");
            ringGo.transform.SetParent(transform, false);

            _ring = ringGo.AddComponent<LineRenderer>();
            _ring.useWorldSpace = false;
            _ring.loop = true;
            _ring.startWidth = 0.12f;
            _ring.endWidth = 0.12f;
            _ring.numCapVertices = 4;
            _ring.material = new Material(Shader.Find("Sprites/Default"));

            float haloRadius = body.Radius + 0.4f;
            _ring.positionCount = Segments;
            for (int i = 0; i < Segments; i++)
            {
                float angle = i / (float)Segments * Mathf.PI * 2f;
                _ring.SetPosition(i, new Vector3(
                    Mathf.Cos(angle) * haloRadius,
                    Mathf.Sin(angle) * haloRadius,
                    -0.05f));
            }

            _ring.enabled = false;
        }

        public void SetOwner(Color? color)
        {
            if (_ring == null) return;

            if (color == null)
            {
                _ring.enabled = false;
                return;
            }

            Color c = color.Value;
            _ring.startColor = new Color(c.r, c.g, c.b, 0.85f);
            _ring.endColor   = new Color(c.r, c.g, c.b, 0.85f);
            _ring.enabled = true;
        }
    }
}
