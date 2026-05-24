namespace Orbital.Strategy
{
    /// <summary>
    /// An active combat on a single planet. Defender count lives in
    /// state.Population[bodyId]; defender player is the planet's current
    /// owner (or, if the planet is still being colonised, the colonising
    /// player). Invader count and player live here.
    /// </summary>
    public class Contest
    {
        public int InvaderPlayerId;
        public int InvaderCount;
    }
}
