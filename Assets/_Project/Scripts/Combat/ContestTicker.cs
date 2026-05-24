using System.Collections.Generic;
using Orbital.Strategy;

namespace Orbital.Combat
{
    /// <summary>
    /// Pure static ticker for active contests. Each turn handover both sides
    /// take casualties equal to ceil(other side / damageDivisor), floored at
    /// minDamage. Whichever side reaches 0 first loses. Writes damage directly
    /// to state.Population[bodyId] (defender) and contest.InvaderCount
    /// (invader), then returns a Result for each contest that reached a
    /// non-Ongoing resolution so TurnManager can update Ownership.
    /// </summary>
    public static class ContestTicker
    {
        public enum Resolution { Ongoing, DefenderWins, InvaderWins, MutualAnnihilation }

        public class Result
        {
            public int BodyId;
            public Resolution Resolution;
            public int DefenderPlayerId;
            public int InvaderPlayerId;
            public int FinalDefenderCount;
            public int FinalInvaderCount;
        }

        public static List<Result> Tick(
            GameState state, int damageDivisor, int minDamage)
        {
            List<Result> results  = new List<Result>();
            List<int>    resolved = new List<int>();

            foreach (KeyValuePair<int, Contest> kv in state.Contests)
            {
                int     bodyId  = kv.Key;
                Contest contest = kv.Value;

                // Resolve defender identity (owner or colonising player).
                int defenderPlayerId;
                if (state.Ownership.TryGetValue(bodyId, out PlanetOwnership owned))
                    defenderPlayerId = owned.OwnerPlayerId;
                else if (state.Colonisations.TryGetValue(bodyId, out Colonisation col))
                    defenderPlayerId = col.PlayerId;
                else
                    continue; // no recognisable defender — skip

                int defenderCount = state.Population.TryGetValue(bodyId, out int dp) ? dp : 0;
                int invaderCount  = contest.InvaderCount;

                int defenderLoss = System.Math.Max(minDamage, CeilDiv(invaderCount, damageDivisor));
                int invaderLoss  = System.Math.Max(minDamage, CeilDiv(defenderCount, damageDivisor));

                defenderCount -= defenderLoss;
                invaderCount  -= invaderLoss;

                // Clamp to 0 and write back to state.
                defenderCount = System.Math.Max(0, defenderCount);
                invaderCount  = System.Math.Max(0, invaderCount);

                state.Population[bodyId] = defenderCount;
                contest.InvaderCount     = invaderCount;

                Resolution resolution;
                if (defenderCount <= 0 && invaderCount <= 0)
                    resolution = Resolution.MutualAnnihilation;
                else if (defenderCount <= 0)
                    resolution = Resolution.InvaderWins;
                else if (invaderCount <= 0)
                    resolution = Resolution.DefenderWins;
                else
                    resolution = Resolution.Ongoing;

                results.Add(new Result
                {
                    BodyId             = bodyId,
                    Resolution         = resolution,
                    DefenderPlayerId   = defenderPlayerId,
                    InvaderPlayerId    = contest.InvaderPlayerId,
                    FinalDefenderCount = defenderCount,
                    FinalInvaderCount  = invaderCount
                });

                if (resolution != Resolution.Ongoing)
                    resolved.Add(bodyId);
            }

            foreach (int bodyId in resolved)
                state.Contests.Remove(bodyId);

            return results;
        }

        private static int CeilDiv(int a, int b)
            => (a + b - 1) / b;
    }
}
