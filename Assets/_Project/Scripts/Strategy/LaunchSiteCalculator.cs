using System.Collections.Generic;
using Orbital.Core;

namespace Orbital.Strategy
{
    /// <summary>
    /// Pure static calculator for the Classic-mode rocket production rule:
    ///   • 1 rocket at the active player's home planet (always)
    ///   • floor(nonHomeCapturedPlanets / 2) bonus rockets, placed on randomly
    ///     selected captured non-home planets (without replacement)
    ///
    /// The Rng is seeded from TurnNumber * 31 + CurrentPlayerId so that the same
    /// game state always produces the same site list.
    /// </summary>
    public static class LaunchSiteCalculator
    {
        /// <summary>
        /// Returns the ordered list of body IDs the given player may launch from
        /// this turn. Home is always index 0.
        /// </summary>
        public static List<int> Calculate(GameState state, int playerId)
        {
            Player player = state.GetPlayer(playerId);
            if (player == null) return new List<int>();

            List<int> result = new List<int> { player.HomeBodyId };

            List<int> nonHomeCaptured = GetNonHomeCaptured(state, playerId, player.HomeBodyId);
            int bonusCount = nonHomeCaptured.Count / 2;

            if (bonusCount > 0)
            {
                int seed = state.TurnNumber * 31 + playerId;
                Rng rng = new Rng(seed);
                rng.Shuffle(nonHomeCaptured);
                for (int i = 0; i < bonusCount; i++)
                    result.Add(nonHomeCaptured[i]);
            }

            return result;
        }

        /// <summary>
        /// Returns the number of rockets a player would have given their non-home
        /// captured planet count. Exposed for testing without needing a full GameState.
        /// </summary>
        public static int RocketCount(int nonHomeCapturedCount)
            => 1 + nonHomeCapturedCount / 2;

        private static List<int> GetNonHomeCaptured(GameState state, int playerId, int homeBodyId)
        {
            List<int> result = new List<int>();
            foreach (KeyValuePair<int, PlanetOwnership> kv in state.Ownership)
            {
                if (kv.Value.OwnerPlayerId == playerId && kv.Key != homeBodyId)
                    result.Add(kv.Key);
            }
            return result;
        }
    }
}
