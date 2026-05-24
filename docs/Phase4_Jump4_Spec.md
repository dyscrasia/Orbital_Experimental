# Orbital (Strategy variant) — Phase 4, Jump 4: Per-planet production & launch sites

## Goal

Three changes that together turn captured planets into real strategic assets:

1. **Visual fix** — a planet only takes on its owner colour once colonisation
   *completes*. During colonisation, the only on-planet visual is the
   `ColonisationView` label. The `OrbitingRocketView` is no longer spawned
   on orbital capture; it appears at the moment of capture-completion.

2. **Per-planet population** — population becomes a property of the planet,
   not the player. Each owned planet has its own pop pool. Home planets
   grow at the full rate (`PopulationGrowthPerTurn`). Captured planets grow
   at half that rate. Newly-captured planets start with the colonists who
   landed.

3. **All owned planets are launch sites** — the Classic
   "1 + floor(captures/2) random bonus rockets" rule is **dropped entirely**.
   Each turn, every planet a player owns becomes a launch site. The slider
   reads from the active site's own population, not from a global player
   pool.

## Scope

**In scope:**
- `GameState.Population` is re-keyed from `int playerId` to `int bodyId`.
- New tunable on `StrategyParameters`: `CapturedPlanetGrowthDivisor` (default 2).
- Per-turn growth iterates the current player's owned planets and grants
  `+PopulationGrowthPerTurn` to homes and
  `+PopulationGrowthPerTurn / CapturedPlanetGrowthDivisor` to captured planets.
- `LaunchSiteCalculator.Calculate` returns every body the player owns
  (home + captured), with no Classic 1+floor(captures/2) selection.
- `ColonisationTicker.Completion.FinalColonistCount` is used by TurnManager to
  seed `Population[bodyId]` on capture.
- `HomePopulationView` is renamed/generalised to `PlanetPopulationView` and
  is created for **every** owned planet, not just homes.
- `OrbitingRocketView` is only spawned when an Ownership entry is created
  (i.e. at colonisation completion). It is **not** spawned on orbital
  capture.
- The slider's max is the active launch site's population
  (`state.Population[ActiveLaunchSiteId]`). The slider is hidden if the active
  site has no entry in `Population` or its pop is 0.

**Out of scope (Jump 5 — DO NOT build now):**
- Combat between opposing colonist groups.
- Restoring the win condition (still returns null this jump).
- Population decay or migration.
- Per-planet build queues that produce *rockets* (we only produce *people*
  this jump; rockets are still summoned by being a launch site).
- Variable growth rates per body type (Lava → Metals etc.). The
  half-rate-for-captured rule is the same for every body type.

## Architectural rules

- `GameState`, `LaunchSiteCalculator`, the strategy resolvers/tickers all
  stay pure data — no Unity deps.
- All mutations to `GameState.Population` happen in `TurnManager`. Views
  read but never write.
- Determinism is preserved: per-turn growth iterates `state.Ownership` in
  whatever order; the operation is commutative and idempotent per turn.

## Data model changes

### `Scripts/Strategy/GameState.cs`

The signature of `Population` does not change (`Dictionary<int, int>`), but
the **semantics** change: keys are now body IDs, not player IDs. Update the
field's XML comment:

```csharp
/// <summary>People resident on each owned planet, keyed by body ID. Each owned
/// planet (home or captured) has an entry once it is in Ownership. Removed
/// entries imply zero population; absent entries imply the planet is not
/// owned and therefore has no civilian population to draw from.</summary>
public Dictionary<int, int> Population { get; } = new Dictionary<int, int>();
```

### `Scripts/Strategy/StrategyParameters.cs`

Add one field under the existing `[Header("Population")]`:

```csharp
[Tooltip("Captured planets grow at PopulationGrowthPerTurn divided by this. " +
         "Default 2 means half the home rate. Higher means slower growth on " +
         "captured planets.")]
[Min(1)]
public int CapturedPlanetGrowthDivisor = 2;
```

## Modifications to `Scripts/Strategy/LaunchSiteCalculator.cs`

Replace the entire body. The new rule:

```csharp
/// <summary>Returns the body IDs the player may fire from this turn.
/// Strategy variant: every planet the player owns. Order: home first,
/// then captured planets ordered by ascending body ID for determinism.</summary>
public static List<int> Calculate(GameState state, int playerId)
{
    Player p = state.GetPlayer(playerId);
    List<int> sites = new List<int>();
    if (p == null) return sites;

    // Home always first.
    sites.Add(p.HomeBodyId);

    // All other planets the player owns, sorted by ID for deterministic order.
    List<int> captured = new List<int>();
    foreach (KeyValuePair<int, PlanetOwnership> kv in state.Ownership)
    {
        if (kv.Value.OwnerPlayerId == playerId && kv.Key != p.HomeBodyId)
            captured.Add(kv.Key);
    }
    captured.Sort();
    sites.AddRange(captured);

    return sites;
}
```

Note: this returns **all** owned planets, including ones with pop = 0. The
player can still position the rocket and fire empty — useful for "scouting"
or repositioning even when no colonists are available. The slider just shows
0/0 in that case and any launch carries no cargo.

The TurnNumber-based seeded random selection that Classic used for bonus
sites is removed entirely. No `Rng` calls remain in this calculator.

## Modifications to `Scripts/Presentation/TurnManager.cs`

### `BeginGame()`

Replace the per-player population seeding with per-body seeding for both homes:

```csharp
// Initialise both home planets' populations.
int start = _strategyParams != null ? _strategyParams.StartingPopulation : 0;
_gameState.Population.Clear();
_gameState.Population[p1Home] = start;
_gameState.Population[p2Home] = start;
```

### `AdvanceToNextPlayer()`

Replace the existing single-player growth grant with a per-planet pass that
runs **after** the player flip (so the player whose turn is starting receives
growth on their planets). Keep the colonisation tick *before* the player flip
where it already is.

```csharp
// 1. Tick colonisations (still happens before player flip — completions
//    belong to the player whose turn just ended).
List<ColonisationTicker.Completion> completions = ColonisationTicker.Tick(_gameState);
foreach (ColonisationTicker.Completion c in completions)
    ApplyColonisationCompletion(c);
if (completions.Count > 0)
{
    RefreshOwnershipViews();
    RefreshColonisationViews();
    RefreshPlanetPopulationViews();
}

// 2. Flip player.
_gameState.CurrentPlayerId = _gameState.CurrentPlayerId == 1 ? 2 : 1;
_gameState.TurnNumber++;

// 3. Grow population on every planet the *new* current player owns.
GrowOwnedPlanetPopulations(_gameState.CurrentPlayerId);

// 4. Existing phase transition / UI refresh.
_gameState.Phase = GamePhase.BetweenTurns;
_turnUI.Show(_gameState);
```

`GrowOwnedPlanetPopulations`:

```csharp
private void GrowOwnedPlanetPopulations(int playerId)
{
    if (_strategyParams == null) return;
    int homeRate     = _strategyParams.PopulationGrowthPerTurn;
    int divisor      = Mathf.Max(1, _strategyParams.CapturedPlanetGrowthDivisor);
    int capturedRate = homeRate / divisor;

    Player p = _gameState.GetPlayer(playerId);
    if (p == null) return;

    foreach (KeyValuePair<int, PlanetOwnership> kv in _gameState.Ownership)
    {
        if (kv.Value.OwnerPlayerId != playerId) continue;

        bool isHome  = kv.Key == p.HomeBodyId;
        int amount   = isHome ? homeRate : capturedRate;
        int existing = _gameState.Population.TryGetValue(kv.Key, out int v) ? v : 0;
        _gameState.Population[kv.Key] = existing + amount;
    }
}
```

### `HandleRocketResolved(...)` — remove the OrbitingRocketView spawn

Currently this method calls `SpawnOrReplaceOrbitingRocketView` on every
orbital capture. **Remove that call** from this method. The OrbitingRocketView
should only exist for *owned* planets, and ownership is now only written at
colonisation completion.

The method otherwise stays the same:
- `ColonisationResolver.Resolve(...)` → `ApplyColonisationChange(...)`.
- `RefreshColonisationViews()`.
- `RefreshPlanetPopulationViews()` — newly added, see below.
- Win-check still called (still returns null).

### `ApplyColonisationCompletion(c)` — spawn rocket and seed population

