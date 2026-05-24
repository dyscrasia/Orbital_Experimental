# Orbital — Phase 3 Spec: Basic Competitive Game

## Goal

Turn the physics-and-galaxy prototype into the smallest possible competitive game. Two human players, hot-seat, each takes one shot per turn from their own home planet. Captures into orbit claim a planet for the firing player, and a new capture dislodges any enemy rocket already in that orbit. The game ends when one player lands a rocket in orbit of the *opposing* player's home planet.

That's the whole game. No resources, no research, no rocket types, no AI yet. We're isolating the question: is this turn-and-shot loop satisfying as a 1-on-1 game, before we add anything on top?

## Scope

**In scope:**
- Two-player hot-seat turn structure
- One rocket per turn, fired from the active player's home
- Planet ownership tracking, with visual indication (player color)
- Capture/dislodge: a rocket capturing into a planet's orbit kicks any existing enemy rocket out
- Win condition: capture the opposing player's home planet
- Win/lose screen with restart option
- Persistent visual: rockets that successfully captured into orbit stay in their orbits visually, in the owning player's color. They serve as ownership markers as well as obstacles for future shots? (No — they don't physically interact with new rockets in flight. See "Dislodge" below.)

**Out of scope (do NOT build):**
- AI opponent (we'll add a basic AI in a later phase)
- Resources, economy, research, tech tree
- Multiple rocket types or payloads
- Layered home defenses (interceptors, shields)
- Save/load mid-game (game lives entirely in memory; new game = full reset)
- Networked multiplayer
- Match settings/menus beyond a "New Game" button

## Architectural rules

- Game state stays pure data, separate from presentation. Add a `GameState` data class that owns turn state, player list, planet ownership map, win state.
- Continue using deterministic RNG and TurnAction-style serializability (even though we're not saving yet, designing as if we are makes future features easy).
- Existing Phase 1 / Phase 2 systems (multi-body gravity, capture, kinematic orbit, galaxy generator) all stay unchanged. This phase wraps them in a turn structure rather than modifying them.

## File map

All paths relative to `Assets/_Project/`.

### Scripts/Strategy/
- **`Player.cs`** — pure data class. Fields: `int Id`, `string Name`, `Color Color`, `int HomeBodyId`.
- **`GameState.cs`** — pure data class. Fields:
  - `IReadOnlyList<Player> Players`
  - `int CurrentPlayerId` (whose turn it is)
  - `Dictionary<int, PlanetOwnership> Ownership` (keyed by body ID)
  - `int TurnNumber`
  - `GamePhase Phase` (enum: `WaitingForLaunch`, `RocketInFlight`, `BetweenTurns`, `GameOver`)
  - `int? WinnerId` (null until game ends)
- **`PlanetOwnership.cs`** — data class. Fields: `int OwnerPlayerId`, `int OrbitingRocketId` (the rocket holding the orbit; needed for dislodge bookkeeping), `float OrbitRadius`, `float OrbitAngle`, `float OrbitAngularSpeed`, `int OrbitDirection`.
- **`TurnManager.cs`** — `MonoBehaviour` (or pure C# coordinator if you prefer). Manages turn progression. Methods:
  - `void StartTurn()` — sets phase to `WaitingForLaunch`, positions the rocket at the active player's home, signals the UI.
  - `void OnLaunch()` — called when the player fires; sets phase to `RocketInFlight`.
  - `void OnRocketResolved(Outcome outcome, int? capturedBodyId)` — applies ownership change, checks dislodge, checks win condition, advances to next player.
  - `void EndGame(int winnerId)` — sets phase to `GameOver`, displays win UI.
  - `void NewGame()` — resets state, regenerates galaxy if desired, starts at player 1.

### Scripts/Combat/
- **`OwnershipResolver.cs`** — pure static class. Method:
  - `OwnershipChange ResolveCapture(GameState state, int firingPlayerId, int capturedBodyId, RocketState rocket)` — determines whether the capture is a fresh capture, a dislodge, or invalid (e.g. trying to capture own home). Returns an `OwnershipChange` describing what happened (added, replaced, etc.).
- **`OwnershipChange.cs`** — data class describing the result: `int NewOwnerId`, `int? PreviousOwnerId`, `bool DislodgedExistingRocket`.
- **`WinConditionChecker.cs`** — pure static class. Method:
  - `int? CheckForWin(GameState state)` — returns the winning player's ID if win condition met (any player owns the *other* player's home), else null.

### Scripts/Presentation/
- **`PlanetOwnershipView.cs`** — `MonoBehaviour` attached to each planet's view object. Reads from `GameState.Ownership` and displays a colored halo around the planet matching the owner's color. Empty planets show no halo.
- **`OrbitingRocketView.cs`** — `MonoBehaviour` rendering each captured-into-orbit rocket. There can be many of these in the scene over the course of a game (one per captured planet). Each one is colored by its owning player. They are kinematically driven (using the existing orbit math) and don't physically interact with new in-flight rockets.
- **`TurnUI.cs`** — `MonoBehaviour` with TextMeshProUGUI elements. Displays:
  - Whose turn it is (large, top of screen, in player color)
  - Turn number
  - Each player's planet count (in their color)
  - "Press Space to begin your turn" prompt during pass-the-controller moments
- **`WinScreenUI.cs`** — `MonoBehaviour`. Shows full-screen overlay with "Player N Wins!" in player color and a `New Game` button.
- **Modify `PrototypeScenarioController.cs`** — wire it into the turn manager. The rocket no longer auto-resets on R; instead, after a rocket resolves, control passes to `TurnManager.OnRocketResolved`. R can become a "give up turn" or just not be available.
- **Modify `AimController.cs`** — only allow aim/launch when `GameState.Phase == WaitingForLaunch`. Position the rocket at the active player's home each turn.

### Tests/
- **`OwnershipResolverTests.cs`** — verify capture, dislodge, capturing own already-owned planet (no-op), capturing own home (impossible), and so on.
- **`WinConditionCheckerTests.cs`** — verify a capture of enemy home triggers a win, and other states do not.

## Turn flow (the core loop)

1. **Game start.** `TurnManager.NewGame()`:
   - Generate a galaxy (existing Phase 2 generator).
   - Create two `Player` instances (Player 1 = blue, Player 2 = red, default colors — settable).
   - Assign `Player1.HomeBodyId = galaxy.Player1HomeId`, same for Player 2.
   - Initialize `GameState.Ownership` with each player owning their own home planet only.
   - Set `CurrentPlayerId = 1`, `TurnNumber = 1`, `Phase = WaitingForLaunch`.
   - UI shows "Player 1's Turn — Press Space to begin".

2. **Pass-the-controller pause.** Spacebar advances from this prompt to active aim mode. This prevents accidental clicks during the handoff and gives the human a moment to reset.

3. **Active aim and launch.** Player 1 aims and fires using the existing aim system. Rocket starts at Player 1's home. AimController only listens during `WaitingForLaunch` phase. On launch, phase becomes `RocketInFlight`.

4. **Rocket resolves.** Existing physics + capture + outcome detection runs unchanged. When the outcome triggers (Crashed / Captured-into-Orbit / Escaped):
   - `TurnManager.OnRocketResolved(outcome, capturedBodyId)` fires.
   - If outcome is `Orbited`:
     - Call `OwnershipResolver.ResolveCapture(...)`.
     - Update `GameState.Ownership` accordingly.
     - If `DislodgedExistingRocket`, remove the previously orbiting `OrbitingRocketView` (with a small visual flash for feedback).
     - Spawn a new `OrbitingRocketView` colored by the firing player.
   - Else (Crashed/Escaped): no ownership change.
   - Run `WinConditionChecker.CheckForWin(state)`. If returns a winner ID, transition to `GameOver` and show `WinScreenUI`.
   - If no win, advance turn: `CurrentPlayerId = (currentPlayerId == 1) ? 2 : 1`, increment `TurnNumber`, set `Phase = BetweenTurns`. Show "Player 2's Turn — Press Space to begin".

5. **Repeat from step 2 with the next player active.**

## Capture / dislodge rules

When a rocket is captured into the orbit of a body B by firing player P:

- If B is unowned: P now owns B. New `OrbitingRocketView` spawns in P's color.
- If B is already owned by P: this is a no-op for ownership. Optionally, the new rocket still replaces the old one visually (older rocket fades out, new one takes orbit position). Doesn't matter much gameplay-wise; pick whichever feels cleaner.
- If B is owned by the *other* player O: P now owns B (dislodge). Old `OrbitingRocketView` is destroyed. New one spawns in P's color.
- If B is P's own home planet: ignored — your own home isn't recapturable through normal capture (you already own it). Outcome registers as Orbited but ownership state is unchanged.
- If B is the *other* player's home planet: P now owns B. Win condition triggers. (See `WinConditionChecker`.)

## Visual conventions

- Player 1 default color: bright blue
- Player 2 default color: bright red
- Empty planets render in their existing body-type color (rocky brown, etc.)
- Owned planets render with a colored ring/halo at the body's outer edge in the owner's color, *outside* the planet sprite. Subtle but unmissable.
- Orbiting rockets render as small triangles in the owner's color, on their kinematic orbit.
- The aim arrow during active aim uses the active player's color.

## UI elements

A simple, no-frills HUD:

- **Top center:** "Player N's Turn" in player color, large, plus turn number ("Turn 17") in smaller text.
- **Top left:** Player 1 name, color swatch, planet count (e.g. "Blue: 3 planets").
- **Top right:** Player 2 name, color swatch, planet count ("Red: 2 planets").
- **Bottom center during pass-the-controller:** "Press Space to begin your turn" prompt.
- **Bottom center during active turn:** brief help text ("Click your home planet, drag to aim, release to fire").
- **Win screen:** full-screen colored overlay, "Player N Wins!", and a New Game button.

## New game flow

When the win screen's New Game button is clicked, or via a hotkey (N), `TurnManager.NewGame()` runs the full reset:
- Regenerate the galaxy with a fresh seed (or reuse current — let's allow both via a checkbox, default fresh).
- Reset all ownership.
- Player 1 starts again.

## Success criteria

- A complete match plays end to end: launch → outcome → turn switch → repeat → eventual win.
- Capture, dislodge, and win condition all trigger correctly. Verified by both organic play and unit tests.
- The pass-the-controller pause prevents misclicks during handoff.
- After a few full matches with another human, you have a clear feel for whether the loop is satisfying:
  - Do players have meaningful decisions (which planet to attack, which to defend, when to push for the home shot)?
  - Does dislodging feel rewarding when you do it to your opponent?
  - Does losing a planet feel painful enough to want to retake it next turn?
  - Is the game length right (rough target: 10-30 turns per game)?
- If most answers are "yes," we move to richer mechanics. If "meh," we tune the simple game (planet count, capture parameters, home positioning) before adding anything.

## Tuning notes

After Claude Code implements:

- Match length: in early games, count how many turns until someone wins. If consistently <10, the game is too quick — make home harder to capture (raise home capture parameters' speed range, narrow the angle tolerance). If consistently >30, too slow — tune the other direction.
- Dislodge frequency: how often does a turn re-take an enemy planet vs. claim a fresh one? Both should happen meaningfully. If dislodging is too easy, the game becomes a stalemate. If too hard, planets are sticky and the game becomes about racing for fresh planets.
- Home approach difficulty: capturing the enemy home should require either luck or skill. If a player can win on turn 2 with a clean shot from their home directly to the enemy home, the galaxy layout isn't producing enough strategic depth — bump up min cluster count or lower home masses slightly.

## How to hand this to Claude Code

> Read CLAUDE.md and docs/Phase3_Spec.md carefully. Implement Phase 3 of Orbital according to the spec. Create the files in the locations specified. Pay particular attention to:
> - keeping `GameState`, `TurnManager`, `OwnershipResolver`, and `WinConditionChecker` as pure data/logic classes with no Unity dependencies in the data path
> - Phase 1 and Phase 2 systems must continue to work unchanged — physics, capture, galaxy generator, all of it
> - the rocket's start position changes per turn to the active player's home; AimController must respect the current turn phase
> - orbiting rockets that captured planets persist visually and indicate ownership
>
> When done, write a summary at `docs/Phase3_Implementation_Notes.md` covering what you built, any decisions you made beyond what the spec specifies, and any known gaps before competitive play is fun.

After Claude Code reports done: open Unity, sort out any compile errors, set up the new UI elements (you may need to create a Canvas with the turn header text and player counts manually if Claude Code didn't construct them in code), run a full match against yourself, and tell me:

1. Does a match play end-to-end without errors?
2. Does dislodging feel like a meaningful tactical move, or does it not register?
3. How many turns does a match take?
4. What's the most frustrating moment? The most satisfying?
