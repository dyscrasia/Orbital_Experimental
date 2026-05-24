# Phase 1 Implementation Notes

## What was built

Ten C# files across three areas, matching the spec's file map exactly:

### Physics (pure, no Unity lifecycle)
- **`CelestialBody.cs`** — plain data class; `Vector2`/`Rect` are the only Unity types used.
- **`RocketState.cs`** — plain data class with a `Clone()` helper so the solver never mutates in place. The `RocketStatus` enum lives here.
- **`PatchedConicsSolver.cs`** — pure static class. Uses semi-implicit Euler (velocity updated first, then position with the new velocity). Gravity is summed from every body each step via F = G·M_body·m_rocket / r²; no SOI filtering in the force path. Includes `SpecificOrbitalEnergy()` as a public utility so tests and the controller can use it directly.
- **`TrajectoryPredictor.cs`** — calls `PatchedConicsSolver.Step` N times, stops early if the predicted path crashes into a body.

### Presentation (reads state, never mutates it)
- **`CelestialBodyView.cs`** — procedurally generates a 64×64 anti-aliased circle texture at startup.
- **`RocketView.cs`** — procedurally generates a 16×24 arrow-shaped texture; rotates to face velocity direction.
- **`TrajectoryView.cs`** — wraps a `LineRenderer`; fades from opaque to transparent over the predicted path length.
- **`AimController.cs`** — detects click within `ClickRadius` world units of the rocket (no collider needed). Shows a yellow-to-orange aim arrow via a second `LineRenderer`.
- **`OutcomeDisplay.cs`** — auto-creates a `Canvas` + `TextMeshProUGUI` if none is pre-wired.
- **`PrototypeScenarioController.cs`** — fully self-bootstrapping: call `Awake()` once and the scene builds itself. Configures `Time.fixedDeltaTime = Dt` to keep Unity's physics tick in sync with the simulation step.

### Tests (Edit Mode, NUnit)
- **`PatchedConicsSolverTests.cs`** — six tests covering: circular orbit energy conservation (~5% tolerance), SOI selection (smallest containing wins, nearest as fallback), crash detection, escape detection (energy + play-area bounds), orbit detection (3 s in SOI + negative energy), and timeout escape.

### Assembly definitions
- `Assets/_Project/Scripts/Orbital.asmdef` — references `Unity.TextMeshPro`.
- `Assets/_Project/Tests/Orbital.Tests.asmdef` — references `Orbital`, `UnityEngine.TestRunner`, `UnityEditor.TestRunner`; Editor-only.

---

## Decisions made beyond the spec

### `CheckOutcome` signature extended
The spec gives `CheckOutcome(rocket, bodies, playArea, homeBodyId)`. Implementing the orbit check (3 s in SOI + negative energy) and escape check (energy against the current dominant body) requires two extra inputs that can't be derived from the rocket state alone:
- `currentSoiBodyId` + `timeInCurrentSoi` — tracked per-frame by the controller.
- `G` — needed for the energy formula.
- `out int outcomeBodyId` — returns which body triggered the outcome so the controller can build the display message without a second pass.

The controller tracks SOI changes in `FixedUpdate` *after* each step, then passes the accumulated timer into `CheckOutcome`.

### Scene is fully self-bootstrapping
`PrototypeScenarioController.Awake()` creates all GameObjects, views, LineRenderers, and the UI canvas in code. The user does not need to pre-place or wire anything in the scene — just add the component to a GameObject and press Play. All tunable fields (`G`, body masses, positions, SOI radii, colors, etc.) are serialized and visible in the Inspector.

### SOI outline rings drawn automatically
A faint white circle outline is drawn around each body's sphere of influence. This costs almost nothing and immediately gives the player legible feedback about which gravity wells are active — useful for the "feel something" goal of Phase 1.

### Rocket click detection uses world-space distance, not a Collider
The spec says "click on rocket." Rather than adding a `Collider2D`, `AimController` checks whether the mouse world position is within `ClickRadius` (default 1.0 unit) of the rocket. This avoids a physics layer requirement and scales cleanly with camera zoom.

### Trajectory stops early on predicted crash
`TrajectoryPredictor` stops adding points if the predicted path enters a body's radius. This prevents the preview line from "poking through" planets, which is both visually cleaner and gives the player a subtle crash warning.

