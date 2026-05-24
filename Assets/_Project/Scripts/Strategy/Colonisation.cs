namespace Orbital.Strategy
{
    /// <summary>
    /// One in-progress colonisation. Lives in GameState.Colonisations keyed by body ID.
    /// Removed when the timer reaches 0 (the planet becomes owned)
    /// or when the colonisation is cancelled by some future mechanic.
    /// </summary>
    public class Colonisation
    {
        public int PlayerId;
        public int TurnsRemaining;
        // ColonistCount removed in Jump 5 — the count lives in GameState.Population[bodyId].
    }
}
