# ADR-007: Engine-neutral layers, ports for the platform, and a pure presentation-logic layer

**Status:** Accepted
**Date:** 2026-10-04
**Deciders:** Craig (product owner)
**Builds on:** [ADR-004](ADR-004-portability-determinism.md), [ADR-006](ADR-006-unity-engine.md)

## Context
- Starting the Unity 6 / C# codebase ([ADR-006](ADR-006-unity-engine.md)) is the cheapest moment
  to set the structure, and the conformance fixtures prove behavior stays correct.
- Without a deliberate structure these roles tend to blur:
  - rules reading a global dial registry, so they are not usable without it.
  - direct calls for secrets, time and storage paths spreading platform knowledge through the logic.
  - gameplay mixing the running level, shared move evaluation, the screen state machine and file I/O.
  - the view mixing engine code with decisions that need no engine (the input state machine,
    camera fit math, timeline layout, piece variation).
  - the generator drawing its own seeds and reading the wall clock.
- The product wants the same game on Android and desktop now, iOS possibly later, and tools that
  run the rules headless (bot calibration, CI).

## Decision
1. **Layered assemblies** with enforced one-way references (TRD §2.1): `Core` → `Gameplay` →
   `Generation`; `Config` and `Platform.Abstractions` as leaves; `Persistence`; a pure
   `Presentation`; then the Unity layers (`Unity.Platform`, `Unity.View`) and a single
   composition root (`App`). Logic layers use `noEngineReferences`, so a Unity call in them does
   not compile.
2. **Rule parameters are passed in:** `Core` takes an immutable `RuleSet` snapshot; it never reads
   the dial registry. `Gameplay.RuleSets` builds snapshots from dials.
3. **Platform behind ports** (TRD §2.3): `HostInfo`, `IStorage`, `IDataSource`, `ISeedSource`,
   `IStopwatch`, `ILog`, `IAppLifecycle`. Nothing else asks the platform anything.
4. **Presentation is split into deciding and drawing.** Everything that can be decided without an
   engine (timeline planning, input state machine, gesture recognition, camera fit, piece
   variation, screen flow, view-models) lives in `Cascade.Presentation`, with its own small
   vector types, and is unit-tested. `Unity.View` only turns that output into GameObjects,
   materials and UI Toolkit elements.
5. **The logic layers share one folder** (`Assets/Cascade/Logic/`), one asmdef per layer. It is
   deliberately not a UPM package (one project, no second consumer); a headless .NET project
   compiles the same folder by glob for tests and tools.
6. **One composition root** (`Cascade.App`) builds the object graph. No service locator, no
   singletons, no static state in logic.

## Options Considered

### Option A: Unstructured code (roles mixed in a few assemblies)
| Dimension | Assessment |
|---|---|
| Complexity | Lowest |
| Testability | Medium: the view logic needs the engine |
| Reuse | Low: platform details stay mixed into logic |

**Pros:** least effort now.
**Cons:** carries blurred roles into a long-lived codebase; Android
would add `if (android)` branches into logic.

### Option B: Layered assemblies + ports + pure presentation logic (chosen)
| Dimension | Assessment |
|---|---|
| Complexity | Medium: about a dozen assemblies, a few interfaces |
| Testability | High: all decisions run under `dotnet test` with no engine |
| Reuse | High: the `Logic/` folder has no Unity references and compiles under plain .NET |
| Risk | Over-abstraction; mitigated by keeping ports few and small |

**Pros:** every platform difference is one class in one assembly; a rule cannot depend on Unity;
the same tests guard both runners.
**Cons:** more files and asmdefs up front; contributors must learn the layer table.

### Option C: Two layers only (Logic, Unity)
| Dimension | Assessment |
|---|---|
| Complexity | Low |
| Testability | Good for rules, weak for presentation decisions |
| Reuse | Medium |

**Pros:** simplest.
**Cons:** loses the ability to say "Gameplay never draws" or "Presentation never calls Unity" to
the compiler; the boundaries that matter most would be conventions again.

## Trade-off Analysis
B costs a short upfront design that the TRD already contains, and it removes the biggest recurring
risk in a multi-platform game: platform details leaking into rules. A is cheaper only until the
first platform difference. C keeps too little enforced.

## Consequences
- **Easier:** adding iOS or a headless server (a new `Platform` implementation only); testing
  gestures, timelines and input without a device; replacing a UI or rendering approach.
- **Harder:** every new class must be placed in a layer; "just reading a dial here" is not allowed.
- **Revisit:** if the number of ports grows past about ten, or if two assemblies are always
  changed together, merge them.

## Action Items
1. [ ] U1: create the assemblies and the architecture test (TRD §9.1)
2. [ ] U2-U4: build layer by layer in the TRD §10 order
3. [ ] Keep the layer table in TRD §2.1 current; changing a boundary needs a new ADR
