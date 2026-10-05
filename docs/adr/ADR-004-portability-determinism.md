# ADR-004: Engine-neutral core, specified RNG and golden event logs for portability

**Status:** Accepted
**Date:** 2026-10-03
**Deciders:** Craig (product owner)

## Context
- The game runs on Unity 6 and C# ([ADR-006](ADR-006-unity-engine.md)), and the rules may later be reused on other platforms or engines.
- Seeds must regenerate identical levels for retries, debugging and possible future level baking.
- A language's built-in RNG and string hashing differ between runtimes and versions, and can't be
  reproduced elsewhere, so the game specifies its own.
- The design docs, not the code, are the spec.

## Decision
1. **Pure core:** the logic layers (`Core`, `Gameplay`, `Generation`, `Config`, `Persistence`, `Presentation`; each with a single role) use plain data and never reference an engine. The layering is refined in [ADR-007](ADR-007-layers-and-ports.md).
2. **Specified RNG:** PCG32 (XSH-RR), with sub-streams seeded through FNV-1a and SplitMix64
   (TRD §4). Known-answer test vectors are committed.
3. **Event contract:** `resolve()` returns typed events that serialise to JSON.
   **Golden event logs** for scripted sessions are the conformance test for any port. They are
   stored in `conformance/` and are the oracle the C# code must match (TRD §9.2).

## Options Considered

### Option A: The platform's built-in RNG + ad hoc events
| Dimension | Assessment |
|---|---|
| Complexity | Lowest |
| Portability | None: a port can't reproduce levels |
| Testability | OK on one runtime only |

**Pros:** zero effort.
**Cons:** locks the seeded content to one runtime, and gives no way to check another.

### Option B: Specified RNG + event logs (chosen)
| Dimension | Assessment |
|---|---|
| Complexity | Low: about 60 lines for the RNG, plus JSON serialisation of events |
| Portability | High: same seed, same level, same events anywhere |
| Testability | Excellent: golden logs catch any behavior change |

**Pros:**
- Reproducible levels across languages.
- A cross-engine conformance suite.
- Bug reports can be replayed.

**Cons:**
- A hand-written PRNG may be slower than the built-in one. That's acceptable at our scale; measure it.

### Option C: Share one native core library across all platforms
| Dimension | Assessment |
|---|---|
| Complexity | High (interop, per-platform builds) |
| Portability | Highest |
| Testability | Split toolchains |

**Pros:** the rules are written once.
**Cons:** native interop and per-platform builds for a small amount of logic.

## Trade-off Analysis
B costs very little and keeps every future option open. A saves a day now but makes any later
port a rewrite plus behavior drift. C pays interop costs before we know it's needed.

## Consequences
- **Easier:** porting later, replaying bug reports, level baking later, regression testing.
- **Harder:**
  - Every change to generation output must bump `GENERATOR_VERSION`.
  - Every behavior change must update the golden logs deliberately.
- **Revisit:** RNG speed if the bot misses its budget.

## Action Items
1. [x] PCG32, SplitMix64 and FNV-1a, plus known-answer tests
2. [x] Typed events with JSON round-trip tests
3. [x] Golden event log fixtures for 3+ scripted sessions in `conformance/` (U0)
4. [ ] Make the C# code pass the conformance fixtures (U2, U3)
5. [ ] Turn the C# determinism rules (TRD §4.3) into tests, and run the conformance suite in a
   Player build on an ARM64 device

The cross-language rules that matter are listed in TRD §4.3.
