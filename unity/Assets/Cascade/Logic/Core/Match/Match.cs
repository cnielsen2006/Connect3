using System;
using System.Collections.Generic;

namespace Cascade.Core
{
    /// <summary>What a match qualifier is: a run of 3, 4, or 5+ tiles, or a Cross of two runs.</summary>
    public enum QualifierKind
    {
        Run3,
        Run4,
        Run5,
        Cross,
    }

    /// <summary>One thing a match group counts for scoring and specials (PRD §2.3). <see cref="Length"/> is the run length; 0 for <see cref="QualifierKind.Cross"/>.</summary>
    public sealed record Qualifier(QualifierKind Kind, int Length = 0);

    /// <summary>A maximal run of at least 3 same-colored tiles along one line.</summary>
    public sealed record MatchRun(int Axis, int Color, ValueList<CellId> Cells);

    /// <summary>One match group: the superset of cells to clear (ascending), every qualifier, and the color.</summary>
    public sealed record MatchGroup(ValueList<CellId> Cells, ValueList<Qualifier> Qualifiers, int Color);

    /// <summary>Finds runs, builds match groups, lists qualifiers (TRD §3.2). Also color-safety helpers for spawning.</summary>
    public static class Match
    {
        /// <summary>The shortest run that matches.</summary>
        public const int MinRun = 3;

        /// <summary>Every maximal run of at least <see cref="MinRun"/> same-colored tiles, line by line.</summary>
        public static List<MatchRun> FindRuns(Board board)
        {
            var runs = new List<MatchRun>();
            foreach (var line in board.Topology.Lines)
            {
                int start = 0;
                var cells = line.Cells;
                int n = cells.Count;
                while (start < n)
                {
                    var t = board.TileAt(cells[start]);
                    if (t == null) { start++; continue; }
                    int end = start + 1;
                    while (end < n)
                    {
                        var u = board.TileAt(cells[end]);
                        if (u == null || u.Color != t.Color) break;
                        end++;
                    }
                    if (end - start >= MinRun) runs.Add(new MatchRun(line.Axis, t.Color, Slice(cells, start, end)));
                    start = end;
                }
            }
            return runs;
        }

        static ValueList<CellId> Slice(IReadOnlyList<CellId> cells, int lo, int hi)
        {
            var a = new CellId[hi - lo];
            for (int i = lo; i < hi; i++) a[i - lo] = cells[i];
            return ValueList<CellId>.Wrap(a);
        }

        static bool Touch(Topology topology, MatchRun a, MatchRun b)
        {
            // Different-axis runs of one color that share a cell or whose end cells are neighbors.
            if (a.Axis == b.Axis || a.Color != b.Color) return false;
            foreach (var c in a.Cells)
                foreach (var d in b.Cells)
                    if (c == d) return true;
            var aEnds = new[] { a.Cells[0], a.Cells[a.Cells.Count - 1] };
            var bFirst = b.Cells[0];
            var bLast = b.Cells[b.Cells.Count - 1];
            foreach (var e in aEnds)
                foreach (var n in topology.Neighbors(e))
                    if (n == bFirst || n == bLast) return true;
            return false;
        }

        static Qualifier QualifierFor(MatchRun run)
        {
            int n = run.Cells.Count;
            var kind = n == 3 ? QualifierKind.Run3 : n == 4 ? QualifierKind.Run4 : QualifierKind.Run5;
            return new Qualifier(kind, n);
        }

        /// <summary>
        /// Runs (of at least 3) that include any of <paramref name="cells"/>. On a board with no other
        /// matches (the state between moves) this is every run, at a fraction of the cost of <see cref="FindRuns"/>.
        /// </summary>
        public static List<MatchRun> FindRunsThrough(Board board, IEnumerable<CellId> cells)
        {
            var runs = new List<MatchRun>();
            var seen = new HashSet<(int, int)>(); // (axis, first cell): membership only, never iterated
            foreach (var cell in cells)
            {
                var t = board.TileAt(cell);
                if (t == null) continue;
                foreach (var slot in board.Topology.LinesThrough(cell))
                {
                    var lc = slot.Line.Cells;
                    int lo = slot.Index;
                    while (lo > 0 && board.TileAt(lc[lo - 1]) is Tile u1 && u1.Color == t.Color) lo--;
                    int hi = slot.Index + 1;
                    while (hi < lc.Count && board.TileAt(lc[hi]) is Tile u2 && u2.Color == t.Color) hi++;
                    if (hi - lo >= MinRun && seen.Add((slot.Line.Axis, lc[lo].Value)))
                        runs.Add(new MatchRun(slot.Line.Axis, t.Color, Slice(lc, lo, hi)));
                }
            }
            return runs;
        }

