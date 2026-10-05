using System;
using System.Collections.Generic;

namespace Cascade.Core
{
    /// <summary>Line / Bomb / Color activation and earning rules (PRD §2.4, TRD §3.3).</summary>
    public static class Specials
    {
        /// <summary>One special kind name per qualifier that earns one: Run4 -> Line, Run5 -> Color, Cross -> Bomb.</summary>
        public static List<string> Earned(IEnumerable<Qualifier> qualifiers)
        {
            var list = new List<string>();
            foreach (var q in qualifiers)
            {
                if (q.Kind == QualifierKind.Run4) list.Add(SpecialKinds.Line);
                else if (q.Kind == QualifierKind.Run5) list.Add(SpecialKinds.Color);
                else if (q.Kind == QualifierKind.Cross) list.Add(SpecialKinds.Bomb);
            }
            return list;
        }

        /// <summary>Makes a special of this kind; a Line piece's axis is random (one <c>RandBelow(2)</c> draw).</summary>
        public static Special Make(string kind, Pcg32 rng)
        {
            if (kind == SpecialKinds.Line) return new Special.Line(rng.RandBelow(2));
            return kind == SpecialKinds.Bomb ? new Special.Bomb() : (Special)new Special.Color();
        }

        /// <summary>The most common tile color on the board; ties go to the lowest color id; null if the board is empty.</summary>
        public static int? MostCommonColor(Board board)
        {
            var counts = new int[Math.Max(board.Colors, 1)];
            int[] grown = counts;
            foreach (var cell in board.Topology.Cells)
            {
                var t = board.TileAt(cell);
                if (t == null) continue;
                if (t.Color >= grown.Length) Array.Resize(ref grown, t.Color + 1);
                if (t.Color >= 0) grown[t.Color]++;
            }
            int best = -1, bestN = 0;
            for (int c = 0; c < grown.Length; c++)
                if (grown[c] > bestN) { best = c; bestN = grown[c]; }
            return best < 0 ? (int?)null : best;
        }

        /// <summary>Cells hit by <paramref name="tile"/> (a special) sitting at <paramref name="cell"/>, ascending. Includes the cell itself.</summary>
        /// <param name="partnerColor">For a Color piece: the color of the tile it was swapped with, if any.</param>
        public static List<CellId> Fire(Board board, CellId cell, Tile tile, SpecialRules rules, int? partnerColor = null)
        {
            var topo = board.Topology;
            switch (tile.Special)
            {
                case Special.Line line:
                    return Sorted(topo.LineThrough(cell, line.Axis));
                case Special.Bomb _:
                    return Sorted(topo.Area(cell, rules.BombRadius));
                case Special.Color _:
                    {
                        int? target = partnerColor;
                        if (target == null || !AnyOfColor(board, target.Value)) target = MostCommonColor(board);
                        var hit = new List<CellId>();
                        foreach (var c in topo.Cells)
                        {
                            var t = board.TileAt(c);
                            if (c == cell || (t != null && target != null && t.Color == target.Value)) hit.Add(c);
                        }
                        return hit; // topo.Cells is ascending, so the union with {cell} is already sorted
                    }
                default:
                    throw new ArgumentException("not a special");
            }
        }

        static bool AnyOfColor(Board board, int color)
        {
            foreach (var c in board.Topology.Cells)
            {
                var t = board.TileAt(c);
                if (t != null && t.Color == color) return true;
            }
            return false;
        }

        static List<CellId> Sorted(IReadOnlyList<CellId> cells)
        {
            var list = new List<CellId>(cells);
            list.Sort((a, b) => a.Value.CompareTo(b.Value)); // cell ids are unique
            return list;
        }
    }
}
