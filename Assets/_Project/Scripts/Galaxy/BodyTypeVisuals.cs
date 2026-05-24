using UnityEngine;

namespace Orbital.Galaxy
{
    /// <summary>
    /// Associates a body type (by TypeName) with an animated sprite set.
    /// Create one asset per body type under Assets/_Project/Data/.
    /// </summary>
    [CreateAssetMenu(fileName = "BodyTypeVisuals", menuName = "Orbital/Body Type Visuals")]
    public class BodyTypeVisuals : ScriptableObject
    {
        [Tooltip("Must match the TypeName in a BodyTypeDefinition (e.g. \"Rocky\"). Leave empty for the home planet override asset.")]
        public string TypeName;

        [Tooltip("One or more sprite variants. One is picked per body deterministically based on body ID. Leave empty to use the default colored circle.")]
        public Sprite[] SpriteVariants;

        [Tooltip("Degrees per second the sprite rotates around the Z axis.")]
        public float RotationSpeedDegreesPerSecond = 5f;

        [Tooltip("Multiplier applied on top of the radius-based scale. " +
                 "Use to compensate for transparent padding differences between sprite sets without retouching PNGs.")]
        public float VisualScaleMultiplier = 1f;
    }
}
