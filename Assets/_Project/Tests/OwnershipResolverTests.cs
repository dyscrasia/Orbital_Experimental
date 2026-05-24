// OwnershipResolver.ResolveCapture is obsolete since Jump 3 but its tests remain
// as documentation of the method's behaviour. Suppress the warning here.
#pragma warning disable CS0618

using System.Collections.Generic;
using NUnit.Framework;
using Orbital.Combat;
using Orbital.Physics;
using Orbital.Strategy;
using UnityEngine;

namespace Orbital.Tests
{
    public class OwnershipResolverTests
    {
        private GameState _state;
        private Player _p1;
        private Player _p2;
        private RocketState _dummyRocket;

        [SetUp]
        public void SetUp()
        {
            _p1 = new Player(1, "Player 1", Color.blue, homeBodyId: 10);
            _p2 = new Player(2, "Player 2", Color.red,  homeBodyId: 20);

            _state = new GameState(new List<Player> { _p1, _p2 });

            _dummyRocket = new RocketState
            {
                OrbitRadius = 3f, OrbitAngle = 0f,
                OrbitAngularSpeed = 1f, OrbitDirection = 1
            };
        }

        [Test]
        public void FreshCapture_UnownedBody_ReturnsCorrectChange()
        {
            // Body 30 is unowned
            OwnershipChange change = OwnershipResolver.ResolveCapture(_state, _p1.Id, 30, _dummyRocket);

            Assert.IsNotNull(change);
            Assert.AreEqual(30, change.BodyId);
            Assert.AreEqual(_p1.Id, change.NewOwnerId);
            Assert.IsNull(change.PreviousOwnerId);
            Assert.IsFalse(change.DislodgedExistingRocket);
        }

        [Test]
        public void Dislodge_EnemyOwnedBody_ReturnsCorrectChange()
        {
            // P2 already owns body 30 with an orbiting rocket
            _state.Ownership[30] = new PlanetOwnership
                { OwnerPlayerId = _p2.Id, OrbitingRocketId = 5 };

            OwnershipChange change = OwnershipResolver.ResolveCapture(_state, _p1.Id, 30, _dummyRocket);

            Assert.IsNotNull(change);
            Assert.AreEqual(_p1.Id, change.NewOwnerId);
            Assert.AreEqual(_p2.Id, change.PreviousOwnerId);
            Assert.IsTrue(change.DislodgedExistingRocket);
        }

        [Test]
        public void ReCapture_SamePlayerAlreadyOwns_RefreshesVisual()
        {
            // P1 already owns body 30 (same player re-captures their own planet)
            _state.Ownership[30] = new PlanetOwnership
                { OwnerPlayerId = _p1.Id, OrbitingRocketId = 3 };

            OwnershipChange change = OwnershipResolver.ResolveCapture(_state, _p1.Id, 30, _dummyRocket);

            Assert.IsNotNull(change);
            Assert.AreEqual(_p1.Id, change.NewOwnerId);
            Assert.AreEqual(_p1.Id, change.PreviousOwnerId);
            Assert.IsTrue(change.DislodgedExistingRocket); // old view gets replaced
        }

        [Test]
        public void OwnHome_ReturnsNull_NoOwnershipChange()
        {
            // P1 firing at P1's own home — no-op
            OwnershipChange change = OwnershipResolver.ResolveCapture(
                _state, _p1.Id, _p1.HomeBodyId, _dummyRocket);

            Assert.IsNull(change);
        }

        [Test]
        public void EnemyHome_ReturnsDislodge_WinCheckerHandlesRest()
        {
            // Capturing enemy's home should behave like any other dislodge:
            // OwnershipResolver returns an OwnershipChange; WinConditionChecker detects the win.
            _state.Ownership[_p2.HomeBodyId] = new PlanetOwnership
                { OwnerPlayerId = _p2.Id, OrbitingRocketId = -1 };

            OwnershipChange change = OwnershipResolver.ResolveCapture(
                _state, _p1.Id, _p2.HomeBodyId, _dummyRocket);

            Assert.IsNotNull(change);
            Assert.AreEqual(_p1.Id, change.NewOwnerId);
            Assert.AreEqual(_p2.Id, change.PreviousOwnerId);
        }

        [Test]
        public void InvalidPlayerId_ReturnsNull()
        {
            OwnershipChange change = OwnershipResolver.ResolveCapture(_state, 99, 30, _dummyRocket);
            Assert.IsNull(change);
        }
    }
}
