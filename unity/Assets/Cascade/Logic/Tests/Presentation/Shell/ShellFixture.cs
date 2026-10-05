using System;
using System.Collections.Generic;
using Cascade.Config;
using Cascade.Core;
using Cascade.Gameplay;
using Cascade.Generation;
using Cascade.Platform.Abstractions;
using Cascade.Presentation.Shell;
using Cascade.Tests.Support;

namespace Cascade.Tests.PresentationTests
{
    /// <summary>A job runner that holds jobs until the test runs them, to prove the controller copes with real asynchrony.</summary>
    public sealed class ManualJobRunner : IJobRunner
    {
        readonly Queue<Action> _jobs = new Queue<Action>();

        /// <summary>Jobs started and not yet run. The controller must never have more than one.</summary>
        public int Pending => _jobs.Count;

        public int Started { get; private set; }

        public void Run<T>(Func<T> work, Action<T> onDone, Action<Exception> onError)
        {
            Started++;
            _jobs.Enqueue(() =>
            {
                T result;
                try { result = work(); }
                catch (Exception e) { onError(e); return; }
                onDone(result);
            });
        }

        /// <summary>Runs the oldest pending job (its callback may start another).</summary>
        public bool RunNext()
        {
            if (_jobs.Count == 0) return false;
            _jobs.Dequeue()();
            return true;
        }

        public void RunAll()
        {
            while (RunNext()) { }
        }
    }

    /// <summary>
    /// A level source with hand-made levels: no generation, so tests are instant. Records every call and
    /// the largest number of calls running at once.
    /// </summary>
    public sealed class FakeLevelSource : ILevelSource
    {
        public List<(int Number, ulong Seed)> Calls { get; } = new List<(int, ulong)>();

        /// <summary>When set, <see cref="Get"/> throws it.</summary>
        public Exception? Fail { get; set; }

        /// <summary>Levels at or above this number are unwinnable (one move, a huge goal).</summary>
        public int HardFrom { get; set; } = int.MaxValue;

        public Level Get(int levelNumber, ulong seed)
        {
            Calls.Add((levelNumber, seed));
            if (Fail != null) throw Fail;
            return levelNumber >= HardFrom ? Hard(levelNumber, seed) : Easy(levelNumber, seed);
        }

        /// <summary>One legal move scores at least 60, so a level with a 60-point goal is won by its first move.</summary>
        public static Level Easy(int n, ulong seed, string? intro = null) =>
            new Level(n, seed, 6, 6, new (int, int)[0], 4, new[] { Goal.Score(60) }, 40, 60, 1_000_000,
                new[] { "Line" }, intro, 0.25, 0.9, 0.88, 1);

        public static Level Hard(int n, ulong seed) =>
            new Level(n, seed, 6, 6, new (int, int)[0], 4, new[] { Goal.Score(1_000_000), Goal.Collect(500, 0, 1) }, 2, 2_000_000, 3_000_000,
                new string[0], null, 0.75, 0.4, null, 1);
    }

    /// <summary>Records what the controller hands to persistence.</summary>
    public sealed class RecordingSink : IProgressSink
    {
        public List<SaveData> Progress { get; } = new List<SaveData>();
        public List<PlayerSettings> Settings { get; } = new List<PlayerSettings>();
        public void SaveProgress(SaveData data) => Progress.Add(data);
        public void SaveSettings(PlayerSettings settings) => Settings.Add(settings);
    }

    /// <summary>The whole shell wired the way the composition root does it, with test doubles.</summary>
    public sealed class ShellFixture
    {
        public static readonly HostInfo DesktopDev = TestDials.Desktop;
        public static readonly HostInfo DesktopRelease = new HostInfo(PlatformKind.Desktop, true, true, false, false, 96f);
        public static readonly HostInfo Android = new HostInfo(PlatformKind.Android, false, false, true, false, 420f);

        public DialRegistry Dials { get; }
        public MemoryStorage Storage { get; } = new MemoryStorage();
        public FakeSeedSource Seeds { get; }
        public PlayerProgress Progress { get; }
        public ScreenFlow Flow { get; }
        public FakeLevelSource Source { get; } = new FakeLevelSource();
        public ManualJobRunner Runner { get; } = new ManualJobRunner();
        public RecordingSink Sink { get; } = new RecordingSink();
        public ListLog Log { get; } = new ListLog();
        public LiveSettings Settings { get; }
        public GameController Game { get; }
        /// <summary>The source the controller uses: <see cref="Source"/> unless a factory was given.</summary>
        public ILevelSource LevelSource { get; }
        public HostInfo Host { get; }

        /// <param name="immediate">Use <see cref="ImmediateJobRunner"/> instead of the manual runner.</param>
        public ShellFixture(HostInfo? host = null, SaveData? save = null, bool immediate = false, Func<DialRegistry, ILevelSource>? makeSource = null,
            PlayerSettings? settings = null)
        {
            Host = host ?? DesktopDev;
            var data = new DictionaryDataSource().With("dials.toml", RepoFiles.ReadData("dials.toml"));
            Dials = DialRegistry.Load(data, Host, Storage);
            Seeds = new FakeSeedSource(1000);
            Progress = new PlayerProgress(save ?? new SaveData(), Seeds);
            Flow = new ScreenFlow(Host, Progress.IsUnlocked);
            Settings = new LiveSettings(settings ?? new PlayerSettings(), Dials);
            IJobRunner runner = immediate ? new ImmediateJobRunner() : (IJobRunner)Runner;
            LevelSource = makeSource != null ? makeSource(Dials) : Source;
            Game = new GameController(Progress, LevelSource, Seeds, Dials, runner, Flow, Sink, Log);
        }

        /// <summary>Title to Play via the map, answering generation when the runner is manual.</summary>
        public void StartLevel(int n)
        {
            if (Flow.Current == ScreenId.Title) Flow.Play();
            Flow.SelectLevel(n);
            Runner.RunAll();
            Flow.StartLevel();
        }

        /// <summary>Plays the best hint moves until the session ends or <paramref name="maxMoves"/> run out.</summary>
        public void PlayToEnd(int maxMoves = 200)
        {
            for (int i = 0; i < maxMoves && Game.Session!.IsPlaying; i++)
            {
                var hint = Game.Hint();
                if (hint == null) break;
                Game.TrySwap(hint.Value.A, hint.Value.B);
            }
        }
    }
}
