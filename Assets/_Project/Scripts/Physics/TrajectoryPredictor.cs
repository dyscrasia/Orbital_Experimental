using System.Collections.Generic;
using UnityEngine;

namespace Orbital.Physics
{
    /// <summary>
    /// Runs the patched-conics solver forward N steps with zero thrust to predict a trajectory.
    /// Pure static — no Unity dependencies beyond the value-type Vector2.
    /// </summary>
    public static class TrajectoryPredictor
    {
        /// <summary>
        /// Predict the trajectory of a rocket from a given state with no thrust applied.
        /// </summary>
        /// <param name="initialState">Starting rocket state (velocity should be the hypothetical launch velocity).</param>
        /// <param name="bodies">Celestial bodies in the scene.</param>
        /// <param name="steps">Number of simulation steps to predict.</param>
        /// <param name="dt">Fixed timestep in seconds.</param>
        /// <param name="G">Gravitational constant.</param>
        /// <returns>List of world positions, one per step (including the initial position).</returns>
        public static List<Vector2> Predict(
            RocketState initialState,
            IReadOnlyList<CelestialBody> bodies,
            int steps,
            float dt,
            float G)
        {
            List<Vector2> positions = new List<Vector2>(steps + 1);
            positions.Add(initialState.Position);

            RocketState state = initialState.Clone();

            for (int i = 0; i < steps; i++)
            {
                state = PatchedConicsSolver.Step(state, bodies, dt, G);
                positions.Add(state.Position);

                // Stop early if the rocket crashes — no point drawing trajectory into a planet
                bool crashed = false;
                foreach (CelestialBody body in bodies)
                {
                    if ((state.Position - body.Position).magnitude <= body.Radius)
                    {
                        crashed = true;
                        break;
                    }
                }
                if (crashed) break;
            }

            return positions;
        }
    }
}
