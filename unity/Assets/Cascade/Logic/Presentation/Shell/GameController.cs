using System;
using System.Collections.Generic;
using Cascade.Config;
using Cascade.Core;
using Cascade.Gameplay;
using Cascade.Generation;
using Cascade.Platform.Abstractions;

namespace Cascade.Presentation.Shell
{
    /// <summary>One goal of the level as a screen shows it: text, progress and how far along it is.</summary>
    public sealed record GoalLine(string Text, int Progress, int Target)
    {
        /// <summary>0 to 1.</summary>
        public double Fraction => Target <= 0 ? 1.0 : Math.Min(1.0, Math.Max(0.0, (double)Progress / Target));

        public bool Done => Progress >= Target;

        /// <summary>For example "250/1,000".</summary>
        public string ProgressText => Fmt.Pair(Progress, Target);
    }

    /// <summary>How a level ended, kept for the result screen (the session may be replaced while the screen is up).</summary>
    public sealed record LevelResult(
        int Number,
        bool Won,
        int Stars,
        int Score,
        int MovesUsed,
        int PreviousBestStars,
        IReadOnlyList<GoalLine> Goals)
    {
        /// <summary>The win beat the player's best stars for this level.</summary>
        public bool NewBest => Won && Stars > PreviousBestStars;
    }

    /// <summary>
    /// Owns the play-session lifecycle (TRD 12.3, PRD 2.7, 3.1): starts level N when <see cref="ScreenFlow"/>
    /// asks for it, generates it through <see cref="IJobRunner"/>, builds the <see cref="Gameplay.Session"/>, passes
    /// swaps to it, records wins and tells the flow when to show the result. No Unity, no UI.
    /// <para><b>Seeds.</b> <see cref="PlayerProgress.StartLevel"/> decides: the frontier level reuses its saved
    /// seed (a retry gives the same level), an earlier level gets a fresh unsaved one. A dev jump always
    /// draws a fresh seed from <see cref="ISeedSource"/> and saves nothing.</para>
    /// <para><b>Generation.</b> Jobs run one at a time, never overlapped, because
    /// <c>GeneratingLevelSource</c> is not thread-safe: a foreground job (the level the player is waiting
    /// for) goes in front of a background pre-generation job, and never interrupts a running job. The newest
    /// foreground request replaces older queued ones; a newer pre-generation request replaces an older queued
    /// one. A finished level lives in the source's own (number, seed) cache, so the retry or the pre-generated
    /// next level returns instantly. While a level is being prepared <see cref="PreparingLevel"/> is set
    /// and <see cref="PreparingText"/> reads "Preparing level N".</para>
    /// <para><b>Ending a level.</b> A win is recorded (and saved through <see cref="PlayerProgress.Changed"/>)
    /// the moment the session is won, but the flow only moves to Result when the view calls
    /// <see cref="ShowResult"/> after it has played the last animation. <see cref="LevelEnded"/> tells the
    /// view when to start waiting. After <see cref="ShowResult"/> the next level is pre-generated
    /// when the frontier advanced.</para>
    /// <para>Threading: every member must be called on the main thread; only the generation work itself
    /// runs elsewhere (and it only touches the level source).</para>
    /// </summary>
    public sealed class GameController : IDisposable
    {
        const int PreparedMemory = 8;

        sealed class Job
        {
            public int Number;
            public ulong Seed;
            public bool Foreground;
            public readonly List<Action<Level>> Done = new List<Action<Level>>();
            public readonly List<Action<Exception>> Failed = new List<Action<Exception>>();
        }

        readonly PlayerProgress _progress;
        readonly ILevelSource _levels;
        readonly ISeedSource _seeds;
        readonly DialRegistry _dials;
        readonly IJobRunner _jobs;
        readonly IProgressSink _sink;
        readonly ILog? _log;
        readonly List<Job> _queue = new List<Job>();
        readonly List<(int Number, ulong Seed)> _prepared = new List<(int, ulong)>();
        Job? _running;
        int _token;
        bool _resultPending;
        bool _disposed;

        /// <param name="colorNames">Names the goal texts use for the colors (the style's palette names); null uses <see cref="Goal.ColorNames"/>.</param>
        public GameController(
            PlayerProgress progress, ILevelSource levels, ISeedSource seeds, DialRegistry dials,
            IJobRunner jobs, ScreenFlow flow, IProgressSink? sink = null, ILog? log = null,
            IReadOnlyList<string>? colorNames = null)
        {
            _progress = progress;
            _levels = levels;
            _seeds = seeds;
            _dials = dials;
            _jobs = jobs;
            Flow = flow;
            _sink = sink ?? NullProgressSink.Instance;
            _log = log;
            ColorNames = colorNames ?? Goal.ColorNames;
            _progress.Changed += OnProgressChanged;
            _dials.Changed += OnDialChanged;
            Flow.LevelRequested += OnLevelRequested;
            Flow.ScreenChanged += OnScreenChanged;
        }

