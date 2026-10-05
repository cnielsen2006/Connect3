# Conformance fixtures

Reference fixtures (TRD §9.2).
**Oracle**: a C# build is conforming when every fixture passes. The original reference implementation is gone,
so C# is now the reference, but fixtures are never regenerated casually: a fixture changes only when the TRD
deliberately changes a rule (bump `GeneratorVersion` if generation output moves). To rewrite `golden/`, `levels/` and `bot/`
after such a change run `dotnet run --project tools/Cascade.Tools -- export conformance`, review the diff against the
TRD, then `verify conformance`.

History: 2026-10-05, combo multiplier (PRD 2.3): `Scored` gained `multiplier` and is emitted per match group;
`GeneratorVersion` 1 to 2; `golden/`, `levels/` and `bot/` re-exported from C#.

All 64-bit integers (seeds, RNG state/inc, outputs of `splitmix64`/`fnv1a_64`) are decimal
**strings**; everything else is a plain JSON number. Levels were generated with
`generator.budget_ms = 0` (no wall clock), so they are fully deterministic.

| Path | Contents |
|---|---|
| `rng/vectors.json` | PCG32 outputs (`next_u32`, `randbelow`, `random`, `shuffle`), `splitmix64`, `fnv1a_64`, 20 `substream` (seed, name) pairs |
| `golden/session_*.json` | 12 scripted moves on an 8x8 board per seed: swap + full event list |
| `profiles/evaluations.json` | `Profile.Evaluate(n)` for n = 1..150, every field (`unlocked` sorted) |
| `profiles/difficulty.json` | the parsed `difficulty.toml` (the Profile input) |
| `levels/level_NNN_{std,big}.json` | the `Level` JSON dump for 37 level numbers x 2 seeds (`std` = 1000003+n, `big` = 0xDEADBEEFCAFEF00D) |
| `bot/runs.json` | 20 bot runs (runs 0 and 1 on 10 levels): `won`, `score`, `moves` |
| `data/` | `dials.toml`, `difficulty.toml` as used for the export |
