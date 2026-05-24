using System.Collections.Generic;
using NUnit.Framework;
using Orbital.Strategy;
using UnityEngine;

namespace Orbital.Tests
{
    /// <summary>
    /// Tests for LaunchSiteCalculator — the Classic-mode rocket production rule:
    ///   1 home rocket + floor(nonHomeCaptured / 2) bonus rockets.
    /// </summary>
    public class LaunchSiteCalculatorTests
    {
        // -------------------------------------------------------------------------
        //  Helpers
        // -------------------------------------------------------------------------

        private const int P1Id   = 1;
        private const int P2Id   = 2;
        private const int P1Home = 100;
        private const int P2Home = 200;

        /// <summary>
        /// Build a GameState where Player 1 owns their home plus the given
        /// extra body IDs (non-home captures). Player 2 owns their home.
        /// </summary>
        private static GameState BuildState(int[] p1NonHomeCaptures, int turnNumber = 1)
        {
            Player p1 = new Player(P1Id, "Player 1", Color.blue, P1Home);
            Player p2 = new Player(P2Id, "Player 2", Color.red,  P2Home);

            GameState state = new GameState(new List<Player> { p1, p2 });
            state.TurnNumber      = turnNumber;
            state.CurrentPlayerId = P1Id;

            state.Ownership[P1Home] = new PlanetOwnership { OwnerPlayerId = P1Id, OrbitingRocketId = -1 };
            state.Ownership[P2Home] = new PlanetOwnership { OwnerPlayerId = P2Id, OrbitingRocketId = -1 };

            foreach (int id in p1NonHomeCaptures)
                state.Ownership[id] = new PlanetOwnership { OwnerPlayerId = P1Id, OrbitingRocketId = 0 };

            return state;
        }

        // -------------------------------------------------------------------------
        //  Rocket count formula: 1 + floor(nonHome / 2)
        // -------------------------------------------------------------------------

        [Test]
        public void RocketCount_ZeroCaptures_IsOne()
            => Assert.AreEqual(1, LaunchSiteCalculator.RocketCount(0));

        [Test]
        public void RocketCount_OneCapture_IsOne()
            => Assert.AreEqual(1, LaunchSiteCalculator.RocketCount(1));

        [Test]
        public void RocketCount_TwoCaptures_IsTwo()
            => Assert.AreEqual(2, LaunchSiteCalculator.RocketCount(2));

        [Test]
        public void RocketCount_ThreeCaptures_IsTwo()
            => Assert.AreEqual(2, LaunchSiteCalculator.RocketCount(3));

        [Test]
        public void RocketCount_FourCaptures_IsThree()
            => Assert.AreEqual(3, LaunchSiteCalculator.RocketCount(4));

        [Test]
        public void RocketCount_FiveCaptures_IsThree()
            => Assert.AreEqual(3, LaunchSiteCalculator.RocketCount(5));

        [Test]
        public void RocketCount_SixCaptures_IsFour()
            => Assert.AreEqual(4, LaunchSiteCalculator.RocketCount(6));

        [Test]
        public void RocketCount_SevenCaptures_IsFour()
            => Assert.AreEqual(4, LaunchSiteCalculator.RocketCount(7));

        [Test]
        public void RocketCount_EightCaptures_IsFive()
            => Assert.AreEqual(5, LaunchSiteCalculator.RocketCount(8));

        // -------------------------------------------------------------------------
        //  Calculate() integration — correct site count and home always first
        // -------------------------------------------------------------------------

        [Test]
        public void Calculate_ZeroNonHome_ReturnsOnlyHome()
        {
            GameState state = BuildState(new int[0]);
            List<int> sites = LaunchSiteCalculator.Calculate(state, P1Id);

            Assert.AreEqual(1, sites.Count);
            Assert.AreEqual(P1Home, sites[0]);
        }

        [Test]
        public void Calculate_OneNonHome_ReturnsOnlyHome()
        {
            GameState state = BuildState(new[] { 301 });
            List<int> sites = LaunchSiteCalculator.Calculate(state, P1Id);

            Assert.AreEqual(1, sites.Count);
            Assert.AreEqual(P1Home, sites[0]);
        }

