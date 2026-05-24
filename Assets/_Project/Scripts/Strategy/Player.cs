using UnityEngine;

namespace Orbital.Strategy
{
    /// <summary>
    /// Pure data class representing one of the two competing players.
    /// </summary>
    public class Player
    {
        public int Id { get; }
        public string Name { get; }
        public Color Color { get; }
        public int HomeBodyId { get; }

        public Player(int id, string name, Color color, int homeBodyId)
        {
            Id = id;
            Name = name;
            Color = color;
            HomeBodyId = homeBodyId;
        }
    }
}
