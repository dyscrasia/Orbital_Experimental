using Orbital.Strategy;

namespace Orbital.Combat
{
    public enum ColonisationOutcome
    {
        NoOp,                       // 0 passengers, or no meaningful operation
        Started,                    // unowned planet — colonisation begins
        Reinforced,                 // same-player colonisation — passengers added, timer recomputed
        Blocked,                    // no meaningful path for this firing player
        StartContest,               // opposing-player rocket on owned or colonising planet, no existing contest
        ReinforceContest_Invader,   // contest exists; rocket is the invader → invader count grows
        ReinforceContest_Defender   // defender (or owner) reinforces their own side
    }

    public class ColonisationChange
    {
        public ColonisationOutcome Outcome;
        public int BodyId;
        public int PlayerId;            // the firing player
        public int PassengersDeployed;  // 0 when Blocked / NoOp
        public int NewColonistCount;    // post-op count for the relevant side
        public int NewTurnsRemaining;   // only relevant for Started / Reinforced colonisation
    }

    /// <summary>
    /// Pure static class — decides what happens when a rocket carrying passengers
    /// enters orbit around a planet. Returns a description; never mutates state.
    /// </summary>
    public static class ColonisationResolver
    {
        /// <summary>
        /// Rule matrix (Jump 5):
        ///   Contest active     → ReinforceContest_Invader / ReinforceContest_Defender / Blocked
        ///   Owned (no contest) → ReinforceContest_Defender (same player) / StartContest (opposing)
        ///   Colonising (no contest) → Reinforced (same) / StartContest (opposing)
        ///   Unowned, not colonising → Started
        ///   passengers == 0    → NoOp always
        /// </summary>
        public static ColonisationChange Resolve(
            GameState state,
            int firingPlayerId,
            int bodyId,
            int passengers,
            int baseDuration,
            int minTurns)
        {
            var noOp = new ColonisationChange
                { Outcome = ColonisationOutcome.NoOp, BodyId = bodyId, PlayerId = firingPlayerId };

            if (passengers <= 0)
                return noOp;

            Player firingPlayer = state.GetPlayer(firingPlayerId);
            if (firingPlayer == null)
                return noOp;

            // Determine which player currently holds the defender role.
            int defenderPlayerId = -1;
            if (state.Ownership.TryGetValue(bodyId, out PlanetOwnership owned))
                defenderPlayerId = owned.OwnerPlayerId;
            else if (state.Colonisations.TryGetValue(bodyId, out Colonisation colonising))
                defenderPlayerId = colonising.PlayerId;

            // ── Contest already active ──────────────────────────────────────────
            if (state.Contests.TryGetValue(bodyId, out Contest contest))
            {
                if (firingPlayerId == contest.InvaderPlayerId)
                {
                    return new ColonisationChange
                    {
                        Outcome            = ColonisationOutcome.ReinforceContest_Invader,
                        BodyId             = bodyId,
                        PlayerId           = firingPlayerId,
                        PassengersDeployed = passengers,
                        NewColonistCount   = contest.InvaderCount + passengers
                    };
                }
                if (firingPlayerId == defenderPlayerId)
                {
                    int current = state.Population.TryGetValue(bodyId, out int dp) ? dp : 0;
                    return new ColonisationChange
                    {
                        Outcome            = ColonisationOutcome.ReinforceContest_Defender,
                        BodyId             = bodyId,
                        PlayerId           = firingPlayerId,
                        PassengersDeployed = passengers,
                        NewColonistCount   = current + passengers
                    };
                }
                // Third party (unreachable in 2-player, kept for robustness)
                return new ColonisationChange
                    { Outcome = ColonisationOutcome.Blocked, BodyId = bodyId, PlayerId = firingPlayerId };
            }

            // ── Owned planet (no contest) ───────────────────────────────────────
            if (state.Ownership.ContainsKey(bodyId))
            {
                if (defenderPlayerId == firingPlayerId)
                {
                    // Same owner fires — resupply (or reinforce own home)
                    int current = state.Population.TryGetValue(bodyId, out int p) ? p : 0;
                    return new ColonisationChange
                    {
                        Outcome            = ColonisationOutcome.ReinforceContest_Defender,
                        BodyId             = bodyId,
                        PlayerId           = firingPlayerId,
                        PassengersDeployed = passengers,
                        NewColonistCount   = current + passengers
                    };
                }
                // Opposing player → start contest
                return new ColonisationChange
                {
                    Outcome            = ColonisationOutcome.StartContest,
                    BodyId             = bodyId,
                    PlayerId           = firingPlayerId,
                    PassengersDeployed = passengers,
                    NewColonistCount   = passengers
                };
            }

            // ── In-progress colonisation (no contest) ──────────────────────────
            if (state.Colonisations.TryGetValue(bodyId, out Colonisation existing))
            {
                if (existing.PlayerId == firingPlayerId)
                {
                    // Reinforce: add passengers to Population[bodyId], recompute timer.
                    int currentPop = state.Population.TryGetValue(bodyId, out int p) ? p : 0;
                    int newCount   = currentPop + passengers;
                    int turns      = ComputeTurns(newCount, baseDuration, minTurns);
                    return new ColonisationChange
                    {
                        Outcome            = ColonisationOutcome.Reinforced,
                        BodyId             = bodyId,
                        PlayerId           = firingPlayerId,
                        PassengersDeployed = passengers,
                        NewColonistCount   = newCount,
                        NewTurnsRemaining  = turns
                    };
                }
                // Opposing player contests the colonisation
                return new ColonisationChange
                {
                    Outcome            = ColonisationOutcome.StartContest,
                    BodyId             = bodyId,
                    PlayerId           = firingPlayerId,
                    PassengersDeployed = passengers,
                    NewColonistCount   = passengers
                };
            }

            // ── Unowned, not colonising → start fresh ──────────────────────────
            int startTurns = ComputeTurns(passengers, baseDuration, minTurns);
            return new ColonisationChange
            {
                Outcome            = ColonisationOutcome.Started,
                BodyId             = bodyId,
                PlayerId           = firingPlayerId,
                PassengersDeployed = passengers,
                NewColonistCount   = passengers,
                NewTurnsRemaining  = startTurns
            };
        }

        private static int ComputeTurns(int colonists, int baseDuration, int minTurns)
            => System.Math.Max(minTurns, (baseDuration + colonists - 1) / colonists);
    }
}
