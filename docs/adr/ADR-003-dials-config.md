# ADR-003: All tunables as dials in TOML with a live dev settings page

**Status:** Accepted
**Date:** 2026-10-03
**Deciders:** Craig (product owner)

## Context
The goal right now is to find the fun. Scoring, animation timings, input thresholds, camera,
difficulty curve and generator settings will be tuned constantly. The user asked that none of
them be baked into code, and that they be editable in a dev settings page.

## Decision
- Every tunable is a named **dial**. Defaults live in `data/dials.toml`, and developer changes
  are saved to `<user dir>/dev_dials.toml`.
- A typed registry (`Cascade.Config.DialRegistry`) handles loading, validation and change notifications.
- A dev page (F1 on desktop, three-finger tap on a phone) edits dials live. In Unity it is generated from per-dial metadata (TRD §12.2).
- Layouts (`data/layouts/*.toml`) and the difficulty profile follow the same pattern.

## Options Considered

### Option A: Constants in code
| Dimension | Assessment |
|---|---|
| Complexity | Lowest |
| Iteration speed | Slow: edit, restart, replay |
| Designer access | None |
| Risk | Values scatter; coder models invent their own |

**Pros:** nothing to build.
**Cons:** contradicts the requirement, and makes tuning slow.

### Option B: TOML dials + live registry + dev page (chosen)
| Dimension | Assessment |
|---|---|
| Complexity | Medium (registry + one UI page) |
| Iteration speed | Fast: change while playing |
| Designer access | Full |
| Risk | Must keep the dial list in sync with the docs |

**Pros:**
- Live tuning.
- One documented list of numbers.
- Easy to port, since the data travels with the game.

**Cons:**
- Building the dev page is an extra milestone (U5).

### Option C: Code-as-config (a config module / env vars)
| Dimension | Assessment |
|---|---|
| Complexity | Low |
| Iteration speed | Medium |
| Designer access | Poor |
| Risk | Not portable to another engine |

**Pros:** simple.
**Cons:** no live editing, and code-as-config does not port.

## Trade-off Analysis
B costs one registry and one screen. In return, all balancing becomes a data task, and coder
models can't quietly hard-code values: an unknown dial is an error, and every dial is listed in
TRD §5.

## Consequences
- **Easier:** tuning while playing, A/B-ing feel, and handing balance work to a non-coder.
- **Harder:**
  - Each new tunable must be added to `dials.toml` and to TRD §5.
  - The core must take dials as parameters, so it stays pure and testable.
- **Revisit:** whether some dials become player-facing settings.

## Action Items
1. [x] `Cascade.Config.DialRegistry` with schema validation and subscriptions (U2)
2. [x] Dev settings page, generated from dial metadata (U5); layout editing is UI Builder
3. [ ] Tests: unknown keys are rejected; overrides take precedence