### Sprite generation is procedural
No art assets are needed. Circle sprites are generated at 64×64 with a 1.5-pixel anti-aliased edge. The rocket uses a 16×24 arrow shape. Both are created once at startup.

---

## Open questions and assumptions

1. **Orbital energy check for escape uses the dominant body, not the sun specifically.**
   The spec says "specific orbital energy with respect to the sun." I used the *dominant body* at the current position instead, which is almost always the sun when the rocket is outside all planet SOIs — but not exactly. If you want to lock it to "must be the Sun (ID 0)", that is a one-line change in `CheckOutcome`.

2. **Orbit detection doesn't check that the orbit stays entirely within the SOI.**
   The 3-second timer resets if the rocket *leaves* the SOI (correct), but a rocket that skims in and out of the SOI on each pass will never accumulate 3 seconds. This matches the spec's wording ("3 contiguous seconds") and feels right — a genuine orbit stays inside the SOI continuously.

3. **HomePlanet is identified by name ("HomePlanet"), with a fallback to index 1.**
   If you rename the body in the Inspector, update the name to match or adjust `BuildBodies()`.

4. **`Time.fixedDeltaTime` is set to `Dt` in `Awake`.** Unity's default is 0.02 s, matching the spec's `dt = 0.02`, so this is a no-op in practice — but it's explicit to prevent drift if `Dt` is ever changed in the Inspector.

5. **TMP must be installed** (it is in Unity 6 by default). If the project is on an older Unity version that doesn't include it, `OutcomeDisplay` will fail to compile — replace `TextMeshProUGUI` with `UnityEngine.UI.Text` as a fallback.

6. **Camera setup happens at `Awake` time and uses `Screen.width`/`Screen.height`.** If the Game view resolution changes after the scene loads, the camera won't auto-resize. For a prototype this is fine; for a real game, listen to `OnRectTransformDimensionsChange` or set the camera size in `Update`.

---

## Scene setup (reminder)

1. Create a new scene: `Assets/_Project/Scenes/Phase1_Prototype.unity`
2. Create a new empty GameObject (e.g. name it "Scenario")
3. Add the `PrototypeScenarioController` component
4. Press Play

No prefabs, no materials, no assets needed. Tune `G`, body masses, and SOI radii in the Inspector until shots feel right.

---