        /// <summary>All match groups of the board, ordered by their first (lowest) cell.</summary>
        public static List<MatchGroup> FindGroups(Board board) => GroupsFromRuns(board.Topology, FindRuns(board));

        /// <summary>Merges runs into groups (union-find) and lists every qualifier of each group.</summary>
        public static List<MatchGroup> GroupsFromRuns(Topology topology, IReadOnlyList<MatchRun> runs)
        {
            int count = runs.Count;
            var parent = new int[count];
            for (int i = 0; i < count; i++) parent[i] = i;
            int Find(int i)
            {
                while (parent[i] != i)
                {
                    parent[i] = parent[parent[i]];
                    i = parent[i];
                }
                return i;
            }

            var crosses = new List<(int I, int J)>();
            for (int i = 0; i < count; i++)
                for (int j = i + 1; j < count; j++)
                    if (Touch(topology, runs[i], runs[j]))
                    {
                        crosses.Add((i, j));
                        int rj = Find(j);
                        parent[Find(i)] = rj;
                    }

            // members: root -> run indexes, groups in order of the first run of each root
            var rootOrder = new List<int>();
            var members = new List<int>?[count];
            for (int i = 0; i < count; i++)
            {
                int r = Find(i);
                if (members[r] == null) { members[r] = new List<int>(); rootOrder.Add(r); }
                members[r]!.Add(i);
            }

            var groups = new List<MatchGroup>();
            foreach (int root in rootOrder)
            {
                var idxs = members[root]!;
                var inGroup = new bool[count];
                foreach (int i in idxs) inGroup[i] = true;
                var cellSet = new SortedSet<int>(); // ordered set of cell ids (ids are unique ints)
                foreach (int i in idxs) foreach (var c in runs[i].Cells) cellSet.Add(c.Value);
                var cells = new CellId[cellSet.Count];
                int k = 0;
                foreach (int v in cellSet) cells[k++] = new CellId(v);
                var quals = new List<Qualifier>();
                foreach (int i in idxs) quals.Add(QualifierFor(runs[i]));
                foreach (var (i, _) in crosses) if (inGroup[i]) quals.Add(new Qualifier(QualifierKind.Cross));
                groups.Add(new MatchGroup(ValueList<CellId>.Wrap(cells), quals, runs[idxs[0]].Color));
            }
            groups.Sort((x, y) => x.Cells[0].Value.CompareTo(y.Cells[0].Value)); // groups are disjoint: no ties
            return groups;
        }

        /// <summary>Length of the same-color run along <paramref name="cells"/> that includes position idx (assumed <paramref name="color"/>).</summary>
        static int Extent(Board board, IReadOnlyList<CellId> cells, int idx, int color)
        {
            int n = 1;
            int i = idx - 1;
            while (i >= 0 && board.TileAt(cells[i]) is Tile t1 && t1.Color == color) { n++; i--; }
            i = idx + 1;
            while (i < cells.Count && board.TileAt(cells[i]) is Tile t2 && t2.Color == color) { n++; i++; }
            return n;
        }

        /// <summary>True if any of <paramref name="cells"/> currently sits in a run of at least 3.</summary>
        public static bool HasMatchThrough(Board board, IEnumerable<CellId> cells)
        {
            foreach (var cell in cells)
            {
                var t = board.TileAt(cell);
                if (t == null) continue;
                foreach (var slot in board.Topology.LinesThrough(cell))
                    if (Extent(board, slot.Line.Cells, slot.Index, t.Color) >= MinRun) return true;
            }
            return false;
        }

        /// <summary>Longest run <paramref name="color"/> would make if placed at <paramref name="cell"/> (an existing tile there is ignored).</summary>
        public static int RunIfPlaced(Board board, CellId cell, int color)
        {
            int best = 1;
            foreach (var slot in board.Topology.LinesThrough(cell))
                best = Math.Max(best, Extent(board, slot.Line.Cells, slot.Index, color));
            return best;
        }

        /// <summary>
        /// Uniform among colors that do not complete a run of 3 at <paramref name="cell"/>; if every
        /// color would, uniform among those making the shortest run (TRD §3.4).
        /// </summary>
        public static int PickColor(Board board, CellId cell, Pcg32 rng)
        {
            int colors = board.Colors;
            var lens = new int[colors];
            var ok = new List<int>();
            for (int c = 0; c < colors; c++)
            {
                lens[c] = RunIfPlaced(board, cell, c);
                if (lens[c] < MinRun) ok.Add(c);
            }
            if (ok.Count > 0) return rng.Choice(ok);
            int shortest = int.MaxValue;
            foreach (int n in lens) shortest = Math.Min(shortest, n);
            var pool = new List<int>();
            for (int c = 0; c < colors; c++) if (lens[c] == shortest) pool.Add(c);
            return rng.Choice(pool);
        }
    }
}
