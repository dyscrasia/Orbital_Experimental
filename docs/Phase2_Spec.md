# Orbital — Phase 2 Spec: Procedural Galaxy Generation

## Goal

Replace the hard-coded Phase 1 scenario with a procedural generator that produces small (15-25 body), strategically interesting galaxies on demand, with home planets at fixed opposite ends and bodies clustered with corridors between clusters. Add a developer tool to inspect and regenerate galaxies via seed input, and a quality evaluator that rejects unplayable layouts.

## Scope

**In scope:**
- `GalaxyGenerator` — deterministic procedural generator from seed + parameters
- `GalaxyEvaluator` — quality scoring and rejection of bad layouts
- Multiple planet sub-types with visual variety (5 types: Rocky, Ice, Lava, Gas, Water)
- `GalaxyParameters` — ScriptableObject holding all generator tunables
- `GalaxyVisualizer` — runtime dev tool with seed input and regenerate button
- Integration: existing prototype scenario uses generated galaxies instead of hard-coded data
- Hard-coded fallback still available behind a debug toggle for comparison

**Out of scope (do NOT build):**
- Turn system, economy, research, AI (Phases 3-5)
- Multiple rocket types, payloads (Phase 6)
- Per-planet resource specialization mechanics (Phase 6 — visual sub-types only in Phase 2)
- Final art for planets (Phase 6 — colored circles still fine)
- Sun bodies (Phase 2 galaxies have no central star — see design notes below)

## Architectural rules (from CLAUDE.md)

- All generation is **deterministic from a seed**. Use the existing `Rng` wrapper. Never `UnityEngine.Random`.
- Generator is **pure C#** — no Unity dependencies in the algorithm. The output is a list of `CelestialBody` data records, suitable for handing to the existing simulation.
- All tunables (body count range, cluster count, play area size, capture parameters, body type definitions) live on a `GalaxyParameters` ScriptableObject — adjustable in the inspector without recompilation.
- The evaluator is also pure C# and unit-testable.

## Design notes on choices

- **Galaxy size: 15-25 bodies.** Tight tactical games, 20-30 minute matches.
- **Distribution: clustered with corridors.** 3-5 loose clusters of 3-7 bodies each. Corridors between clusters create natural shot lanes and chokepoints.
- **Home spawns: fixed opposite ends.** Player 1 home at left edge, Player 2 home at right edge. Symmetric, predictable, easy to balance.
- **No central sun in Phase 2.** With small galaxies, a strong central sun would dominate the gravitational field and make planet-to-planet pinball less interesting. The home planets and clusters provide all the gravity. (We can re-add suns in later phases if balance demands it.)
- **5 planet sub-types** (Rocky, Ice, Lava, Gas, Water) for visual variety. In Phase 2 they share gameplay parameters (mass, radius, capture criteria) — only color differs. Differentiation by type comes in Phase 6 with the resource specialization system.

## File map

All paths relative to `Assets/_Project/`.

### Scripts/Galaxy/
- **`GalaxyParameters.cs`** — `ScriptableObject` holding all generator tunables. Fields:
  - `int MinBodies = 15`, `int MaxBodies = 25`
  - `int MinClusters = 3`, `int MaxClusters = 5`
  - `int MinBodiesPerCluster = 3`, `int MaxBodiesPerCluster = 7`
  - `float PlayAreaWidth = 80`, `float PlayAreaHeight = 40`
  - `float HomePlanetXOffsetFromEdge = 5` (so home planets sit a few units in from the left/right edges)
  - `float MinBodySeparation = 2.5f` (minimum centre-to-centre distance between any two bodies)
  - `float ClusterCenterXMin = -25`, `float ClusterCenterXMax = 25` (cluster centres only in the middle band, not where home planets are)
  - `float ClusterCenterYJitter = 12`
  - `float ClusterRadius = 6` (max distance of a body from its cluster centre)
  - `int OutlierCountMin = 0`, `int OutlierCountMax = 3` (lone planets outside any cluster, for variety)
  - `BodyTypeDefinition[] BodyTypes` — array of 5 entries (one per sub-type)
  - `CaptureCriteria DefaultCaptureCriteria` — applied to all generated bodies for now

- **`BodyTypeDefinition.cs`** — small data class. Fields:
  - `string TypeName` (Rocky, Ice, Lava, Gas, Water)
  - `Color VisualColor`
  - `float MinMass`, `float MaxMass`
  - `float MinRadius`, `float MaxRadius`
  - `float Weight` (relative selection probability)

- **`Galaxy.cs`** — output data class. Fields:
  - `int Seed`
  - `IReadOnlyList<CelestialBody> Bodies`
  - `int Player1HomeId`, `int Player2HomeId`
  - `Rect PlayArea`
  - `GalaxyEvaluation Evaluation` (the scoring breakdown)

- **`GalaxyGenerator.cs`** — pure static class. Method:
  - `Galaxy Generate(int seed, GalaxyParameters parameters)` — runs the algorithm below. Will internally retry up to `MaxAttempts = 50` times with derived seeds if the evaluator rejects layouts; throws or returns null if no acceptable galaxy found.

