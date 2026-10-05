# Cascade: Product Requirements Document (PRD)

| | |
|---|---|
| Status | Draft v0.3 |
| Date | 2026-10-04 |
| Related | [MRD](MRD.md) · [TRD](TRD.md) · [Art styles](art/README.md): [I](art/STYLE-I-Claymation.md) · [J](art/STYLE-J-StudioSet.md) · **[K (v1)](art/STYLE-K-Pebbles.md)** |

## 1. Product summary

Cascade is a single-player match-3 puzzle game. The player swaps adjacent pieces to line up 3 or
more of the same color, which clears them. Each level has a goal and a move limit. **Levels are
generated when the player reaches them**, from a difficulty profile that maps level number to
generation parameters. Designers change the game's progression by editing that profile.

**Design principle: skill over luck.** The player should earn their matches. New pieces never
arrive already forming a match, and big chain reactions come from the player setting them up.

**Design principle: dials, not constants.** Every number that affects feel or balance (scoring,
timings, input thresholds, difficulty curve) is a named dial in a data file. All dials can be
edited live on a dev settings page (§5.2) and none is hard-coded.

**Platforms:** desktop (Windows, macOS) and **Android** phones and tablets (§6), from one
codebase. Every rule, screen and dial is the same on both; only input (§2.2) and a few layout
details (§5.3) adapt to the device.

## 2. Core gameplay

### 2.1 Board and pieces
- v1 boards are rectangular grids, default 8×8 (range 6×6 to 9×9).
- **Shaped boards (later band):** some cells can be cut out of the rectangle so the board forms a
  cross, a heart, a ring and so on. A cut-out cell is a **hole**: no piece can sit there and
  nothing can match through it. Pieces fall straight down past holes.
- 4–7 piece colors depending on difficulty. More colors means fewer matches, so a harder level.
- **Tile flow:** cleared pieces are removed and the board refills. In v1 this is classic gravity
  (pieces above fall down, and new pieces enter at the top of each column). Gravity is only the
  *default flow*, not a rule built into the game; see §2.6.
- **Refill never creates matches.** The color of each new piece is chosen so that it does not
  form a match where it lands. Existing pieces that fall can still line up and match. That is
  the consequence of the player's move, so it counts as a cascade (§2.3).

### 2.2 Moves and input
- The player swaps two neighboring pieces (orthogonal in v1).
- A swap is legal only if it creates at least one match, or if both pieces are special pieces
  (§2.4). An illegal swap animates the attempt, bounces back and does not use a move.
- **Input** works the same with a mouse or a finger. The table below is written for a pointer;
  on touch, "press" is a finger down, "release" is a finger up and "click" is a tap. The rules:

| Action | Result |
|---|---|
| Press on a piece | That piece is selected **immediately** and visibly (lift and glow) |
| Drag while holding | Nothing changes until the pointer is **well inside** a neighboring piece. That means its *inner zone*: the middle part of the piece, by default the central 60% of the cell (dial `input.drag_inner_fraction`) |
| Pointer enters a neighbor's inner zone | An arrow appears from the selected piece to that neighbor. It disappears if the pointer leaves the inner zone |
| Release with the arrow showing | Swap the two pieces |
| Release without the arrow (including barely inside a neighbor's edge) | Counts as a click on the original piece: it stays selected |
| Click a neighbor while a piece is selected | Swap |
| Click any other piece | Move the selection to that piece |
| Click the selected piece again | Deselect |

- Highlight, arrow and hover update every frame for smooth, responsive feedback.
- **Touch specifics** (Android):
  - Only the first finger on the board drives the press/drag/click rules. A second finger
    cancels any drag in progress (no swap; the piece stays selected) and starts a view gesture.
  - **Pinch** zooms the board around the midpoint of the two fingers; **two-finger drag** pans;
    **double-tap** on empty space resets to auto-fit (§5.3).
  - A finger hides what is under it, so the selected piece and the swap arrow must stay
    readable around the fingertip (the lift and glow extend past the piece's edge).
  - The Android **Back** gesture/button does what Esc does on desktop: pause during play, and
    go back one screen elsewhere. Back on the title screen leaves the app.
  - Touch thresholds (inner zone, double-tap time, movement allowed in a tap) are their own
    dials, separate from the mouse ones, because fingers are less precise than a cursor.
- Input is accepted while animations play; a new swap waits until the board has settled.

### 2.3 Matches and resolution
A **match** is 3 or more same-colored pieces in a line. When one move produces several matches
that touch or overlap, they form one **match group**.

- **Every qualifier scores.** Within a match group, each pattern it qualifies for scores in
  full: each straight run, each special-making shape, and each intersection. A cross-shaped group
  scores as both of its runs *and* as a Bomb shape.
- **Clear the superset.** Every piece that belongs to any qualifier in the group is cleared once.
- **Cascades:** after the board refills, any new matches resolve the same way, with a cascade
  multiplier that grows each round.

#### Combo multiplier
Combining matches is the main source of big scores, and the player must *see* it. One **combo
multiplier** applies to every match group's points, and it rises in two ways within one move:
- **Simultaneous groups:** when one resolution round produces several separate match groups
  (for example two matches on different axes created by the same swap), each further group
  scores at a higher multiplier than the one before. Groups are taken in a fixed order (by their
  first cell) so the result is deterministic.
- **Cascade rounds:** each round after the first, when pieces falling or entering into the
  cleared space form new matches, raises the multiplier for everything in that round.

`multiplier = 1 + cascade_step × round + group_step × (groups already scored this round)`,
where `round` starts at 0 for the player's own match. Both steps are dials. The multiplier is
shown and celebrated (§5.4.1), never applied silently.

**Later: lingering multiplier (not in v1).** The multiplier may stay alive for a dial-set time
(ms) after the move settles, so the player can race to use it on the next move; it then decays.
The rules must not prevent it: the combo level is a value the next move *starts from* (default
0), supplied by the session, so the rules stay deterministic and need no clock (TRD §3.1).

| Pattern | Qualifies for |
|---|---|
| 3 in a line | Clear + points |
| 4 in a line | **Line piece** |
| Two runs crossing or touching at an end (L, T, + shapes) | **Bomb piece** |
| 5+ in a line | **Color piece** |

### 2.4 Special pieces
- **Earned specials are not placed at the match.** Each special earned by a group is given to a
  randomly chosen piece. That can be a normal piece already on the board, or one of the new pieces
  about to drop in. The player has to spot where it went and plan around it. The odds of "board"
  vs "incoming" are a dial.
- A Line piece is randomly horizontal or vertical, and its piece shows which.

| Special | When it fires |
|---|---|
| Line | Clears its whole row or column |
| Bomb | Clears the 3×3 area around itself |
| Color | Clears every piece of one color: the color it was swapped with, or the most common color on the board if something else set it off |

- **Specials fire when cleared**, whether by a match or by another special's blast. Chains are
  fully allowed: a bomb that hits a line piece sets it off, and so on. Finding these chains is
  the player's skill.
- **Swapping two specials** is always legal: both fire. Color + Color clears the whole board.
- All specials are unlocked by level band (§4).

### 2.5 Level goals
- **Score:** reach N points.
- **Collect:** clear N pieces of given color(s).
- Later levels may combine 2 goals.
- (Blocker goals are removed along with blockers; see §7.)

### 2.6 Board topology and tile flow (designed for extension)
Top-down gravity is where we start, **not** a fixed rule. The game must support new ways for pieces
to arrive and move without rewriting the rules. Each level describes its board as:

| Concept | Meaning | v1 | Later examples |
|---|---|---|---|
| **Cells** | Places a piece can sit, grouped into one or more **layers** | One rectangular layer (holes in a later band) | Several stacked or overlapping layers |
| **Neighbors** | Which cells can swap and which line up for matches | Orthogonal, same layer | Cross-layer links, shared cells where boards overlap |
| **Flow** | For each cell, where its replacement piece comes from | "The cell above" (skipping holes) | Sideways or upward flow, diagonal slides, conveyor belts |
| **Sources** | Where brand-new pieces enter | One spawner above each column | Spawners anywhere, limited or scripted spawners, pieces fed from another layer |
| **Links** | Non-adjacent connections pieces can travel along | None | Funnels, pipes, portals, drops from an upper layer into a lower one |

Rules for future mechanics:
- Match, swap and special rules are written against cells and neighbors, never against
  "up/down" or "row/column" directly. A Line piece clears along its *line* in the topology.
- Refill is one generic "settle" step that moves pieces along flow and links and pulls from
  sources until every reachable cell is full. Gravity is just one flow layout.
- A piece keeps one identity from spawn to clear, so every movement can be animated as a path.
- **Multi-level boards (2½D):** a level may have more than one layer. The camera can rotate or
  tilt between layers, and the player may act on more than one layer. How cross-layer matching and
  input should work is an open question (§8), but the data model must allow it from the start.
- The generator and the difficulty profile treat topology as more parameters (`board_layout`,
  `flow`, `sources`), unlocked by band like any other mechanic.

### 2.7 Win / lose
- **Win:** all goals met within the move limit. 1–3 stars, set by score thresholds (§3.2).
- **Lose:** out of moves. The player may retry; a retry gives the *same* level, because the
  level's seed is kept until it is won.
- **Deadlock:** if no legal move exists, the board auto-shuffles at no move cost.

## 3. Dynamic level generation (key feature)

### 3.1 Behavior
- A level is generated **just in time**, when the player starts it, from `(level_number, seed)`.
- **v1 focus is quality over consistency:** each new level gets a fresh random seed. Levels are not
  stored or shared, and two players (or two playthroughs) get different level 42s.
- The same `(level_number, seed, generator_version)` always gives the same level. This is used for
  retries, debugging and tests, not for sharing.
- Each level is checked before it is shown: no matches already on the board, at least one legal
  move, and goals the player can actually reach.
- **Auto-playtesting:** to know how hard a generated level really is, the generator has a simple
  computer player (the "bot"). The bot plays each candidate level many times in a fraction of a
  second. The fraction of its runs that win is the level's measured difficulty. If that misses
  the target, the generator adjusts the move limit or goal size and tries again. The player never
  sees the bot.
- Generation should not be noticeable to the player. The generation time is measured
  (TRD §7). The app generates in the background behind a loading screen and
  pre-generates the next level.

### 3.2 Difficulty curve: pressure and release
Difficulty follows the standard **tension and release** pacing used by commercial match-3 games.
Every few levels pressure builds to a **pinch level** (a hard one). The level after a pinch is a
**release level** (noticeably easier), so the player is rewarded and frustration resets. The
overall trend still rises.

The curve is one number per level, `difficulty(n)` in 0..1:

```
difficulty(n) = clamp( trend(n) + amplitude(n) * wave(phase(n)), 0, 1 )

trend(n)      = smooth rise over the whole game (ease-out curve from points in the profile)
phase(n)      = (n - 1) mod P                  # position inside a cycle of P levels
wave(p)       = -R                              if p == 0    (release level)
              = ((p - 1) / (P - 2)) ^ K         for 1 <= p <= P-1  (pressure ramp, 1.0 = pinch)
amplitude(n)  = how strong the swing is, which can grow slowly with n
```

Defaults (all dials): cycle length `P = 7`, ramp shape `K = 1.6` (pressure builds slowly, then
steeply), release depth `R = 0.6`, amplitude 0.10 → 0.18. With P = 7, levels 7, 14, 21 … are
pinch levels, and levels 8, 15, 22 … are release levels.

Difficulty becomes concrete as a **target bot win rate**:
`target_win_rate = lerp(easiest_win_rate, hardest_win_rate, difficulty)`, with defaults 0.97 and
0.35. Colors, board size and unlocks come from the band table (§4). The move limit and goal size
are then tuned by auto-playtesting until the level hits the target.

**Stars:** the generator records the bot's winning scores. The 2-star and 3-star thresholds are
those scores' 50th and 85th percentiles (dials).

### 3.3 Mechanic introduction
When a mechanic first unlocks (for example Line pieces at level 11), that level is an **intro
level**. Its difficulty is forced down (dial, default 0.1), and it shows a one-time tooltip
explaining the new mechanic.

### 3.4 Adaptive difficulty (stretch)
An optional offset nudges difficulty down after repeated losses (e.g. −0.05 after 3 fails) and up
after several 3-star wins in a row. It can be turned off in settings.

## 4. Progression bands (initial proposal)

| Levels | Introduced |
|---|---|
| 1–10 | Basic matching, score goals, 4 colors |
| 11–20 | Collect goals, Line pieces |
| 21–35 | Bomb pieces, 5 colors |
| 36–50 | Shaped boards (holes) |
| 51–75 | Color pieces, 6 colors |
| 76+ | Combined goals, 7 colors possible |

## 5. Screens and UX

### 5.1 Screens
1. **Title:** Play, Settings, Quit (no Quit on Android; the system Back/Home leaves the app).
2. **Level map:** a scrolling path of levels with stars earned; click the next level to play.
3. **Level intro:** goal, move limit, mechanic tooltip if this is an intro level.
4. **Gameplay:** board, moves left, goal progress, score, pause.
5. **Result:** win (stars, next) or lose (retry, map).
6. **Settings:** sound (on/off), animation speed, adaptive difficulty on/off. Sound is short effects for swap, rejected swap, clear (rising in pitch with each cascade step), landing, special creation and firing, shuffle, win and lose; no music in v1 (TRD §12.4).
7. **Dev settings (dev builds):** see §5.2.

### 5.2 Dev settings page
An in-game page, opened with a hotkey (default F1) on desktop or a three-finger tap on a phone,
in dev builds only, that lists **every dial**
grouped by section. Sections: scoring, specials, input, animation, camera/zoom, difficulty curve,
generator, art style.
- Changes apply live, so you can play with the new value straight away.
- **Save** writes the changed dials to a dev overrides file. **Reset** restores defaults.
- The page also shows the current level's seed, its `difficulty(n)`, the target and measured bot
  win rate, and a "jump to level N" field.
- **Auto-play** toggle (also F2): the app plays itself, starting levels, making the suggested move whenever the
  board is still, then moving on after a win or retrying after a loss. A dev aid for watching many levels
  without touching the device; compiled out of release builds like the rest of this page. It has two modes: *Next* plays the
  simple suggested move, *Plan* looks several moves ahead (knowing the coming refills) and plays the first move of the
  best line; while it thinks it shows the lines it is trying as translucent ghost arrows on the board (TRD 7.1).

### 5.3 Layout and board view
- **Flexible layout:** every HUD element (title, level name, moves, score, goals, buttons) and the
  board itself is placed by **layout data** (UI Toolkit UXML/USS assets), not by code. Each
  element is anchored to a screen edge or corner with an offset and size relative to the window.
- **Rearranging is easy:**
  - Moving the score to the top, the goals under the board, or the board to the left is a data
    change.
  - Layout is edited visually in the Unity Editor (UI Builder) and shown live in Play mode.
  - Separate layouts can exist for wide and tall windows. A phone in portrait uses the tall
    layout; a phone in landscape or a tablet uses whichever fits its aspect ratio.
- **Board zoom:** the board automatically zooms to fill the space the layout gives it, so it uses
  the screen well at any window size or aspect ratio.
- **Manual zoom and pan:** the player can zoom with the mouse wheel and pan with a right- or
  middle-drag. Double-click on empty space resets to auto-fit. On touch: pinch, two-finger drag
  and double-tap (§2.2). Zoom limits are dials.
- Resizing the window re-flows the layout and re-fits the board live. On Android, rotating the
  device or entering split-screen is a resize.
- **Safe area (Android):** no HUD element or board cell is placed under a camera cutout, rounded
  corner or the system gesture bar. Layout anchors are measured from the safe area, not the raw
  screen edge.
- **Touch targets:** on touch devices every button is at least a minimum physical size (a dial,
  default 9 mm). A 9×9 board must still fit the width of a phone in portrait at auto-fit (about
  7 mm per cell); the tall layout gives the board the full width for this.

### 5.4 Art direction
- **Pieces are real 3D models** in a handmade claymation style. **v1 implements
  [K: Pebbles](art/STYLE-K-Pebbles.md).** [I: Claymation](art/STYLE-I-Claymation.md) and
  [J: Studio Set](art/STYLE-J-StudioSet.md) stay documented as alternatives. The style is a
  swappable preset, so changing it later does not touch game logic.
- Each color has a distinct silhouette as well as a distinct color, for color-blind players.
- Each piece instance gets small random variation (rotation, squash, tint) from a seed, so no two
  look identical.
- 3D enables richer animation, and we should use it: squash-and-stretch on landing, wobble on
  select, pieces tumbling along funnels, crumble or sink on clear, camera moves between layers.
- **Animation pacing** is set by durations (for example "a swap takes 180 ms"), not frame rate.
  The game renders as fast as the display allows (vsync) and is not optimized prematurely. Every
  duration is a dial, and the player's "animation speed" setting multiplies them all.

### 5.4.1 Combo celebration
The multiplier is the game's biggest moment, so it gets lots of visual celebration that grows
with it:
- A **multiplier badge** (for example `×2.5`) pops up over the matched pieces each time the
  multiplier rises, and the HUD keeps showing the current value until the move settles.
- Celebration has **tiers** (dial thresholds on the multiplier, for example ×1.5, ×2.5, ×4, ×6).
  Each tier adds to the one before: a bigger and bouncier badge, a warmer color, a burst of
  pebble-dust or sparkle particles, a short camera punch, and a pitched-up sound (the existing
  cascade pitch rise, plus a stinger per tier).
- Score popups show the points *with* the multiplier applied, so the player sees why a match
  was worth more.
- The HUD shows the highest multiplier of the move next to the score until the move settles.
- Everything is a dial (tier thresholds, sizes, durations, particle counts, camera punch) on the
  dev page; the animation-speed setting scales the durations. The `reduce_effects` dial turns off
  the camera punch and particles but keeps the badge (a player-facing setting comes with the
  accessibility pass).

## 6. Non-functional requirements
- Runs smoothly on a mid-range laptop on **Windows and macOS** (Unity standalone builds).
- Runs smoothly on a **mid-range Android phone** (64-bit, Android 10 or later). U7 is
  measured on a Galaxy S23 (a high-end phone); a mid-range phone is added before wider
  playtesting (TRD §13.10) in portrait and landscape, and on Android tablets.
- On Android the game survives being sent to the background, the screen turning off, rotation
  and being killed by the system: progress is saved before the app is suspended and the player
  returns to where they were (the title screen at worst, never a lost level result).
- The Android build is an installable APK that playtesters can sideload; the desktop and Android
  builds come from the same source revision.
- Progress (current level, stars, current level's seed) is saved in the operating system's
  standard per-user app data folder (TRD §8) and survives a crash.
- All gameplay logic is deterministic given a seed (needed for retries, debugging and tests).

## 7. Out of scope for v1
- Level baking and sharing (see §9), online features, monetisation, lives/energy, boosters,
  final art.
- **Store publishing** (Google Play listing, release signing, store compliance work) and **iOS**
  builds. Android in v1 means a sideloadable playtest build.
- **Blockers** (ice, stone, chains) are dropped for now: they slow the game down without adding
  fun. They may come back later as an optional mechanic.
- Also out of scope as *content* for v1, but the architecture must not prevent them: non-gravity
  flow, extra sources, funnels/links, overlapping boards and multi-layer boards (§2.6).

## 8. Open questions
- Multi-layer boards: can a match span layers? Does the player swap on one "active" layer at a
  time, or on any visible layer? How does the camera choose its view?
- Overlapping boards: does a shared cell belong to both boards' lines for matching?
- Android: lock to portrait on phones, or support both orientations? v1 supports both; revisit
  after playtesting.

## 9. Future: level baking
Once generation quality is proven, levels may be **baked** the first time they are encountered:
the generated level is stored and reused on replay and served to other players, so everyone's
level 42 is the same. This is deferred; v1 only generates as the player goes. The deterministic
seed design keeps this option open (see TRD §11).
