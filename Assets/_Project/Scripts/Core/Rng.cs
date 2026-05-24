using System;

namespace Orbital.Core
{
    /// <summary>
    /// Deterministic RNG wrapper for all game-state randomness.
    ///
    /// Use this anywhere game logic needs randomness. NEVER call
    /// UnityEngine.Random or System.Random directly from game-state
    /// code - it breaks save/replay/AI/networking guarantees.
    ///
    /// Presentation code (visual jitter, particle randomness) MAY use
    /// UnityEngine.Random freely since it doesn't affect game state.
    /// </summary>
    public sealed class Rng
    {
        private readonly Random _random;
        public int Seed { get; }

        public Rng(int seed)
        {
            Seed = seed;
            _random = new Random(seed);
        }

        /// <summary>Random int in [0, int.MaxValue).</summary>
        public int NextInt() => _random.Next();

        /// <summary>Random int in [0, maxExclusive).</summary>
        public int NextInt(int maxExclusive) => _random.Next(maxExclusive);

        /// <summary>Random int in [minInclusive, maxExclusive).</summary>
        public int Range(int minInclusive, int maxExclusive)
            => _random.Next(minInclusive, maxExclusive);

        /// <summary>Random float in [0.0, 1.0).</summary>
        public float NextFloat() => (float)_random.NextDouble();

        /// <summary>Random float in [min, max).</summary>
        public float Range(float min, float max)
            => min + (float)_random.NextDouble() * (max - min);

        /// <summary>Random double in [0.0, 1.0).</summary>
        public double NextDouble() => _random.NextDouble();

        /// <summary>Random bool with given probability of being true (default 0.5).</summary>
        public bool Chance(float probability = 0.5f)
            => _random.NextDouble() < probability;

        /// <summary>Pick a uniformly random element from a non-empty list.</summary>
        public T Pick<T>(System.Collections.Generic.IList<T> items)
        {
            if (items == null || items.Count == 0)
                throw new ArgumentException("Cannot pick from empty collection.");
            return items[_random.Next(items.Count)];
        }

        /// <summary>Fisher-Yates shuffle of a list, in place.</summary>
        public void Shuffle<T>(System.Collections.Generic.IList<T> items)
        {
            for (int i = items.Count - 1; i > 0; i--)
            {
                int j = _random.Next(i + 1);
                (items[i], items[j]) = (items[j], items[i]);
            }
        }

        /// <summary>
        /// Create a sub-RNG seeded deterministically from this one and a label.
        /// Useful when one system needs its own stable RNG stream
        /// (e.g. galaxy gen, planet attribute rolling).
        /// </summary>
        public Rng SubStream(string label)
        {
            unchecked
            {
                int hash = Seed;
                foreach (char c in label)
                    hash = hash * 31 + c;
                return new Rng(hash);
            }
        }
    }
}
