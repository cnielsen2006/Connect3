# Cascade: Technical Requirements Document (TRD)

| | |
|---|---|
| Status | Draft v1.0 (Unity 6 / C#) |
| Date | 2026-10-04 |
| Related | [MRD](MRD.md) · [PRD](PRD.md) · [ADRs](adr/README.md) · [Art styles](art/README.md): [I](art/STYLE-I-Claymation.md) · [J](art/STYLE-J-StudioSet.md) · **[K (v1)](art/STYLE-K-Pebbles.md)** |

> **For implementers:** the docs are the spec, and code follows them. If something is missing or
> ambiguous, update the doc first (or ask), then write the code. Do not hard-code any number that
> PRD/TRD call a dial; read it from the dial registry (§5).

> **Scope.** The game is built on Unity 6 and C# ([ADR-006](adr/ADR-006-unity-engine.md)). The
> **game rules, algorithms, RNG, dials and difficulty profile** (§3-§7) are engine-neutral and
> checked by the conformance fixtures (§9.2). The structure is described in
> [ADR-007](adr/ADR-007-layers-and-ports.md) and §2; everything platform-specific is in §1, §8,
> §12, §13 and §14.

## 1. Stack

| Concern | Choice |
|---|---|
| Engine | **Unity 6** (6000.x LTS). The exact editor version is pinned in `ProjectSettings/ProjectVersion.txt` and in §14; the team upgrades together, never per machine |
| Language | C# 9 (what Unity 6 compiles), nullable reference types **on** in all logic assemblies |
| Targets | Android (arm64 only: Unity 6000.6's Android player ships no x86_64 libraries; see §13.1), Windows and macOS standalone |
| Scripting backend | IL2CPP on every shipped target (required for Android arm64); Mono only inside the Editor |
| Rendering | **Universal Render Pipeline (URP)**; Shader Graph for the clay material; MSAA set from a dial (§12.1) |
| Input | Unity **Input System** package: mouse and keyboard on desktop, `EnhancedTouch` for multi-touch |
| UI | **UI Toolkit** (runtime). Screens and HUD are UXML + USS data (§12.2) |
| Config and dials | TOML files, parsed by **Tomlyn** into a plain table and mapped by hand (no reflection binding, so IL2CPP stripping cannot break it) |
| JSON (saves, events, level dumps) | **Newtonsoft Json.NET** (`com.unity.nuget.newtonsoft-json`), explicit converters |
| 3D assets | glTF 2.0 (`.glb`) imported with **glTFast**; placeholder pieces are Editor-generated meshes (§12.1) |
| Testing | NUnit (Unity Test Framework). One source tree of logic tests, two runners: `dotnet test` (fast, no Unity license) and Unity EditMode; conformance also runs in a device Player build (§9) |
| Headless tools | `tools/Cascade.Tools`, a .NET 8 console that compiles the logic sources (difficulty report, rate calibration, text view, fixture verifier) |
| Build | Unity batch mode through `Cascade.Editor.BuildScript` (§13.1) |

## 2. Architecture

The rule is a **pure-logic core** (no engine types, deterministic,
unit-tested) and a **view** that renders the core's state and animates the events it emits. The
roles are kept sharp, so that every platform (Android, desktop, a later iOS or a
headless server) reuses everything except one thin layer ([ADR-007](adr/ADR-007-layers-and-ports.md)).

### 2.1 Layers and assemblies

Each layer is one assembly definition (`.asmdef`). Logic layers set `noEngineReferences: true`,
so a `using UnityEngine;` in them is a compile error, and list their references explicitly, so an
upward dependency is a compile error too.

| Layer (assembly) | Role | Allowed references | Must never contain |
|---|---|---|---|
| `Cascade.Core` | The rules of the game: topology, flow, board, matching, resolution, specials, scoring, goals, moves, events, `Level`, RNG | none | engine types, file/clock/network access, the dial registry, system RNG, `Dictionary` iteration order dependence |
| `Cascade.Config` | Dial registry (typed, validated, change notifications), dial metadata, TOML to values | none | any knowledge of rules or UI |
| `Cascade.Platform.Abstractions` | The **ports**: small interfaces the logic needs from its host (§2.3) | none | implementations |
| `Cascade.Gameplay` | `Session` (a running level), `MoveEvaluator` (shallow move value, used by hints and the bot), `PlayerProgress` rules (`StartLevel`, `RecordWin`) and the data they work on (`SaveData`, `PlayerSettings`, `GeneratorVersion`; Persistence only serialises them), `RuleSets` (dials to the immutable rule and generator snapshots) | Core, Config, Abstractions | rendering, input, persistence I/O |
| `Cascade.Generation` | Difficulty curve and profile, level generator, validator, bot, tuner, `ILevelSource` | Core, Config, Gameplay, Abstractions | rendering, input, persistence I/O |
| `Cascade.Persistence` | Serialising progress and settings, atomic writes, corrupt-file recovery, all through `IStorage` | Gameplay, Abstractions | `System.IO` calls (it only uses the port) |
| `Cascade.Presentation` | Presentation **logic** with no rendering: `TimelinePlanner` (events to a timeline), `InputStateMachine` (PRD §2.2), `GestureRecognizer`, `CameraFit`, `PieceVariation`, `ScreenFlow` (title, map, intro, play, result, settings, dev), screen view-models | Core, Config, Gameplay, Generation, Abstractions | `UnityEngine` types (it has its own `Float2`, `Float3`, `RectF`, `ColorF` value types) |
| `Cascade.Unity.Platform` | Implements the ports on Unity: `UnityHost`, `FileStorage` (`persistentDataPath`), `DataCatalog` (TOML as assets), `CryptoSeedSource`, lifecycle and Back, safe area | all logic layers, Unity | game rules |
| `Cascade.Unity.View` | Everything you can see or touch: `BoardView`, `PieceView`, `TimelinePlayer`, camera rig, style applier, Input System adapters, UI Toolkit screens bound to view-models | Presentation (and below), Unity | rule decisions; it never changes logic state |
| `Cascade.App` | The **composition root**: the only place that builds the object graph (dials, host, storage, level source, session factory, screens) | everything | logic of its own |
| `Cascade.Editor` | Editor-only: `.toml` importer, placeholder mesh generator, build script, dev menu items | all | runtime code |

```
Cascade.App ─► Cascade.Unity.View ─► Cascade.Presentation ─┐
     │                 │                                    ▼
     └► Cascade.Unity.Platform ───────────────► Cascade.Generation ─► Cascade.Gameplay ─► Cascade.Core
                                         Cascade.Persistence ──────────────┘                 ▲
          Cascade.Config and Cascade.Platform.Abstractions are leaves any logic layer may use│
```

Dependency rule: arrows only point down. `Core` references nothing. The logic layers
(`Core` … `Presentation`) live together under `Assets/Cascade/Logic/` and the Unity layers beside
them. They are plain folders with assembly definitions, not a package: there is one project, so the
assemblies give the enforcement and a package would add nothing. The headless .NET projects
(§2.2) compile the `Logic/` folder by glob, which is all the reuse needed.

### 2.2 Repository layout

```
Connect3/
  docs/                            this document set
  conformance/                     reference fixtures (§9.2); read-only
    rng/  golden/  levels/  profiles/  bot/  data/
  tools/
    Cascade.Tools/                 .NET console; compiles the logic sources by glob
    Cascade.Tests.Headless/        .NET test project; compiles the same test sources by glob
  unity/                           the Unity project root
    Assets/Cascade/
      Logic/           no Unity references; one asmdef per layer (§2.1)
        Core/  Config/  Platform.Abstractions/  Gameplay/  Generation/  Persistence/  Presentation/
        Tests/{Core,Config,Gameplay,Generation,Persistence,Presentation,Conformance}/
      Unity.Platform/  Unity.View/  App/  Editor/
      Data/            dials.toml, dials.android.toml, difficulty.toml (§5, §6)
      Styles/          K_Pebbles style asset, materials, fleck textures (§12.1)
      UI/              UXML, USS, fonts (Delius, Delius Swash Caps; SIL OFL) (§12.2)
      Art/             meshes (.glb), textures
      Scenes/          Boot.unity (one scene; screens are UI, not scenes)
    ProjectSettings/  Packages/manifest.json
  Builds/                          build output; **ignored by the repo's ignore file** (§14)
```

### 2.3 Ports

The logic never asks "what platform am I on?". It is handed small interfaces
(`Cascade.Platform.Abstractions`), and the composition root picks the implementation. This
keeps platform, secret, clock and storage-path knowledge out of the logic.

| Port | Provides | Unity implementation | Test implementation |
|---|---|---|---|
| `HostInfo` (record) | `Kind` (`Desktop`, `Android`, later `Ios`), `HasMouse`, `HasKeyboard`, `HasTouch`, `Dev`, `ScreenDpi` | `UnityHost.Detect()` | a literal |
| `IStorage` | `TryRead(name)`, `WriteAtomic(name, bytes)`, `Move(name, newName)`, `Delete(name)` in the user data folder | `FileStorage` (temp file then replace) | `MemoryStorage` |
| `IDataSource` | `ReadText(key)` for shipped data (`dials.toml`, `difficulty.toml`) | `DataCatalog` (a ScriptableObject listing the TOML assets) | files on disk or literals |
| `ISeedSource` | `NextSeed() -> ulong`, the only non-deterministic input to a level | `CryptoSeedSource` (`RandomNumberGenerator`) | a counter |
| `IStopwatch` | elapsed milliseconds, for `generator.budget_ms` | `System.Diagnostics.Stopwatch` | a fake clock |
| `ILog` | `Info/Warn/Error` | `Debug.Log*` | a list |
| `IAppLifecycle` | events `Paused`, `Resumed`, `BackPressed`, `ScreenChanged` (size, safe area, orientation) | `LifecycleBridge` | a manual trigger |

### 2.4 Roles at a glance

| Question | Answered by | Never by |
|---|---|---|
| "Is this swap legal, what cleared, what did it score?" | `Core` (`Rules.Resolve`) | the view, the UI |
| "What numbers does the rule use?" | an immutable `RuleSet` built from dials in `Gameplay.RuleSets` and handed in | `Core` reading the dial registry |
| "What does the player do next?" | `Gameplay.Session` | a screen or a MonoBehaviour |
| "How does this event list look in time?" | `Presentation.TimelinePlanner` (pure, testable) | `Unity.View` |
| "Put this timeline on screen at time *t*" | `Unity.View.TimelinePlayer` | `Presentation` |
| "Which finger gesture is this?" | `Presentation.GestureRecognizer` | an Input System callback |
| "Where is the safe area, what is the DPI?" | `Unity.Platform` through `IAppLifecycle` / `HostInfo` | UI code |
| "Where do files go?" | the `IStorage` the composition root chose | any other class |

### 2.5 Language conventions

- **Names and order.** Rules use PascalCase method names with the algorithm and order of operations
  fixed by §3-§7. Behavior changes go through the docs first.
- Data is `sealed record`/`readonly struct` and immutable.
  `Event` is an abstract record with one sealed record per kind; consumers use exhaustive
  `switch` expressions. Unity 6 needs an `IsExternalInit` polyfill for records; `Core` carries it.
- **No static mutable state** in any logic assembly: generation runs on a worker thread (§12.3).
- `Core`/`Generation` never use `System.Random`, `Guid`, `DateTime`, `GetHashCode()`,
  `Dictionary`/`HashSet` iteration order, or culture-dependent formatting (§4.3).

## 3. Core model

The core does **not** assume one rectangular grid with top-down gravity (PRD §2.6). It is
built around a topology graph, with the classic grid as one preset
([ADR-002](adr/ADR-002-topology-flow-core.md)). Types below are in `Cascade.Core`.

**Topology (static for a level):**
- `CellId`: a `readonly struct` over `int` (C# 9 has no `record struct`; it is implicitly convertible from `int`). `Topology.Pos(cell) -> (Layer, X, Y)` gives
  logical coordinates that the view maps to world space. Logic never reads coordinates to
  decide rules.
- Holes are simply absent from the topology: no cell, no neighbor, nothing matches through them.
- `Topology.Neighbors(cell)`: cells it can swap with.
- `Topology.Lines`: ordered sequences of cells along which matches are found. On a grid these are
  rows and columns, split at holes. `Topology.LineThrough(cell, axis)` is what a Line piece clears.
- `Topology.Area(cell, radius)`: the cells a Bomb hits.
- `Topology.Links`: directed non-adjacent edges (funnels, pipes, portals, layer drops). Empty in v1.

**Flow:**
- `Flow.Feed(cell) -> IReadOnlyList<CellOrSource>`: where a cell pulls its next piece from, in
  priority order. Gravity: the nearest cell above (skipping holes), else the column's source.
- `Source`: `Id`, the cells it feeds, and a policy (`RandomColors` in v1; `Limited(n)` and
  `Scripted` later). Each source draws from its own RNG sub-stream.
- `Flow.Settle(board, rng) -> (Board, IReadOnlyList<Moved | Spawned>)` pulls pieces along `Feed`
  until no reachable empty cell can be filled. It must terminate and be deterministic, so it
  processes cells in a fixed order.

**State:**
- `Board`: `Cells` (an array indexed by `CellId`, never an unordered map) plus references to its
  `Topology` and `Flow`, and `SpecialsEnabled` (default `{Line, Bomb, Color}`, kept by `Copy()`):
  earned specials of a kind not in it are dropped when placing (the qualifier still scores).
  Levels set it from their unlocks.
- `Cell`: `Tile?`. (No blockers in v1.)
- `Tile`: `Id: int` (stable from spawn to clear), `Color: int`, `Special?`.
  `Special` is `Line(axis)`, `Bomb` or `Color`.

**Rule parameters.** `Rules.Resolve(board, swap, rng, ruleSet)` takes an immutable
`RuleSet { ScoringRules, SpecialRules }` snapshot. `Core` never reads dials. `Gameplay.RuleSets`
builds a snapshot from the dial registry; a live dial change builds a new snapshot used from the
next move on (§5).

### 3.1 Resolution algorithm (`Rules.Resolve`)

```
Resolve(board, swap, rng, ruleSet) -> (board, events)
  1. Swap the two tiles.               emit Swapped(a, b)
     If both are specials: fire both (go to step 4 with them queued). Else:
  2. depth = 0
  3. loop:
       groups = Match.FindGroups(board)          # see 3.2
       if no groups and no queued specials: break
       for each group, in order of its lowest cell id, with i = 0, 1, ...:
           emit Matched(group.cells, group.qualifiers)
           mult_i = Scoring.Multiplier(startLevel + depth, i, ruleSet.Scoring)
           score += Scoring.Score(group.qualifiers, mult_i, ruleSet.Scoring)   # Scored is emitted in step 4
           earned += specials earned by group.qualifiers
           queue for clearing: every cell in group (the superset)
  4.   clear wave by wave:
         clear the queued cells           emit Cleared(tile_ids, wave)
         any cleared tile that was special fires; the cells it hits form the next wave
         repeat until a wave fires nothing  (each tile fires at most once)
       then emit Scored(points_i, depth, mult_i) for every group in group order (even when 0 points,
       so the k-th Scored of a round is the k-th Matched), then the special-clear bonus and the
       flat Color+Color bonus as extra Scored events (below)
  5.   Settle(board) with no-match spawning (3.4)    emit Moved / Spawned
  6.   assign earned specials (3.3)        emit SpecialCreated(tile_id, special) as each lands
       (after settle, because "incoming" hosts are the tiles settle just spawned)
       depth += 1
  7. If no legal move remains: shuffle (Moves)   emit Shuffled(mapping)
```

Clarifications (fixed behavior):
- `Scored(points, depth, multiplier)` is emitted once per match group, after the round's clears and in
  group order (so the view can escalate the celebration group by group and place each popup over its
  own group), then once more per iteration for the special-clear bonus:
  `round(sum of qualifier scores × multiplier)` for a group, and `round(special_tile_cleared ×
  tiles cleared in waves ≥ 1 × multiplier of the iteration's last group)` for the bonus.
  `multiplier(level, i) = 1 + cascade_step × level + group_step × i` (PRD §2.3), where `level =
  startLevel + depth`. `startLevel` is a parameter of `Resolve`, default 0; the session passes
  the lingering level later (PRD §2.3), so `Core` stays free of clocks. Rounding is
  **round half to even** (`Math.Round(x, MidpointRounding.ToEven)`). Color+Color adds `combo_color_color` flat (no multiplier) to the first iteration; its
  tiles all clear in wave 0, so `special_tile_cleared` does not apply to them.
- A Color piece fires on the color of the tile it was swapped with (this move only); if that color
  is gone, or it was not swapped, on the most common color on the board (ties: lowest color id).
  Swapping a Color piece with a normal piece is legal only if it makes a match (PRD §2.2).
- Special-with-special swaps other than Color+Color fire both pieces where they land.
- `Spawned.Cell` is the cell where the piece enters; if it then travels, a `Moved` follows.
- Shuffle permutes colors among tiles; tiles keep ids and specials. `Shuffled.Mapping` is
  `(tile_id, new_color)` pairs.

The core never waits on animation; the view replays the event list.

### 3.2 Match groups and qualifiers
- **Runs:** maximal runs of ≥ 3 same-colored tiles along each line.
- **Groups:** runs that share a cell, or are orthogonally adjacent end-to-end, are merged into
  one group (union-find).
- **Qualifiers** in a group. *All* of them count for scoring and specials (PRD §2.3):
  - each run of length 3 → `Run3`
  - each run of length 4 → `Run4` (earns Line)
  - each run of length ≥ 5 → `Run5` (earns Color)
  - each pair of runs on different axes that intersect or touch → `Cross` (earns Bomb)
- Runs only merge (and count a `Cross`) when they are the same color, on different axes, and
  share a cell or have end cells that are neighbors. Parallel runs never merge.
- The cleared set is the union of all run cells in the group.

### 3.3 Placing earned specials
For each earned special, pick a host tile with the `specials` RNG sub-stream:
- With probability `specials.incoming_chance` (dial, default 0.4), pick a random tile that will
  spawn in this step's settle.
- Otherwise pick a random normal (non-special) tile that is on the board after clearing.
- If there is no candidate in the chosen pool, use the other pool. If neither has one, the
  special is lost.
- A Line piece's axis is random.

### 3.4 No-match spawning
After deciding the final resting cell of every piece in a settle pass, choose colors for the new
pieces in a fixed cell order. A new piece's color is drawn uniformly from the colors that **do not
complete a run of 3** with the pieces around its final cell. If every color would match (rare),
pick the color that makes the shortest run. Pieces that already existed and fell may still form
matches; those are legitimate cascades.

### 3.5 Legal moves and shuffle
- `Moves.FindLegal(board)` lists every neighbor swap that makes a match, plus every
  special-with-special swap.
- If the list is empty, shuffle tile colors (keeping specials) with the `shuffle` sub-stream
  until there are no matches and at least one legal move. Give up after 100 tries and regenerate
  the colors.

### 3.6 Events
Events reference tile ids and cell ids, never pixels: `Swapped(a, b)`, `SwapRejected(a, b)`,
`Matched(cells, qualifiers)`, `Cleared(tile_ids, wave)`, `SpecialFired(tile_id, cells)`,
`SpecialCreated(tile_id, special)`, `Moved(tile_id, path)`,
`Spawned(tile_id, source_id, cell, color)`, `Scored(points, depth, multiplier)`, `Shuffled(mapping)`.
`Moved` carries the full path so a piece can tumble through a funnel later.

**JSON contract (it is what the golden logs contain).** Each event is
an object with a `"type"` member (the type name, for example `"Swapped"`) followed by its fields
under their **snake_case** names (`tile_ids`, `source_id`), in declaration order. Lists are JSON
arrays; `qualifiers` is `[{"kind": "Run3", "length": 3}]`; a `special` is `{"kind": "Line",
"axis": 0}` or `{"kind": "Bomb"}`; `mapping` is `[[tile_id, color], ...]`. The C# serializer
uses explicit converters and `JsonProperty` names so this does not depend on reflection naming
policy.

**v1 scope:** only the `Grid(w, h, holes)` topology preset and the `GravityDown` flow preset are
built. The interfaces above exist from the start so later mechanics are presets, not rewrites.

## 4. Determinism and RNG

### 4.1 Generator
- **PCG32** (PCG-XSH-RR, 64-bit state and 32-bit output; the reference algorithm from
  pcg-random.org) in `Cascade.Core.Rng`, using `ulong`/`uint` with unchecked wrap-around (C#'s
  default), so sequences are identical to the reference vectors and any other implementation.
- **Seeding sub-streams:** `Substream(levelSeed, name)`:
  - `h = Fnv1a64(Encoding.UTF8.GetBytes(name))`
  - `s = SplitMix64(levelSeed ^ h)`
  - `inc = SplitMix64(s)`
  - initialize PCG32 with `initstate = s` and stream increment `inc | 1`, using the reference
    seeding (`state = 0; step; state += initstate; step`). Sanity check: increment 109 with
    initstate 42 reproduces the reference demo (`0xa15c02b7, 0x7b47f409, ...`).
- **Named streams:** `layout`, `tiles`, `goals`, `spawn`, `specials`, `shuffle`, `bot`. Each
  Source draws from its own stream, `spawn.<source id>`.
- **Helpers:** `NextU32()`, `RandBelow(n)` (rejection sampling with no modulo bias),
  `Random()` (u32 / 2^32 as `double`), `Choice(list)`, `Shuffle(list)` (Fisher-Yates).
- **New seeds** come from `ISeedSource.NextSeed()` (§2.3) when a level is first started. The seed is
  saved and reused for retries until the level is won. Nothing in `Core` or `Generation`
  generates a seed on its own.
- `GENERATOR_VERSION` is bumped whenever generation output changes, and saves store it. The generator
  version starts at 1.

### 4.2 Why this is enough
Every random decision goes through a named PCG32 stream; every ordering decision goes through
arrays or sorted keys. Given the same seed and rule set, any conforming implementation produces
the same events.

### 4.3 Determinism rules (the places languages and runtimes differ)
| Hazard | Rule |
|---|---|
| `Dictionary`/`HashSet` iteration order | never iterate one in logic; use arrays indexed by id, or sort keys with an ordinal comparer first |
| `GetHashCode()`, `string.GetHashCode()` | banned (randomised per run); use `Fnv1a64` |
| Integer division and `%` on negatives | C# truncates but the rules need floor semantics. Use `MathX.FloorDiv` / `MathX.FloorMod` wherever an operand may be negative |
| Rounding | `Math.Round(x, MidpointRounding.ToEven)` everywhere (round half to even); never `Math.Round(x)` with the default overload on a `float`, and never `float` in logic: use `double` |
| `Math.Pow`, `Exp`, `Log` | may differ in the last bit between CPUs and runtimes. Allowed only in the difficulty profile (`ramp_power`); never feed a floating comparison into a rules decision (§3). Multiplications and additions are exact IEEE and safe if the order of operations is kept |
| Sorting | `List.Sort` is unstable: use a stable sort (`OrderBy` with a tiebreak key) wherever ties exist |
| String comparison and formatting | ordinal comparers; `CultureInfo.InvariantCulture` for any number-to-text in a stored file |
| `ulong` in JSON | written and read as decimal text (a JSON number loses precision above 2^53); the save file already stores seeds this way |

## 5. Dials (tunable values)

Every value the PRD calls a dial lives in `Data/dials.toml`, with dev overrides in
`<user data dir>/dev_dials.toml` (§8). `Cascade.Config.DialRegistry`:
- loads the defaults (through `IDataSource`), then the platform overrides for the current host
  (`dials.<kind>.toml`, for example `dials.android.toml`, if it exists), then the dev overrides
  (through `IStorage`)
- a platform file may only change values of existing dials (an unknown key is an error there too)
- validates types and ranges (an unknown key is an error)
- exposes typed access (`dials.Scoring.Run3`)
- notifies subscribers on change, so the dev settings page can apply changes live
- carries **metadata per dial** (section, key, type, min, max, step, description). The dev
  settings page (§12.2) is generated from this metadata, so a new dial needs no UI work.

`Gameplay.RuleSets.From(dials)` turns dials into the immutable snapshots `Core` takes (§3);
generation reads its own `GeneratorSettings` snapshot the same way.

Initial dials and defaults:

```toml
[scoring]
run3 = 60
run4 = 120
run5 = 200
run_extra_per_tile = 40      # each tile beyond 5
cross = 150                  # each Cross qualifier
special_tile_cleared = 20    # per tile cleared by a special
cascade_step = 0.5           # multiplier = 1 + cascade_step * round + group_step * groups_already_scored
group_step = 0.25            # each further group in the same round
combo_color_color = 5000

[specials]
incoming_chance = 0.4
bomb_radius = 1              # 3x3

[stars]
two_star_percentile = 50
three_star_percentile = 85
min_wins = 3                 # fewer bot wins than this -> use the fallback below
fallback_two = 1.25          # fallback star2 = fallback_two * median bot score (all runs)
fallback_three = 1.6         # fallback star3 = fallback_three * median bot score

[input]
drag_inner_fraction = 0.6    # neighbor's central fraction that counts as "well inside"
double_click_ms = 300
touch_inner_fraction = 0.5   # touch version of drag_inner_fraction (fingers are less precise)
double_tap_ms = 300
tap_slop_mm = 2.0            # finger movement still counted as a tap, not a drag
pinch_min_mm = 4.0           # change in finger spread before a pinch starts zooming
min_touch_target_mm = 9.0    # smallest button on touch devices (PRD §5.3)

[render]
multisamples = 4             # MSAA samples (0, 2, 4, 8), applied to the URP asset at startup
target_fps = 0               # 0 = the display's refresh rate; Unity mobile defaults to 30 (§13.9)

[animation]                  # durations in ms; settings.animation_speed multiplies them all
select_lift = 90
swap = 180
swap_reject = 220
clear = 250
special_fire = 300
fall_cells_per_sec = 14
spawn_drop = 160
land_squash = 0.2
settle_wobbles = 1
wave_stagger = 60

[celebration]                # PRD 5.4.1; tier = how many tier_N thresholds the multiplier has reached (0 to 4)
tier_1 = 1.5
tier_2 = 2.5
tier_3 = 4.0
tier_4 = 6.0
badge_scale_base = 1.0       # badge size at tier 0
badge_scale_step = 0.35      # extra size per tier
badge_ms = 700
group_stagger_ms = 140       # groups scored in one round show their badges this far apart
particles_per_tier = 10      # burst size = particles_per_tier x tier
camera_punch_per_tier = 0.012  # zoom-in per tier, fraction of the view
stinger_from_tier = 2
reduce_effects = 0           # 1 = no particles or camera punch (the badge stays)

[camera]
fit_margin = 0.04            # fraction of board area kept as margin when auto-fitting
zoom_min = 0.6               # relative to auto-fit
zoom_max = 3.0
zoom_step = 1.12             # per wheel notch
pitch_deg = 25               # K: Pebbles

[audio]
master_volume = 0.7          # 0-1, multiplies every cue; the player's sound toggle mutes everything (§12.4)
cascade_pitch_semitones = 1.0  # a clear sounds this many semitones higher per cascade depth
cue_cooldown_ms = 40         # the same cue is not started again within this time (many pieces land at once)

[pieces]
variation_yaw_deg = 13
variation_squash_min = 0.82
variation_brightness = 0.06

[generator]
bot_runs = 20                # K
tune_rounds = 3              # R
win_rate_tolerance = 0.08
budget_ms = 1000             # wall-clock cap per level, 0 = none (§7); re-measure on the reference device
min_bot_runs = 5             # the budget never cuts a measurement below this many runs
bot_random_chance = 0.25     # chance the bot plays a random legal move
bot_weight_goal = 10.0       # bot move value: goal progress (tiles of wanted colors cleared)
bot_weight_special = 3.0     #   ... specials earned
bot_weight_points = 0.01     #   ... points
move_limit_min = 10          # tuner bounds for move_limit
move_limit_max = 50
goal_step = 0.1              # tuner: goal size change per step once move_limit is at a bound
goal_score_round = 50        # score goal targets round to a multiple of this
collect_min = 6              # smallest collect goal
collect_two_color_chance = 0.4  # chance a collect goal names two colors (needs >= 5 colors)
combo_scale = 0.75           # each half of a combo goal is this fraction of a single goal
holes_chance = 0.8           # chance a level past the holes unlock is shaped (the intro always is)
min_cells = 36               # shaped boards must keep at least this many cells
```

`[render] target_fps` is a dial; there is no safe-area fallback dial because Unity reports the real safe area (§12.2).

## 6. Difficulty profile

`Data/difficulty.toml` holds the curve (PRD §3.2) and the bands (PRD §4). The format and
every key are as below:

```toml
[meta]
version = 2

[difficulty]                 # difficulty(n) = clamp(trend + amplitude * wave, 0, 1)
trend = { curve = "ease_out", points = [[1, 0.05], [150, 0.75]] }
amplitude = { curve = "linear", points = [[1, 0.10], [150, 0.18]] }
cycle_length = 7             # P
ramp_power = 1.6             # K
release_depth = 0.6          # R
intro_level_difficulty = 0.1
easiest_win_rate = 0.97
hardest_win_rate = 0.35

[params.colors]
curve = "step"
points = [[1, 4], [21, 5], [51, 6], [76, 7]]

[params.board_size]
curve = "step"
points = [[1, 7], [11, 8]]

[params.move_limit]          # starting guess; the tuner adjusts it
curve = "linear"
points = [[1, 30], [150, 20]]

[params.goal_amount]         # multiplier on the goal size; the tuner adjusts it
curve = "linear"
points = [[1, 1.0], [150, 1.6]]

[unlocks]
goal_collect = 11
special_line = 11
special_bomb = 21
holes = 36
special_color = 51
combo_goals = 76

[goal_weights]               # weighted pools by band; key = first level of the band
1  = { score = 1.0 }
11 = { score = 0.4, collect = 0.6 }
76 = { score = 0.3, collect = 0.4, combo = 0.3 }

[params.score_per_move]      # points per move used to size a score goal
curve = "linear"
points = [[1, 165.0], [10, 170.0], [11, 235.0], ...]     # full list in the file

[params.collect_per_move]    # tiles of ALL colors cleared per move, used to size a collect goal
curve = "linear"
points = [[1, 5.5], [10, 5.3], [11, 8.1], ...]
```

**Curve shapes** (`Cascade.Generation.Curve`):
- Each shape maps level `n` through sorted control points `(n_i, v_i)` and is constant outside them.
- Between two control points, with `t` = position in the segment from 0 to 1:
  - `linear`: interpolate with `t`.
  - `ease_out`: interpolate with `1 - (1 - t)^2` (computed as a product, not `Pow`).
  - `step`: use the value of the last point at or before `n`.

`Profile.Evaluate(n) -> LevelParams` includes `Difficulty`, `TargetWinRate`, `IsIntro`,
`IsPinch`, `IsRelease`, `Intro`, `Colors`, `BoardSize`, the `MoveLimit` guess, `GoalAmount`,
the goal pool and the unlock flags. Invalid TOML is rejected on load with a `ProfileException`
naming the key. In dev builds the profile hot-reloads: on desktop and in the Editor from the data
asset or `dev_difficulty.toml` in the user data folder, on a phone from that same file
(`Profile.ReloadIfChanged()`; an invalid edit keeps the old profile and sets `LastError`).

Clarifications (fixed behavior):
- **Pinch / release:** level `n` is a pinch when `n mod P == 0` (phase `P-1`, wave 1.0) and a
  release when `(n-1) mod P == 0` and `n > 1` (phase 0, wave `-R`). `cycle_length` must be >= 3.
- **Intro levels** are the levels equal to an `[unlocks]` value (11, 21, 36, 51, 76 by default).
  Their difficulty is `intro_level_difficulty`; `Level.Intro` names the mechanic(s) ("Collect
  goals and Line pieces" on 11). A goal-type intro forces that goal type (11: collect, 76: combo)
  and the holes intro (36) is always shaped. Color count changes (21, 51, 76) are not intros.
- **Goal pool:** the band with the largest key <= n; a kind whose unlock is not reached yet
  (`collect`, `combo`) is dropped from it.
- **Goal semantics:** a *collect* goal counts tiles of its color(s) cleared by any means (a match
  or a special's blast) and sums over its colors (up to two colors from 5 colors up). A *combo* is
  one score goal and one collect goal, both required, each sized `combo_scale` x a single goal.
- **Goal sizing:** score target = `score_per_move(n) * move_limit * goal_amount`, rounded to
  `goal_score_round`; collect target = `collect_per_move(n) * move_limit * goal_amount * k /
  colors` (k = number of goal colors), at least `collect_min`. The two rate curves are the per-move
  rates at which the bot wins a size-1.0 goal at the target win rate (`Cascade.Tools calibrate`
  prints suggestions); they jump where colors, board size or holes change what is reachable.
  The tuner then refines the result, so these only need to be close.
- **Shaped boards** (`Cascade.Generation.LevelGenerator`): from the holes unlock, with probability
  `holes_chance` (always on the intro level) one of the symmetric templates `corners(k)`,
  `cross(c)`, `ring(m)` or `diamond(e)` (every size/parameter combination that is connected and
  keeps >= `min_cells` cells) is chosen with the `layout` stream. If the final board fails
  validation, the level falls back to the plain rectangle.

## 7. Generation pipeline

```
LevelParams + seed
   ├─ 1. layout   : grid size; holes from shape templates (from the holes unlock on); gravity flow
   ├─ 2. tiles    : fill colors with no pre-made matches (tiles stream)
   ├─ 3. goals    : type from the weighted pool, size from goal_amount (goals stream)
   ├─ 4. validate : no matches, >= 1 legal move, goal colors present
   ├─ 5. playtest : bot plays bot_runs times -> win rate + winning scores
   └─ 6. tune     : if |win_rate - target| > tolerance, adjust move_limit (±1–3) or goal size
                    and go back to 5, up to tune_rounds; otherwise keep the closest candidate
                    stars thresholds = percentiles of the bot's winning scores
```

- **Bot** (`Cascade.Generation.Bot`): a greedy, one-move-lookahead player that drives the real
  `Session`. A move is valued by `Gameplay.MoveEvaluator`: swap in place, look only at the runs
  through the two cells, add one level of special blasts, swap back. Value =
  `goal_progress*bot_weight_goal + specials_made*bot_weight_special + points*bot_weight_points`
  (10, 3, 0.01). Only the chosen move is resolved in full. Ties are broken with the `bot` stream,
  and with probability `bot_random_chance` (0.25) it plays a random legal move instead, so it
  stays close to a human. Each run uses `Streams(SplitMix64(levelSeed ^ SplitMix64(runIndex + 1)))`,
  so runs differ while the initial board (the level's `tiles` stream) is the same. The same
  shallow evaluation backs `Session.Hint()`.
- **Tuner** (`Cascade.Generation.Tuner`): one measurement = `bot_runs` runs. If `|win - target| >
  win_rate_tolerance`, `move_limit` moves by `clamp(round(10 * |error|), 1, 3)` (fewer moves when
  too easy) within `move_limit_min..max`; at a bound the goals are scaled by `goal_step` per step
  instead. At most `tune_rounds` adjustments; the closest candidate is kept. `MeasuredWinRate`
  is that candidate's own measurement, so re-running the bot on the stored level reproduces it.
- **Stars:** `star2`/`star3` are the `two_star_percentile`/`three_star_percentile` of the bot's
  scores *at the moment it wins* (a level ends as soon as its goals are met, so that is the score
  a player is awarded stars on). With fewer than `stars.min_wins` wins the fallback is
  `fallback_two/three` x the median score of all runs. On score-goal levels these cluster just
  above the goal, because the level ends when the goal is reached.
- **Budget.** Measured with `Cascade.Tools report 1 100` (.NET 8 Release on the dev machine, default
  dials, `budget_ms` 1000, JIT warm-up included): mean ~24 ms, median ~15 ms, p95 ~83 ms, max ~203 ms,
  91 % of levels inside the tolerance, ~43,000 simulated bot moves per second, and no level near
  the budget (the output is identical to `budget_ms = 0`, as the levels never reach it). The
  reference phone (IL2CPP, arm64) is still to be measured in U7 before any dial is changed; the
  dials stay as they are until then.
  The budget logic: no new round or run begins after `budget_ms` has elapsed on the
  injected `IStopwatch` since generation started (never fewer than `min_bot_runs` runs); the
  closest candidate so far is kept. `budget_ms = 0` disables the cap. `bot_runs` and
  `tune_rounds` stay at 20 and 3 (with 20 runs the win-rate noise is already ~ ±0.1).
- **Determinism:** `(level number, seed, generator_version, profile, dials)` fully determine the
  level whenever the budget does not trigger, which is the normal case. The wall-clock budget is
  the one deliberate exception, so `GeneratingLevelSource` caches its last 8 levels by
  `(number, seed)` to make a retry return the identical `Level`. Tests inject a fake stopwatch
  or set `budget_ms = 0`.
- **API:** `new GeneratingLevelSource(dials, profile, stopwatch).Get(n, seed)`. The seed is
  always supplied by the caller (from `ISeedSource`); `Get(n)` without a seed does not exist.
  `LastStats` (ms, rounds, bot runs, bot moves, within_tolerance, over_budget) describes the last
  real generation. The type is the **only mutable object** in the layer and is documented as not
  thread-safe; the app calls it from one background task at a time (§12.3).

### 7.1 Planner (best line, not next move)

`Gameplay.Planner` finds the best *sequence* of moves for the current session; the dev auto-play uses it
(`Session.Hint()` stays the cheap greedy "next move" and remains a selectable mode).

- **Oracle:** `Session.Copy()` clones the board, the goal tracker *and the RNG streams*, so a copy that plays a move
  sees exactly the refills and special placements the real session will produce. The search therefore evaluates
  real outcomes, not estimates. A dev tool, not a player model: a human cannot see the refills.
- **Search:** beam search with iterative deepening. At each node the legal moves are ranked with
  `MoveEvaluator` (shallow, cheap) and only the top `planner_width` are played for real on a session copy.
  Depth grows from 1 to `planner_depth`; each depth pass may play at most `planner_max_nodes` moves
  (a node budget, not wall clock, so the result is deterministic and testable). The first depth always completes. The result of the last
  *completed* depth is used, and the deepening stops at the first depth that finds a win (deeper lines only win later).
- **Leaf value:** won = `planner_win_bonus + planner_moves_left_weight * moves_left`; lost = progress only;
  otherwise `planner_progress_weight * mean(min(1, progress_i / target_i)) + planner_score_weight * score`.
  Progress counts goal fraction, so a line that is closer to the goals beats a line with more points that
  does not move the goals. A node's value is the best of its children; the first move of the best line wins
  (ties: first in ranking order, so the plan is deterministic).
- **Receding horizon:** only the first move of the line is played, then the plan is recomputed on the new state.
- **Result:** `Plan { Moves, Value, Wins, Nodes, Depth, Probes }`. With `RecordProbes` the planner also records
  every line it evaluated at the final depth (`PlanProbe { Moves, Value }`, in search order, capped at
  `planner_max_probes`) so a view can replay the research.
- **Ghost view (dev builds):** while auto-play is in `Plan` mode the stage replays the probes as translucent
  ghost arrows over the board (one arrow per ply, numbered by ply, fading with age), one probe per
  `planner_probe_ms` for at most `planner_think_ms`, then shows the chosen line as bright arrows for
  `planner_show_ms` before the first move is swapped. Like the hint arrow it only draws (TRD §12.1).
  The ghost never touches the real session; the planner works on copies.
- **Modes:** the dev page's auto-play has a mode, `Next` (greedy `Hint()`, the default behavior before) and
  `Plan` (this section). The planner is synchronous and small (about `planner_max_nodes` resolves, a few ms);
  moving it to a background task is a later change if a profile asks for it.

## 8. Persistence

`Cascade.Persistence` reads and writes only through `IStorage` (§2.3). The Unity implementation
stores files under `Application.persistentDataPath`, with the company and product name set to
`Phoenix` and `Cascade` in Player Settings:

| File | Windows | macOS | Android |
|---|---|---|---|
| `save.json` (progress) | `%USERPROFILE%\AppData\LocalLow\Phoenix\Cascade\save.json` | `~/Library/Application Support/Phoenix/Cascade/save.json` | the app's internal storage: Player Setting **Write Access = Internal**, `.../files/save.json` |
| `settings.json` (player settings) | same folder | same folder | same folder |
| `dev_dials.toml`, `dev_difficulty.toml` overrides | same folder | same folder | same folder (dev builds only) |

The Android folder is removed on uninstall. **Auto Backup is off** (`android:allowBackup="false"`,
set by the build script's manifest step, §13.1) so a reinstall during playtesting starts clean.

```json
{ "save_version": 1, "generator_version": 1,
  "current_level": 42, "current_seed": "9182736455463728190",
  "stars": {"1": 3, "2": 2}, "adaptive_offset": -0.05 }
```

`current_seed` is a **decimal string** (a `ulong` does not survive a JSON number).

Writes are atomic: write to a temp name in the same folder, then replace. The full level is never
saved, only what is needed to regenerate it.

Behavior (`Persistence.SaveService`,
`Gameplay.PlayerProgress`):
- Loading never throws. A missing file gives defaults; an unreadable or malformed `save.json`
  (bad JSON, wrong types, unknown `save_version`) is moved to `save.json.corrupt` and defaults are
  used. `settings.json` is tolerant per field (a bad field keeps its default).
- `settings.json`: `{ "sound": true, "animation_speed": 1.0, "adaptive": true }`.
- **Seeds:** `current_seed` belongs to `current_level` only. `StartLevel` reuses it for retries
  (drawing and storing one if absent or from another `generator_version`); replaying an earlier
  level uses a fresh, unsaved seed. `RecordWin` keeps the best stars, and when the won level was
  the frontier it advances `current_level` and draws the next seed.
- Tests and portable installs inject an `IStorage`; there is no environment-variable override.

## 9. Testing strategy

### 9.1 Layers

| Layer | Tests |
|---|---|
| Core/Rng | Known-answer tests: fixed seed gives fixed first 10 outputs (the reference vectors are in `conformance/rng/`) |
| Core/Match | Hand-built boards: run 3/4/5, L, T, +, two separate groups, qualifier lists |
| Core/Rules | Superset clearing, special chains (bomb→line→color), special+special swap, cascade depth, no-match spawning |
| Core/Flow | Settle on gravity with holes; also sideways flow, a funnel link and two layers (test-only topologies) to keep the abstraction honest |
| Core/Moves | Legal-move finder; shuffle always produces a valid board |
| Core/Events | JSON round trip of every event type |
| Generation | Property tests over 300+ (level, seed) pairs in the suite (2,000 checked ad hoc): no initial matches, ≥ 1 move, goals reachable, determinism |
| Generation/Curve | Pinch levels at n ≡ 0 (mod P), release levels after them, clamp, step/linear/ease_out values |
| Config | Unknown dial rejected; override precedence (defaults < platform < dev); unknown key in a platform file rejected; change notifications; metadata present for every dial |
| Persistence | Corrupt file moved aside; atomic write leaves the old file if the write fails; seed round trip as a string |
| Presentation/Gestures | Scripted touch sequences: tap, double-tap, drag into inner zone, drag cancelled by a second finger, pinch, two-finger pan, slop thresholds from dials |
| Presentation/Timeline | Golden event logs in, expected timeline (start, duration, target) out; the sync-to-final-board rule |
| Presentation/InputStateMachine | The PRD §2.2 table, row by row |
| Architecture | A test that loads each logic assembly and fails if it references `UnityEngine`, `System.IO`, `System.Random` or `System.DateTime` outside the allowed files |
| Android (manual) | The on-device checklist in §13.11, run on the reference device and the emulator for every shared build |

All logic tests are NUnit tests in `unity/Assets/Cascade/Logic/Tests/`. Two runners
compile the **same files**: `dotnet test tools/Cascade.Tests.Headless` (seconds, CI, no Unity
license) and Unity's EditMode runner (checks the real Unity compiler and settings). The
Conformance tests (below) are also built into a **Player test build** that runs on the Android
device, because IL2CPP and ARM64 are where floating-point and stripping surprises show up.

### 9.2 Conformance suite

`conformance/` holds the reference fixtures (U0). A C# build is
**conforming** when every fixture below passes, and no fixture is regenerated from C#.

| Fixture | Compares | Must match |
|---|---|---|
| `rng/vectors.json` | PCG32 reference vectors; `Substream` outputs for 20 `(seed, name)` pairs | exactly |
| `golden/session_*.json` | scripted sessions: initial board, swaps, resulting event list | byte-for-byte after canonical JSON |
| `profiles/evaluations.json` | `Profile.Evaluate(n)` for n = 1..150 | every field; doubles compared as `==` first, then within 1e-12 and reported (one known case: n = 134 `difficulty` differs by one ulp from the exact `(1 - t) * (1 - t)` product §6 specifies) |
| `levels/*.json` | `Level` for a reference set of `(n, seed)` (every intro level, every pinch/release phase, shaped boards, n up to 150) | exactly, including `MeasuredWinRate` and star thresholds |
| `bot/*.json` | per-run win flag and final score for 20 bot runs on 10 levels | exactly |

If a fixture differs, the difference is **investigated**, not re-exported: it is either a code bug
or an undocumented behavior of the reference, in which case the TRD is corrected first (§4.3 lists
the usual causes). Fixtures change only when the TRD deliberately changes a rule, bumping
`GENERATOR_VERSION` if generation output moves.

Cross-CPU float differences are the one accepted gap: a level generated on an arm64 phone may
differ from the same `(n, seed)` on x86_64 if a `Pow` rounds differently and flips a threshold
(§4.3). The conformance run on the device measures how often (expected: never on the reference
set), and saves keep only the seed, never the level.

## 10. Milestones and done criteria

Each milestone is **done** when everything in its "Done when" column is true. That tells an
implementer (human or model) exactly when to stop and hand back for review. Milestones use the
**U** series.

| # | Milestone | Done when |
|---|---|---|
| U0 | **Conformance fixtures** | `conformance/` holds the §9.2 fixtures; `Level` and `Profile` JSON dumps exist; `.gitignore` is set up for Unity and `Builds/` (§14); ADR-006 and ADR-007 are Accepted |
| U1 | **Walking skeleton on a phone** | The Unity 6 project exists with the §2 assemblies (empty but compiling, `noEngineReferences` on); `BuildScript` produces a signed APK in `Builds/Android/` from the command line; the APK installs on the reference device and the emulator and shows a UI Toolkit screen; the architecture test (§9.1) passes. *This de-risks Android before any rules are built.* |
| U2 | **Core** | `Cascade.Core`, `Cascade.Config` and `Cascade.Persistence` are implemented per §3-§8; every `rng/` and `golden/` fixture passes under `dotnet test`, Unity EditMode **and** the Player test build on the reference device; the Core test list in §9.1 passes |
| U3 | **Gameplay and generation** | `Gameplay` and `Generation` are implemented; `profiles/`, `levels/` and `bot/` fixtures pass on all three runners; the difficulty report (`Cascade.Tools report 1..100`) runs within `generator.budget_ms`, and the measured times are recorded (§7); the 10,000-level deadlock check (MRD §5) runs clean |
| U4 | **Playable in Unity** | `Cascade.Presentation` and `Cascade.Unity.View` draw a generated level with placeholder 3D pieces in K colors with distinct silhouettes; press/drag/click input exactly per PRD §2.2 (mouse, in the Editor and a Windows build); every event animates through the `TimelinePlanner`/`TimelinePlayer` split; HUD shows moves and score; auto-fit, wheel zoom and pan work |
| U5 | **Dials, dev page, game shell** | All §5 dials load from TOML and the generated dev page edits them live; UXML screens for title, map, intro, play (HUD and pause), result and settings; save and load through `FileStorage`; retry reuses the seed; stars shown; wide and tall layouts switch by aspect ratio |
| U6 | **K: Pebbles art pass** (done on desktop and the emulator; the S23 check is open) | Pieces, tray, lighting and HUD match the [K style doc](art/STYLE-K-Pebbles.md) and its animation table, on desktop and on the reference device within the §13.9 frame-rate target |
| U7 | **Android complete** (touch input, rotation and portrait/landscape, including switching during play, confirmed on the S23 on 2026-10-05; the rest of §13.11, the frame rate and the generation budget are still open) | The full flow works with touch alone per PRD §2.2, including pinch, two-finger pan, double-tap reset and Back; portrait and landscape both lay out inside the safe area; the §13.11 checklist passes (background/resume, rotation, process kill, save survives); the game holds the display refresh rate on the reference device; levels 1-100 generate within the Android `generator.budget_ms`; desktop builds, the dev page and all tests still pass; APK and desktop build come from the same revision |
| U8 | Stretch | Adaptive difficulty, background pre-generation of level N+1 (required on Android by U7 already), polish (sound: §12.4, done early) |
| U9 | Topology mechanics | Extra sources, sideways flow, funnels, overlapping and multi-layer boards, each as a preset plus a profile unlock |

## 11. Future: level baking (deferred)

Not in v1. If levels are later baked on first encounter for replay and sharing:
- Store the serialised `Level` (not just the seed), keyed by `level_number`, so the stored level
  survives generator and profile changes.
- Players request level N from a shared store; if it is missing, generate, validate, then publish.
- The v1 design keeps this cheap: generation is pure and seeded, `Level` is a plain immutable
  record with a JSON form (already used by the conformance fixtures), and the game only talks to
  an `ILevelSource` interface (`Get(levelNumber, seed) -> Level`).

## 12. Presentation

Presentation is split in two on purpose ([ADR-007](adr/ADR-007-layers-and-ports.md)): what is
**decided** lives in `Cascade.Presentation` (pure, unit-tested, reusable on any engine), what is
**drawn** lives in `Cascade.Unity.View`.

### 12.1 3D board and pieces
- **Placeholder pieces (U4)** are meshes generated by an Editor tool (`Cascade.Editor`) from 2D
  outlines extruded and rounded: each color is a distinct silhouette from the K doc (oval, leaf,
  rounded square, capsule, disc, scallop, kidney). Final meshes are authored in Blender, exported
  as glTF 2.0 and imported with glTFast; swapping placeholder for final art is replacing assets
  referenced by the style, not code.
- **Style** is a `PieceStyle` ScriptableObject (`Assets/Cascade/Styles/`): the palette, the
  materials, the light rig and camera defaults, the tray and HUD theme colors. Another style is
  another asset. The clay look is a **hand-written URP HLSL shader** `Cascade/Clay` (base color,
  fleck texture, sheen, rim light, the K sun + ambient rig), not Shader Graph: Shader Graph cannot
  be authored from code, and the Editor builder has to be re-runnable and diffable. It is SRP
  Batcher compatible; per-piece color, brightness and fleck offset come from a small pooled
  material set (`PieceMaterials`, brightness and fleck offset are quantised by style dials), not
  from `MaterialPropertyBlock`. The grain is URP's `FullScreenPassRendererFeature` with the
  `Cascade/Grain` material (`material.grain_alpha`, settable at runtime); both run on mobile GPUs,
  so there is no fixed-function path. Shaders are reached through the style's materials,
  so they ship in a build as long as the style asset is referenced.
- **Soft shadows only:** a contact shadow is a blob quad (radial alpha texture, unlit) under each
  piece; there are no shadow maps. The tray (`BoardTray`) is one mesh rebuilt for any cell set:
  top quads, a rounded lip swept along every boundary loop, and a dark floor in enclosed holes.
- **Specials are overlays** on any base pebble: a quartz vein (draped capsule, horizontal or
  vertical mesh), a hag-stone hole (rim torus + dark disc) and a banded agate (a thin shell over
  the pebble top whose bands follow the silhouette). They are generated per silhouette so they
  lie on the surface of every shape.
- **Per-instance variation** (yaw, squash, brightness, fleck offset) comes from
  `Presentation.PieceVariation`, a pure function of the tile id and the `[pieces]` dials.
- `BoardView` keeps a `tile_id -> PieceView` map: `Spawned` creates a piece above the source,
  `Moved` moves it along its path, `Cleared` plays the style's clear effect and then returns the
  piece to a pool.
- **Animation:** `TimelinePlanner` turns an event list into a flat `Timeline` of `Tween`s
  (target tile id or UI id, property, from, to, start ms, duration ms, easing). It has no engine
  types, so it is tested with the golden event logs. `TimelinePlayer` (Unity) applies the timeline
  at time *t* each frame; because it only evaluates a timeline at a time, pause, resume and the
  speed setting are trivial and no third-party tweening library is used. Durations come from
  `[animation]` × `settings.animation_speed`. The view never changes logic state.
- **Rendering:** URP with MSAA from `render.multisamples`, the frame rate from `render.target_fps`
  (§13.9). No optimisation work until something is measured as slow.

**As built in U4:** `BoardView` (tray, pieces, swap arrow, `Sync`, `Verify`), `TimelinePlayer` (runs a move timeline and small
pose timelines side by side, one `TimelineSampler` each; effects through `FxPresenter`; score popups are UI Toolkit labels
styled by `.popup` in `Hud.uss`, positioned with `BoardCameraRig.WorldToPanel`), `BoardController` (the glue: `InputStateMachine`,
`SwapQueue`, `Session.TrySwap`, `TimelinePlanner`) and `MouseAdapter` (device to pointer calls only). `BoardCameraRig` reads the
size it renders to from `ScreenSize`, which tests override to render offscreen. The composition root is `AppBootstrap`; level
generation runs on `Awaitable.BackgroundThreadAsync`. The view does not need a uGUI `EventSystem`: UI Toolkit reads the Input System itself.

**As built in U6:** see [STYLE-K-Pebbles §10](art/STYLE-K-Pebbles.md). Added to the view: the board **slot clip** (the
clay, tray and fx shaders discard fragments outside the BoardSlot, from `_CascadeClip` / `_CascadeClipVP` published by
`BoardCameraRig`; style flag `camera.clipToSlot`), `TimelinePlayer.ManualClock` (tools and tests advance the clock by hand to render
deterministic frame sequences), the dev-only `FpsCounter` (`-showfps`, "Show FPS" on the dev page, `[fps]` log lines every 5 s) and
`Editor/ShaderChecks` (`Cascade/Check Shaders`: compile, support and the UnityPerMaterial check behind SRP Batcher compatibility).
`UnityHost.ApplyFrameRate` sets `Application.targetFrameRate` from `render.target_fps` (0 = display rate) and `vSyncCount` 0 on mobile.
Measured on the emulator only (60 Hz, software GL): 49 to 59 fps average in 5 s windows during autoplayed cascades, 60 on the menus;
the S23 120 fps check (§13.10) is still to do on the device.

### 12.2 UI, layout, camera and zoom
- **Screens and HUD are UI Toolkit assets** (`Assets/Cascade/UI/*.uxml`, `*.uss`). Moving the score, the goals or the board is editing UXML/USS
  (flex layout, anchors by `position: absolute` and percentage offsets), with the Editor's **UI
  Builder** as the interactive layout editor. There is no on-device layout edit mode:
  edit in the Editor and see the change live in Play mode.
- **Panel scale:** the panel scales with the screen, reference 1080x1080, and `UiRoot` sets `match` per layout: a wide layout is 1080 panel units tall, a tall one 1080 units wide, so USS sizes mean the same on every device of that shape.
- **Shell structure:** one document (`Shell.uxml`) holds a `safe` element (inset by the safe area, so Android cutouts never overlap a page) with one child per page (`Title`, `Map`, `Loading`, `Intro`, `Hud` = the Play page with its pause overlay, `Result`, `Settings`) and the dev overlay. `UiRoot` shows exactly the page `ScreenFlow` says, with a fade whose time is a USS transition. The Unity camera stays on in every page because it draws the sand backdrop; the board, rig and pointer adapter (`PlayStage`) are active only during Play. The pause overlay offers Resume and Quit to map (the flow has no Settings from Play).
- **Start-up options** (`BootOptions`): `-level N -seed S -autoplay K`, `-screen`, `-datadir`, `-shot`. Tests inject the data folder, host and job runner through `BootOptions.Pending`.
- **Wide and tall layouts** are one UXML with a root USS class, `layout--wide` or `layout--tall`,
  chosen by `ScreenFlow` from the aspect ratio of the screen (closest to 1:1 wins). A phone in
  portrait uses tall; landscape and tablets use whichever is closer.
- **Safe area:** `Unity.Platform` publishes `Screen.safeArea` (cutouts, system bars, gesture
  area) through `IAppLifecycle.ScreenChanged`; the view applies it as padding on the root element,
  so every anchor is measured from the safe area (PRD §5.3).
- **Touch targets:** a USS rule gives every button a `min-width/min-height`; `StyleApplier`
  converts `input.min_touch_target_mm` to pixels with `HostInfo.ScreenDpi` and sets it on touch
  devices.
- **Board slot:** the layout contains an empty element named `BoardSlot`. Its resolved rectangle
  is what `CameraFit` fits the board into (a screen-space rectangle in, a camera distance and
  offset out). It is recomputed when the slot's geometry changes.
- **Dev settings page:** generated from the dial metadata (§5): one foldout per section, one
  field per dial, live apply, Save writes `dev_dials.toml`, Reset restores defaults; plus the
  level's seed, `difficulty(n)`, target and measured win rate, and a "jump to level N" field
  (PRD §5.2). Compiled only into Editor and Development builds: the scripting condition is `UNITY_EDITOR || DEVELOPMENT_BUILD || CASCADE_DEV` (`CASCADE_DEV` forces it on elsewhere).
- **Board camera** (`Presentation.CameraFit`, pure math; `Unity.View` applies the result):
  - **Auto-fit:** compute the board's projected bounds and set the camera distance so the board
    fills the slot minus `fit_margin`.
  - **Zoom:** wheel (zoom toward the cursor by `zoom_step`) or pinch (around the finger midpoint),
    clamped to `zoom_min..zoom_max` relative to auto-fit.
  - **Pan:** right- or middle-drag, or two-finger drag, clamped so the board stays partly visible.
  - **Reset:** double-click or double-tap on empty space resets to auto-fit. A size change re-fits
    unless the user has zoomed.
- **Picking:** a ray from the pointer to the board plane gives a cell plus the position within
  that cell, computed by `CameraFit`'s inverse projection (pure). The input state machine
  (PRD §2.2) uses that position to test the `drag_inner_fraction` zone.

### 12.3 Fixed behavior
- **Dev mode:** F1 opens the dev settings page in Editor and Development desktop builds; on a
  phone a three-finger tap does (§13.8). Without that condition (a release player) neither exists, and the dev overlay is removed from the UI tree.
- **Animation speed:** the player setting `animation_speed` divides every duration and the fall
  speed multiplies by it (2.0 = twice as fast). Range 0.5-3.0.
- **Timeline:** `TimelinePlanner` lays the events out on one timeline. Swaps run first; wave 0 of
  each cascade round starts when everything before it has landed; later waves start
  `special_fire × 0.55 + wave_stagger` after the previous wave; falls and spawns start when the
  clears of that round finish. Pieces spawning into the same column start one cell higher each so
  they never overlap. When the timeline ends the scene is synced to the final board, so a bug in
  an animation can never leave the view out of step with the logic.
- **Input while animating:** selecting is allowed at any time (the selection uses the final
  board); a swap requested while the board is animating is queued (one slot, last one wins) and
  played when the board has settled.
- **Level generation** runs on a background thread (`Awaitable.BackgroundThreadAsync`) behind a
  "Preparing level N" screen, and the next level is pre-generated while the result screen shows
  (§7). Logic layers have no static state and no Unity calls, so this is safe; results are
  handed back to the main thread through the awaiter.
- **Stars** are shown on the result screen and the map. On a score-goal level the level ends as
  soon as the goal is met, so 2 and 3 stars sit just above the goal (open design question: play
  on until moves run out).
- **Settings:** `adaptive` is stored but has no effect until U8. `sound` mutes audio (§12.4).

### 12.5 Combo celebration (PRD 5.4.1)
Pure plan, drawn by the view, driven by `CelebrationParams` (the `[celebration]` dials; it rides on
`TimingParams.Celebration` so live dial and animation-speed changes reach the planner as before).
- For each `Scored` with `multiplier > 1`, `TimelinePlanner` adds a `ShowFx` of kind `ComboBadge` (text
  `x2.6`, tier, size `badge_scale_base + badge_scale_step × tier`) over that group's cells. The k-th
  `Scored` of a round maps to the k-th `Matched`'s center; the bonus events use the last group's center.
  Successive groups of one round start `group_stagger_ms` apart.
- From tier 1 (unless `reduce_effects`): a `Burst` fx (`particles_per_tier × tier` particles) and a
  `CameraPunch` instant (`camera_punch_per_tier × tier`, over half the badge time). From
  `stinger_from_tier`: `PlaySound(Stinger, tier)`.
- `ScoreApplied` carries the multiplier. The composition root forwards it to `HudViewModel.SetCombo`,
  which shows the highest multiplier of the move next to the score until `Sync` (move settled) resets it.
- View: `FxPresenter` draws the badge and the burst as UI Toolkit elements on the popup layer
  (`.combo-badge--tierN`, `.spark`), `BoardCameraRig.Punch` zooms the field of view in and back, and
  `SoundPlayer` synthesizes the stinger.

### 12.4 Audio

Sound follows the same split as animation: the plan is pure, the playing is the view's.
- `TimelinePlanner` emits `PlaySound(cue, depth, startMs)` instants next to the animation they belong
  to. `SoundCue` is an enum: `Swap`, `Reject`, `Clear`, `Land`, `SpecialCreate`, `LineFire`,
  `BombFire`, `ColorFire`, `Shuffle`, `Win`, `Lose`, `Stinger`. `Clear` carries the cascade depth (0 =
  the player's move); the player raises its pitch by `[audio] cascade_pitch_semitones` per depth, so a
  chain audibly climbs. `Stinger` carries the celebration tier (§12.5) and rises two semitones per
  tier above 1. Cues are divided by the animation speed like every other time.
- `Unity.View.SoundPlayer` plays cues. Placeholder clips are **synthesized at startup** (short
  enveloped tones and noise, no asset files), so audio needs no import pipeline until real
  sound design replaces them; a cue name maps to a clip, and a clip asset may later override the
  synthesized one with no change to the planner.
- `settings.sound` off means no cue is started (and none is queued). `[audio] master_volume` scales
  every cue; `cue_cooldown_ms` stops the same cue from stacking. Both apply live.
- The result page cue (`Win`, `Lose`) is played by the composition root when the result is shown.
- No music in v1.

## 13. Android and platform builds

Android is a first-class target ([ADR-006](adr/ADR-006-unity-engine.md)); Windows and macOS
standalone builds come from the same project and revision. There is **no separate mobile
codebase**: the only per-platform code is in `Cascade.Unity.Platform` and the platform dial files.

### 13.1 Toolchain and build
- **Editor:** the Unity 6 version in `ProjectVersion.txt`, with the Android Build Support module
  (OpenJDK, Android SDK and NDK from Unity Hub). On this machine the standalone SDK at
  `%LOCALAPPDATA%\Android\Sdk` is used for `adb` and the emulator only; the build uses Unity's
  bundled tools so the versions match.
- **Player settings (Android):** IL2CPP; architecture **ARM64 only** (the 6000.6 Android player has
  no x86_64 libraries, so the emulator runs the APK through its ARM translation, §13.10); build target **APK**, not an App Bundle (Unity writes a sideloadable APK directly,
  so neither `bundletool` nor `build_apps` exist in this pipeline); graphics APIs Vulkan then
  OpenGL ES 3 with automatic fallback; orientation **Auto Rotation** (portrait and landscape);
  Write Access **Internal**; Optimized Frame Pacing on; Managed Stripping Level **Medium** with a
  `link.xml` that preserves Tomlyn and Newtonsoft types used by the data layer.
- **`Cascade.Editor.BuildScript`** is the one way to build. From the repo root:
  `Unity -batchmode -nographics -quit -projectPath unity -buildTarget Android -executeMethod
  Cascade.Editor.BuildScript.BuildAndroid [-dev] -logFile Builds/build.log`. It writes
  `Builds/Android/cascade-<version>-<git short hash>[-dev].apk`. `-install` also runs
  `adb install -r`. `BuildWindows` and `BuildMac` write to `Builds/Windows/` and `Builds/Mac/`.
  The script sets every setting above itself, so a fresh checkout builds the same on any machine.
- **Signing:** playtest builds are signed with a keystore that is never in the repo. The build
  script reads `CASCADE_KEYSTORE`, `CASCADE_KEYSTORE_PASS`, `CASCADE_KEY_ALIAS` and
  `CASCADE_KEY_PASS` from the environment and sets them on the Player Settings **in memory
  only**, so they are not saved into `ProjectSettings`. Release signing is out of scope (PRD §7).
- **Application ID:** `com.phoenix.cascade` for playtest builds and `com.phoenix.cascade.dev` for
  dev builds (§13.8), so both can be installed side by side and a dev build never touches a
  playtester's save. The ID is fixed: changing it later is a different app to Android and loses
  saves.
- **Manifest:** min SDK 29 (Android 10); target SDK is whatever Google Play currently requires,
  recorded as a constant in `BuildScript` (Unity 6 lets it be set explicitly); `allowBackup` is
  `false` and no permissions are requested. These are applied by a Gradle/manifest template that
  `BuildScript` keeps in sync.
- **Build host:** the Windows dev machine. Output is only ever written to `Builds/`.

### 13.2 What the U1 skeleton must prove
Because the engine is now a mainstream one, there is no research spike. The U1 skeleton still
answers four **cheap-to-check** questions on the reference device and the emulator, and records
them in [ADR-006](adr/ADR-006-unity-engine.md):
1. **Build and install:** the command-line build produces an APK that installs and starts.
2. **IL2CPP and data:** a `DataCatalog` TOML asset parses with Tomlyn and a Newtonsoft round trip
   works in a stripped IL2CPP build.
3. **Determinism on device:** a Player test run of the `rng/` and `golden/` fixtures passes on
   ARM64 (it will be extended each milestone).
4. **Frame rate:** an empty URP scene reaches the display refresh rate once `target_fps` is set
   (§13.9).

### 13.3 Host (`UnityHost`)
One place answers "where am I running?" so nothing else checks `Application.platform`:

```csharp
public sealed record HostInfo(
    PlatformKind Kind,      // Desktop | Android (Ios later)
    bool HasMouse, bool HasKeyboard, bool HasTouch,
    bool Dev,               // Editor or Development build; see 13.8
    float ScreenDpi);
```

`UnityHost.Detect()` builds it from `Application.platform`, `Input System` devices and
`Screen.dpi`. The composition root passes the `Kind` to `DialRegistry` (platform dial file) and the
`Has*` flags to the input adapters.

### 13.4 Data (`DataCatalog`)
Shipped data files are not read from `StreamingAssets` (on Android that is inside the APK and needs
a web request). A small `ScriptedImporter` imports every `.toml` as a `TextAsset`, and a
`DataCatalog` ScriptableObject lists them by key. `DataCatalog` implements `IDataSource`, so
`dials.toml`, `dials.android.toml` and `difficulty.toml` load the same way in the Editor, on a
phone and in a test. There is no `resource_path`, and no code builds a file path to shipped data.

### 13.5 Touch input (`Presentation.GestureRecognizer`, `Unity.View.TouchAdapter`)
- `GestureRecognizer` is pure: it takes timestamped pointer down/move/up events (id, x, y in
  pixels) plus the screen DPI, and emits `Tap`, `DoubleTap`, `DragStart/DragMove/DragEnd`,
  `PinchZoom(factor, center)`, `Pan(dx, dy)` and `Cancel`. Thresholds come from the `[input]`
  touch dials, converted from mm with the DPI.
- `TouchAdapter` reads Input System `EnhancedTouch` (`Touch.activeTouches`) and feeds the
  recognizer. Single-finger events go to the **same** `InputStateMachine` as the mouse (with
  `touch_inner_fraction` instead of `drag_inner_fraction`); pinch, pan and double-tap go to
  `CameraFit`, the same code paths as wheel zoom, mouse pan and double-click.
- A `MouseAdapter` feeds the same machine on desktop. Neither adapter contains a rule.
- No hover on touch: the arrow (PRD §2.2) is driven by the drag position only.
- Back is an Input System action bound to the Android Back key (surfaced as Escape by the Input
  System; verified on device in U1/U7) and to Esc on desktop. Both raise `BackPressed`, which each
  screen in `ScreenFlow` handles.

### 13.6 Lifecycle (`LifecycleBridge`)
- Unity has **no resize event on mobile**: `LifecycleBridge` polls screen size, orientation and
  safe area each frame and raises `ScreenChanged` when any of them changes. It also raises
  `Paused`/`Resumed` from `OnApplicationPause`/`OnApplicationFocus`. It never relies on
  `OnApplicationQuit` (Android does not call it reliably).
- **On pause** (app to background, screen off): write `save.json` and `settings.json` (atomic,
  §8) and freeze the `TimelinePlayer` clock.
- **On resume:** unfreeze and continue the timeline. Unity restores GPU resources after a context
  loss itself; the §13.11 checklist verifies pieces and materials after resume.
- **Process killed while in a level:** on the next launch the player is on the map with the same
  `current_level` and `current_seed`, so starting the level again gives the same board (the same
  rule as retry, §8). A level in progress is not saved move by move in v1.
- Level generation in progress is simply restarted after a kill; it is deterministic.

### 13.7 Layout on phones
See §12.2: the same UXML/USS, `layout--tall` by aspect ratio, safe-area padding from the platform,
a `min-height` per button from the DPI. The tall layout is reworked so a 9×9 board uses the full
safe width (PRD §5.3). Rotation and split-screen arrive as `ScreenChanged` and use the normal
reflow and re-fit path.

### 13.8 Dev builds
- `Dev` is true in the Editor and in Unity **Development Builds**; `BuildScript -dev` makes one and
  sets the `.dev` application ID. There is no command line on a phone, so nothing else is needed.
- In a dev build a **three-finger tap** opens the dev settings page (F1 on desktop).
- Dev overrides (`dev_dials.toml`, `dev_difficulty.toml`) live in the user data folder (§8).
- Dev auto-play (F2, dev page) has two modes, `Next` and `Plan` (§7.1); F3 switches mode.
- The text view is a desktop tool: `Cascade.Tools text` (§14).

### 13.9 Performance
- Target: the display refresh rate during play, including a large cascade. **Unity on Android caps
  at 30 fps unless told otherwise**, so `target_fps = 0` makes `UnityHost` set
  `Application.targetFrameRate` to the display's refresh rate at startup (and `vSyncCount` to
  0 on mobile, where vsync is the compositor's). No optimisation work before the device
  measurement says it is needed (PRD §5.4).
- Levers, all dials overridable in `dials.android.toml`: `render.multisamples`, `render.target_fps`,
  `generator.budget_ms`, `generator.bot_runs`, `generator.min_bot_runs`. Generator dials change how
  a level is tuned (move limit, goal size), so a level can differ between desktop and Android for
  the same `(n, seed)`; that is accepted in v1 (no cross-device level sharing).
- Background generation of the next level during the result screen (§12.3) is required on
  Android, not optional.

### 13.10 Devices
- **Reference device:** **Samsung Galaxy S23** (Snapdragon 8 Gen 2, 6.1" 1080×2340, 120 Hz
  adaptive display, center punch-hole camera). All §10 performance checks use it with the
  display on its default "Adaptive" motion-smoothness setting: the game must never drop below
  60 fps and should reach 120 fps. The S23 is a high-end phone, so passing on it does not prove
  mid-range performance (PRD §6); a mid-range device is added before wider playtesting.
- **Emulator:** an x86_64 `google_apis` system image at **API 30 or newer** (API 36 is used; it
  ships the ARM translation layer the arm64-only APK needs; the API 29 image cannot run it, so the
  min SDK is verified on a device). **Vulkan segfaults under that translation** (verified in U1), so
  on the emulator launch with GLES: `adb shell am start -n com.phoenix.cascade.dev/com.unity3d.player.UnityPlayerGameActivity -e unity -force-gles`.
  Real devices use Vulkan. Run `adb kill-server` after a Unity batch build if `adb` reports the
  device offline (Unity restarts its own adb daemon).
- **Tablet:** any Android tablet for the landscape and large-screen layout check.

### 13.11 On-device checklist (manual, every shared build)
1. Install over a previous build: progress and settings are kept.
2. Title, map, intro, play, result and settings all work by touch; there is no Quit button.
3. Swap by drag and by tap-tap; a drag released at a neighbor's edge does not swap; a second
   finger during a drag cancels it.
4. Pinch zooms, two-finger drag pans, double-tap on empty space resets.
5. Back pauses during play and goes back one screen elsewhere; on the title it leaves the app.
6. Rotate during a cascade: layout reflows, the board re-fits, the animation finishes correctly.
7. Home during a cascade, wait, return: the same state, nothing lost or replayed; materials and
   meshes intact after resume.
8. Kill the app from recents during a level, relaunch: map at the same level; starting it gives
   the same board.
9. Nothing is under the cutout or the gesture bar in either orientation.
10. A 9×9 level in portrait: every cell can be tapped accurately without zooming.
11. The Player conformance test run (§9.1) passes on this device.

## 14. Dev environment

Tools: Unity Hub with the Unity 6 version from `ProjectVersion.txt` (modules: Android Build
Support with OpenJDK/SDK/NDK, Windows Build Support (IL2CPP)); .NET 8 SDK for `tools/`; Android
platform-tools (`adb`) on PATH; a code editor with Unity support.

```powershell
# fast loop: logic tests and tools, no Unity needed
dotnet test tools/Cascade.Tests.Headless
dotnet run --project tools/Cascade.Tools -- report 1 100        # difficulty report (CSV)
dotnet run --project tools/Cascade.Tools -- calibrate           # suggested rate curves
dotnet run --project tools/Cascade.Tools -- text                # terminal view of the rules
dotnet run --project tools/Cascade.Tools -- verify conformance  # §9.2 fixtures

# Unity: open unity/ in the Hub; run tests in Window > General > Test Runner, or:
Unity -batchmode -nographics -projectPath unity -runTests -testPlatform EditMode -testResults Builds/editmode.xml

# builds (all output under Builds/)
Unity -batchmode -nographics -quit -projectPath unity -buildTarget Android `
  -executeMethod Cascade.Editor.BuildScript.BuildAndroid -dev -install -logFile Builds/build.log
```

**Ignore file.** The repo's ignore file excludes everything generated, and `Builds/` is the one
folder all build artifacts go to:

```
/Builds/
/unity/Library/
/unity/Temp/
/unity/Logs/
/unity/UserSettings/
/unity/obj/
/unity/Build/
*.csproj
*.sln
*.keystore
*.apk
*.aab
```

(`Packages/manifest.json`, `packages-lock.json` and `ProjectSettings/` are committed; `.meta`
files are committed.)