        [Test]
        public void Calculate_TwoNonHome_ReturnsTwoSites_HomeFirst()
        {
            GameState state = BuildState(new[] { 301, 302 });
            List<int> sites = LaunchSiteCalculator.Calculate(state, P1Id);

            Assert.AreEqual(2, sites.Count);
            Assert.AreEqual(P1Home, sites[0]);
            // The bonus site must be one of the captured bodies
            Assert.IsTrue(sites[1] == 301 || sites[1] == 302);
        }

        [Test]
        public void Calculate_FourNonHome_ReturnsThreeSites()
        {
            GameState state = BuildState(new[] { 301, 302, 303, 304 });
            List<int> sites = LaunchSiteCalculator.Calculate(state, P1Id);

            Assert.AreEqual(3, sites.Count);
            Assert.AreEqual(P1Home, sites[0]);
        }

        [Test]
        public void Calculate_BonusSites_AreSubsetOfCapturedPlanets()
        {
            int[] captured = { 301, 302, 303, 304, 305, 306 };
            GameState state = BuildState(captured);
            List<int> sites = LaunchSiteCalculator.Calculate(state, P1Id);

            // sites[0] is home; sites[1..] must all be in 'captured'
            HashSet<int> capturedSet = new HashSet<int>(captured);
            for (int i = 1; i < sites.Count; i++)
                Assert.IsTrue(capturedSet.Contains(sites[i]),
                    $"Bonus site {sites[i]} is not a captured non-home planet.");
        }

        [Test]
        public void Calculate_NoDuplicateSites()
        {
            GameState state = BuildState(new[] { 301, 302, 303, 304 });
            List<int> sites = LaunchSiteCalculator.Calculate(state, P1Id);

            HashSet<int> seen = new HashSet<int>();
            foreach (int id in sites)
            {
                Assert.IsFalse(seen.Contains(id), $"Duplicate site ID {id} in result.");
                seen.Add(id);
            }
        }

        // -------------------------------------------------------------------------
        //  Determinism — same state always yields same placements
        // -------------------------------------------------------------------------

        [Test]
        public void Calculate_SameState_SamePlacements()
        {
            GameState stateA = BuildState(new[] { 301, 302, 303, 304 }, turnNumber: 3);
            GameState stateB = BuildState(new[] { 301, 302, 303, 304 }, turnNumber: 3);

            List<int> sitesA = LaunchSiteCalculator.Calculate(stateA, P1Id);
            List<int> sitesB = LaunchSiteCalculator.Calculate(stateB, P1Id);

            Assert.AreEqual(sitesA.Count, sitesB.Count);
            for (int i = 0; i < sitesA.Count; i++)
                Assert.AreEqual(sitesA[i], sitesB[i],
                    $"Site at index {i} differs between two identical states.");
        }

        [Test]
        public void Calculate_DifferentTurn_MayDifferentPlacements()
        {
            // With 4 non-home planets, only 1 bonus site is picked from 4 candidates.
            // Turn 1 and Turn 2 use different seeds, so placements are usually different.
            // (This could theoretically pass by coincidence but the seed formula makes it very unlikely.)
            GameState stateTurn1 = BuildState(new[] { 301, 302, 303, 304 }, turnNumber: 1);
            GameState stateTurn2 = BuildState(new[] { 301, 302, 303, 304 }, turnNumber: 2);

            List<int> sites1 = LaunchSiteCalculator.Calculate(stateTurn1, P1Id);
            List<int> sites2 = LaunchSiteCalculator.Calculate(stateTurn2, P1Id);

            // We can't guarantee they differ, but we can verify both have the right count.
            Assert.AreEqual(2, sites1.Count);
            Assert.AreEqual(2, sites2.Count);
            // And both bonus sites are valid.
            HashSet<int> valid = new HashSet<int> { 301, 302, 303, 304 };
            Assert.IsTrue(valid.Contains(sites1[1]));
            Assert.IsTrue(valid.Contains(sites2[1]));
        }

        [Test]
        public void Calculate_InvalidPlayerId_ReturnsEmptyList()
        {
            GameState state = BuildState(new int[0]);
            List<int> sites = LaunchSiteCalculator.Calculate(state, 99);
            Assert.AreEqual(0, sites.Count);
        }
    }
}
