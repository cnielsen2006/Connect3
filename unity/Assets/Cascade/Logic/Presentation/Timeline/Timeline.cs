using System;
using System.Collections.Generic;

namespace Cascade.Presentation
{
    /// <summary>
    /// The animation of one event list as a flat, immutable list of items sorted by start time (what
    /// <see cref="TimelinePlanner.Plan"/> returns, TRD 12.1). It has no engine types: the Unity
    /// <c>TimelinePlayer</c> advances a <see cref="TimelineSampler"/> by the frame time and applies what it returns.
    /// </summary>
    public sealed class Timeline
    {
        public static readonly Timeline Empty = new Timeline(new TimelineItem[0], 0, 0, new string[0]);

        internal Timeline(IReadOnlyList<TimelineItem> unsorted, double totalMs, double endMs, IReadOnlyList<string> warnings)
        {
            var list = new List<TimelineItem>(unsorted);
            for (int i = 0; i < list.Count; i++) list[i].Order = i;
            list.Sort((a, b) =>
            {
                int c = a.StartMs.CompareTo(b.StartMs);
                return c != 0 ? c : a.Order.CompareTo(b.Order);
            });
            Items = list;
            TotalMs = totalMs;
            EndMs = Math.Max(endMs, totalMs);
            Warnings = warnings;
        }

        /// <summary>Every item, sorted by <see cref="TimelineItem.StartMs"/>, ties in the order the planner emitted them.</summary>
        public IReadOnlyList<TimelineItem> Items { get; }

        /// <summary>
        /// When the board has settled: every tween, clear, wash and landing is over. The
        /// <see cref="SyncToFinalBoard"/> marker sits here; input may unlock and a queued swap may play. It
        /// shortens with the animation speed. Score popups may overhang it (see <see cref="EndMs"/>).
        /// </summary>
        public double TotalMs { get; }

        /// <summary>When the last effect has faded: <see cref="TotalMs"/> or later (a score popup outlives the settle).</summary>
        public double EndMs { get; }

        /// <summary>Inconsistencies found while planning (an event naming a tile the planner has not seen). Empty for a consistent event log; the scene is still synced at the end.</summary>
        public IReadOnlyList<string> Warnings { get; }

        public IEnumerable<T> Of<T>() where T : TimelineItem
        {
            foreach (var i in Items) if (i is T t) yield return t;
        }
    }

    /// <summary>The value of one pose property of one piece at a time.</summary>
    public readonly struct PoseSample
    {
        public PoseSample(int tileId, PoseProperty property, Float2 value) { TileId = tileId; Property = property; Value = value; }
        public int TileId { get; }
        public PoseProperty Property { get; }
        /// <summary>The value; for every property but <see cref="PoseProperty.Position"/> read <see cref="Scalar"/>.</summary>
        public Float2 Value { get; }
        public double Scalar => Value.X;
    }

    /// <summary>A live effect and its progress.</summary>
    public readonly struct FxSample
    {
        public FxSample(ShowFx fx, double u) { Fx = fx; U = u; }
        public ShowFx Fx { get; }
        /// <summary>Progress 0..1 of the effect.</summary>
        public double U { get; }
    }

    /// <summary>What to apply for one time step (see <see cref="TimelineSampler.Advance"/>). Apply in this order: Fired, FxStarted, Poses, Fx, FxEnded.</summary>
    public sealed class SampleFrame
    {
        internal SampleFrame(double timeMs, List<Instant> fired, List<ShowFx> started, List<ShowFx> ended, List<PoseSample> poses, List<FxSample> fx, bool settled, bool finished)
        {
            TimeMs = timeMs; Fired = fired; FxStarted = started; FxEnded = ended; Poses = poses; Fx = fx; Settled = settled; Finished = finished;
        }

