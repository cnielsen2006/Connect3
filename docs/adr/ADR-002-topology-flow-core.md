# ADR-002: Topology + flow board model instead of a hard-coded gravity grid

**Status:** Accepted
**Date:** 2026-10-03
**Deciders:** Craig (product owner)

## Context
v1 plays like classic match-3: a rectangular grid where pieces fall down. Planned later mechanics
break that model:
- pieces arriving from other sources
- funnels and portals
- overlapping boards
- rotating 2½D boards with several layers the player controls (PRD §2.6)

If the rules assume "row, column, down", every one of those becomes a rewrite.

## Decision
Model the board as a **topology** and a **flow** (TRD §3):
- The **topology** holds cells, neighbors, lines, areas and links.
- The **flow** gives each cell its feed list and holds the sources.
- One generic `settle()` moves pieces along the flow.
- Rules only ever ask the topology. Gravity on a grid is just a preset.

v1 builds only the `grid` and `gravity_down` presets. Test-only topologies (sideways flow, a
funnel, two layers) keep the abstraction honest.

## Options Considered

### Option A: 2D array + gravity (classic)
| Dimension | Assessment |
|---|---|
| Complexity | Low |
| Cost | Fastest to a first working core |
| Extensibility | Poor: each new flow mechanic touches matching, settle and input |
| Team familiarity | High (standard tutorials) |

**Pros:** simple and fast.
**Cons:** it's exactly the commitment the user said not to make.

### Option B: Topology graph + flow (chosen)
| Dimension | Assessment |
|---|---|
| Complexity | Medium: indirection for neighbors and lines |
| Cost | Roughly +20–30% effort on the core |
| Extensibility | High: new mechanics are presets and data |
| Team familiarity | Less common, but small surface |

**Pros:**
- Funnels, layers and other sources become data.
- `Moved(path)` events animate any route.

**Cons:**
- Slightly slower inner loops for the bot.
- More abstraction to explain.

### Option C: A full physics simulation
| Dimension | Assessment |
|---|---|
| Complexity | High |
| Cost | High |
| Extensibility | Very high but non-deterministic |
| Team familiarity | n/a |

**Pros:** maximum freedom.
**Cons:** breaks determinism, so seeded levels, retries and the bot all stop working. Rejected.

## Trade-off Analysis
B costs a little up front and a little speed in the bot loop. Option A would save that now but
would need rewriting when the 2½D and funnel mechanics land, which is the game's planned point
of difference. Determinism rules out C.

## Consequences
- **Easier:**
  - New piece sources, funnels and layers.
  - Animating arbitrary paths.
  - Shaped boards (holes simply aren't cells).
- **Harder:** a bot that is fast enough may need caching of lines and neighbors.
- **Revisit:** cross-layer matching rules (PRD §8) before U9 (topology mechanics, TRD §10).

## Action Items
1. [x] Implement `Topology`, `Flow` and `settle()` with the grid and gravity presets
2. [x] Add the test-only sideways, funnel and two-layer topologies to the test suite
3. [ ] Benchmark bot moves per second once the bot runs
