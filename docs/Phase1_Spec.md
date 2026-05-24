# Orbital — Phase 1 Spec: Physics Prototype

## Goal

Prove the core mechanic is fun. Build a minimal playable scene where the player can fire a rocket, watch it pinball through gravity wells, and feel something. Nothing else matters in this phase.

## Scope

**In scope:**
- One Unity scene, one hard-coded scenario
- One rocket starting at a home planet
- Sun + 3 other planets scattered in a small play area
- Patched-conics physics (deterministic, fixed timestep)
- Click-and-drag aim from the rocket; release to fire
- Trajectory preview line showing the first ~3 seconds of predicted flight
- Outcome detection: Crashed / Orbited / Escaped
- On-screen text showing the outcome
- Press R to reset

**Out of scope (do NOT build these now):**
- Turn system, economy, research, AI, procedural generation
- Multiple rockets, rocket types, payloads
- Art beyond Unity primitives (a circle for a planet is fine; we'll do art in later phases)
- Audio
- UI chrome (menus, HUD, settings)
- Save/load (the prototype lives in memory only)

## Architectural rules (from CLAUDE.md)

- **Physics math is pure C#.** `PatchedConicsSolver` and the data classes have NO Unity dependencies (no `MonoBehaviour`, no `Time`, no `UnityEngine.Random`). Use `System.Numerics.Vector2` or define our own struct if useful — but `UnityEngine.Vector2` is acceptable as a value type as long as nothing else from Unity creeps in.
- **Deterministic, fixed timestep.** Simulation runs at `dt = 0.02` (50 Hz). Never use `Time.deltaTime` for physics math. Render at whatever framerate; sim ticks on a fixed schedule.
- **Game state is data; presentation reads state.** `MonoBehaviour` views read from data classes but never mutate them.
- **Tunable values exposed.** Anything a designer might want to change (G, masses, thrust scale, play area size) lives on a ScriptableObject or public field, not buried in code.

## File map

All paths relative to `Assets/_Project/`.

### Scripts/Physics/
- **`CelestialBody.cs`** — pure data class. Fields: `int Id`, `string Name`, `Vector2 Position`, `float Mass`, `float Radius`, `float SoiRadius`.
- **`RocketState.cs`** — pure data class. Fields: `Vector2 Position`, `Vector2 Velocity`, `float Mass`, `float Fuel`, `int CurrentBodyId`, `RocketStatus Status` (enum: `Prelaunch`, `InFlight`, `Crashed`, `Orbited`, `Escaped`), `float TimeInFlight`.
- **`PatchedConicsSolver.cs`** — pure static class with these methods:
  - `RocketState Step(RocketState rocket, IReadOnlyList<CelestialBody> bodies, float dt, float G)` — advance one fixed timestep. Find dominant body, apply gravity from that body only, integrate position and velocity (semi-implicit Euler is fine; RK4 if you want extra accuracy).
  - `int FindDominantBody(Vector2 position, IReadOnlyList<CelestialBody> bodies)` — return the ID of the smallest SOI that contains the position; if none, return the ID of the nearest body (the sun, in practice).
  - `Outcome CheckOutcome(RocketState rocket, IReadOnlyList<CelestialBody> bodies, Rect playArea, int homeBodyId)` — return one of `None`, `Crashed`, `Orbited`, `Escaped`. Rules below.
- **`TrajectoryPredictor.cs`** — runs the solver forward N steps with thrust set to zero, returns `List<Vector2>` of predicted positions. Used by the aim UI to draw the preview line.

### Scripts/Presentation/
- **`PrototypeScenarioController.cs`** — `MonoBehaviour` that owns the scene. In `Awake`, builds the hard-coded scenario (list of `CelestialBody`, one `RocketState`). In `FixedUpdate`, advances the simulation by `dt` while the rocket status is `InFlight`. Handles outcome detection and reset on R press. Holds references to the views.
- **`CelestialBodyView.cs`** — `MonoBehaviour`. Bound to a `CelestialBody` by ID. Updates its transform position from the data each frame. For Phase 1, render as a flat-shaded circle (a `SpriteRenderer` with a circle sprite, scaled by radius).
- **`RocketView.cs`** — `MonoBehaviour`. Bound to the `RocketState`. Updates transform position and rotation (rotate to face velocity direction) each frame. Render as a small triangle sprite for now.
- **`AimController.cs`** — `MonoBehaviour`. Handles mouse input when rocket status is `Prelaunch`. Detects click on rocket; while held, calculates aim direction (from rocket to mouse) and thrust magnitude (clamp(distance/5, 0, 1)). Updates the trajectory preview each frame during drag. On release, sets the rocket's velocity and changes status to `InFlight`.
- **`TrajectoryView.cs`** — `MonoBehaviour` with a `LineRenderer`. Receives a list of points (from `TrajectoryPredictor`) and renders the preview line. Hides itself when not aiming.
- **`OutcomeDisplay.cs`** — `MonoBehaviour` with a `TextMeshProUGUI`. Shows outcome text when status changes from `InFlight` to a terminal state. Hides when status is `Prelaunch`.

### Tests/ (optional but recommended for the solver)
- **`PatchedConicsSolverTests.cs`** — NUnit tests for: single-body circular orbit conserves energy roughly; two-body rocket transitions SOI correctly; rocket crashes when path intersects a body; rocket escapes when energy is positive and outside play area.

## Hard-coded scenario

Play area: 50 units wide × 30 units tall, centred at origin. Camera fits the whole area in view.

| Body         | Position    | Mass | Radius | SOI |
|--------------|-------------|------|--------|-----|
| Sun          | (0, 0)      | 100  | 1.5    | 30  |
| HomePlanet   | (-20, 0)    | 5    | 0.8    | 4   |
| IcePlanet    | (5, 8)      | 3    | 0.6    | 3   |
| LavaPlanet   | (10, -6)    | 4    | 0.7    | 3.5 |
| TargetPlanet | (20, 0)     | 5    | 0.8    | 4   |

Rocket starts at `HomePlanet.Position + (1.5, 0)` with zero velocity. Mass 0.1, fuel 50 (unused in Phase 1 since no thrust during flight), max launch speed 8 units/sec.

## Simulation parameters (starting values, expect to tune)

- `G = 1.0` (gameplay-tuned, not real-world physics)
- `dt = 0.02` (50 Hz sim)
- Max sim time per shot: 60 seconds (after that, declare `Escaped`)
- Trajectory preview steps: 150 (3 seconds of preview at 50 Hz)

## Aim mechanic (detailed)

1. While `Status == Prelaunch`, mouse-down within rocket's collider starts an aim drag.
2. While dragging, every frame:
   - `direction = (mouseWorldPos - rocketPos).normalized`
   - `distance = (mouseWorldPos - rocketPos).magnitude`
   - `thrust01 = clamp(distance / 5, 0, 1)` — drag 5 units for full thrust
   - Render an arrow from rocket to cursor (the aim arrow)
   - Compute hypothetical initial velocity: `direction * thrust01 * maxLaunchSpeed`
   - Call `TrajectoryPredictor.Predict(rocket with that velocity, bodies, 150 steps, dt, G)` → list of points
   - Update `TrajectoryView` line with those points
3. On mouse-up:
   - Apply that initial velocity to the rocket
   - Set `Status = InFlight`
   - Hide aim arrow and trajectory preview
   - From now on, `FixedUpdate` advances the sim each tick

## Outcome detection rules

Each `FixedUpdate` after `Status` becomes `InFlight`, call `CheckOutcome`:

- **Crashed**: rocket position is within any body's `Radius`. Immediate. Capture which body.
- **Orbited**: rocket has been within the same non-home body's SOI for at least 3 contiguous seconds AND its specific orbital energy with respect to that body is negative (closed orbit). Capture which body.
- **Escaped**: rocket is outside the play area `Rect` AND its specific orbital energy with respect to the sun is non-negative. Or sim time exceeds 60 seconds.
- **None**: keep simulating.

When status transitions to `Crashed`, `Orbited`, or `Escaped`, freeze the rocket and tell `OutcomeDisplay` to show e.g. "Orbited TargetPlanet!" / "Crashed into LavaPlanet" / "Escaped to deep space".

## Reset

While in any terminal state, R press:
- Reset `RocketState` to its starting position, velocity zero, `Status = Prelaunch`.
- Hide outcome display.
- Aim is available again.

## Success criteria

- A shot can be initiated in under 5 seconds (click, drag, release).
- The rocket visibly bends as it passes near other planets — gravity wells are felt, not just visualized.
- All three outcomes (crash, orbit, escape) are reachable with sensible inputs.
- After 30 minutes of play, you can land in orbit of `TargetPlanet` at least once.
- Small changes to aim/thrust produce meaningfully different outcomes, but in a learnable way (not chaotic-different).
- It is fun to take 30 shots in a row without doing anything else.

## Tuning notes

All numbers above are starting points. After Claude Code implements the spec, expect to spend real time tuning `G`, body masses, SOI sizes, thrust scale, and the play-area dimensions until shots feel right. Two tells that tuning is needed:

- Shots feel "magnetic" — rockets snap to bodies too hard. Lower body masses or G.
- Shots feel "floaty" — rockets fly past everything in a near-straight line. Raise G or body masses.

Expose every tunable as either a public field on `PrototypeScenarioController` or a ScriptableObject (`ScenarioConfig`) so you can adjust live in the inspector without recompiling.

## How to hand this to Claude Code

1. Make a `docs/` folder at the project root and drop this spec into it as `Phase1_Spec.md`.
2. From the project root, open a terminal and run `claude` to start Claude Code.
3. Use this prompt:

> Read CLAUDE.md and docs/Phase1_Spec.md carefully. Implement Phase 1 of Orbital according to the spec. Create the files in the locations specified. Pay particular attention to:
> - keeping the physics math pure (no MonoBehaviour or Time.deltaTime in the solver)
> - deterministic fixed-timestep simulation
> - exposing tunable values via the inspector
>
> When you're done, write a short summary at `docs/Phase1_Implementation_Notes.md` covering what you built, any decisions you made beyond what the spec specifies, and any open questions or assumptions you had to make.

After Claude Code reports done, open Unity, let it import, fix any compile errors it surfaces (most likely a missing `using` or a reference Claude Code couldn't auto-wire), then create a new scene called `Phase1_Prototype` under `Assets/_Project/Scenes/` and add the `PrototypeScenarioController` to a GameObject in it. Wire up its references. Run.

If the shot doesn't feel good, that's not a bug — it's the work of Phase 1. Tune until it does.
