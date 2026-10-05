using System;
using System.Collections;
using System.IO;
using Cascade.App;
using Cascade.Core;
using Cascade.Gameplay;
using Cascade.Platform.Abstractions;
using Cascade.Presentation;
using Cascade.Presentation.Shell;
using Cascade.Unity.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Cascade.Tests.Play
{
    /// <summary>
    /// Loads the Boot scene and drives the whole game like a player: a test mouse and keyboard from the Input System test framework
    /// (real pointer and key events through <see cref="MouseAdapter"/> and <c>LifecycleBridge</c>), UI buttons pressed with real UI Toolkit
    /// events, screenshots of the real frame (board, tray, UI) and waits on the real clock. Every run gets its own empty data folder, so
    /// saves can be inspected and a fresh boot (<see cref="Reboot"/>) reads what the previous one wrote. Shared by the PlayMode tests.
    /// </summary>
    public sealed class PlayHarness
    {
        public const string ShotDir = @"C:\projects\Connect3\Builds\shots";

        readonly InputTestFixture input = new InputTestFixture();
        public Mouse Mouse = null!;
        public Keyboard Keyboard = null!;
        public AppBootstrap App = null!;
        public string DataDir = "";
        public Func<HostInfo, HostInfo>? HostOverride;

        /// <summary>When set, level generation goes through this gate (see <see cref="GatedJobRunner"/>).</summary>
        public GatedJobRunner? Gate;
        int width = 1280, height = 800;
        SafeArea safeArea = new SafeArea(0, 0, 0, 0);

        public Session Session => App.Session ?? throw new InvalidOperationException("no session");
        public BoardController Controller => App.Controller;
        public BoardView View => App.Board;
        public HudScreen Hud => App.Hud;
        public ScreenFlow Flow => App.Flow;
        public UiRoot Ui => App.Ui;
        public GameController Game => App.Game;

        /// <summary>Boots the game on the title screen (the way a player starts it) at the given window size.</summary>
        public IEnumerator SetUp(int w = 1280, int h = 800, Func<HostInfo, HostInfo>? host = null, bool gated = false)
        {
            Debug.Log("[PlayTest] setup");
            width = w; height = h;
            HostOverride = host;
            Gate = gated ? new GatedJobRunner() : null;
            input.Setup();
            Mouse = InputSystem.AddDevice<Mouse>();
            Keyboard = InputSystem.AddDevice<Keyboard>();
            Directory.CreateDirectory(ShotDir);
            DataDir = Path.Combine(Path.GetTempPath(), "cascade-playtest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DataDir);
            yield return Boot();
        }

        /// <summary>Boots straight into a level (menus skipped) like <c>-level N -seed S</c>.</summary>
        public IEnumerator SetUpInLevel(int level = 1, ulong seed = 20241004, int w = 1280, int h = 800)
        {
            yield return SetUp(w, h);
            yield return StartLevel(level, seed);
        }

        IEnumerator Boot()
        {
            var gate = Gate;
            BootOptions.Pending = new BootOptions
            {
                DataDir = DataDir,
                HostOverride = HostOverride,
                JobRunnerWrap = gate == null ? null : (Func<IJobRunner, IJobRunner>)(inner => { gate.Inner = inner; return gate; }),
            };
            var load = SceneManager.LoadSceneAsync("Boot");
            while (!load.isDone) yield return null;
            App = Object.FindAnyObjectByType<AppBootstrap>();
            Assert.That(App, Is.Not.Null, "AppBootstrap is in the Boot scene");
            yield return WaitUntil(() => App.Ready, 30, "the app to boot");
            Resize(width, height, safeArea);
            yield return Frames(3);
        }

        /// <summary>Starts the app again on the same data folder: a new process in all but name.</summary>
        public IEnumerator Reboot(Func<HostInfo, HostInfo>? host = null)
        {
            ReleaseTargets();
            if (host != null) HostOverride = host;
            yield return Boot();
        }

        public IEnumerator TearDown()
        {
            ReleaseTargets();
            input.TearDown();
            yield return null;
            try { if (Directory.Exists(DataDir)) Directory.Delete(DataDir, true); }
            catch (IOException) { }
        }

        /// <summary>Jumps into level <paramref name="level"/> with this seed, skipping the intro, and waits until it is on screen.</summary>
        public IEnumerator StartLevel(int level, ulong seed)
        {
            App.StartLevel(level, seed);
            yield return WaitForPlay(level);
        }

        public IEnumerator WaitForPlay(int level)
        {
            yield return WaitUntil(() => Flow.Current == ScreenId.Play && App.Stage.Active && Game.Session != null && Game.Session.Level.Number == level, 120, "level " + level + " in play");
            yield return WaitUntil(() => App.Rig.HasSlot && App.Rig.Board != null, 10, "board slot measured");
            yield return Frames(3);
        }

        public IEnumerator WaitForScreen(ScreenId id, float timeout = 60)
        {
            yield return WaitUntil(() => Flow.Current == id, timeout, "the " + id + " screen");
            yield return UiSettled();
        }

        // ---- waiting ----

        public static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        public static IEnumerator Seconds(float s)
        {
            float t = Time.realtimeSinceStartup + s;
            while (Time.realtimeSinceStartup < t) yield return null;
        }

        /// <summary>Long enough for a page fade (0.22 s) and the transitions on it.</summary>
        public static IEnumerator UiSettled() => Seconds(0.5f);

        public static IEnumerator WaitUntil(Func<bool> condition, float timeoutSeconds, string what)
        {
            float t = Time.realtimeSinceStartup + timeoutSeconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > t) Assert.Fail("timed out waiting for " + what);
                yield return null;
            }
        }

        /// <summary>Waits until the board has settled (the timeline's final sync fired).</summary>
        public IEnumerator Settled(float timeoutSeconds = 30)
        {
            yield return WaitUntil(() => !Controller.IsAnimating, timeoutSeconds, "the board to settle");
            yield return null;
        }

        // ---- offscreen rendering and screenshots ----
        // A batch-mode Editor has no Game view to capture, so the harness renders the camera and the UI into textures of a
        // chosen size (the lifecycle bridge is told that size) and composites them: what a player would see in a window of that size.

        RenderTexture? sceneRt, uiRt;
        PanelSettings? panel;

        public int Width => width;
        public int Height => height;

        /// <summary>Pretends the window has this size and safe area: the camera, the layout class, the safe-area insets and the render targets follow.</summary>
        public void Resize(int w, int h, SafeArea? safe = null)
        {
            width = w; height = h;
            if (safe.HasValue) safeArea = safe.Value;
            var doc = App.GetComponent<UIDocument>();
            panel = doc.panelSettings;
            ReleaseTargets();
            sceneRt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { name = "PlayTest scene" };
            uiRt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { name = "PlayTest ui" };
            App.Rig.UnityCamera.targetTexture = sceneRt;
            panel.targetTexture = uiRt;
            panel.clearColor = true; // an offscreen panel does not clear itself: stale pixels would pile up
            panel.colorClearValue = Color.clear;
            App.Lifecycle.Override(new ScreenInfo(w, h, safeArea));
        }

        void ReleaseTargets()
        {
            if (App != null && App.Stage != null && App.Rig != null && App.Rig.UnityCamera != null) App.Rig.UnityCamera.targetTexture = null;
            if (panel != null) { panel.targetTexture = null; panel.clearColor = false; }
            if (sceneRt != null) { sceneRt.Release(); Object.Destroy(sceneRt); sceneRt = null; }
            if (uiRt != null) { uiRt.Release(); Object.Destroy(uiRt); uiRt = null; }
        }

        static Texture2D Read(RenderTexture rt)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            return tex;
        }

        /// <summary>Writes the current frame (board and UI) to Builds/shots/name.png.</summary>
        public IEnumerator Shot(string name)
        {
            yield return null; // the previous frame has been rendered
            string path = Path.Combine(ShotDir, name + ".png");
            var scene = Read(sceneRt!);
            var ui = Read(uiRt!);
            var sp = scene.GetPixels32();
            var up = ui.GetPixels32();
            for (int i = 0; i < sp.Length; i++)
            {
                // UI Toolkit renders premultiplied alpha: out = ui + scene * (1 - ui.a)
                float ia = 1f - up[i].a / 255f;
                sp[i].r = (byte)Mathf.Min(255, up[i].r + sp[i].r * ia);
                sp[i].g = (byte)Mathf.Min(255, up[i].g + sp[i].g * ia);
                sp[i].b = (byte)Mathf.Min(255, up[i].b + sp[i].b * ia);
                sp[i].a = 255;
            }
            scene.SetPixels32(sp);
            File.WriteAllBytes(path, scene.EncodeToPNG());
            Object.Destroy(scene);
            Object.Destroy(ui);
            Debug.Log("[PlayTest] shot " + name + " " + sceneRt!.width + "x" + sceneRt.height);
        }

        // ---- the mouse and keyboard ----

        /// <summary>Unity screen position (origin bottom left) of a cell's middle.</summary>
        public Vector2 ScreenOf(CellId cell)
        {
            var p = App.Rig.Board!.ProjectCell(cell);
            return new Vector2((float)p.X, App.Rig.ScreenSize.y - (float)p.Y);
        }

        public Vector2 ScreenOf(Float2 topLeftPx) => new Vector2((float)topLeftPx.X, App.Rig.ScreenSize.y - (float)topLeftPx.Y);

        public IEnumerator MoveMouse(Vector2 pos)
        {
            input.Set(Mouse.position, pos);
            yield return null;
        }

        public IEnumerator Press(Vector2 pos)
        {
            input.Set(Mouse.position, pos);
            input.Press(Mouse.leftButton);
            yield return null;
        }

        public IEnumerator Release()
        {
            input.Release(Mouse.leftButton);
            yield return null;
        }

        public IEnumerator Click(Vector2 pos)
        {
            yield return Press(pos);
            yield return Release();
        }

        /// <summary>Presses and releases a key (Esc and F1 reach the app through the Input System).</summary>
        public IEnumerator Key(UnityEngine.InputSystem.Controls.KeyControl key)
        {
            input.Press(key);
            yield return null;
            input.Release(key);
            yield return null;
        }

        /// <summary>Press on a cell, drag in steps to the middle of another cell and release there.</summary>
        public IEnumerator Drag(CellId from, CellId to, int steps = 6)
        {
            var a = ScreenOf(from);
            var b = ScreenOf(to);
            yield return Press(a);
            for (int i = 1; i <= steps; i++)
                yield return MoveMouse(Vector2.Lerp(a, b, i / (float)steps));
            yield return Release();
        }

        public IEnumerator Scroll(Vector2 pos, float notches)
        {
            input.Set(Mouse.position, pos);
            yield return null;
            input.Set(Mouse.scroll, new Vector2(0f, 120f * notches));
            yield return null;
            input.Set(Mouse.scroll, Vector2.zero);
            yield return null;
        }

        public IEnumerator RightDrag(Vector2 from, Vector2 to, int steps = 5)
        {
            input.Set(Mouse.position, from);
            input.Press(Mouse.rightButton);
            yield return null;
            for (int i = 1; i <= steps; i++)
            {
                input.Set(Mouse.position, Vector2.Lerp(from, to, i / (float)steps));
                yield return null;
            }
            input.Release(Mouse.rightButton);
            yield return null;
        }

        /// <summary>
        /// Presses a UI Toolkit button. A panel rendered to a texture (the offscreen harness) gets no pointer events from the
        /// screen, so the click is sent as the navigation "submit" event that the button's Clickable also reacts to.
        /// </summary>
        public IEnumerator ClickElement(VisualElement e)
        {
            using (var ev = NavigationSubmitEvent.GetPooled())
            {
                ev.target = e;
                e.SendEvent(ev);
            }
            yield return Frames(2);
        }

        // ---- the game ----

        /// <summary>The first legal move of the session.</summary>
        public (CellId A, CellId B) FirstLegal()
        {
            var moves = Session.LegalMoves;
            Assert.That(moves.Count, Is.GreaterThan(0), "the board has a legal move");
            return moves[0];
        }

        /// <summary>A neighbor swap that is not legal (it would not match).</summary>
        public (CellId A, CellId B)? FindIllegal()
        {
            var topo = Session.Board.Topology;
            var legal = new System.Collections.Generic.HashSet<(int, int)>();
            foreach (var (a, b) in Session.LegalMoves) { legal.Add((a.Value, b.Value)); legal.Add((b.Value, a.Value)); }
            foreach (var a in topo.Cells)
                foreach (var b in topo.Neighbors(a))
                    if (!legal.Contains((a.Value, b.Value))) return (a, b);
            return null;
        }

        /// <summary>The view must show exactly the session's board.</summary>
        public void AssertViewMatches(string when)
        {
            string? bad = View.Verify(Session.Board);
            Assert.That(bad, Is.Null, "view differs from the session board " + when + ": " + bad);
        }

        /// <summary>Plays the suggested moves with real mouse drags until the level ends (or <paramref name="maxMoves"/>).</summary>
        public IEnumerator PlayToEnd(int maxMoves = 120)
        {
            int moves = 0;
            while (Session.IsPlaying && moves < maxMoves)
            {
                var hint = Session.Hint();
                Assert.That(hint, Is.Not.Null, "a playing level has a legal move");
                var (a, b) = hint!.Value;
                yield return Drag(a, b, 3);
                moves++;
                yield return Settled();
                yield return Frames(1);
            }
        }

        /// <summary>The session is over and the result page is up.</summary>
        public IEnumerator WaitForResult() => WaitForScreen(ScreenId.Result, 30);
    }
}
