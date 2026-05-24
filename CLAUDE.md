# Orbital - Project Context for Claude Code

## What this is
Orbital is a 2D turn-based tactical-strategy game in a procedurally
generated galaxy. Two empires (player + AI or human) compete to
colonize a galaxy and conquer each other's home planet via real-time
gravity-shot rocket launches resolved between turns.

The strategic layer is turn-based. The tactical layer is the moment
of firing each rocket - a real-time skill shot that pinballs through
the gravitational fields of intervening planets. Failed shots crash
and deposit cargo on whichever planet ate the rocket.

## Game variants — scope guard

Orbital will eventually ship as three variants:
- **Classic** — simple, beautiful, minimal rules. Currently under development.
- **Arcade** — additional fun/chaos rules (e.g. rocket splitting, boost pads, black holes).
- **Strategy** — builder elements, resource economy, tech trees, more depth.

We are currently building the **Classic** variant only. Do not propose or
implement mechanics that belong in Arcade or Strategy. When in doubt about
scope, ask before building.

## Engine and target
- Unity 6 LTS, URP 2D Renderer (or Unity 2022 LTS if 6 not available)
- C# 10 / .NET Standard 2.1
- Target platforms: Windows + macOS (Steam), Linux later

## Architectural commitments (do not break)
- ALL game-state logic is deterministic from seed + TurnAction sequence.
- NO `UnityEngine.Random` in game logic. Use the `Rng` wrapper in
  `Assets/_Project/Scripts/Core/Rng.cs`.
- NO `Time.deltaTime` in physics math. Use a fixed simulation step.
- Game state is pure data, no Unity dependencies. Presentation reads
  from state but never the reverse.
- All tunable data lives in ScriptableObjects under `Assets/_Project/Data/`.
- Every player action serializes as a `TurnAction`. Save state is
  "world seed + sequence of TurnActions taken."

## Code style
- PascalCase for types and public members
- camelCase for parameters and locals
- _camelCase for private fields
- One class per file, file name matches class name
- Prefer explicit types over `var` when the type isn't obvious
- Keep methods short; if a method exceeds ~40 lines, consider extracting

## Folder structure (Assets/_Project/)
- Scripts/Core/         RNG, save/load, TurnAction, common utilities
- Scripts/Galaxy/       generation, evaluation, celestial body data
- Scripts/Physics/      multi-body gravity solver, predictor, sphere of influence
- Scripts/Strategy/     turn manager, empire state, economy, research
- Scripts/Combat/       defense layers, interceptors, landing/crash
- Scripts/AI/           tactical and strategic AI, headless sim
- Scripts/Presentation/ UI, camera, visual feedback (reads state only)
- Data/                 ScriptableObject definitions
- Art/Source/           Blender source files (.blend)
- Art/Sprites/          exported sprite sheets imported by Unity

## Physics model
We use **multi-body gravity** for rocket flight. Each simulation step,
the rocket accumulates gravitational acceleration from every body in
the system (F = G·M·m/r² summed over all bodies). Planets are on
rails — their orbits are pre-computed and they don't perturb each other.
`FindDominantBody` / SOI radius is kept only for the escape-energy check
in `CheckOutcome`; it plays no role in the force calculation.

## Orbital capture
Orbit capture uses a **capture-window mechanic**, not energy-based
detection. Each non-home planet has a `CaptureRingRadius`, speed range
`[CaptureMinSpeed, CaptureMaxSpeed]`, and `CaptureAngleToleranceDegrees`.
When the rocket crosses the capture ring inbound, all three criteria are
evaluated. On success the rocket's velocity is snapped to the exact
circular orbit speed at that radius (tangential, preserving CW/CCW
direction) and `Status` is set to `Orbited`. Physics continues running
so the rocket visually orbits the planet. The old energy-based Orbited
path in `CheckOutcome` has been removed.

## Launch interaction model (Phase 3+)
Rocket launch uses a **position-then-aim** two-phase drag, all within a single
mouse gesture. The transition between phases is driven by cursor distance from
the active player's home planet center.

