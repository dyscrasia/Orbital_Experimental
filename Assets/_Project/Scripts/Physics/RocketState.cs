using UnityEngine;

namespace Orbital.Physics
{
    public enum RocketStatus
    {
        Prelaunch,
        InFlight,
        Crashed,
        Orbited,
        Escaped
    }

    /// <summary>
    /// Pure data class representing the full state of one rocket at a single moment.
    /// Clone this rather than mutating it; the solver returns a new instance each step.
    /// </summary>
    public class RocketState
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public float Mass;
        public float Fuel;
        public int CurrentBodyId;
        public RocketStatus Status;
        public float TimeInFlight;

        // Populated at the moment of orbital capture.
        // When Status == Orbited the solver advances these kinematically instead of
        // running the gravity simulation, so the orbit is perfectly stable.
        public int CapturedBodyId = -1;
        public float OrbitRadius;
        public float OrbitAngle;          // current angular position, radians
        public float OrbitAngularSpeed;   // radians per second (= circularSpeed / radius)
        public int OrbitDirection;        // +1 = CCW, -1 = CW

        public RocketState Clone() => new RocketState
        {
            Position = Position,
            Velocity = Velocity,
            Mass = Mass,
            Fuel = Fuel,
            CurrentBodyId = CurrentBodyId,
            Status = Status,
            TimeInFlight = TimeInFlight,
            CapturedBodyId = CapturedBodyId,
            OrbitRadius = OrbitRadius,
            OrbitAngle = OrbitAngle,
            OrbitAngularSpeed = OrbitAngularSpeed,
            OrbitDirection = OrbitDirection
        };
    }
}
