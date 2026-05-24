using System.Collections.Generic;
using Orbital.Strategy;

namespace Orbital.Combat
{
    public static class ColonisationTicker
    {
        /// <summary>
        /// Describes a colonisation that completed this tick.
        /// TurnManager uses this to build a PlanetOwnership entry and refresh views.
        /// </summary>
        public class Completion
        {
            public int BodyId;
            public int PlayerId;
            public int FinalCount; // count read from Population[bodyId] at completion time
        }

        /// <summary>
        /// Decrement every in-progress colonisation by 1. Any that reach 0 are
        /// removed from state.Colonisations and returned as completions.
        /// TurnManager is responsible for adding the resulting PlanetOwnership entries.
        /// Mutates state.Colonisations only — does not touch state.Ownership.
        /// </summary>
        public static List<Completion> Tick(GameState state)
        {
            List<Completion> completions = new List<Completion>();
            List<int> toComplete = new List<int>();

            foreach (KeyValuePair<int, Colonisation> kv in state.Colonisations)
            {
                kv.Value.TurnsRemaining -= 1;
                if (kv.Value.TurnsRemaining <= 0)
                    toComplete.Add(kv.Key);
            }

            foreach (int bodyId in toComplete)
            {
                Colonisation col = state.Colonisations[bodyId];
                completions.Add(new Completion
                {
                    BodyId     = bodyId,
                    PlayerId   = col.PlayerId,
                    FinalCount = state.Population.TryGetValue(bodyId, out int pop) ? pop : 0
                });
                state.Colonisations.Remove(bodyId);
            }

            return completions;
        }
    }
}
