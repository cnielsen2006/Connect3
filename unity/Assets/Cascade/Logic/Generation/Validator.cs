using System;
using System.Collections.Generic;
using System.Globalization;
using Cascade.Core;

namespace Cascade.Generation
{
    /// <summary>
    /// Static level checks (TRD §7 step 4): no matches on the board, at least one legal move, goals
    /// reachable (colors exist on the board), board connected.
    /// </summary>
    public static class Validator
    {
        /// <summary>
        /// Problems found with <paramref name="level"/>; an empty list means it is playable. Builds the
        /// starting board from the level's seed.
        /// </summary>
        public static List<string> Validate(Level level)
        {
            var problems = new List<string>();
            if (level.MoveLimit < 1) problems.Add("move_limit must be >= 1");
            if (level.Colors < 3 || level.Colors > 8) problems.Add("colors out of range: " + level.Colors.ToString(CultureInfo.InvariantCulture));
            if (level.Goals.Count == 0) problems.Add("no goals");
            foreach (var s in level.Specials)
                if (Array.IndexOf(SpecialKinds.All, s) < 0) { problems.Add("unknown specials: " + level.Specials); break; }
            if (level.Star2 > level.Star3) problems.Add("star2 must not exceed star3");
            foreach (var g in level.Goals)
            {
                if (g.Target < 1) problems.Add("goal target must be >= 1");
                if (g.Kind == GoalKind.Collect)
                    foreach (int c in g.Colors)
                        if (c < 0 || c >= level.Colors)
                        {
                            problems.Add("collect goal names a color that does not exist: " + g.Colors);
                            break;
                        }
            }
            if (problems.Count > 0) return problems;

            Board board;
            try { board = level.BuildBoard(new Streams(level.Seed)); }
            catch (InvalidOperationException e) { return new List<string> { "cannot build a board: " + e.Message }; }
            var topo = board.Topology;
            if (topo.Cells.Count < 9) problems.Add("too few cells");
            if (topo.Cells.Count > 0 && ConnectedCount(topo) != topo.Cells.Count) problems.Add("board is not connected");
            if (Match.FindRuns(board).Count > 0) problems.Add("initial board has matches");
            if (Moves.FindLegal(board).Count == 0) problems.Add("no legal move on the initial board");
            var present = new bool[Math.Max(board.Colors, 1)];
            foreach (var c in topo.Cells)
            {
                var t = board.TileAt(c);
                if (t != null && t.Color >= 0 && t.Color < present.Length) present[t.Color] = true;
            }
            foreach (var g in level.Goals)
            {
                if (g.Kind != GoalKind.Collect) continue;
                foreach (int c in g.Colors)
                    if (c < 0 || c >= present.Length || !present[c])
                    {
                        problems.Add("goal color not on the board: " + g.Colors);
                        break;
                    }
            }
            return problems;
        }

        /// <summary>True if <see cref="Validate"/> finds nothing.</summary>
        public static bool IsValid(Level level) => Validate(level).Count == 0;

        /// <summary>Number of cells reachable from the first cell through neighbor links (breadth-first).</summary>
        internal static int ConnectedCount(Topology topo)
        {
            if (topo.Cells.Count == 0) return 0;
            var seen = new bool[topo.CellCapacity];
            var queue = new Queue<CellId>();
            seen[topo.Cells[0].Value] = true;
            queue.Enqueue(topo.Cells[0]);
            int count = 1;
            while (queue.Count > 0)
            {
                foreach (var n in topo.Neighbors(queue.Dequeue()))
                    if (!seen[n.Value]) { seen[n.Value] = true; count++; queue.Enqueue(n); }
            }
            return count;
        }
    }
}
