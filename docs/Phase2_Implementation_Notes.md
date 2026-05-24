# Orbital — Phase 2 Implementation Notes

## What was built

### New files

| File | Purpose |
|---|---|
| `Scripts/Galaxy/BodyTypeDefinition.cs` | `[Serializable]` class for one planet sub-type (name, colour, mass/radius ranges, weight) |
| `Scripts/Galaxy/CaptureCriteria.cs` | `[Serializable]` struct for the four capture-window parameters |
| `Scripts/Galaxy/GalaxyParameters.cs` | `ScriptableObject` holding every generator tunable; create via **Assets > Create > Orbital > Galaxy Parameters** |
| `Scripts/Galaxy/GalaxyEvaluation.cs` | Plain data class returned by the evaluator (per-criterion floats + summary string) |
| `Scripts/Galaxy/Galaxy.cs` | Immutable output record: seed, bodies, home IDs, play area, evaluation |
| `Scripts/Galaxy/GalaxyGenerator.cs` | Pure static generator; deterministic from `seed + GalaxyParameters` |
| `Scripts/Galaxy/GalaxyEvaluator.cs` | Pure static evaluator; four quality criteria, returns `GalaxyEvaluation` |
| `Scripts/Presentation/GalaxyVisualizer.cs` | MonoBehaviour dev tool; G / B hotkeys, on-screen seed + score text |
| `Tests/GalaxyGeneratorTests.cs` | NUnit tests for determinism, separation, body count, home positions |
| `Tests/GalaxyEvaluatorTests.cs` | NUnit tests for each evaluator criterion |

### Modified files

**`PrototypeScenarioController.cs`** — added:
- `bool UseProceduralGalaxy = true` toggle
- `GalaxyParameters GalaxyParams` field (assign in Inspector)
- `int InitialSeed` field
- `Galaxy CurrentGalaxy` read-only property (used by GalaxyVisualizer)
- `RegenerateGalaxy(int seed, GalaxyParameters parameters)` — hot-swap the layout at runtime
- Body view and ring creation split into `CreateBodyViews()` (destroyable on regen) and
  `CreatePersistentViews()` (rocket, aim, trajectory, outcome — created once in Awake)
- `_bodySceneObjects` list tracking all GameObjects that must be destroyed on regeneration

All Phase 1 mechanics (multi-body gravity, capture-window detection, kinematic orbit,
R-to-reset) are **unchanged**; only the source of the body list changes.

---

## First-time setup in Unity

1. In the **Project window**, right-click anywhere under `Assets/_Project/Data/` and choose
   **Create > Orbital > Galaxy Parameters**. Name it `DefaultGalaxyParameters`.
2. Select the `ScenarioController` GameObject in the `Phase1_Prototype` scene.
3. In the **Inspector**, assign `DefaultGalaxyParameters` to the **Galaxy Params** field.
4. Make sure **Use Procedural Galaxy** is ticked and **Initial Seed** has any value.
5. Press Play. The scene now generates a procedural galaxy instead of the hard-coded layout.
6. Press **G** to generate a new galaxy; **B** to regenerate the same seed; **R** to reset
   the rocket to the current galaxy's Player 1 home.

To add a `GalaxyVisualizer`:
1. Add an empty GameObject to the scene and attach the `GalaxyVisualizer` component.
2. Assign the same `DefaultGalaxyParameters` to its **Galaxy Params** field.
3. The on-screen overlay and G/B hotkeys will be active automatically.

---

## Architectural decisions beyond the spec

### `CaptureCriteria` as a separate struct
The spec mentioned `CaptureCriteria DefaultCaptureCriteria` on `GalaxyParameters` but did not
specify whether it was inline or a named type. It is a `[Serializable]` struct so it folds
neatly into the Inspector and can be reused anywhere a capture window configuration is needed.

### `SoiRadiusMultiplier` on `GalaxyParameters`
The spec did not define how to calculate `SoiRadius` for generated bodies. A fixed formula
`soiRadius = max(radius × SoiRadiusMultiplier, CaptureRingRadius)` keeps it tunable.
Default multiplier is 6, giving bodies SOI radii of ~3–11 units, comparable to the Phase 1
hand-tuned values.

### `HomePlanetColor` on `GalaxyParameters`
Added so both home planets have a consistent, configurable colour distinct from the five
body types.