- **`GalaxyEvaluator.cs`** — pure static class. Method:
  - `GalaxyEvaluation Evaluate(IReadOnlyList<CelestialBody> bodies, int p1HomeId, int p2HomeId, Rect playArea)` — runs the criteria below, returns a `GalaxyEvaluation` with per-criterion scores and an overall pass/fail flag.

- **`GalaxyEvaluation.cs`** — data class. Fields: per-criterion floats (PathViability, RegionBalance, Spread, Symmetry), overall `bool IsAcceptable`, and a `string` summary for debug display.

### Scripts/Presentation/
- **`GalaxyVisualizer.cs`** — `MonoBehaviour` providing the dev tool. Exposes in inspector:
  - Reference to a `GalaxyParameters` ScriptableObject
  - `int CurrentSeed` field (defaults to a random value at scene start)
  - Public method `Regenerate()` callable from a GUI button
  - Public method `RegenerateWithRandomSeed()`
  
  In Update(), accept hotkey input: pressing **G** regenerates with a new random seed, pressing **B** regenerates the same seed (for repeatability while iterating). Render the galaxy (via the existing CelestialBodyView pipeline) and display the seed and evaluation summary as on-screen text via TextMeshPro. Optionally show cluster centres as faint markers and corridor regions as faint shaded zones for debugging.

- **`PrototypeScenarioController.cs`** — modify existing class. Add a `bool UseProceduralGalaxy = true` toggle. When true, on Awake call `GalaxyGenerator.Generate(seed, parameters)` instead of building the hard-coded scenario. The home planet for the player rocket is `galaxy.Player1HomeId`. The hard-coded scenario remains intact behind the toggle for comparison.

### Tests/
- **`GalaxyGeneratorTests.cs`** — verify:
  - Same seed produces the same galaxy (determinism)
  - Generated galaxies always satisfy `MinBodySeparation`
  - Body count is within `[MinBodies, MaxBodies]`
  - Home planets are placed at the configured edge offsets
- **`GalaxyEvaluatorTests.cs`** — verify each criterion behaves correctly on hand-crafted layouts.

## Generator algorithm

```
Generate(seed, parameters):
    rng = new Rng(seed)
    
    for attempt in 1..MaxAttempts:
        attemptRng = rng.SubStream($"attempt-{attempt}")
        bodies = []
        
        # 1. Place home planets at fixed edge positions
        homeY1 = attemptRng.Range(-3, 3)  # small Y jitter for variety
        homeY2 = attemptRng.Range(-3, 3)
        p1Home = new CelestialBody(0, "Home1", (-PlayAreaWidth/2 + HomePlanetXOffsetFromEdge, homeY1), ...)
        p2Home = new CelestialBody(1, "Home2", (PlayAreaWidth/2 - HomePlanetXOffsetFromEdge, homeY2), ...)
        bodies.Add(p1Home)
        bodies.Add(p2Home)
        
        # 2. Decide cluster count and place cluster centres
        clusterCount = attemptRng.Range(MinClusters, MaxClusters + 1)
        clusterCenters = []
        for i in 0..clusterCount-1:
            # Distribute roughly evenly along X with jitter
            cx = lerp(ClusterCenterXMin, ClusterCenterXMax, (i + 0.5) / clusterCount) + attemptRng.Range(-3, 3)
            cy = attemptRng.Range(-ClusterCenterYJitter, ClusterCenterYJitter)
            clusterCenters.Add((cx, cy))
        
        # 3. For each cluster, generate bodies around its centre
        for cluster in clusterCenters:
            count = attemptRng.Range(MinBodiesPerCluster, MaxBodiesPerCluster + 1)
            for j in 0..count-1:
                body = generateBodyNear(cluster, ClusterRadius, bodies, attemptRng, parameters)
                if body != null: bodies.Add(body)
        
        # 4. Add a few outlier planets for variety
        outlierCount = attemptRng.Range(OutlierCountMin, OutlierCountMax + 1)
        for k in 0..outlierCount-1:
            body = generateBodyAnywhereInPlayArea(bodies, attemptRng, parameters)
            if body != null: bodies.Add(body)
        
        # 5. Cap body count
        if bodies.Count > MaxBodies:
            # remove non-home bodies until count is within range
        
        # 6. Evaluate
        eval = GalaxyEvaluator.Evaluate(bodies, p1Home.Id, p2Home.Id, playArea)
        if eval.IsAcceptable:
            return new Galaxy(seed, bodies, p1Home.Id, p2Home.Id, playArea, eval)
    
    # If we couldn't generate an acceptable layout after MaxAttempts, return the best we found
    return bestGalaxyFound  # or throw
```

`generateBodyNear(centre, maxRadius, existingBodies, rng, params)`:
- Try up to 30 times: pick a random offset within `maxRadius`, compute candidate position, check it's at least `MinBodySeparation` from every existing body and within play area. If valid, pick a body type by weighted random, generate mass and radius from that type's range, return the body. If no valid position found, return null.

`generateBodyAnywhereInPlayArea(...)`: same but offset from a random point in the play area, with constraint that it's not within `ClusterRadius` of any cluster centre (so outliers feel like outliers).

