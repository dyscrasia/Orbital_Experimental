using UnityEngine;

namespace Orbital.Physics
{
    /// <summary>
    /// Pure data class representing a celestial body (star or planet).
    /// No Unity dependencies beyond the acceptable Vector2 value type.
    /// Planets are on rails in Phase 1 — Position does not change during simulation.
    /// </summary>
    public class CelestialBody
    {
        public int Id;
        public string Name;
        public Vector2 Position;
        public float Mass;
        public float Radius;
        public float SoiRadius;

        // Capture-window parameters — evaluated when the rocket crosses CaptureRingRadius inbound.
        // Set CaptureRingRadius = 0 to disable capture for this body.
        public float CaptureRingRadius;
        public float CaptureMinSpeed;
        public float CaptureMaxSpeed;
        public float CaptureAngleToleranceDegrees;
    }
}