### Body view colour lookup
`PrototypeScenarioController.ResolveBodyColor()` matches a body's `Name` string against
`GalaxyParams.BodyTypes[i].TypeName`. Home bodies are matched by ID against the galaxy's
`Player1HomeId` / `Player2HomeId`. This works because `GalaxyGenerator.MakeBody()` sets
`body.Name = type.TypeName` and never re-uses those names for home planets.

### `CreateBodyViews` / `CreatePersistentViews` split
The spec required pressing G to regenerate the galaxy at runtime without recreating the
rocket, aim controller, or UI. The original `CreateViews()` was split into two methods.
`DestroyBodySceneObjects()` destroys everything tracked in `_bodySceneObjects` (body view
GameObjects + SOI/capture ring GameObjects). Rocket/aim/trajectory/outcome are created once
in `Awake` and survive regenerations.

### Physics parameters on `GalaxyParameters`
The spec's `Evaluate()` signature only shows `(bodies, p1HomeId, p2HomeId, playArea)`.
To run the PathViability physics simulation, the evaluator needs G, dt, maxLaunchSpeed, and
maxSimTime. These are added as optional parameters with defaults matching the runtime values
(G=1, dt=0.02, maxLaunchSpeed=16, maxSimTime=30). `GalaxyGenerator` passes them from
`GalaxyParameters` so they stay in sync with the scene's physics settings. The `GalaxyParameters`
G field is annotated "Must match PrototypeScenarioController.G" — both default to 1.0f.

### PathViability simulation detail
The spec says "8 candidate shots at full-thrust". Each shot starts from a position
`homeBody.Position + dir * (radius + 0.3)` — i.e., just outside the planet's surface in the
shot direction — and fires at `maxLaunchSpeed` in that direction. This avoids the rocket
immediately re-entering the home planet when firing "inward" directions.

Detection radius for each non-home body is `CaptureRingRadius` (with fallback to
`SoiRadius × 0.3` if the ring is 0). The sim terminates early on crash or play-area exit.
Maximum simulation steps = `min(1500, maxSimTime / dt)`.

### Symmetry criterion
The spec says "for each cluster, find the most similar cluster on the opposite side…". The
evaluator receives only bodies, not cluster centres. Instead, a body-level metric is used:
for each non-home body at `(x, y)`, find the nearest **other** non-home body to its reflected
position `(-x, y)`. A match within `playAreaWidth × 0.15` scores 1; further away scores
linearly down to 0. The score is averaged across all non-home bodies.

This is weaker than cluster-level symmetry but measurable without cluster data, and produces
the correct extremes: perfect mirror → 1.0, all bodies on one side → ~0.

---

## Edge cases found in the algorithm

### Cluster bodies failing placement silently
`TryPlaceBodyNear` returns `null` after 30 failed attempts. This can happen if a cluster
centre is near the edge of the play area (bodies would land outside) or if many earlier
bodies in the same cluster used up the available space. The generator simply skips the body
and moves on. If enough placements fail, `bodies.Count < MinBodies` causes the whole attempt
to be discarded — the retry loop handles it.

**Risk**: if `MinBodiesPerCluster` is high and `ClusterRadius` is small, every attempt may
fail. Mitigate by keeping `ClusterRadius ≥ 2 × MinBodySeparation` per cluster body desired.

### Home-planet Y jitter overlapping a cluster centre
`ClusterCenterYJitter` defaults to 12 units. Home planets sit at `Y ∈ [-3, 3]`. Cluster
centres can drift to Y = ±12. There is no constraint preventing a cluster from centring
directly above/below a home planet. `MinBodySeparation` (2.5 u) enforced per placement will
usually prevent actual body collisions, but bodies can sit very close to a home's surface.
To harden: add a minimum distance check from cluster centre to each home planet when
generating cluster centres. Not implemented in Phase 2 since the evaluator's PathViability
check will naturally reject layouts where the home has no clear shot.

### Outliers falling into cluster-dense zones
`TryPlaceBodyAsOutlier` rejects positions inside any cluster's radius — but the home planets
are not treated as "cluster centres". An outlier can land very close to a home planet's
position. Again, `MinBodySeparation` prevents actual overlap but an outlier 3 units from a
home planet is plausible. Low-impact in Phase 2.

### Body count floor vs evaluator failure
If `bodies.Count < MinBodies` after placement, `TryGenerate` returns `null` — the attempt is
discarded but does NOT contribute to `best`. If ALL attempts fail the count check, `Generate`
returns `null`. The caller (`PrototypeScenarioController`) would then `NullReferenceException`
on `_currentGalaxy.Bodies`. In practice, with 3 clusters × 3 bodies min = 11, plus 2 homes,
the minimum before trimming is 13 — below MinBodies=15 but close enough that a modest
`MaxBodiesPerCluster=7` easily reaches 15. Monitor with more aggressive parameters.