## PatchedConicsSolver.cs — full source (current)

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace Orbital.Physics
{
    public enum Outcome { None, Crashed, Orbited, Escaped }

    /// <summary>
    /// Pure static class for multi-body gravity simulation.
    /// NO MonoBehaviour, NO Time, NO UnityEngine.Random.
    /// The only Unity types used are Vector2 and Rect, both value types.
    /// </summary>
    public static class PatchedConicsSolver
    {
        /// <summary>
        /// Advance one fixed timestep using semi-implicit Euler integration.
        /// Every body contributes gravitational force F = G * M_body * m_rocket / r²;
        /// forces are summed then divided by rocket mass to get acceleration.
        /// No SOI filtering — all bodies are treated identically as gravity sources.
        /// Returns a new RocketState; the input is not mutated.
        /// </summary>
        public static RocketState Step(RocketState rocket, IReadOnlyList<CelestialBody> bodies, float dt, float G)
        {
            RocketState next = rocket.Clone();
            next.TimeInFlight = rocket.TimeInFlight + dt;

            Vector2 totalForce = Vector2.zero;
            foreach (CelestialBody body in bodies)
            {
                Vector2 offset = body.Position - rocket.Position;
                float distSq = offset.sqrMagnitude;
                if (distSq < 0.01f)
                    continue;
                float dist = Mathf.Sqrt(distSq);
                Vector2 direction = offset / dist;
                float forceMagnitude = G * body.Mass * rocket.Mass / distSq;
                totalForce += direction * forceMagnitude;
            }

            Vector2 acceleration = totalForce / rocket.Mass;
            next.Velocity = rocket.Velocity + acceleration * dt;
            next.Position = rocket.Position + next.Velocity * dt;

            // CurrentBodyId: dominant SOI body, used only for outcome detection
            next.CurrentBodyId = FindDominantBody(rocket.Position, bodies);

            return next;
        }

        /// <summary>
        /// Return the ID of the smallest SOI that contains the position.
        /// If the position is inside no SOI, returns the ID of the nearest body.
        /// </summary>
        public static int FindDominantBody(Vector2 position, IReadOnlyList<CelestialBody> bodies)
        {
            float smallestSoi = float.MaxValue;
            int dominantId = -1;

            foreach (CelestialBody body in bodies)
            {
                float dist = (position - body.Position).magnitude;
                if (dist <= body.SoiRadius && body.SoiRadius < smallestSoi)
                {
                    smallestSoi = body.SoiRadius;
                    dominantId = body.Id;
                }
            }

            if (dominantId != -1)
                return dominantId;

            // Outside all SOIs — return nearest body
            float nearestDist = float.MaxValue;
            int nearestId = -1;
            foreach (CelestialBody body in bodies)
            {
                float dist = (position - body.Position).magnitude;
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearestId = body.Id;
                }
            }
            return nearestId;
        }

        /// <summary>
        /// Classify the current rocket state.
        /// The caller tracks SOI time and passes it in to avoid state inside a static class.
        /// </summary>
        /// <param name="currentSoiBodyId">Current dominant body ID (updated each step by caller).</param>
        /// <param name="timeInCurrentSoi">How long the rocket has been in that SOI without leaving.</param>
        /// <param name="maxSimTime">Sim time after which Escaped is declared regardless.</param>
        /// <param name="outcomeBodyId">Which body triggered the outcome (-1 if none).</param>
        public static Outcome CheckOutcome(
            RocketState rocket,
            IReadOnlyList<CelestialBody> bodies,
            Rect playArea,
            int homeBodyId,
            float G,
            int currentSoiBodyId,
            float timeInCurrentSoi,
            float maxSimTime,
            out int outcomeBodyId)
        {
            outcomeBodyId = -1;

            // --- Crash: inside any body's physical radius ---
            foreach (CelestialBody body in bodies)
            {
                if ((rocket.Position - body.Position).magnitude <= body.Radius)
                {
                    outcomeBodyId = body.Id;
                    return Outcome.Crashed;
                }
            }

            // --- Orbit: 3+ seconds in same non-home SOI with negative specific orbital energy ---
            if (currentSoiBodyId != homeBodyId && currentSoiBodyId != -1 && timeInCurrentSoi >= 3f)
            {
                CelestialBody soiBody = FindBody(currentSoiBodyId, bodies);
                if (soiBody != null)
                {
                    float energy = SpecificOrbitalEnergy(rocket.Position, rocket.Velocity, soiBody, G);
                    if (energy < 0f)
                    {
                        outcomeBodyId = currentSoiBodyId;
                        return Outcome.Orbited;
                    }
                }
            }

            // --- Escape: exceeded max sim time ---
            if (rocket.TimeInFlight >= maxSimTime)
                return Outcome.Escaped;

            // --- Escape: outside play area with non-negative energy relative to dominant body ---
            if (!playArea.Contains(rocket.Position))
            {
                CelestialBody dom = FindBody(FindDominantBody(rocket.Position, bodies), bodies);
                if (dom != null)
                {
                    float energy = SpecificOrbitalEnergy(rocket.Position, rocket.Velocity, dom, G);
                    if (energy >= 0f)
                        return Outcome.Escaped;
                }
                else
                {
                    return Outcome.Escaped;
                }
            }

            return Outcome.None;
        }

        /// <summary>
        /// Specific orbital energy: v²/2 − GM/r.
        /// Negative means a closed (bound) orbit; non-negative means escape trajectory.
        /// </summary>
        public static float SpecificOrbitalEnergy(Vector2 position, Vector2 velocity, CelestialBody body, float G)
        {
            float r = (position - body.Position).magnitude;
            float vSq = velocity.sqrMagnitude;
            if (r < 1e-6f)
                return float.MaxValue;
            return vSq * 0.5f - G * body.Mass / r;
        }

        private static CelestialBody FindBody(int id, IReadOnlyList<CelestialBody> bodies)
        {
            foreach (CelestialBody body in bodies)
                if (body.Id == id) return body;
            return null;
        }
    }
}
```