        public ScreenFlow Flow { get; }
        public PlayerProgress Progress => _progress;
        public IReadOnlyList<string> ColorNames { get; }

        // -- state ----------------------------------------------------------------------------

        /// <summary>The level of the last successful preparation (intro, play and result show it); null before the first.</summary>
        public Level? Level { get; private set; }

        /// <summary>The running (or just ended) session of <see cref="Level"/>; null before the first level.</summary>
        public Session? Session { get; private set; }

        /// <summary>The level number the player is waiting for, or null when nothing is being prepared for the screen.</summary>
        public int? PreparingLevel { get; private set; }

        /// <summary>"Preparing level N" while <see cref="PreparingLevel"/> is set, otherwise empty.</summary>
        public string PreparingText => PreparingLevel.HasValue ? "Preparing level " + Fmt.Plain(PreparingLevel.Value) : "";

        /// <summary>The reason the last preparation failed (the flow went back to the map); null after a success.</summary>
        public string? LastError { get; private set; }

        /// <summary>How the last level ended; null until one has.</summary>
        public LevelResult? LastResult { get; private set; }

        /// <summary>The session is over and <see cref="ShowResult"/> has not been called yet; swaps are refused.</summary>
        public bool ResultPending => _resultPending;

        /// <summary>True if the level (number, seed) finished generating in this run (among the last few).</summary>
        public bool IsPrepared(int number, ulong seed) => _prepared.Contains((number, seed));

        /// <summary>True if the frontier level (the "Next level" after a win) is already generated.</summary>
        public bool NextLevelReady =>
            _progress.Data.CurrentSeed.HasValue && IsPrepared(_progress.CurrentLevel, _progress.Data.CurrentSeed.Value);

        // -- events ---------------------------------------------------------------------------

        /// <summary>A level and its session are ready (raised just before the flow moves to the intro).</summary>
        public event Action? LevelReady;

        /// <summary>Preparing failed; the argument is the message. Raised just before the flow returns to the map.</summary>
        public event Action<string>? LoadFailed;

        /// <summary>A swap was played (the view animates the result's events). Raised before <see cref="LevelEnded"/>.</summary>
        public event Action<MoveResult>? MovePlayed;

        /// <summary>The session just ended (won or lost). Play the last animation, then call <see cref="ShowResult"/>.</summary>
        public event Action<LevelResult>? LevelEnded;

        /// <summary>Anything a screen could show changed (preparing state, session, result).</summary>
        public event Action? Changed;

        // -- play -----------------------------------------------------------------------------

        /// <summary>
        /// The view's swap request. Returns what the session did, or null if input is not accepted now (no
        /// session, not on the Play page, paused, dev overlay open, or the level already ended).
        /// A played move raises <see cref="MovePlayed"/>; a move that ends the level also records a win and
        /// raises <see cref="LevelEnded"/>.
        /// </summary>
        public MoveResult? TrySwap(CellId a, CellId b)
        {
            var session = Session;
            if (session == null || !Flow.InputEnabled || _resultPending) return null;
            var result = session.TrySwap(a, b);
            if (!result.Played) return result;
            LevelResult? ended = result.Status == SessionStatus.Playing ? null : EndLevel(session);
            MovePlayed?.Invoke(result);
            if (ended != null) LevelEnded?.Invoke(ended);
            Changed?.Invoke();
            return result;
        }

        /// <summary>A good legal move for the hint button, or null (level over, paused, nothing legal).</summary>
        public (CellId A, CellId B)? Hint() => Flow.InputEnabled && !_resultPending ? Session?.Hint() : null;

        /// <summary>
        /// The view calls this when the last animation of a finished level is done: the flow moves to Result
        /// and, after a win that advanced the frontier, the next level is pre-generated in the background.
        /// Harmless if the player already left the Play page.
        /// </summary>
        public void ShowResult()
        {
            if (!_resultPending) return;
            _resultPending = false;
            var result = LastResult;
            Flow.LevelEnded(result != null && result.Won);
            if (_prefetchAfterResult is { } next) Prefetch(next.Number, next.Seed);
            _prefetchAfterResult = null;
            Changed?.Invoke();
        }

        (int Number, ulong Seed)? _prefetchAfterResult;

        LevelResult EndLevel(Session session)
        {
            bool won = session.Status == SessionStatus.Won;
            int number = session.Level.Number;
            int previousBest = _progress.StarsFor(number);
            int frontierBefore = _progress.CurrentLevel;
            if (won) _progress.RecordWin(number, session.Stars);

            var result = new LevelResult(number, won, session.Stars, session.Score, session.MovesUsed, previousBest, Lines(session));
            LastResult = result;
            _resultPending = true;
            _prefetchAfterResult = won && _progress.CurrentLevel != frontierBefore && _progress.Data.CurrentSeed.HasValue
                ? (_progress.CurrentLevel, _progress.Data.CurrentSeed.Value)
                : ((int, ulong)?)null;
            return result;
        }