        public double TimeMs { get; }
        /// <summary>Instant actions whose time passed since the previous call, in timeline order (create pieces, change color/special, remove pieces, score, sync).</summary>
        public IReadOnlyList<Instant> Fired { get; }
        /// <summary>Effects that started since the previous call (create their visuals); an effect that started and ended within the step is in neither list.</summary>
        public IReadOnlyList<ShowFx> FxStarted { get; }
        /// <summary>Effects that ended since the previous call (destroy their visuals).</summary>
        public IReadOnlyList<ShowFx> FxEnded { get; }
        /// <summary>
        /// The current value of every pose property that any started tween drives: the value of the latest
        /// started tween per (piece, property), whether it is still running or finished (a finished tween
        /// holds its final value). Pieces removed by a <see cref="RemovePiece"/> are left out.
        /// </summary>
        public IReadOnlyList<PoseSample> Poses { get; }
        /// <summary>Effects alive now, with their progress.</summary>
        public IReadOnlyList<FxSample> Fx { get; }
        /// <summary>The time is at or past <see cref="Timeline.TotalMs"/>: the board has settled (the sync marker has fired).</summary>
        public bool Settled { get; }
        /// <summary>The time is at or past <see cref="Timeline.EndMs"/>: nothing more will happen.</summary>
        public bool Finished { get; }
    }

    /// <summary>
    /// Pure, resumable evaluation of a <see cref="Timeline"/>: the Unity player calls
    /// <see cref="Advance"/> with the current timeline time each frame and applies the returned frame.
    /// Pause = stop calling (or keep the same time); speed = scale the time you pass in (the planner already
    /// divided durations by the player's animation speed; do not scale again for that). Jumping to a later
    /// time fires everything in between once; passing an earlier time restarts the sampler from 0.
    /// </summary>
    public sealed class TimelineSampler
    {
        readonly Timeline _timeline;
        int _next;
        double _prev = double.NegativeInfinity;
        readonly HashSet<int> _removed = new HashSet<int>();

        public TimelineSampler(Timeline timeline) { _timeline = timeline; }

        public Timeline Timeline => _timeline;

        /// <summary>The time of the last <see cref="Advance"/> (negative infinity before the first).</summary>
        public double TimeMs => _prev;

        /// <summary>Forget everything; the next <see cref="Advance"/> fires from the beginning again.</summary>
        public void Reset()
        {
            _next = 0;
            _prev = double.NegativeInfinity;
            _removed.Clear();
        }

        public SampleFrame Advance(double tMs)
        {
            if (tMs < _prev) Reset();
            var items = _timeline.Items;
            var fired = new List<Instant>();
            var started = new List<ShowFx>();
            var ended = new List<ShowFx>();
            double prev = _prev;

            while (_next < items.Count && items[_next].StartMs <= tMs)
            {
                var it = items[_next++];
                if (it is Instant inst)
                {
                    fired.Add(inst);
                    if (inst is RemovePiece rp) _removed.Add(rp.TileId);
                }
                else if (it is ShowFx fx && fx.EndMs > tMs) started.Add(fx);
            }

            var poses = new List<PoseSample>();
            var index = new Dictionary<(int, PoseProperty), int>(); // lookup only
            var fxLive = new List<FxSample>();
            for (int i = 0; i < _next; i++)
            {
                var it = items[i];
                if (it is Tween tw)
                {
                    if (_removed.Contains(tw.TileId)) continue;
                    var key = (tw.TileId, tw.Property);
                    var sample = new PoseSample(tw.TileId, tw.Property, tw.ValueAt(tMs));
                    if (index.TryGetValue(key, out int at)) poses[at] = sample;
                    else { index[key] = poses.Count; poses.Add(sample); }
                }
                else if (it is ShowFx fx)
                {
                    if (fx.EndMs > tMs)
                        fxLive.Add(new FxSample(fx, fx.DurationMs <= 0 ? 1.0 : Math.Min(1.0, Math.Max(0.0, (tMs - fx.StartMs) / fx.DurationMs))));
                    else if (fx.StartMs <= prev && fx.EndMs > prev) ended.Add(fx); // alive at the previous call, over now
                }
            }
            _prev = tMs;
            return new SampleFrame(tMs, fired, started, ended, poses, fxLive,
                tMs >= _timeline.TotalMs, tMs >= _timeline.EndMs);
        }
    }
}
