# ADR-006: Unity 6 and C# for the game

**Status:** Accepted
**Date:** 2026-10-04
**Deciders:** Craig (product owner)

## Context
- Android phones and tablets are a requirement (MRD MR-12). A separate mobile codebase is not
  acceptable (MRD §6).
- The rules, generator and difficulty curve are engine-neutral, with a specified RNG and golden
  event logs (ADR-004), so the engine choice affects the view and platform layers, not the rules.
- Real 3D lighting and materials are needed for the claymation pieces.

## Decision
Build the game on **Unity 6 (6000.x LTS) with C#**, targeting Android first and Windows and macOS
from the same project.

- Structure the code as in [ADR-007](ADR-007-layers-and-ports.md): ports for everything
  platform-specific, an immutable `RuleSet` instead of reading dials in the core, presentation
  logic separated from drawing, one composition root.
- Check behavior against the reference conformance fixtures (TRD §9.2).
- URP for rendering, UI Toolkit for screens, the Input System for input, IL2CPP builds, a
  command-line build script writing to an ignored `Builds/` folder (TRD §1, §13).

## Options Considered

### Option A: Unity 6 + C# (chosen)
| Dimension | Assessment |
|---|---|
| Complexity | Medium: the view is the main new work |
| Cost | Free tier |
| Android | Mature: IL2CPP arm64, touch, lifecycle, safe area, device coverage |
| Code reuse | Docs, data (TOML), fixtures and algorithms |

**Pros:** best mobile device support and tooling; real 3D lighting and materials for the claymation
pieces; large ecosystem; the logic layer stays engine-neutral, so it is reusable.
**Cons:** Unity version churn; a heavier editor; C# and IL2CPP details to respect (TRD §4.3, §13.2).
Mitigated by `dotnet test` and the headless tools for fast iteration.

### Option B: Godot 4 (C# or GDScript)
| Dimension | Assessment |
|---|---|
| Complexity | Medium: same size as Unity |
| Cost | Free, MIT |
| Android | Good, with a smaller device-testing base than Unity |
| Code reuse | Same as Unity |

**Pros:** open source, light.
**Cons:** a smaller ecosystem and fewer mobile-specific tools; the product owner prefers Unity.
The layered design (ADR-007) keeps this door open for the logic.

### Option C: A custom or less common engine with its own Android runtime
| Dimension | Assessment |
|---|---|
| Complexity | High: engine and platform work, GLES/Vulkan rework |
| Android | Experimental or self-maintained |

**Pros:** no dependency on Unity.
**Cons:** that is engine work, not game work, and a permanent maintenance cost.

## Trade-off Analysis
C pays a permanent maintenance cost and may simply fail. B is plausible but is not a better fit
than A for device coverage and tooling.

## Consequences
- **Easier:** Android and desktop from one project; device testing; real-time 3D look; hiring
  and tooling.
- **Harder:**
  - Determinism must be actively protected in C# (TRD §4.3) and checked on the device (TRD §9.1).
  - IL2CPP stripping can break reflection-based libraries, so data code maps TOML and JSON by hand.
  - Layout editing is done in UI Builder rather than in-game.
- **Revisit:** if Unity's licensing or runtime terms change materially, or if the logic layer is
  wanted on a platform Unity does not target (it is reusable as a plain .NET library).

## Action Items
1. [x] U0: export the conformance fixtures (TRD §10)
2. [ ] U1: walking skeleton on a phone; record in this ADR (a) APK installs and runs, (b) TOML and
   Json.NET work under IL2CPP stripping, (c) fixtures pass on ARM64, (d) 120 fps reachable on the
   S23, (e) how the Android Back key arrives
3. [ ] Pin the Unity 6 patch version and package versions in U1

## U1 findings (2026-10-04, Unity 6000.6.4f1, URP 17.6, Input System 1.20, Tomlyn 0.19.0, Newtonsoft 3.2.2)
Measured on the API 36 emulator (x86_64 image, ARM translation); the S23 checks (d) and (e) and the
real-device run of (a) are still open until a device is available.
- **(a) Build and install: pass on the emulator.** `BuildScript.BuildAndroid -dev` writes a 46 MB
  arm64 APK in about 40 s warm (2 min cold); it installs and runs. The APK is signed with Unity's
  debug keystore unless `CASCADE_KEYSTORE*` are set.
- **(b) IL2CPP data: pass.** `dials.toml` and `difficulty.toml` parse with Tomlyn and a Newtonsoft
  round trip (including a 64-bit seed as a string) works in a Medium-stripped IL2CPP player, with
  `link.xml` preserving both assemblies. Tomlyn 0.19.0 is used because 2.x's netstandard2.0 build
  pulls in `System.Text.Json` 10.
- **(c) Fixtures on ARM64:** not yet; arrives with U2 (no rules are ported in U1).
- **(d) Frame rate:** not measurable on the emulator (60 Hz, reached). Needs the S23.
- **(e) Back key:** open, needs a device.
- **Surprise 1:** the 6000.6 Android player has **no x86_64 libraries** (only arm64-v8a, armeabi-v7a,
  armv9). The emulator therefore needs an API 30+ image with ARM translation; the API 29 image
  cannot run the game.
- **Surprise 2:** **Vulkan segfaults** in `UnityGfxDeviceWorker` on the emulator (translation plus
  gfxstream); forcing GLES works. Not seen as a device issue, but check on the S23.
- **Pitfall recorded:** `EditorSceneManager.NewScene` unloads unreferenced assets, so references
  must be loaded after it when building a scene in code (it silently nulled the data catalog and the
  panel settings).

## U6 findings (2026-10-05, same emulator, dev APK, GLES, 60 Hz display)
- **(d) Frame rate, emulator only:** `UnityHost` sets `targetFrameRate` 60 and `vSyncCount` 0 (log line `[fps] ... target 60 vsync 0`).
  Menus hold 58 to 60 fps; level 80 played by `-autoplay 60` (cascades, shuffles, specials) gave 5 s averages of 49 to 59 fps
  (min 32 to 58, worst frame 33 to 67 ms; the first window includes a 2.5 s level load). That is a software-translated GPU, so it says
  the app runs and stays near the 60 Hz display, not that the S23 reaches 120 fps: **still open on the S23.**
- **Shaders on the player:** all five compile for GLES3 and Direct3D12 players; the UnityPerMaterial check (`Cascade/Check Shaders`) passes. The
  Editor's own SRP Batcher query reports "Not initialized" in batch mode, so the text check of the CBUFFER is the authority.
- No game errors in logcat; the only `E Unity` line is Unity's harmless Play Asset Delivery class probe (`AssetPackManager` not found).
- **Pitfall:** an open notification shade (a stray adb swipe) pauses the app (`HasFocus = 0`); run `cmd statusbar collapse` first.
