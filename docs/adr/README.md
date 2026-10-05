# Architecture Decision Records

| ADR | Decision | Status |
|---|---|---|
| [002](ADR-002-topology-flow-core.md) | Topology + flow board model instead of a hard-coded gravity grid | Accepted |
| [003](ADR-003-dials-config.md) | All tunables as dials in TOML with a live dev settings page | Accepted |
| [004](ADR-004-portability-determinism.md) | Engine-neutral core, specified RNG and golden event logs | Accepted |
| [006](ADR-006-unity-engine.md) | Unity 6 and C# for the shipping game; Android first | Accepted |
| [007](ADR-007-layers-and-ports.md) | Engine-neutral layers, platform ports, pure presentation logic | Accepted |

New ADRs take the next number. To reverse a decision, write a new ADR and mark the old one
"Superseded by ADR-00N".