**Positioning phase** (cursor inside `home.Radius + PositioningThreshold`):
- The rocket slides around the home planet's surface, tracking the cursor's
  angular position: `pos = home.Position + dir * (home.Radius + RocketSurfaceOffset)`.
- Aim arrow and trajectory preview are hidden.

**Aiming phase** (cursor moves outside the threshold — one-way transition):
- Rocket position freezes at the last surface point.
- Aim arrow appears from frozen rocket to cursor.
- Trajectory preview updates as the cursor moves.
- Drag magnitude/direction from the frozen rocket sets launch velocity.

**On release:**
- If still in positioning phase: no launch. Rocket stays at its last surface
  position; the player can start another drag to reposition or aim.
- If in aiming phase: rocket fires with the computed velocity.

Tunables on `AimController`: `PositioningThreshold` (default 3 world units,
how far past the surface before switching modes) and `RocketSurfaceOffset`
(default 0.7, gap between surface and rocket sprite).

## Classic rocket production rule

At the start of each player's turn (when Space is pressed to enter WaitingForLaunch):

  **rockets = 1 + floor(nonHomeCapturedPlanets / 2)**

- The home planet always provides exactly 1 rocket.
- Every 2 non-home captured planets grant 1 additional bonus rocket.
- Bonus rockets are placed on randomly selected captured non-home planets
  (without replacement). Selection is seeded deterministically:
  `seed = TurnNumber * 31 + CurrentPlayerId` so the same game state
  always produces the same placements.
- After each rocket resolves the player may fire the next one. Pressing
  Enter (or the End Turn button) forfeits remaining rockets.

`LaunchSiteCalculator` (pure static, no Unity deps) implements this rule.
`LaunchSiteView` is the visual marker (distinct from `OrbitingRocketView`).

## Planet visualization
`CelestialBodyView` supports per-type sprite animations via `BodyTypeVisuals`
ScriptableObjects (created via Assets > Create > Orbital > Body Type Visuals).
Each asset has a `TypeName` (matching a `BodyTypeDefinition.TypeName`), an
`AnimationFrames` sprite array, and an `AnimationFps` (default 30).

`GalaxyParameters.TypeVisuals` holds the registry. `PrototypeScenarioController`
looks up the matching visuals by `body.Name` (which equals the type name) and
passes them to `CelestialBodyView.Initialize`. If no visuals are found the view
falls back to the colored circle.

Planets render as **single rotating sprites**: `BodyTypeVisuals` holds a `SpriteVariants`
array (one is chosen per body deterministically via `body.Id % variants.Length`) and a
`RotationSpeedDegreesPerSecond`. The sprite rotates around the Z axis in `LateUpdate`
using `Time.unscaledDeltaTime`. Home planets always use `GalaxyParameters.HomeVisuals`
(regardless of body type) if that field is set; otherwise they fall through to the
type-based TypeVisuals lookup.

Currently only **Rocky** planets will have sprites filled in. Ice, Lava, Gas, and
Water body types retain the colored circle until their sprite sets are added.

## Economy model
Single shared empire stockpile of ~5 resources (Metals, Water, O2,
Energy, Rare). Per-planet resource logistics are NOT modeled day to
day. New colonies have a one-time bootstrap cost paid by the
colonization rocket's payload.

## Testing
- Unit tests for pure-data systems (galaxy gen, physics, economy)
  go under `Assets/_Project/Tests/`
- Use Unity Test Framework (NUnit-based)
- Pure-data systems should be unit-testable without Unity at all

## When working on a task
- Read the spec carefully before writing code
- If a spec is ambiguous, surface the ambiguity rather than guessing
- Match existing patterns in nearby files
- Update or add unit tests for any non-trivial logic
- Keep changes scoped - don't refactor unrelated code unless asked

## Things this project does NOT do (do not propose them)
- Multi-resource per-planet logistics (we use a shared empire stockpile)
- Patched-conics single-body gravity (we use full multi-body summation)
- 3D rendering (it's 2D, even though Blender is in the asset pipeline)
- Real-time strategic layer (turn-based only; only shot resolution
  is real-time)
