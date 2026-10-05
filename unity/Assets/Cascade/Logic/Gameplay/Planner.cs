using System;
using System.Collections.Generic;
using Cascade.Core;

namespace Cascade.Gameplay
{
    /// <summary>The <c>[planner]</c> dials as an immutable snapshot (TRD §7.1).</summary>
    public sealed record PlannerSettings
    {
        /// <summary>Most moves looked ahead.</summary>
        public int Depth { get; init; } = 3;
        /// <summary>Candidate moves played for real at each step of the search.</summary>
        public int Width { get; init; } = 6;
        /// <summary>Budget of played moves per plan; the first depth always completes.</summary>
        public int MaxNodes { get; init; } = 400;
        public double WinBonus { get; init; } = 100.0;
        public double MovesLeftWeight { get; init; } = 2.0;
        public double ProgressWeight { get; init; } = 50.0;
        public double ScoreWeight { get; init; } = 0.001;
        /// <summary>Most explored lines kept for the ghost replay (0 = none).</summary>
        public int MaxProbes { get; init; } = 200;
    }

    /// <summary>One line the planner evaluated: the moves played, in order, and the value of where they ended.</summary>
    public sealed record PlanProbe(IReadOnlyList<(CellId A, CellId B)> Moves, double Value);

    /// <summary>The planner's answer: the best line found. <see cref="Moves"/>[0] is the move to play now.</summary>
    public sealed record Plan(
        IReadOnlyList<(CellId A, CellId B)> Moves,
        double Value,
        bool Wins,
        int Nodes,
        int Depth,
        IReadOnlyList<PlanProbe> Probes);

    /// <summary>
    /// Finds the best sequence of moves for a session (TRD §7.1): beam search with iterative deepening over real
    /// <see cref="Session.Copy"/> playouts. A copy carries the RNG streams, so it sees the refills the real session
    /// will produce (an oracle). Never changes the session it is given.
    /// </summary>
    public static class Planner
    {
        /// <summary>The best line for <paramref name="session"/>, or null if it is not playing or has no legal move.</summary>
        public static Plan? Find(Session session, PlannerSettings settings, bool recordProbes = false)
        {
            if (!session.IsPlaying) return null;
            var search = new Search(settings, recordProbes);
            Plan? best = null;
            for (int depth = 1; depth <= Math.Max(1, settings.Depth); depth++)
            {
                var pass = search.Run(session, depth);
                if (pass == null) break; // out of budget mid-pass: keep the last completed depth
                best = pass;
                if (pass.Wins) break; // deeper lines only win later
            }
            return best;
        }

        /// <summary>The value of a session state as the planner sees it (TRD §7.1 leaf value).</summary>
        public static double Evaluate(Session s, PlannerSettings st)
        {
            if (s.Status == SessionStatus.Won) return st.WinBonus + st.MovesLeftWeight * s.MovesLeft;
            var progress = s.Progress;
            double sum = 0;
            int n = s.Goals.Count;
            for (int i = 0; i < n; i++) sum += Math.Min(1.0, (double)progress[i] / s.Goals[i].Target);
            return st.ProgressWeight * (n == 0 ? 0 : sum / n) + st.ScoreWeight * s.Score;
        }

        sealed class Search
        {
            readonly PlannerSettings st;
            readonly bool record;
            int nodes;
            bool exhausted;
            List<PlanProbe> probes = new List<PlanProbe>();

            public Search(PlannerSettings settings, bool recordProbes) { st = settings; record = recordProbes && settings.MaxProbes > 0; }

            /// <summary>One pass to <paramref name="depth"/>; null if the node budget ran out before it finished (never for depth 1).</summary>
            public Plan? Run(Session root, int depth)
            {
                exhausted = false;
                nodes = 0;
                probes = new List<PlanProbe>();
                var path = new List<(CellId A, CellId B)>();
                var (value, line) = Visit(root, depth, path, depth == 1);
                if (exhausted || line == null || line.Count == 0) return null;
                var last = root.Copy();
                foreach (var (a, b) in line) last.TrySwap(a, b);
                return new Plan(line, value, last.Status == SessionStatus.Won, nodes, depth, Decimate());
            }

            (double Value, List<(CellId A, CellId B)>? Line) Visit(Session node, int depthLeft, List<(CellId A, CellId B)> path, bool ignoreBudget)
            {
                if (node.Status != SessionStatus.Playing || depthLeft == 0) return Leaf(node, path);
                var ranked = node.RankedMoves(st.Width);
                if (ranked.Count == 0) return Leaf(node, path);
                double bestValue = double.NegativeInfinity;
                List<(CellId A, CellId B)>? bestLine = null;
                foreach (var move in ranked)
                {
                    if (!ignoreBudget && nodes >= st.MaxNodes) { exhausted = true; break; }
                    var child = node.Copy();
                    if (child.TrySwap(move.A, move.B).Outcome != MoveOutcome.Played) continue;
                    nodes++;
                    path.Add(move);
                    var (v, line) = Visit(child, depthLeft - 1, path, ignoreBudget);
                    path.RemoveAt(path.Count - 1);
                    if (exhausted) break;
                    if (v > bestValue) { bestValue = v; bestLine = line; }
                }
                return bestLine == null ? Leaf(node, path) : (bestValue, bestLine);
            }

            (double, List<(CellId A, CellId B)>?) Leaf(Session node, List<(CellId A, CellId B)> path)
            {
                double v = Evaluate(node, st);
                var line = new List<(CellId A, CellId B)>(path);
                if (record) probes.Add(new PlanProbe(line, v));
                return (v, line);
            }

            /// <summary>The recorded lines, evenly thinned to <c>MaxProbes</c>, in search order.</summary>
            IReadOnlyList<PlanProbe> Decimate()
            {
                if (!record) return Array.Empty<PlanProbe>();
                int cap = st.MaxProbes;
                if (probes.Count <= cap) return probes;
                var kept = new List<PlanProbe>(cap);
                for (int i = 0; i < cap; i++) kept.Add(probes[(int)((long)i * probes.Count / cap)]);
                return kept;
            }
        }
    }
}