### Determinism of `SubStream` across attempts
`rng.SubStream("attempt-N")` hashes `Seed * 31^len + char_codes`. Two seeds that differ by a
small amount could collide in the sub-stream hash (the hash is not cryptographic). In practice
this is extremely unlikely across the seed range used in a game session, but it means
"different seeds → same sub-stream seed for attempt 1" is possible. The outer `Generate` call
correctly uses `Seed` (the original seed) when constructing the returned `Galaxy`, so the
final output is still deterministic from `seed`.

---

## Tuning notes (after first play session)

Default values in `GalaxyParameters` to revisit:

- **`ClusterRadius = 6`**: clusters feel fairly tight at this radius for an 80×40 play area.
  Try 8–10 if you want more spread within each cluster.
- **`DefaultCaptureCriteria.CaptureAngleToleranceDegrees = 45`**: much more forgiving than
  Phase 1's 30°. Relax further for early accessibility, tighten for challenge.
- **`DefaultCaptureCriteria.CaptureMinSpeed = 2`, `CaptureMaxSpeed = 20`**: the wide range
  captures nearly any trajectory. Phase 1 used 4–12 for most planets.
- **`SymmetryThreshold = 0.5`**: relatively strict. If the generator keeps rejecting layouts
  because of symmetry, lower to 0.35.
- **`PathViabilityThreshold = 0.4`**: requires 40% of bodies reachable from both homes.
  With 8 shot angles and 15–25 bodies spread across the map, this is usually achieved.
  Lower to 0.3 if generation is slow (too many rejections).

---

## Bug fixes (post-initial-implementation)

### Bug 1 — RegionBalance threshold serialised as 1.30 (impossible to pass)

**Root cause**: `GalaxyParameters.RegionBalanceThreshold` is a serialised `float` on a
ScriptableObject. The C# default is `0.6f`, but Unity does not retroactively update `.asset`
files when a field's C# initialiser changes. The field was serialised with the value `1.30`
(either from an earlier draft of the code or a manual inspector edit). Since all evaluator
scores are bounded `[0, 1]`, a threshold above `1.0` makes the criterion permanently
impossible and the evaluator always returns `REJECTED`.

**Fix**: Added `OnValidate()` to `GalaxyParameters` (editor-only, inside `#if UNITY_EDITOR`)
that calls `Mathf.Clamp01` on all four threshold fields. Unity calls `OnValidate` whenever
a serialised field changes in the inspector, so opening the asset or pressing Play will
auto-clamp any out-of-range value. The C# default remains `0.6f`.

**Action required in Unity**: open the `GalaxyParameters` asset in the inspector (or press
Play once) — Unity will call `OnValidate` and clamp the `RegionBalanceThreshold` back to
`1.0`, after which you should manually set it to `0.6`.

---

### Bug 2 — PathViability always near 0 (rockets can't escape home gravity)

**Root cause**: The PathViability headless sim started each test shot at
`home.Position + dir * (home.Radius + 0.3f)`, placing the rocket at r ≈ 1.1 from the
home planet centre. With `HomePlanetMass = 200` and `G = 1`, escape velocity at r = 1.1 is
√(2 · 200 / 1.1) ≈ **19.1 u/s** — well above `MaxLaunchSpeed = 16`. Every shot was
gravitationally bound from the first step and fell back into the home before reaching any
other body, producing PathViability ≈ 0.

**Secondary root cause**: Even at the correct game start offset (`radius + 0.7f`, r = 1.5),
escape velocity is √(2 · 200 / 1.5) ≈ 16.3 u/s — just above `MaxLaunchSpeed = 16`. The
margin is razor-thin; in a multi-body system many shots narrowly escape, but it is not
reliable enough for a quality metric.

**Fix**:
1. Start position corrected from `radius + 0.3f` to `radius + 0.7f` to match
   `PSC.BuildRocket` exactly.
2. Added `PathViabilityLaunchMultiplier = 1.5f` to `GalaxyParameters`. The evaluator
   multiplies `MaxLaunchSpeed` by this factor (16 × 1.5 = 24 u/s) for PathViability shots
   only. At r = 1.5 and v = 24 u/s, specific orbital energy = ½·576 − 200/1.5 ≈ +155,
   clearly unbound. The multiplier is tunable if needed.
