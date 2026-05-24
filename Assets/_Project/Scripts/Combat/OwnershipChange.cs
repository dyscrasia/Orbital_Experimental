namespace Orbital.Combat
{
    /// <summary>
    /// Describes the result of an ownership resolution after a successful orbital capture.
    /// </summary>
    public class OwnershipChange
    {
        public int BodyId { get; }
        public int NewOwnerId { get; }

        /// <summary>Null if the body was previously unowned.</summary>
        public int? PreviousOwnerId { get; }

        /// <summary>
        /// True whenever an existing OrbitingRocketView must be destroyed:
        /// enemy dislodge, or same-player re-capture (refreshes the visual).
        /// </summary>
        public bool DislodgedExistingRocket { get; }

        public OwnershipChange(int bodyId, int newOwnerId, int? previousOwnerId, bool dislodgedExistingRocket)
        {
            BodyId = bodyId;
            NewOwnerId = newOwnerId;
            PreviousOwnerId = previousOwnerId;
            DislodgedExistingRocket = dislodgedExistingRocket;
        }
    }
}
