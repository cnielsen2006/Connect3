using System;
using System.Collections.Generic;
using Cascade.Core;
using Cascade.Gameplay;

namespace Cascade.Generation
{
    /// <summary>A candidate level could not be made valid (should not happen with a sane profile).</summary>
    public sealed class GenerationException : Exception
    {
        public GenerationException(string message) : base(message) { }
    }

    /// <summary>
    /// LevelParams + seed to a candidate <see cref="Level"/> (TRD §7 steps 1-3). Deterministic: all
    /// randomness comes from the named streams <c>layout</c> (holes) and <c>goals</c>; the board
    /// itself comes from <c>tiles</c> when the level builds it.
    /// </summary>
    public static class LevelGenerator
    {
        /// <summary>The generator version stamped on every level (see <see cref="Gameplay.GeneratorVersion"/>).</summary>
        public const int GeneratorVersion = Gameplay.GeneratorVersion.Current;

        // -- layout ----------------------------------------------------------------------------

        /// <summary>One shape template: its name and its holes, sorted by (x, y).</summary>
        public readonly struct HoleOption
        {
            public HoleOption(string name, (int X, int Y)[] holes) { Name = name; Holes = holes; }
            public string Name { get; }
            public (int X, int Y)[] Holes { get; }
        }

        static (int X, int Y)[] Cells(int size, Func<int, int, bool> hole)
        {
            var list = new List<(int, int)>();
            for (int x = 0; x < size; x++)
                for (int y = 0; y < size; y++)
                    if (hole(x, y)) list.Add((x, y)); // x-major: already sorted by (x, y)
            return list.ToArray();
        }

        static int Edge(int v, int s) => Math.Min(v, s - 1 - v);

        /// <summary>Every symmetric shape template for a size x size board: corners, cross, ring, diamond.</summary>
        public static List<HoleOption> HoleOptions(int size)
        {
            var opts = new List<HoleOption>();
            foreach (int k in new[] { 2, 3 })
                opts.Add(new HoleOption("corners" + k, Cells(size, (x, y) => Edge(x, size) + Edge(y, size) < k)));
            foreach (int c in new[] { 2, 3 })
                opts.Add(new HoleOption("cross" + c, Cells(size, (x, y) => Edge(x, size) < c && Edge(y, size) < c)));
            for (int m = 1; m < size - 3; m++)
            {
                if ((size - m) % 2 != 0) continue;
                int lo = (size - m) / 2;
                int mm = m;
                opts.Add(new HoleOption("ring" + m, Cells(size, (x, y) => x >= lo && x < lo + mm && y >= lo && y < lo + mm)));
            }
            foreach (int e in new[] { 0, 2 })
                opts.Add(new HoleOption("diamond" + e,
                    Cells(size, (x, y) => Math.Abs(2 * x - (size - 1)) + Math.Abs(2 * y - (size - 1)) > size + e)));
            opts.RemoveAll(o => o.Holes.Length == 0);
            return opts;
        }

        /// <summary>True if the grid with these holes is one connected region (and not empty).</summary>
        public static bool IsConnected(int width, int height, IEnumerable<(int X, int Y)> holes)
        {
            var topo = Topology.Grid(width, height, holes);
            return topo.Cells.Count > 0 && Validator.ConnectedCount(topo) == topo.Cells.Count;
        }

        /// <summary>Holes for a shaped board (<c>layout</c> stream); none before the holes unlock.</summary>
        public static (int X, int Y)[] PickHoles(LevelParams p, Streams streams, GeneratorSettings g)
        {
            if (!p.Shaped) return new (int, int)[0];
            var rng = streams.Layout;
            if (!p.ForceHoles && rng.Random() >= g.HolesChance) return new (int, int)[0];
            int size = p.BoardSize;
            var options = new List<HoleOption>();
            foreach (var o in HoleOptions(size))
                if (size * size - o.Holes.Length >= g.MinCells && IsConnected(size, size, o.Holes)) options.Add(o);
            if (options.Count == 0) return new (int, int)[0];
            return rng.Choice(options).Holes;
        }

        // -- goals -----------------------------------------------------------------------------

