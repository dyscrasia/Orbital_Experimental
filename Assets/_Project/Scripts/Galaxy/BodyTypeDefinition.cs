using System;
using UnityEngine;

namespace Orbital.Galaxy
{
    [Serializable]
    public class BodyTypeDefinition
    {
        public string TypeName;
        public Color VisualColor;
        public float MinMass;
        public float MaxMass;
        public float MinRadius;
        public float MaxRadius;
        [Tooltip("Relative selection probability. Higher = picked more often.")]
        public float Weight = 1f;
    }
}