## Evaluator criteria

`Evaluate(bodies, p1HomeId, p2HomeId, playArea)` runs four checks:

**1. PathViability (0-1)**: For each home, simulate 8 candidate shots (different launch angles around the home, full-thrust) using the existing `PatchedConicsSolver` headlessly. Record what fraction of the *other* (non-home) bodies any of those candidate shots came within capture-ring distance of, integrated over the flight. Score = average over both homes. Threshold: `>= 0.4` (at least 40% of bodies are reachable in some shot).

**2. RegionBalance (0-1)**: Partition non-home bodies into "Player 1 territory" and "Player 2 territory" by which home they're closer to. Score = `1 - |p1Count - p2Count| / totalNonHomeBodies`. Threshold: `>= 0.6` (no player has more than 70% of bodies near them).

**3. Spread (0-1)**: Compute average pairwise distance between bodies normalized by play area diagonal. Reject if any cluster has bodies tighter than `MinBodySeparation` (should be impossible if generator is correct, but a safety check). Score: continuous, threshold `>= 0.3`.

**4. Symmetry (0-1)**: For each cluster, find the most similar cluster on the opposite side of the centre line and score how closely they mirror in body count and centre distance from x=0. Score = average across clusters. Threshold: `>= 0.5` (rough mirroring).

Overall `IsAcceptable = all four scores >= their thresholds`. Tunable via `GalaxyParameters` if we want to relax during iteration.

## Visualizer

The visualizer is a debug scene tool. In the prototype scene:

- A `GalaxyVisualizer` GameObject in the scene with a reference to a `GalaxyParameters` SO.
- Hotkeys:
  - **G**: regenerate with new random seed
  - **B**: regenerate with current seed (deterministic re-roll, useful for verifying a fix didn't change a known-good galaxy)
- On-screen text showing: current seed, body count, evaluation summary (e.g., "PathViability: 0.62, Balance: 0.85, Spread: 0.41, Symmetry: 0.78 — ACCEPTABLE")
- The rocket and aim system from Phase 1 still work — pick up where the rocket starts (Player 1's home) and take shots in the new galaxy.

## Integration with existing scene

- In the Phase 1 scene, the existing `PrototypeScenarioController` is updated. When `UseProceduralGalaxy` is true (the default), it calls the generator and uses the resulting `Galaxy.Bodies` list as its scenario.
- The rocket starts at Player 1's home planet position + a small offset.
- All existing systems (multi-body gravity, capture detection, kinematic orbit, R reset) continue to work unchanged.
- After R reset in procedural mode, the rocket also resets to the same generated galaxy. Pressing G generates a new galaxy and resets the rocket there.

## Hard-coded fallback

`UseProceduralGalaxy = false` falls back to the Phase 1 hard-coded scenario unchanged. Useful for regression testing capture parameters and for fast iteration on fixed body layouts when something feels off.

## Success criteria

- Hitting G 20 times produces 20 galaxies, all of which look strategically interesting (not all clumped on one side, not absurdly spread out).
- Same seed produces the same galaxy (determinism — verifiable in test).
- The evaluator correctly rejects pathological layouts: hand-craft a galaxy where all bodies are on one side, run it through the evaluator, confirm `IsAcceptable == false`.
- Taking shots in a generated galaxy feels recognizably like Phase 1 — the physics, capture, and orbit mechanics are unchanged. The novelty is just the layout.
- After 5-10 generated galaxies, you can identify a "favorite" — a particular layout that feels especially fun. The evaluator's score for that layout should be high.

## Tuning notes

After Claude Code implements the spec, expect to tune:
- `ClusterCenterYJitter` and `ClusterRadius` to taste (clusters too tight feels constrained; too loose feels random)
- Body type weights (current spec gives them all equal probability — you may want Rocky to dominate visually)
- Evaluator thresholds — if it's rejecting too many galaxies, lower thresholds
- Mass and radius ranges per body type — the gravitational field of a generated galaxy may feel different from the hand-tuned Phase 1 scenario; you may need to retune

## How to hand this to Claude Code

From the project root:

```
claude
```

Then prompt:

> Read CLAUDE.md and docs/Phase2_Spec.md carefully. Implement Phase 2 of Orbital according to the spec. Create the files in the locations specified. Pay particular attention to:
> - keeping the generator and evaluator pure C# with no Unity dependencies
> - deterministic output from a seed (use the Rng wrapper)
> - the evaluator's PathViability check should reuse the existing PatchedConicsSolver headlessly
> - the existing Phase 1 prototype scene should keep working — the rocket, aim, capture, and orbit mechanics are unchanged; only the body layout source is replaced
>
> When done, write a summary at docs/Phase2_Implementation_Notes.md covering what you built, any decisions you made beyond what the spec specifies, and any edge cases you found in the algorithm.

After Claude Code reports done, switch back to Unity. The existing `Phase1_Prototype` scene should continue to work, but instead of the four hard-coded planets you'll see a fresh procedural galaxy on each load. Press **G** to regenerate. Press **R** to reset the rocket. Take some shots, find a layout you like, and tell me what feels good or off.
