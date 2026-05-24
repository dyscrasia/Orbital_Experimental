namespace Orbital.Galaxy
{
    /// <summary>
    /// Per-criterion scores and overall pass/fail from GalaxyEvaluator.
    /// </summary>
    public class GalaxyEvaluation
    {
        public float PathViability;
        public float RegionBalance;
        public float Spread;
        public float Symmetry;
        public bool IsAcceptable;
        public string Summary;
    }
}
