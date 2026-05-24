using Orbital.Physics;
using Orbital.Strategy;

namespace Orbital.Combat
{
    /// <summary>
    /// Pure static class — determines the ownership change resulting from a capture.
    /// Has no Unity dependencies and no side effects; callers apply the returned change.
    /// </summary>
    public static class OwnershipResolver
    {
        /// <summary>
        /// Resolves a capture event and returns the resulting ownership change.
        /// Returns null when the capture is a no-op (own home planet).
        /// </summary>
        /// <param name="state">Current game state.</param>
        /// <param name="firingPlayerId">The player who fired the rocket.</param>
        /// <param name="capturedBodyId">The body the rocket entered orbit of.</param>
        /// <param name="rocket">The rocket at the moment of capture (orbit params, future use).</param>
        /// <remarks>No longer called in Jump 3+; ColonisationResolver handles orbital captures.</remarks>
        [System.Obsolete("Replaced by ColonisationResolver.Resolve in Jump 3.")]
        public static OwnershipChange ResolveCapture(
            GameState state,
            int firingPlayerId,
            int capturedBodyId,
            RocketState rocket)
        {
            Player firingPlayer = state.GetPlayer(firingPlayerId);
            if (firingPlayer == null) return null;

            // Own home planet — ownership unchanged, outcome is ignored.
            if (capturedBodyId == firingPlayer.HomeBodyId) return null;

            state.Ownership.TryGetValue(capturedBodyId, out PlanetOwnership existing);

            int? previousOwnerId = existing?.OwnerPlayerId;

            // DislodgedExistingRocket is true whenever there is a prior orbiting rocket
            // to remove: enemy dislodge OR same-player re-capture (refreshes the visual).
            bool dislodged = existing != null && existing.OrbitingRocketId >= 0;

            return new OwnershipChange(capturedBodyId, firingPlayerId, previousOwnerId, dislodged);
        }
    }
}
