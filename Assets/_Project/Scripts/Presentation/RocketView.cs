using Orbital.Physics;
using UnityEngine;

namespace Orbital.Presentation
{
    /// <summary>
    /// Presentation view for the rocket.
    /// Reads from a RocketState and updates position and rotation each frame.
    /// The rocket sprite rotates to face its velocity direction.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class RocketView : MonoBehaviour
    {
        private RocketState _data;
        private SpriteRenderer _spriteRenderer;

        public void Initialize(RocketState data, Color color)
        {
            _data = data;
            _spriteRenderer = GetComponent<SpriteRenderer>();
            _spriteRenderer.sprite = CreateRocketSprite(color);
            _spriteRenderer.color = color;

            // Rocket: 0.6 × 1.0 world units — large enough to track visually across the play area
            transform.localScale = new Vector3(0.6f, 1.0f, 1f);
            gameObject.name = "Rocket";
        }

        /// <summary>Called by PrototypeScenarioController to swap the data reference on reset.</summary>
        public void SetData(RocketState data) => _data = data;

        private void LateUpdate()
        {
            if (_data == null) return;

            transform.position = new Vector3(_data.Position.x, _data.Position.y, 0f);

            if (_data.Velocity.sqrMagnitude > 1e-6f)
            {
                float angle = Mathf.Atan2(_data.Velocity.y, _data.Velocity.x) * Mathf.Rad2Deg - 90f;
                transform.rotation = Quaternion.Euler(0f, 0f, angle);
            }
        }

        /// <summary>Procedurally generate a simple arrow-shaped (diamond) sprite for the rocket.</summary>
        private static Sprite CreateRocketSprite(Color color)
        {
            const int W = 16;
            const int H = 24;
            Texture2D tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;

            Color[] pixels = new Color[W * H];
            float cx = W * 0.5f;
            float cy = H * 0.5f;

            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    // Arrow shape: wider in the middle-top, narrow at tip and tail
                    float ny = (y - cy) / cy;           // -1..1 (bottom to top)
                    float halfWidth;
                    if (ny >= 0f)
                        halfWidth = (1f - ny) * cx;     // narrows toward tip
                    else
                        halfWidth = (1f + ny) * cx * 0.6f; // narrows toward tail
                    float dist = Mathf.Abs(x - cx);
                    float alpha = dist <= halfWidth ? 1f : 0f;
                    pixels[y * W + x] = new Color(color.r, color.g, color.b, alpha);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();

            return Sprite.Create(
                tex,
                new Rect(0, 0, W, H),
                new Vector2(0.5f, 0.5f),
                H); // pixels per unit = H → sprite is 1 unit tall at scale 1
        }
    }
}
