using Orbital.Galaxy;
using Orbital.Physics;
using UnityEngine;

namespace Orbital.Presentation
{
    /// <summary>
    /// Presentation view for a single celestial body.
    /// Reads from a CelestialBody data object and updates the transform.
    /// Renders as an animated sprite when BodyTypeVisuals are available for the body's
    /// type, otherwise falls back to a flat-shaded colored circle.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class CelestialBodyView : MonoBehaviour
    {
        private CelestialBody _data;
        private SpriteRenderer _spriteRenderer;
        private float _rotationSpeed;

        public void Initialize(CelestialBody data, Color color, BodyTypeVisuals visuals = null)
        {
            _data = data;
            _spriteRenderer = GetComponent<SpriteRenderer>();

            transform.position = new Vector3(data.Position.x, data.Position.y, 0f);
            gameObject.name = data.Name;

            if (visuals != null && visuals.SpriteVariants != null && visuals.SpriteVariants.Length > 0)
            {
                // Art sprites are 1024×1024 at 256 PPU → native world size 4×4 (radius 2).
                // Scale = body.Radius / 2 normalises native radius 2 → body.Radius.
                float scale = data.Radius / 2f * visuals.VisualScaleMultiplier;
                transform.localScale = new Vector3(scale, scale, 1f);

                int variantIndex = Mathf.Abs(data.Id) % visuals.SpriteVariants.Length;
                _spriteRenderer.sprite = visuals.SpriteVariants[variantIndex];
                _spriteRenderer.color = Color.white;
                _rotationSpeed = visuals.RotationSpeedDegreesPerSecond;
            }
            else
            {
                // Colored circle fallback: CreateCircleSprite bakes PPU = resolution,
                // so native world size is 1×1 (radius 0.5). Scale by diameter gives radius = body.Radius.
                float diameter = data.Radius * 2f;
                transform.localScale = new Vector3(diameter, diameter, 1f);
                _spriteRenderer.sprite = CreateCircleSprite(64, Color.white);
                _spriteRenderer.color = color;
                _rotationSpeed = 0f;
            }
        }

        private void LateUpdate()
        {
            if (_data == null) return;
            transform.position = new Vector3(_data.Position.x, _data.Position.y, 0f);
            if (_rotationSpeed != 0f)
                transform.Rotate(0f, 0f, _rotationSpeed * Time.unscaledDeltaTime);
        }

        /// <summary>Procedurally generate a white filled-circle sprite.</summary>
        private static Sprite CreateCircleSprite(int resolution, Color color)
        {
            Texture2D tex = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            float center = resolution * 0.5f;
            float radiusSq = center * center;

            Color[] pixels = new Color[resolution * resolution];
            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float dx = x - center + 0.5f;
                    float dy = y - center + 0.5f;
                    float distSq = dx * dx + dy * dy;
                    // Smooth anti-aliased edge over 1.5 pixels
                    float edge = center - 1.5f;
                    float alpha = distSq <= edge * edge ? 1f
                                : distSq >= radiusSq ? 0f
                                : 1f - (Mathf.Sqrt(distSq) - edge) / 1.5f;
                    pixels[y * resolution + x] = new Color(color.r, color.g, color.b, alpha);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();

            return Sprite.Create(
                tex,
                new Rect(0, 0, resolution, resolution),
                new Vector2(0.5f, 0.5f),
                resolution); // pixels per unit = resolution → sprite is 1 unit wide at scale 1
        }
    }
}
