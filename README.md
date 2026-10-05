# Cascade

A match-3 puzzle game whose levels are generated on the fly from a tunable difficulty curve,
instead of being authored or pre-generated in advance.

## Status

The game is built on **Unity 6 and C#**, with Android as a first-class target
([ADR-006](docs/adr/ADR-006-unity-engine.md)). The milestones are in [TRD §10](docs/TRD.md)
(U0-U9). [conformance/](conformance/README.md) holds the reference fixtures the C# code is checked
against. **U0 and U1 are done** on the emulator (the S23 checks are pending); the logic layers (U2, U3) are built and tested; **U4 (playable in Unity, mouse, Editor and Windows build), U5 (dials page and game shell) and U6 (K: Pebbles art pass, [style doc](docs/art/STYLE-K-Pebbles.md)) are done**; **U7 (Android) is partly signed off**: touch, rotation and portrait/landscape work on the S23; the rest of the [TRD 13.11](docs/TRD.md) checklist is open. Still to check on the Galaxy S23 itself: the 120 fps target (TRD 13.9: build a dev APK and run it with `-showfps`) and the Vulkan path.

## Unity build

```powershell
dotnet test tools/Cascade.Tests.Headless          # logic tests, no Unity needed
& "$env:ProgramFiles\Unity\Hub\Editor\6000.6.4f1\Editor\Unity.exe" -batchmode -nographics -quit `
  -projectPath unity -buildTarget Android -executeMethod Cascade.Editor.BuildScript.BuildAndroid -dev -logFile Builds/build.log
# Windows player: ... -buildTarget StandaloneWindows64 -executeMethod Cascade.Editor.BuildScript.BuildWindows  (Builds/Windows/cascade.exe;
#   `cascade.exe -level 40 -seed 7` starts at level 40; `-autoplay 10` plays 10 hint moves, checks the view and quits; `-screen title|map|settings|dev` opens a page; `-datadir PATH` chooses the save folder, `-level`/`-autoplay` runs use a scratch folder; `-shot PATH` saves a screenshot of the window and quits; `-showfps` shows a frame-rate readout in dev builds)
# PlayMode tests (real mouse events, screenshots in Builds/shots): Unity -batchmode -projectPath unity -runTests -testPlatform PlayMode -testResults Builds/playmode.xml
# APK: Builds/Android/cascade-<version>-<hash>-dev.apk (sideload with adb install -r)
```

See [TRD §14](docs/TRD.md) for the dev environment, the command-line builds and the repo layout.
Build output goes to `Builds/`, which is ignored by the repo.

## Design docs

- [MRD](docs/MRD.md): market requirements
- [PRD](docs/PRD.md): product requirements
- [TRD](docs/TRD.md): technical requirements
- [Art styles](docs/art/README.md): candidate 3D looks I, J, K
- [ADRs](docs/adr/README.md): architecture decision records