        static int RoundTo(double value, int step) =>
            Math.Max(step, (int)Math.Round(value / step, MidpointRounding.ToEven) * step);

        static Goal ScoreGoal(LevelParams p, int moves, double scale, GeneratorSettings g)
        {
            double target = p.ScorePerMove * moves * p.GoalAmount * scale;
            return Goal.Score(RoundTo(target, g.GoalScoreRound));
        }

        static Goal CollectGoal(LevelParams p, int moves, double scale, Pcg32 rng, GeneratorSettings g)
        {
            int k = p.Colors >= 5 && rng.Random() < g.CollectTwoColorChance ? 2 : 1;
            var order = new List<int>();
            for (int i = 0; i < p.Colors; i++) order.Add(i);
            rng.Shuffle(order);
            var colors = order.GetRange(0, k);
            colors.Sort();
            double target = p.CollectPerMove * moves * p.GoalAmount * scale * k / p.Colors;
            return new Goal(GoalKind.Collect, Math.Max(g.CollectMin, MathX.RoundHalfEven(target)), colors);
        }

        /// <summary>
        /// Goal type from the weighted pool (<c>goals</c> stream); size from goal_amount and the
        /// per-move rates; the tuner refines it later.
        /// </summary>
        public static ValueList<Goal> PickGoals(LevelParams p, int moves, Streams streams, GeneratorSettings g)
        {
            var rng = streams.Goals;
            double total = 0;
            foreach (var (_, w) in p.GoalPool) total += w;
            double r = rng.Random() * total;
            string kind = p.GoalPool[p.GoalPool.Count - 1].Kind;
            foreach (var (k, w) in p.GoalPool)
            {
                if (r < w) { kind = k; break; }
                r -= w;
            }
            if (p.ForceGoal != null) kind = p.ForceGoal;
            if (kind == GoalKinds.Score) return new[] { ScoreGoal(p, moves, 1.0, g) };
            if (kind == GoalKinds.Collect) return new[] { CollectGoal(p, moves, 1.0, rng, g) };
            double s = g.ComboScale;
            var score = ScoreGoal(p, moves, s, g); // evaluated before the collect goal draws from the stream
            return new[] { score, CollectGoal(p, moves, s, rng, g) };
        }

        /// <summary>Resizes goal targets (tuner), keeping each goal's kind and colors.</summary>
        public static ValueList<Goal> ScaleGoals(IReadOnlyList<Goal> goals, double factor, GeneratorSettings g)
        {
            var list = new List<Goal>();
            foreach (var goal in goals)
            {
                int target = goal.Kind == GoalKind.Score
                    ? RoundTo(goal.Target * factor, g.GoalScoreRound)
                    : Math.Max(1, MathX.RoundHalfEven(goal.Target * factor));
                list.Add(new Goal(goal.Kind, target, goal.Colors));
            }
            return list;
        }

        // -- candidate -------------------------------------------------------------------------

        /// <summary>A validated, untuned candidate level. Star thresholds are provisional until tuned.</summary>
        public static Level MakeCandidate(LevelParams p, ulong seed, GeneratorSettings g)
        {
            var streams = new Streams(seed);
            var holes = PickHoles(p, streams, g);
            var goals = PickGoals(p, p.MoveLimit, streams, g);
            int scoreGoal = 0;
            foreach (var goal in goals) if (goal.Kind == GoalKind.Score) scoreGoal = Math.Max(scoreGoal, goal.Target);
            var level = new Level(
                p.Number, seed, p.BoardSize, p.BoardSize, holes, p.Colors, goals, p.MoveLimit,
                scoreGoal, scoreGoal + 1, p.Specials, p.Intro, p.Difficulty, p.TargetWinRate, null, GeneratorVersion);
            var problems = Validator.Validate(level);
            if (problems.Count > 0 && holes.Length > 0) // fall back to the plain rectangle
            {
                level = level with { Holes = new (int, int)[0] };
                problems = Validator.Validate(level);
            }
            if (problems.Count > 0)
                throw new GenerationException("level " + p.Number + " seed " + seed + ": " + string.Join("; ", problems));
            return level;
        }
    }
}
