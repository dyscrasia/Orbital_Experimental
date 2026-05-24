using System.Collections.Generic;

namespace Orbital.Strategy
{
    public enum GamePhase
    {
        BetweenTurns,
        WaitingForLaunch,
        RocketInFlight,
        GameOver
    }

    /// <summary>
    /// Pure data class that owns all mutable game state.
    /// No Unity dependencies — readable by both logic and presentation layers.
    /// </summary>
    public class GameState
    {
        public IReadOnlyList<Player> Players { get; }
        public int CurrentPlayerId { get; set; }
        public Dictionary<int, PlanetOwnership> Ownership { get; } = new Dictionary<int, PlanetOwnership>();
        public int TurnNumber { get; set; }
        public GamePhase Phase { get; set; }
        public int? WinnerId { get; set; }

        /// <summary>
        /// Body IDs the current player can launch from this turn.
        /// Populated by TurnManager at StartTurn; entries are removed as rockets fire.
        /// </summary>
        public List<int> AvailableLaunchSites { get; set; } = new List<int>();

        /// <summary>The body ID of the currently selected launch site.</summary>
        public int ActiveLaunchSiteId { get; set; } = -1;

        public GameState(IReadOnlyList<Player> players)
        {
            Players = players;
        }

        public Player CurrentPlayer
        {
            get
            {
                foreach (Player p in Players)
                    if (p.Id == CurrentPlayerId) return p;
                return null;
            }
        }

        public Player GetPlayer(int id)
        {
            foreach (Player p in Players)
                if (p.Id == id) return p;
            return null;
        }

        public int GetPlayerPlanetCount(int playerId)
        {
            int count = 0;
            foreach (PlanetOwnership o in Ownership.Values)
                if (o.OwnerPlayerId == playerId) count++;
            return count;
        }
    }
}
