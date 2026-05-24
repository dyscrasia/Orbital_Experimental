using Orbital.Strategy;

namespace Orbital.Combat
{
    /// <summary>
    /// Pure static class — checks whether any player has met the win condition.
    /// Win condition: a player owns the other player's home planet.
    /// </summary>
    public static class WinConditionChecker
    {
        /// <summary>
        /// Returns the winning player's ID if the win condition is met, otherwise null.
        /// </summary>
        public static int? CheckForWin(GameState state)
        {
            foreach (Player player in state.Players)
            {
                foreach (Player other in state.Players)
                {
                    if (other.Id == player.Id) continue;

                    if (state.Ownership.TryGetValue(other.HomeBodyId, out PlanetOwnership ownership)
                        && ownership.OwnerPlayerId == player.Id)
                    {
                        return player.Id;
                    }
                }
            }
            return null;
        }
    }
}
