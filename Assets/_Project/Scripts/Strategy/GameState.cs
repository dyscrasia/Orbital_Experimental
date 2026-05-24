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

        /// <summary>People resident on each owned planet, keyed by body ID. Each owned
        /// planet (home or captured) has an entry once it is in Ownership. Removed
        /// entries imply zero population; absent entries imply the planet is not
        /// owned and therefore has no civilian population to draw from.</summary>
        public Dictionary<int, int> Population { get; } = new Dictionary<int, int>();

        /// <summary>In-progress colonisations keyed by body ID. A planet may appear
        /// in Colonisations XOR Ownership (never both — completion moves it).</summary>
        public Dictionary<int, Colonisation> Colonisations { get; }
            = new Dictionary<int, Colonisation>();

        /// <summary>Active contests keyed by body ID. A contested planet simultaneously
        /// has an Ownership or Colonisation entry (defender side) and an entry here
        /// (invader side). Removed when the contest resolves.</summary>
        public Dictionary<int, Contest> Contests { get; }
            = new Dictionary<int, Contest>();

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
