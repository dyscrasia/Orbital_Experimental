using UnityEngine;

namespace Orbital.Strategy
{
    [CreateAssetMenu(fileName = "StrategyParameters",
                     menuName = "Orbital/Strategy Parameters")]
    public class StrategyParameters : ScriptableObject
    {
        [Header("Population")]
        [Tooltip("People added to a player's home planet at the start of each of " +
                 "their turns.")]
        public int PopulationGrowthPerTurn = 10;

        [Tooltip("Starting population for each player on turn 1.")]
        public int StartingPopulation = 0;

        [Tooltip("Captured planets grow at PopulationGrowthPerTurn divided by this. " +
                 "Default 2 means half the home rate. Higher means slower growth on " +
                 "captured planets.")]
        [Min(1)]
        public int CapturedPlanetGrowthDivisor = 2;

        [Header("Combat")]
        [Tooltip("Per turn each side loses ceil(other / divisor) people, floored at ContestMinDamage.")]
        [Min(1)]
        public int ContestDamageDivisor = 5;

        [Tooltip("Floor on damage per turn so combat always progresses.")]
        [Min(0)]
        public int ContestMinDamage = 1;

        [Header("Colonisation")]
        [Tooltip("Total colonist-turns needed to capture a planet. " +
                 "Turns to complete = max(MinColonisationTurns, ceil(BaseDuration / colonists)).")]
        public int ColonisationBaseDuration = 20;

        [Tooltip("Floor on turns-to-complete, even with very large colonist counts.")]
        public int MinColonisationTurns = 1;
    }
}
