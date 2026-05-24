using System.Collections.Generic;
using Orbital.Physics;
using UnityEngine;

namespace Orbital.Galaxy
{
    /// <summary>
    /// Output data class produced by GalaxyGenerator.
    /// Immutable after construction — all fields set in the constructor.
    /// </summary>
    public class GalaxyData
    {
        public int Seed { get; }
        public IReadOnlyList<CelestialBody> Bodies { get; }
        public int Player1HomeId { get; }
        public int Player2HomeId { get; }
        public Rect PlayArea { get; }
        public GalaxyEvaluation Evaluation { get; }

        public GalaxyData(
            int seed,
            IReadOnlyList<CelestialBody> bodies,
            int player1HomeId,
            int player2HomeId,
            Rect playArea,
            GalaxyEvaluation evaluation)
        {
            Seed = seed;
            Bodies = bodies;
            Player1HomeId = player1HomeId;
            Player2HomeId = player2HomeId;
            PlayArea = playArea;
            Evaluation = evaluation;
        }
    }
}
