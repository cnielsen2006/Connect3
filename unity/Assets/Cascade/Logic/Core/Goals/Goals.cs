using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Cascade.Core
{
    /// <summary>The kinds of level goal (PRD §2.5).</summary>
    public enum GoalKind
    {
        /// <summary>Reach a total score.</summary>
        Score,
        /// <summary>Clear a number of tiles of the goal's colors.</summary>
        Collect,
    }

    /// <summary>
    /// One level goal. A <see cref="GoalKind.Collect"/> goal counts cleared tiles of
    /// <see cref="Colors"/>. Validated on construction; immutable.
    /// </summary>
    public sealed record Goal
    {
        /// <summary>Neutral default color names for text; the view may show its own palette names instead.</summary>
        public static readonly string[] ColorNames = { "Red", "Blue", "Green", "Yellow", "Purple", "Orange", "Pink", "Teal" };

        public Goal(GoalKind kind, int target, ValueList<int>? colors = null)
        {
            colors ??= ValueList<int>.Empty;
            if (kind != GoalKind.Score && kind != GoalKind.Collect) throw new ArgumentException("unknown goal kind " + kind);
            if (target < 1) throw new ArgumentException("goal target must be >= 1");
            if (kind == GoalKind.Collect && colors.Count == 0) throw new ArgumentException("a collect goal needs at least one color");
            Kind = kind;
            Target = target;
            Colors = colors;
        }

        public GoalKind Kind { get; }
        public int Target { get; }
        /// <summary>Collect only: the colors whose cleared tiles count (empty for a score goal).</summary>
        public ValueList<int> Colors { get; }

        /// <summary>A score goal.</summary>
        public static Goal Score(int target) => new Goal(GoalKind.Score, target);

        /// <summary>A collect goal for the given colors.</summary>
        public static Goal Collect(int target, params int[] colors) => new Goal(GoalKind.Collect, target, colors);

        /// <summary>
        /// Raw progress toward this goal (not clamped to the target). <paramref name="collected"/> holds
        /// the number of cleared tiles per color index (colors beyond its length count as 0).
        /// </summary>
        public int Amount(int score, IReadOnlyList<int> collected)
        {
            if (Kind == GoalKind.Score) return score;
            int sum = 0;
            foreach (int c in Colors) if (c >= 0 && c < collected.Count) sum += collected[c];
            return sum;
        }

        /// <summary>Human-readable text, for example "Score 1,000 points" or "Clear 12 Red / Blue pieces".</summary>
        public string Describe(IReadOnlyList<string>? colorNames = null)
        {
            if (Kind == GoalKind.Score) return "Score " + Target.ToString("N0", CultureInfo.InvariantCulture) + " points";
            var names = colorNames ?? ColorNames;
            var sb = new StringBuilder("Clear ").Append(Target.ToString(CultureInfo.InvariantCulture)).Append(' ');
            for (int i = 0; i < Colors.Count; i++)
            {
                if (i > 0) sb.Append(" / ");
                sb.Append(names[MathX.FloorMod(Colors[i], names.Count)]);
            }
            return sb.Append(" pieces").ToString();
        }

        /// <summary>JSON object: <c>{"kind","target","colors"}</c> with the kind as "score" or "collect".</summary>
        public void WriteJson(JsonWriter w)
        {
            w.BeginObject().Member("kind", Kind == GoalKind.Score ? "score" : "collect").Member("target", Target);
            JsonCodec.WriteInts(w.Name("colors"), Colors);
            w.EndObject();
        }

        public static Goal FromJson(JsonValue v)
        {
            string k = v["kind"].AsString();
            GoalKind kind = k == "score" ? GoalKind.Score : k == "collect" ? GoalKind.Collect
                : throw new FormatException("unknown goal kind \"" + k + "\"");
            var colors = v.TryGet("colors");
            return new Goal(kind, v["target"].AsInt(), colors == null ? ValueList<int>.Empty : JsonCodec.ReadInts(colors));
        }
    }

    /// <summary>Goal progress helpers over a score and per-color cleared counts.</summary>
    public static class GoalProgress
    {
        /// <summary>Per-goal progress, each clamped to its target.</summary>
        public static int[] Progress(IEnumerable<Goal> goals, int score, IReadOnlyList<int> collected)
        {
            var list = new List<int>();
            foreach (var g in goals) list.Add(Math.Min(g.Target, g.Amount(score, collected)));
            return list.ToArray();
        }

        /// <summary>True if every goal is reached.</summary>
        public static bool AllMet(IEnumerable<Goal> goals, int score, IReadOnlyList<int> collected)
        {
            foreach (var g in goals) if (g.Amount(score, collected) < g.Target) return false;
            return true;
        }
    }

    /// <summary>
    /// Accumulates score and per-color cleared counts from events, so progress works the same for
    /// the game and the bot. It keeps a tile id to color map: built from the board before the first
    /// move, extended by Spawned events, rewritten by Shuffled events and pruned as tiles are Cleared.
    /// </summary>
    public sealed class GoalTracker
    {
        int[] _colorOf; // indexed by tile id; -1 = unknown or cleared
        int[] _collected; // indexed by color

        public GoalTracker(IReadOnlyList<Goal> goals, Board board)
        {
            Goals = goals;
            _colorOf = new int[Math.Max(16, board.NextTileId)];
            for (int i = 0; i < _colorOf.Length; i++) _colorOf[i] = -1;
            _collected = new int[Math.Max(board.Colors, 1)];
            foreach (var (_, t) in board.Occupied()) SetColor(t.Id, t.Color);
        }

        GoalTracker(IReadOnlyList<Goal> goals, int score, int[] colorOf, int[] collected)
        {
            Goals = goals; Score = score; _colorOf = colorOf; _collected = collected;
        }

        public IReadOnlyList<Goal> Goals { get; }

        /// <summary>Total points scored so far.</summary>
        public int Score { get; private set; }

        /// <summary>Cleared tile counts by color index.</summary>
        public IReadOnlyList<int> Collected => _collected;

        void SetColor(int tileId, int color)
        {
            if (tileId >= _colorOf.Length)
            {
                int old = _colorOf.Length;
                Array.Resize(ref _colorOf, Math.Max(tileId + 1, old * 2));
                for (int i = old; i < _colorOf.Length; i++) _colorOf[i] = -1;
            }
            _colorOf[tileId] = color;
        }

        /// <summary>Folds a batch of events into the totals.</summary>
        public void Apply(IEnumerable<Event> events)
        {
            foreach (var e in events)
            {
                switch (e)
                {
                    case Scored s:
                        Score += s.Points;
                        break;
                    case Spawned sp:
                        SetColor(sp.TileId, sp.Color);
                        break;
                    case Shuffled sh:
                        foreach (var (tileId, color) in sh.Mapping) SetColor(tileId, color);
                        break;
                    case Cleared cl:
                        foreach (int tileId in cl.TileIds)
                        {
                            int color = tileId < _colorOf.Length ? _colorOf[tileId] : -1;
                            if (color < 0) continue;
                            _colorOf[tileId] = -1;
                            if (color >= _collected.Length) Array.Resize(ref _collected, color + 1);
                            _collected[color]++;
                        }
                        break;
                }
            }
        }

        /// <summary>Per-goal progress, clamped to each target (parallel to <see cref="Goals"/>).</summary>
        public int[] Progress() => GoalProgress.Progress(Goals, Score, _collected);

        /// <summary>True if every goal is reached.</summary>
        public bool Met() => GoalProgress.AllMet(Goals, Score, _collected);

        /// <summary>Colors of collect goals that are not yet complete, ascending and distinct.</summary>
        public List<int> RemainingColors()
        {
            var seen = new SortedSet<int>();
            foreach (var g in Goals)
                if (g.Kind == GoalKind.Collect && g.Amount(Score, _collected) < g.Target)
                    foreach (int c in g.Colors) seen.Add(c);
            return new List<int>(seen);
        }

        /// <summary>An independent copy.</summary>
        public GoalTracker Copy() => new GoalTracker(Goals, Score, (int[])_colorOf.Clone(), (int[])_collected.Clone());
    }
}
