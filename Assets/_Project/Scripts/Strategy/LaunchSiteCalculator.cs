using System.Collections.Generic;

namespace Orbital.Strategy
{
    /// <summary>
    /// Pure static calculator for the Strategy-variant launch site rule:
    ///   every planet the player owns is a launch site.
    ///   Order: home first, then captured planets sorted by ascending body ID.
    /// </summary>
    public static class LaunchSiteCalculator
    {
        /// <summary>Returns the body IDs the player may fire from this turn.
        /// Strategy variant: every planet the player owns. Order: home first,
        /// then captured planets ordered by ascending body ID for determinism.</summary>
        public static List<int> Calculate(GameState state, int playerId)
        {
            Player p = state.GetPlayer(playerId);
            List<int> sites = new List<int>();
            if (p == null) return sites;

            // Home always first.
            sites.Add(p.HomeBodyId);

            // All other planets the player owns, sorted by ID for deterministic order.
            List<int> captured = new List<int>();
            foreach (KeyValuePair<int, PlanetOwnership> kv in state.Ownership)
            {
                if (kv.Value.OwnerPlayerId == playerId && kv.Key != p.HomeBodyId)
                    captured.Add(kv.Key);
            }
            captured.Sort();
            sites.AddRange(captured);

            // Contested planets cannot fire rockets while the contest is unresolved.
            sites.RemoveAll(id => state.Contests.ContainsKey(id));

            return sites;
        }
    }
}