This already writes to `state.Ownership`. Two additions:
- After writing Ownership, also write `_gameState.Population[c.BodyId] = c.FinalColonistCount;`
- Call `SpawnOrReplaceOrbitingRocketView(c.BodyId, ownerColor)` here. This
  is the *only* place that spawns the orbiting-rocket visual now.

### `HandleRocketLaunched()` — subtract from launch site, not player

Replace the per-player subtraction with per-site:

```csharp
int load = _loadingUI != null ? _loadingUI.CurrentLoad : 0;
int siteId = _gameState.ActiveLaunchSiteId;

int siteAvailable = _gameState.Population.TryGetValue(siteId, out int p) ? p : 0;
if (load > siteAvailable) load = siteAvailable; // defensive clamp

_psc.Rocket.PassengerCount = load;
if (load > 0)
    _gameState.Population[siteId] = siteAvailable - load;

_loadingUI.Hide();

// existing body of HandleRocketLaunched continues here.
```

The previous "isHomeSite" check disappears — any owned site can carry cargo.

### `SelectLaunchSite(int bodyId)` — slider visibility based on pop

Replace the `isHomeSite` check with a per-site pop check:

```csharp
Player current = _gameState.CurrentPlayer;
bool hasPop = _gameState.Population.TryGetValue(bodyId, out int avail) && avail > 0;

if (_gameState.Phase == GamePhase.WaitingForLaunch && hasPop)
    _loadingUI.Show(avail, current.Color);
else
    _loadingUI.Hide();
```

If you want the slider to *show but be at 0* even when the planet has no
pop (visual consistency), use `_loadingUI.Show(0, current.Color)` instead.
Default to hiding, which keeps the screen tidier when 4+ launch sites have
empty pops.

### New helper: `RefreshPlanetPopulationViews()`

After Jump 4, the renamed/generalised `PlanetPopulationView` exists for
every body that appears in `state.Ownership` (homes included). This helper:

- For each owned body with no existing view: create one, parent it to a
  shared canvas (or use the per-view canvas pattern from `ColonisationView`),
  position it above the planet, set the player colour.
- For each existing view whose body is no longer in `state.Ownership` (won't
  happen in Jump 4 but defensive for Jump 5+): destroy it.
- Each `LateUpdate`, the view itself re-reads `state.Population[bodyId]` and
  re-positions via world-to-screen — same pattern as the current
  `HomePopulationView`.

Call `RefreshPlanetPopulationViews()` from:
- `BeginGame()` (after `RefreshOwnershipViews()`).
- `ApplyColonisationCompletion()` (a freshly captured planet needs a view).
- `AdvanceToNextPlayer()` (after the growth pass, so labels reflect the
  new pops immediately).
- `HandleRocketLaunched` and `HandleRocketResolved` (pops change here too —
  although the views are updated each `LateUpdate`, calling Refresh keeps
  the create/destroy lifecycle correct).

## Renamed/Generalised: `HomePopulationView.cs` → `PlanetPopulationView.cs`

- Rename the file and class.
- Drop any "home-specific" defaults; `Initialize(body, state, playerId)` works
  for any owned planet.
- Update all references in `TurnManager` (`_homePopulationViews` →
  `_planetPopulationViews`, the matching Clear/Refresh helpers, etc.).

## Visual states reference

State of a planet → what's visible:

| State                              | Owner ring | Orbiting rocket | Colonisation label | Population label |
|------------------------------------|-----------:|----------------:|-------------------:|-----------------:|
| Unowned                            | hidden     | hidden          | hidden             | hidden           |
| Unowned, being colonised           | hidden     | **hidden**      | visible (colonising player colour) | hidden |
| Owned                              | visible (owner) | visible (owner) | hidden          | visible (owner)  |
| Owned, being colonised by opposing | visible (owner) | visible (owner) | (does not occur in Jump 4 — colonising owned planets is Blocked) | visible (owner) |

Compared to Jump 3, the only change to this table is in the "Unowned, being
colonised" row: the OrbitingRocketView is now hidden during colonisation.

## Tunables (post-jump)

`StrategyParameters`:
- `PopulationGrowthPerTurn` (default 10) — home rate.
- `StartingPopulation` (default 0).
- `ColonisationBaseDuration` (default 20).
- `MinColonisationTurns` (default 1).
- `CapturedPlanetGrowthDivisor` (default 2) — captured-planet rate is
  `PopulationGrowthPerTurn / divisor`.

