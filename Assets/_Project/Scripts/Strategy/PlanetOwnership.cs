namespace Orbital.Strategy
{
    /// <summary>
    /// Ownership record for a single body.
    /// Stores both the owning player and the kinematic orbit parameters needed
    /// to render the persistent OrbitingRocketView.
    /// </summary>
    public class PlanetOwnership
    {
        public int OwnerPlayerId;

        /// <summary>
        /// Unique ID used as a key to locate the matching OrbitingRocketView.
        /// -1 means no visual rocket is placed yet (home planets start here).
        /// </summary>
        public int OrbitingRocketId;

        // Kinematic orbit parameters (populated at the moment of capture).
        public float OrbitRadius;
        public float OrbitAngle;
        public float OrbitAngularSpeed;
        public int OrbitDirection;
    }
}
