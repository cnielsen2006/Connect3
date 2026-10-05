# Cascade: Market Requirements Document (MRD)

| | |
|---|---|
| Status | Draft v0.3 |
| Date | 2026-10-04 |
| Related | [PRD](PRD.md) · [TRD](TRD.md) · [Art styles](art/README.md): [I](art/STYLE-I-Claymation.md) · [J](art/STYLE-J-StudioSet.md) · [K](art/STYLE-K-Pebbles.md) |

## 1. Problem / opportunity

Match-3 is one of the largest casual genres (Candy Crush, Bejeweled, Royal Match, Homescapes). Its
dominant production model has a known weakness: **every level is hand-authored.** Studios ship
thousands of levels, and that causes three problems:

1. **High content cost.** Designers author, playtest and rebalance each level by hand.
2. **Rigid difficulty.** A level's difficulty is fixed when it ships, so players get stuck on
   "wall" levels or breeze through ones that are too easy.
3. **Finite content.** Players who reach the end wait for the next content drop or leave.

**Opportunity:** a match-3 game where levels are *generated at runtime* from a parameterised
difficulty model. Designers tune curves, not individual levels. That gives unlimited content and a
difficulty that can be changed at any level by editing a config, without regenerating a level set.

## 2. Target market

| Segment | Description | What they want |
|---|---|---|
| Primary: casual puzzle players | Ages 25–55, short sessions (3–10 min) | Satisfying matches, steady progression, never stuck for long |
| Secondary: "completionists" | Players who finished other match-3 titles | Endless fresh content |
| Tertiary: hobbyist designers / modders | Players who like to tweak | Exposed difficulty knobs |

Platforms: **Android phones and tablets** (MR-12) and **desktop (Windows, macOS)**, built in
Unity 6 from one project. Short casual sessions are mostly played on phones, so playtesting on a
phone is needed to judge session length, touch feel and readability. iOS and web are possible later
targets once the design is validated.

## 3. Competitive landscape

| Product | Level model | Difference for Cascade |
|---|---|---|
| Candy Crush Saga | Hand-authored, 15k+ levels | No authoring cost; difficulty adjustable after release |
| Bejeweled Classic / Zen | Endless random board, no level goals | Structured goals and progression on top of generation |
| Royal Match | Hand-authored with live-ops tuning | Tuning happens at the generator, not per level |
| Roguelike puzzle games (e.g. Puzzle Quest) | Partially procedural | Pure match-3 focus with a clear difficulty curve |

## 4. Market requirements

| ID | Requirement | Priority |
|---|---|---|
| MR-1 | Familiar, instantly understood match-3 swap mechanic | Must |
| MR-2 | Unlimited level progression without authored content | Must |
| MR-3 | Difficulty that rises smoothly and is tunable by a designer without code changes | Must |
| MR-4 | Every generated level is solvable/fair (no "impossible" boards) | Must |
| MR-5 | Reproducible levels (a seed always gives the same board) for debugging and retries | Should |
| MR-5b | Bake levels as they are first encountered so replays and other players get the same level | Won't (v1), future |
| MR-6 | Short session-friendly levels (median 2–5 min) | Should |
| MR-7 | Introduce new mechanics (special pieces, goal types, board shapes; blockers deferred) gradually as levels rise | Should |
| MR-8 | Optional adaptive difficulty based on player performance | Could |
| MR-9 | Monetisation hooks (lives, boosters) | Won't (v1) |
| MR-10 | A distinctive, tactile look: real 3D pieces with a handmade claymation style, so the game stands out from glossy-candy competitors (candidate styles: [Art styles](art/README.md): [I](art/STYLE-I-Claymation.md) · [J](art/STYLE-J-StudioSet.md) · [K](art/STYLE-K-Pebbles.md)) | Should |
| MR-11 | Mechanics beyond top-down gravity (other tile sources, funnels, overlapping boards, multi-level boards) to keep generated levels from feeling samey | Should (later bands) |
| MR-12 | Playable on Android phones and tablets with touch controls, from an installable build that playtesters can sideload | Should |
| MR-13 | Store publishing (Google Play), iOS builds | Won't (v1) |

## 5. Success criteria (playtest phase)

- Playtesters cannot tell generated levels apart from hand-made ones in a blind comparison (≥ 50% misidentification).
- 0 unsolvable or deadlocked levels across 10,000 generated levels in automated testing.
- Win rate per level band follows the target curve within ±10 percentage points (measured by a bot).
- A designer can change the difficulty curve and see the effect without touching code.
- Playtesters can install the Android build on their own phone and play a full session with touch
  alone, without instructions beyond the in-game intro.

## 6. Constraints and assumptions

- Small team / solo developer; **Unity 6 and C#** for the game ([ADR-006](adr/ADR-006-unity-engine.md)).
- Game rules and generation stay
  portable (an engine-neutral logic layer, [ADR-007](adr/ADR-007-layers-and-ports.md)), and the
  design docs, not any one codebase, are the source of truth.
- No backend or online services in v1. Progress is saved locally.
- Android and desktop builds come from the same Unity project, code and data. A separate mobile
  codebase is not acceptable.
- Art and audio are placeholder at first; the gameplay and the generator come first. Placeholder audio is synthesized sound effects (TRD §12.4).

## 7. Risks

| Risk | Mitigation |
|---|---|
| Generated levels feel "samey" | Many varied parameters (board shape, goals, blockers, colors, tile flow); weighted variety |
| One codebase becomes the de facto spec | Docs lead, code follows; engine-neutral core and event log; cross-engine golden tests (they now guard the Unity port) |
| Difficulty spikes or troughs | Simulation bot measures generated levels and rejects outliers |
| Players feel levels are unfair | Solvability checks, guaranteed opening move, auto-shuffle on deadlock |
| The engine has no mature Android runtime | Resolved: Unity 6 ([ADR-006](adr/ADR-006-unity-engine.md)) |
| The C# rules or generator drift from the specified behavior | Golden event logs, RNG vectors and generated-level fixtures must match exactly, including in a build on an Android device (TRD §9.2) |
| Generation is too slow on a phone CPU | Background pre-generation already exists; measure on a reference device and tune `generator.budget_ms` per platform |
