using System.Collections.Generic;
using NUnit.Framework;
using Orbital.Strategy;
using UnityEngine;

namespace Orbital.Tests
{
    /// <summary>
    /// Tests for LaunchSiteCalculator — the Strategy-variant rule:
    ///   every owned planet is a launch site (home first, captured sorted by ascending body ID).
    /// </summary>
    public class LaunchSiteCalculatorTests
    {
        private const int P1Id   = 1;
        private const int P2Id   = 2;
        private const int P1Home = 100;
        private const int P2Home = 200;

        /// <summary>
        /// Build a GameState where Player 1 owns their home plus the given
        /// extra body IDs (non-home captures). Player 2 owns their home.
        /// </summary>
        private static GameState BuildState(int[] p1NonHomeCaptures)
        {
            Player p1 = new Player(P1Id, "Player 1", Color.blue, P1Home);
            Player p2 = new Player(P2Id, "Player 2", Color.red,  P2Home);

            GameState state = new GameState(new List<Player> { p1, p2 });
            state.TurnNumber      = 1;
            state.CurrentPlayerId = P1Id;

            state.Ownership[P1Home] = new PlanetOwnership { OwnerPlayerId = P1Id, OrbitingRocketId = -1 };
            state.Ownership[P2Home] = new PlanetOwnership { OwnerPlayerId = P2Id, OrbitingRocketId = -1 };

            foreach (int id in p1NonHomeCaptures)
                state.Ownership[id] = new PlanetOwnership { OwnerPlayerId = P1Id, OrbitingRocketId = 0 };

            return state;
        }

        [Test]
        public void Calculate_NoCaptures_ReturnsOnlyHome()
        {
            GameState state = BuildState(new int[0]);
            List<int> sites = LaunchSiteCalculator.Calculate(state, P1Id);

            Assert.AreEqual(1, sites.Count);
            Assert.AreEqual(P1Home, sites[0]);
        }

        [Test]
        public void Calculate_OneCapture_ReturnsTwoSites_HomeFirst()
        {
            GameState state = BuildState(new[] { 301 });
            List<int> sites = LaunchSiteCalculator.Calculate(state, P1Id);

            Assert.AreEqual(2, sites.Count);
            Assert.AreEqual(P1Home, sites[0]);
            Assert.AreEqual(301, sites[1]);
        }

        [Test]
        public void Calculate_MultipleCaptures_AllIncluded_SortedAfterHome()
        {
            // IDs deliberately out of order to verify that sorting is applied.
            GameState state = BuildState(new[] { 305, 301, 303 });
            List<int> sites = LaunchSiteCalculator.Calculate(state, P1Id);

            Assert.AreEqual(4, sites.Count);
            Assert.AreEqual(P1Home, sites[0]);
            Assert.AreEqual(301, sites[1]);
            Assert.AreEqual(303, sites[2]);
            Assert.AreEqual(305, sites[3]);
        }

        [Test]
        public void Calculate_DoesNotIncludeEnemyPlanets()
        {
            GameState state = BuildState(new[] { 301, 302 });
            List<int> sites = LaunchSiteCalculator.Calculate(state, P1Id);

            Assert.IsFalse(sites.Contains(P2Home),
                "Enemy home should not appear as a P1 launch site.");
            foreach (int id in sites)
                Assert.AreNotEqual(P2Home, id);
        }

        [Test]
        public void Calculate_NoDuplicateSites()
        {
            GameState state = BuildState(new[] { 301, 302, 303 });
            List<int> sites = LaunchSiteCalculator.Calculate(state, P1Id);

            HashSet<int> seen = new HashSet<int>();
            foreach (int id in sites)
            {
                Assert.IsFalse(seen.Contains(id), $"Duplicate site ID {id} in result.");
                seen.Add(id);
            }
        }

        [Test]
        public void Calculate_SameState_SamePlacements()
        {
            // Determinism: identical states must produce identical, identically-ordered results.
            GameState stateA = BuildState(new[] { 305, 301, 303 });
            GameState stateB = BuildState(new[] { 305, 301, 303 });

            List<int> sitesA = LaunchSiteCalculator.Calculate(stateA, P1Id);
            List<int> sitesB = LaunchSiteCalculator.Calculate(stateB, P1Id);

            Assert.AreEqual(sitesA.Count, sitesB.Count);
            for (int i = 0; i < sitesA.Count; i++)
                Assert.AreEqual(sitesA[i], sitesB[i],
                    $"Site at index {i} differs between two identical states.");
        }

        [Test]
        public void Calculate_InvalidPlayerId_ReturnsEmptyList()
        {
            GameState state = BuildState(new int[0]);
            List<int> sites = LaunchSiteCalculator.Calculate(state, 99);
            Assert.AreEqual(0, sites.Count);
        }

        [Test]
        public void Calculate_ManyCaptures_AllReturned()
        {
            int[] captured = { 310, 302, 308, 304, 306 };
            GameState state = BuildState(captured);
            List<int> sites = LaunchSiteCalculator.Calculate(state, P1Id);

            // All owned planets returned: 1 home + 5 captured.
            Assert.AreEqual(6, sites.Count);
            Assert.AreEqual(P1Home, sites[0]);
        }
    }
}
