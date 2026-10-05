using System.Collections.Generic;
using Cascade.Core;

namespace Cascade.Gameplay
{
    /// <summary>
    /// Cheap, shallow move evaluation shared by hints and the bot (TRD §7). Instead of a full
    /// <see cref="Rules.Resolve"/> it swaps in place, looks only at the runs through the two cells,
    /// adds one level of special blasts, and swaps back.
    /// </summary>
    public static class MoveEvaluator
    {
        /// <summary>
        /// <c>goal_progress * Goal + specials_made * Special + points * Points</c> for swapping
        /// <paramref name="a"/> and <paramref name="b"/>. <paramref name="wanted"/> are the colors of
        /// unfinished collect goals. The board is swapped and swapped back, so it must not be read
        /// from another thread meanwhile; its <see cref="Board.LegalCache"/> is cleared.
        /// </summary>
        public static double Value(Board board, CellId a, CellId b, RuleSet rules, MoveWeights weights,
            IReadOnlyList<int>? wanted = null)
        {
            var topo = board.Topology;
            int cap = topo.CellCapacity;
            int baseScore = 0, earned = 0, extraCount = 0, progress = 0;
            board.Swap(a, b);
            try
            {
                var ta = board.TileAt(a)!;
                var tb = board.TileAt(b)!; // after the swap
                // the color each swapped tile was swapped with (matters for Color pieces)
                int partnerA = tb.Color, partnerB = ta.Color;
                var inCleared = new bool[cap];
                var cleared = new List<CellId>();
                if (ta.Special != null && tb.Special != null)
                {
                    if (ta.Special is Special.Color && tb.Special is Special.Color)
                    {
                        foreach (var c in topo.Cells) { inCleared[c.Value] = true; cleared.Add(c); }
                    }
                    else
                    {
                        inCleared[a.Value] = true; cleared.Add(a);
                        inCleared[b.Value] = true; cleared.Add(b);
                    }
                }
                else
                {
                    var runs = Match.FindRunsThrough(board, new[] { a, b });
                    foreach (var grp in Match.GroupsFromRuns(topo, runs))
                    {
                        baseScore += Scoring.Base(grp.Qualifiers, rules.Scoring);
                        foreach (var kind in Specials.Earned(grp.Qualifiers))
                            if (board.IsSpecialEnabled(kind)) earned++;
                        foreach (var c in grp.Cells)
                            if (!inCleared[c.Value]) { inCleared[c.Value] = true; cleared.Add(c); }
                    }
                }

                var inExtra = new bool[cap];
                var extra = new List<CellId>();
                foreach (var cell in cleared)
                {
                    var t = board.TileAt(cell)!;
                    if (t.Special == null) continue;
                    int? partner = cell == a ? partnerA : cell == b ? (int?)partnerB : null;
                    foreach (var h in Specials.Fire(board, cell, t, rules.Specials, partner))
                        if (!inCleared[h.Value] && !inExtra[h.Value]) { inExtra[h.Value] = true; extra.Add(h); extraCount++; }
                }

                if (wanted != null && wanted.Count > 0)
                {
                    foreach (var cell in cleared) if (Wanted(wanted, board.TileAt(cell)!.Color)) progress++;
                    foreach (var cell in extra) if (Wanted(wanted, board.TileAt(cell)!.Color)) progress++;
                }
                baseScore += Scoring.SpecialCleared(extraCount, rules.Scoring);
            }
            finally
            {
                board.Swap(a, b);
            }
            return progress * weights.Goal + earned * weights.Special + baseScore * weights.Points;
        }

        static bool Wanted(IReadOnlyList<int> wanted, int color)
        {
            for (int i = 0; i < wanted.Count; i++) if (wanted[i] == color) return true;
            return false;
        }
    }
}