        /// <summary>The goals of a session as screen lines, with the controller's color names.</summary>
        public IReadOnlyList<GoalLine> Lines(Session session)
        {
            var progress = session.Progress;
            var lines = new List<GoalLine>(session.Goals.Count);
            for (int i = 0; i < session.Goals.Count; i++)
            {
                var g = session.Goals[i];
                lines.Add(new GoalLine(g.Describe(ColorNames), progress[i], g.Target));
            }
            return lines;
        }

        /// <summary>
        /// Writes progress and settings now (call on app pause, TRD 13.6). Normal changes are already saved
        /// as they happen; this is the belt and braces.
        /// </summary>
        public void Flush(PlayerSettings? settings = null)
        {
            _sink.SaveProgress(_progress.Data);
            if (settings != null) _sink.SaveSettings(settings);
        }

        // -- flow wiring ----------------------------------------------------------------------

        void OnLevelRequested(LevelRequest request)
        {
            int token = ++_token;
            LastError = null;
            _resultPending = false;
            PreparingLevel = request.Level;
            ulong seed = request.Kind == LevelRequestKind.DevJump ? _seeds.NextSeed() : _progress.StartLevel(request.Level);
            int number = request.Level;
            Enqueue(number, seed, foreground: true,
                level => OnLoaded(token, level),
                error => OnLoadFailed(token, number, error));
            Changed?.Invoke();
        }

        void OnScreenChanged(ScreenId from, ScreenId to)
        {
            // Leaving Loading for anything but the intro (Back, cancel) abandons the request.
            if (from == ScreenId.Loading && to != ScreenId.Intro)
            {
                _token++;
                PreparingLevel = null;
                Changed?.Invoke();
            }
        }

        void OnLoaded(int token, Level level)
        {
            if (token != _token) return; // superseded or cancelled
            PreparingLevel = null;
            Level = level;
            Session = new Session(level, RuleSets.From(_dials), RuleSets.WeightsFrom(_dials));
            LevelReady?.Invoke();
            Changed?.Invoke();
            Flow.LevelReady();
        }

        void OnLoadFailed(int token, int number, Exception error)
        {
            _log?.Error("level " + Fmt.Plain(number) + " could not be prepared: " + error.Message);
            if (token != _token) return;
            PreparingLevel = null;
            LastError = error.Message;
            LoadFailed?.Invoke(error.Message);
            Changed?.Invoke();
            Flow.LoadFailed();
        }

        void OnProgressChanged(SaveData data) => _sink.SaveProgress(data);

        void OnDialChanged(DialChange change)
        {
            var session = Session;
            if (session == null) return;
            session.Rules = RuleSets.From(_dials);
            session.Weights = RuleSets.WeightsFrom(_dials);
        }

        // -- generation queue -----------------------------------------------------------------

        void Prefetch(int number, ulong seed)
        {
            Enqueue(number, seed, foreground: false, null, error =>
                _log?.Warn("pre-generating level " + Fmt.Plain(number) + " failed: " + error.Message));
        }

        void Enqueue(int number, ulong seed, bool foreground, Action<Level>? done, Action<Exception>? failed)
        {
            Job? job = null;
            if (_running != null && _running.Number == number && _running.Seed == seed) job = _running;
            else
            {
                foreach (var queued in _queue)
                    if (queued.Number == number && queued.Seed == seed) { job = queued; break; }
            }

            if (job == null)
            {
                // The newest request of each kind replaces older queued ones; a running job is never touched.
                _queue.RemoveAll(j => j.Foreground == foreground);
                job = new Job { Number = number, Seed = seed, Foreground = foreground };
                if (foreground) _queue.Insert(0, job); else _queue.Add(job);
            }
            else if (foreground && !job.Foreground && _queue.Remove(job))
            {
                _queue.RemoveAll(j => j.Foreground);
                job.Foreground = true;
                _queue.Insert(0, job);
            }

            if (done != null) job.Done.Add(done);
            if (failed != null) job.Failed.Add(failed);
            Pump();
        }

        void Pump()
        {
            if (_disposed || _running != null || _queue.Count == 0) return;
            var job = _queue[0];
            _queue.RemoveAt(0);
            _running = job;
            _jobs.Run(() => _levels.Get(job.Number, job.Seed), level => Finish(job, level, null), error => Finish(job, null, error));
        }

        void Finish(Job job, Level? level, Exception? error)
        {
            _running = null;
            try
            {
                if (level != null)
                {
                    _prepared.Remove((job.Number, job.Seed));
                    if (_prepared.Count >= PreparedMemory) _prepared.RemoveAt(0);
                    _prepared.Add((job.Number, job.Seed));
                    foreach (var done in job.Done.ToArray()) done(level);
                }
                else
                {
                    foreach (var failed in job.Failed.ToArray()) failed(error!);
                }
            }
            finally
            {
                Pump();
            }
        }

        public void Dispose()
        {
            _disposed = true;
            _progress.Changed -= OnProgressChanged;
            _dials.Changed -= OnDialChanged;
            Flow.LevelRequested -= OnLevelRequested;
            Flow.ScreenChanged -= OnScreenChanged;
        }
    }
}
