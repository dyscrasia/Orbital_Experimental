using System.Collections.Generic;
using NUnit.Framework;
using Orbital.Combat;
using Orbital.Strategy;
using UnityEngine;

namespace Orbital.Tests
{
    public class WinConditionCheckerTests
    {
        private GameState _state;
        private Player _p1;
        private Player _p2;

        [SetUp]
        public void SetUp()
        {
            _p1 = new Player(1, "Player 1", Color.blue, homeBodyId: 10);
            _p2 = new Player(2, "Player 2", Color.red,  homeBodyId: 20);

            _state = new GameState(new List<Player> { _p1, _p2 });

            // Default starting state: each player owns their own home
            _state.Ownership[10] = new PlanetOwnership { OwnerPlayerId = _p1.Id, OrbitingRocketId = -1 };
            _state.Ownership[20] = new PlanetOwnership { OwnerPlayerId = _p2.Id, OrbitingRocketId = -1 };
        }

        [Test]
        public void NoWin_WhenBothOwnOnlyTheirHome()
        {
            int? winner = WinConditionChecker.CheckForWin(_state);
            Assert.IsNull(winner);
        }

        [Test]
        public void NoWin_WhenPlayerOwnsNeutralPlanets()
        {
            // P1 owns a neutral planet — no win yet
            _state.Ownership[30] = new PlanetOwnership { OwnerPlayerId = _p1.Id, OrbitingRocketId = 1 };

            int? winner = WinConditionChecker.CheckForWin(_state);
            Assert.IsNull(winner);
        }

        [Test]
        public void P1Wins_WhenOwnsP2Home()
        {
            _state.Ownership[_p2.HomeBodyId] = new PlanetOwnership
                { OwnerPlayerId = _p1.Id, OrbitingRocketId = 5 };

            int? winner = WinConditionChecker.CheckForWin(_state);
            Assert.AreEqual(_p1.Id, winner);
        }

        [Test]
        public void P2Wins_WhenOwnsP1Home()
        {
            _state.Ownership[_p1.HomeBodyId] = new PlanetOwnership
                { OwnerPlayerId = _p2.Id, OrbitingRocketId = 7 };

            int? winner = WinConditionChecker.CheckForWin(_state);
            Assert.AreEqual(_p2.Id, winner);
        }

        [Test]
        public void NoWin_WhenP1OwnsOwnHome_AndP2OwnsNeutral()
        {
            // Neither player owns the other's home
            _state.Ownership[30] = new PlanetOwnership { OwnerPlayerId = _p2.Id, OrbitingRocketId = 2 };

            int? winner = WinConditionChecker.CheckForWin(_state);
            Assert.IsNull(winner);
        }

        [Test]
        public void NoWin_WhenEnemyHomeUnowned()
        {
            // P2's home has been dislodged and is currently unowned (no Ownership entry)
            _state.Ownership.Remove(_p2.HomeBodyId);

            int? winner = WinConditionChecker.CheckForWin(_state);
            Assert.IsNull(winner);
        }
    }
}