Likely worth tuning after a few games: `CapturedPlanetGrowthDivisor` should
push the player to keep capturing rather than parking on one planet. If the
half-rate feels too generous (snowball happens too fast) raise the divisor
to 3 or 4. If it feels too punishing (captured planets are dead weight),
lower it to 1 (= same rate as home).

## Behaviour walkthrough (sanity check)

1. New game. P1 home and P2 home each show `Pop: 0` (via PlanetPopulationView).
2. P1 turn 1: only launch site is home. Slider hidden (pop = 0). P1 fires
   empty rocket. Lands somewhere. Turn passes.
3. P2 turn 1: AdvanceToNextPlayer → tick (nothing) → flip → grow: P2 home +10
   (no other owned planets). P2 sees their home with `Pop: 10`.
4. P2 loads 10, fires at unowned planet X. Capture begins.
   ColonisationView shows `10 colonists · 2 turns` above X. **No ring on X.
   No orbiting rocket on X.** P2 home Pop drops to 0.
5. P1's turn: tick X → 1 turn remaining. Flip → grow P1 home +10.
6. P2's turn: tick X → 0 → completion. X's Ownership = P2, X's Population
   seeded to 10, ring appears red, orbiting rocket appears red,
   ColonisationView disappears, PlanetPopulationView appears on X
   showing `Pop: 10`. Flip → grow: P2 home +10, X +5. P2 home Pop = 10,
   X Pop = 15.
7. P2's next turn: launch sites = [P2 home, X]. P2 can fire from either.
   Slider visible on either; max reads from that site's pop.

## Determinism

- The per-turn growth iterates Ownership. Order doesn't matter — each entry
  is independent. No randomness.
- The new LaunchSiteCalculator sorts captured IDs to keep the launch-site
  ordering deterministic.
- All values are integer-arithmetic; floor division gives reproducible rates.

## Success criteria

- During colonisation, the planet has only the ColonisationView label —
  no ring, no orbiting rocket.
- At capture-completion (the moment the timer hits 0), the ring, the
  orbiting rocket, and the PlanetPopulationView all appear in the same
  frame and the ColonisationView label disappears.
- A freshly-captured planet starts with population equal to the colonists
  who landed (e.g. 10 if the colonising rocket carried 10).
- Each turn handover: home grows by 10, captured planets grow by 5
  (defaults).
- Every owned planet appears in the player's launch sites that turn.
  The Classic random "bonus rockets" selection is gone.
- Slider on an owned launch site reads from that site's population; firing
  drops that site's pop, not anyone else's.
- New Game resets all pops, all colonisations, all ownership.
- Pressing N during play also resets cleanly.

## How to hand this to Claude Code

1. Confirm the spec lives at `docs/Phase4_Jump4_Spec.md`.
2. In a terminal:
   ```
   cd "C:\Users\leigh\Documents\Claude\Projects\Video games\Orbital_Experimental"
   claude
   ```
3. Use this prompt:

> Read CLAUDE.md and every existing doc under docs/ (Phase4 Jumps 1–3 specs and implementation notes, and docs/Phase4_Jump4_Spec.md). Implement Jump 4. Pay particular attention to:
> - Removing the OrbitingRocketView spawn from HandleRocketResolved; it is now spawned only at colonisation completion.
> - Re-keying GameState.Population from playerId to bodyId throughout the codebase. Search for every reader and writer.
> - Renaming HomePopulationView to PlanetPopulationView and instantiating it for every owned planet (homes plus captured).
> - Dropping the Classic random bonus-site selection in LaunchSiteCalculator and returning all owned planets (home first, then captured sorted by ID).
> - The grow-population pass runs *after* the player flip in AdvanceToNextPlayer, on the player whose turn is now starting.
>
> Do not introduce combat or restore the win check — those belong to Jump 5.
>
> When done, write docs/Phase4_Jump4_Implementation_Notes.md covering what changed, decisions beyond the spec, and any open questions.

After Claude Code reports done:
- Let Unity import.
- Resolve any compile errors in-session.
- Drag the `StrategyParameters.asset` onto PSC again only if Unity drops the
  reference (it sometimes does after asset-menu/file renames).
- Play. Check the success-criteria checklist above. Tune
  `CapturedPlanetGrowthDivisor` if the early game feels too slow / too fast.
