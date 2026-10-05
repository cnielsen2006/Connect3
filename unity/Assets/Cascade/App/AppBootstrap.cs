using System;
using System.Collections.Generic;
using Cascade.Config;
using Cascade.Core;
using Cascade.Gameplay;
using Cascade.Generation;
using Cascade.Persistence;
using Cascade.Platform.Abstractions;
using Cascade.Presentation.Shell;
using Cascade.Unity.Platform;
using Cascade.Unity.View;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace Cascade.App
{
    /// <summary>
    /// Composition root (TRD 2.1): it builds the whole object graph once, wires it and then only forwards. In dependency order:
    /// host, <c>FileStorage</c>, <c>DataCatalog</c>, <see cref="DialRegistry"/> (with the dev overrides), <see cref="Profile"/> (hot reload in dev),
    /// <see cref="SaveService"/> (progress and settings load), <see cref="PlayerProgress"/>, <see cref="LiveSettings"/>, <see cref="ScreenFlow"/>,
    /// <see cref="GameController"/> (async level generation on <see cref="UnityJobRunner"/>), the view-models, <see cref="UiRoot"/> (the screens),
    /// the <see cref="PlayStage"/> (board, camera rig, input; active only during Play) and the <see cref="LifecycleBridge"/>.
    /// <para>Who does what at run time: the flow decides which page is up; this class shows the board when it enters Play, hides it on leaving,
    /// forwards Back / pause / screen changes from the lifecycle, applies dials live, calls <see cref="GameController.ShowResult"/> once the last
    /// animation has settled and saves on pause. It contains no game rule.</para>
    /// <para>Start-up options (<see cref="BootOptions"/>): <c>-level N [-seed S] [-autoplay K]</c> goes straight into a level, <c>-screen</c>
    /// opens a page, <c>-datadir</c> chooses the save folder, <c>-shot</c> takes a screenshot of the real window.</para>
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class AppBootstrap : MonoBehaviour
    {
        [SerializeField] DataCatalog catalog = null!;
        [SerializeField] PieceStyle style = null!;
        [Tooltip("Every art style (K, I, J); the player's skin setting picks one at start-up, falling back to 'style'.")]
        [SerializeField] PieceStyle[] skins = System.Array.Empty<PieceStyle>();
        [SerializeField] Camera boardCamera = null!;

        // ---- the graph, public so the dev page and tests can reach it ----
        public BootOptions Options { get; private set; } = null!;
        public HostInfo Host { get; private set; } = null!;
        public IStorage Storage { get; private set; } = null!;
        public string DataDir { get; private set; } = "";
        public DialRegistry Dials { get; private set; } = null!;
        public Profile Profile { get; private set; } = null!;
        public GeneratingLevelSource Levels { get; private set; } = null!;
        public QueuedSeedSource Seeds { get; private set; } = null!;
        public SaveService Saves { get; private set; } = null!;
        public SaveSink Sink { get; private set; } = null!;
        public PlayerProgress Progress { get; private set; } = null!;
        public LiveSettings Live { get; private set; } = null!;
        public ScreenFlow Flow { get; private set; } = null!;
        public GameController Game { get; private set; } = null!;
        public ShellViewModels Models { get; private set; } = null!;
        public UiRoot Ui { get; private set; } = null!;
        public StyleApplier Applier { get; private set; } = null!;
        public PlayStage Stage { get; private set; } = null!;
        public SoundPlayer Sound { get; private set; } = null!;
        public LifecycleBridge Lifecycle { get; private set; } = null!;
        public PieceStyle Style => style;

        // shortcuts the tests and tools use
        public Session? Session => Game.Session;
        public BoardController Controller => Stage.Controller;
        public BoardCameraRig Rig => Stage.Rig;
        public BoardView Board => Stage.View;
        public HudScreen Hud => Ui.Hud;

        /// <summary>True from the first frame the shell is built.</summary>
        public bool Ready { get; private set; }

        /// <summary>The app was asked to quit (<see cref="Application.Quit"/> does nothing in the Editor, so tests listen here).</summary>
        public event Action? QuitRequested;

        int debugUnlocked;
        int autoplayMoves;
        bool autoplayDone, skipIntroPending, resultWaiting;
        float shotAt = -1f;
        int shotFrames;

        void Start()
        {
            Options = BootOptions.Take();
            var detected = UnityHost.Detect();
            Host = Options.HostOverride != null ? Options.HostOverride(detected) : detected;
            if (Host.Dev && Host.Kind == PlatformKind.Desktop) Application.runInBackground = true;
            var log = new UnityLog();

            DataDir = Options.ResolveDataDir();
            Storage = new FileStorage(DataDir);
            Dials = DialRegistry.Load(catalog, Host, Storage);
            ApplyMsaa();
            Profile = Profile.Load(catalog, Storage);
            Levels = new GeneratingLevelSource(Dials, Profile, new SystemStopwatch(), Host.Dev);
            Seeds = new QueuedSeedSource(new CryptoSeedSource());

            Saves = new SaveService(Storage, log);
            Sink = new SaveSink(Saves);
            Progress = new PlayerProgress(Saves.LoadSave(), Seeds);
            Live = new LiveSettings(Saves.LoadSettings(), Dials);
            ApplyFrameRate();
            style = PickSkin(Live.Player.Skin);

            var checks = StackChecks.Run(catalog);
            foreach (var c in checks) Debug.Log($"[Cascade] {(c.Ok ? "OK" : "FAIL")} {c.Name}: {c.Detail}");

            Lifecycle = gameObject.AddComponent<LifecycleBridge>();
            Flow = new ScreenFlow(Host, n => Progress.IsUnlocked(n) || n == debugUnlocked);
            IJobRunner jobs = new UnityJobRunner();
            if (Options.JobRunnerWrap != null) jobs = Options.JobRunnerWrap(jobs);
            Game = new GameController(Progress, Levels, Seeds, Dials, jobs, Flow, Sink, log, ColorNames());
            Models = new ShellViewModels
            {
                Title = new TitleViewModel(Flow, Progress),
                Map = new MapViewModel(Progress, Flow),
                Loading = new LoadingViewModel(Game),
                Intro = new IntroViewModel(Game, Host.Dev),
                Hud = new HudViewModel(Game),
                Pause = new PauseViewModel(Flow),
                Result = new ResultViewModel(Game),
                Settings = new SettingsViewModel(Live, Sink, Flow),
                Dev = new DevViewModel(Dials, Game, Host),
            };

            var doc = GetComponent<UIDocument>();
            Ui = new UiRoot(doc.rootVisualElement, Flow, Models, Models.Title.Title, new TouchSizing(Dials, Host), doc.panelSettings);
            Ui.SetSkin(style.skinId);
            Ui.Settings.SetPreview(style);
            Applier = new GameObject("Style").AddComponent<StyleApplier>();
            Applier.transform.SetParent(transform, false);
            Applier.Apply(style, boardCamera);
            Applier.Refreshed += ApplyDials; // an edit of the style asset reaches the camera fit and the timings too
            Sound = gameObject.AddComponent<SoundPlayer>();
            Sound.Init(Dials, () => Live.Player.Sound);
            Stage = new PlayStage(transform, style, boardCamera, Ui.Hud, Dials, () => { var s = Lifecycle.Current; return new Vector2Int(s.Width, s.Height); }, Sound);
#if UNITY_EDITOR || DEVELOPMENT_BUILD || CASCADE_DEV
            gameObject.AddComponent<DevHotkeys>().Bind(Flow, Models.Dev);
            gameObject.AddComponent<DevAutoPlay>().Bind(this);
            var fps = gameObject.AddComponent<FpsCounter>();
            fps.Init(doc.rootVisualElement);
            fps.Visible = Options.ShowFps;
#endif
            WireEvents();
            Lifecycle.Publish(); // the first screen size and safe area reach the flow and the UI
            ApplyDials();

            Debug.Log($"[Cascade] v{Application.version} {Host.Kind}{(Host.Dev ? " dev" : "")} {Screen.width}x{Screen.height} {SystemInfo.graphicsDeviceType}; data {DataDir}; level {Progress.CurrentLevel}");
            Ready = true;
            RunStartup();
        }

        IReadOnlyList<string> ColorNames()
        {
            var pieces = style.palette.pieces;
            var names = new List<string>(pieces.Length);
            foreach (var p in pieces) names.Add(p.name);
            return names;
        }

        PieceStyle PickSkin(string id)
        {
            foreach (var s in skins)
                if (s != null && s.skinId == id) return s;
            return style;
        }

        /// <summary>The player picked another art style in Settings: it applies at once (UI colours, backdrop, light, board, camera).</summary>
        void ApplySkin()
        {
            var next = PickSkin(Live.Player.Skin);
            if (next == style) return;
            style = next;
            Ui.SetSkin(style.skinId);
            Ui.Settings.SetPreview(style);
            Applier.Apply(style, boardCamera);
            Stage.SetStyle(style, Live.Timing);
            ApplyDials();
        }

        /// <summary>The player's 30/60 choice; a non-zero <c>render.target_fps</c> dial (dev or device override) wins.</summary>
        void ApplyFrameRate() => UnityHost.ApplyFrameRate(Dials.Render.TargetFps > 0 ? Dials.Render.TargetFps : Live.Player.TargetFps);

        void ApplyMsaa()
        {
            var urp = UniversalRenderPipeline.asset;
            if (urp != null && Dials.Render.Multisamples > 0) urp.msaaSampleCount = Dials.Render.Multisamples;
        }

        // ---- wiring ----

        void WireEvents()
        {
            Lifecycle.ScreenChanged += OnScreenChanged;
            Lifecycle.Paused += OnAppPaused;
            Lifecycle.Resumed += OnAppResumed;
            Lifecycle.BackPressed += OnBack;
            Flow.LeaveAppRequested += Quit;
            Flow.ScreenChanged += OnFlowScreenChanged;
            Flow.Changed += SyncBoardState;
            Game.LoadFailed += message => Debug.LogError("[Cascade] level could not be prepared: " + message);
            Dials.Changed += OnDialChanged;
            Live.TimingChanged += _ => ApplyDials();
            Live.PlayerChanged += _ => { ApplyFrameRate(); ApplySkin(); };
            Ui.Hud.HintPressed += OnHint;

            var c = Stage.Controller;
            c.ScoreApplied += (points, depth, multiplier) =>
            {
                Models.Hud.AddScore(points);
                Models.Hud.SetCombo(multiplier, Live.Timing.Celebration.TierOf(multiplier));
            };
            c.MoveSettled += OnMoveSettled;
        }

        void OnScreenChanged(ScreenInfo info)
        {
            Flow.SetScreen(info);
            Ui.ApplyScreen(info);
        }

        void OnBack() => Flow.BackPressed();

        void OnAppPaused()
        {
            Game.Flush(Live.Player); // never rely on OnApplicationQuit (TRD 13.6)
            SyncBoardState();
            // a phone that goes to the background shows the pause menu when it comes back; on desktop a lost focus only saves
            if (Host.Kind == PlatformKind.Android && Flow.Current == ScreenId.Play) Flow.Pause();
        }

        void OnAppResumed() => SyncBoardState();

        void OnFlowScreenChanged(ScreenId from, ScreenId to)
        {
            Debug.Log($"[Cascade] screen {to}");
            if (from == ScreenId.Play && to != ScreenId.Play) Stage.End();
            if (to == ScreenId.Play) BeginPlay();
        }

        void BeginPlay()
        {
            resultWaiting = false;
            Stage.Begin(Game, Live.Timing);
            SyncBoardState();
            Models.Hud.Sync();
        }

        /// <summary>The flow says whether the board takes input and whether the clocks run.</summary>
        void SyncBoardState()
        {
            if (Stage == null || !Stage.Active) return;
            Stage.Controller.InputEnabled = Flow.InputEnabled;
            Stage.Controller.Paused = Flow.Paused || (Lifecycle != null && Lifecycle.IsPaused);
        }

        void OnDialChanged(DialChange change)
        {
            if (change.Section == "render")
            {
                ApplyFrameRate();
                ApplyMsaa();
            }
            ApplyDials();
        }

        /// <summary>Re-reads the dials the view uses (timing with the player's speed, input, camera, piece variation, grain).</summary>
        public void ApplyDials()
        {
            if (Stage == null) return;
            Stage.ApplyDials(Live.Timing);
            Applier.SetGrainAlpha(style.material.grainAlpha);
        }

        void OnHint()
        {
            if (Flow.InputEnabled && !Game.ResultPending) Stage.Controller.ShowHint();
        }

        void OnMoveSettled(MoveResult r)
        {
            Models.Hud.Sync();
            if (Game.ResultPending) resultWaiting = true;
        }

        // ---- levels ----

        /// <summary>
        /// Goes straight to level <paramref name="number"/> and plays it, skipping the intro (<c>-level</c> / <c>-seed</c>, tests, the dev page's
        /// older sibling). The map's unlock rule is waived for this level. A given seed is used for the board: it is stored as the saved seed when
        /// the level is the frontier, otherwise queued for the fresh draw.
        /// </summary>
        public void StartLevel(int number, ulong? seed = null)
        {
            if (seed.HasValue)
            {
                if (number == Progress.CurrentLevel)
                    Progress.Load(Progress.Data with { CurrentSeed = seed, GeneratorVersion = GeneratorVersion.Current });
                else
                    Seeds.Enqueue(seed.Value);
            }
            debugUnlocked = number;
            GoToMap();
            skipIntroPending = true;
            Flow.SelectLevel(number);
        }

        /// <summary>Brings the flow to the map page from wherever it is.</summary>
        public void GoToMap()
        {
            Flow.CloseDev();
            switch (Flow.Current)
            {
                case ScreenId.Map: break;
                case ScreenId.Title: Flow.Play(); break;
                case ScreenId.Settings: Flow.ToTitle(); Flow.Play(); break;
                case ScreenId.Play: Flow.Pause(); Flow.ToMap(); break;
                default: Flow.ToMap(); break;
            }
        }

        void RunStartup()
        {
            var o = Options;
            autoplayMoves = o.AutoplayMoves;
            if (o.Level.HasValue)
            {
                StartLevel(o.Level.Value, o.Seed);
            }
            else
            {
                switch (o.Screen)
                {
                    case "map": Flow.Play(); break;
                    case "settings": Flow.OpenSettings(); break;
                    case "dev": Flow.OpenDev(); break;
                }
            }
            if (o.ShotPath != null) shotAt = Time.realtimeSinceStartup + o.ShotDelaySeconds;
        }

        // ---- per frame ----

        void Update()
        {
            if (!Ready) return;
            if (skipIntroPending && Flow.Current == ScreenId.Intro)
            {
                skipIntroPending = false;
                Flow.StartLevel();
            }
            if (autoplayMoves > 0) TickAutoplay();
            PumpResult();
            TickShot();
        }

        /// <summary>When a level has ended and its last animation has settled, the result page is shown.</summary>
        void PumpResult()
        {
            if (!resultWaiting || !Game.ResultPending) { resultWaiting = false; return; }
            if (Stage.Active && Stage.Controller.IsAnimating) return;
            resultWaiting = false;
            Game.ShowResult();
            if (Game.LastResult is { } result) Sound.Play(result.Won ? Cascade.Presentation.SoundCue.Win : Cascade.Presentation.SoundCue.Lose);
        }

        // ---- -autoplay K: a self-test of a build ----

        void TickAutoplay()
        {
            if (autoplayDone) return;
            var s = Game.Session;
            if (s == null || Flow.Current != ScreenId.Play || !Stage.Active || Stage.Controller.IsAnimating) return;
            string? bad = Stage.View.Verify(s.Board);
            if (bad != null)
            {
                Debug.LogError("[Cascade] autoplay: view out of step after move " + s.MovesUsed + ": " + bad);
                autoplayDone = true;
                Quit();
                return;
            }
            if (!s.IsPlaying || s.MovesUsed >= autoplayMoves)
            {
                Debug.Log($"[Cascade] autoplay done: {s.MovesUsed} moves, score {s.Score}, status {s.Status}, view matches the board");
                autoplayDone = true;
                Quit();
                return;
            }
            var hint = s.Hint();
            if (hint == null) { Debug.LogError("[Cascade] autoplay: no legal move"); autoplayDone = true; Quit(); return; }
            Stage.Controller.RequestSwap(hint.Value.A, hint.Value.B);
        }

        // ---- -shot PATH: a screenshot of the real window (builds and the Editor's Game view) ----

        void TickShot()
        {
            if (shotAt < 0f || Time.realtimeSinceStartup < shotAt) return;
            if (shotFrames == 0)
            {
                ScreenCapture.CaptureScreenshot(Options.ShotPath!);
                Debug.Log("[Cascade] screenshot " + Options.ShotPath);
            }
            if (++shotFrames > 5) { shotAt = -1f; Quit(); }
        }

        /// <summary>Leaves the app (Application.Quit; in the Editor only <see cref="QuitRequested"/> is raised).</summary>
        public void Quit()
        {
            QuitRequested?.Invoke();
            Application.Quit();
        }

        void OnDestroy()
        {
            if (!Ready) return;
            if (Dials != null) Dials.Changed -= OnDialChanged;
            Ui?.Dispose();
            foreach (var d in new IDisposable?[] { Models.Title, Models.Map, Models.Loading, Models.Intro, Models.Hud, Models.Pause, Models.Result, Models.Settings, Models.Dev, Game, Live })
                d?.Dispose();
        }
    }
}
