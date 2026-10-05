using System;
using System.Collections.Generic;
using Cascade.Core;
using Cascade.Gameplay;
using Cascade.Generation;

namespace Cascade.Tests.Generation
{
    /// <summary>What <see cref="DeadlockCheck.Run"/> found.</summary>
    public sealed class DeadlockResult
    {
        public int Levels { get; set; }
        public int Moves { get; set; }
        /// <summary>Auto-shuffles that fired (the deadlock handling working as designed).</summary>
        public int Shuffles { get; set; }
        public List<string> Failures { get; } = new List<string>();
        public bool Clean => Failures.Count == 0;
    }

    /// <summary>
    /// The deadlock check of MRD §5 (0 unsolvable or deadlocked levels across 10,000 generated
    /// levels). For each (level number, seed) pair it generates the level, checks that its starting
    /// board has no matches, has a legal move and shows every goal color, then plays random legal
    /// moves and checks after every move that the board is full, has no pending matches and still has
    /// a legal move. A player can therefore never be stuck. Plain code (no NUnit) so the unit test
    /// and <c>Cascade.Tools deadlock-check</c> run the very same loop.
    /// </summary>
    public static class DeadlockCheck
    {
        /// <summary>The (level number, seed) pair of check number <paramref name="index"/>: levels cycle 1..150.</summary>
        public static (int Number, ulong Seed) Pair(int index) =>
            (1 + index % 150, Rng.SplitMix64((ulong)index * 7919UL + 12345UL));

        /// <param name="generate">Makes the level for a pair; use a candidate maker for speed or a full <c>ILevelSource</c> for completeness.</param>
        public static DeadlockResult Run(int levels, int movesPerLevel, RuleSet rules, Func<int, ulong, Level> generate,
            Action<int>? progress = null)
        {
            var result = new DeadlockResult();
            for (int i = 0; i < levels; i++)
            {
                var (n, seed) = Pair(i);
                string label = "level " + n + " seed " + seed;
                Level level;
                try { level = generate(n, seed); }
                catch (Exception e) { result.Failures.Add(label + ": generation failed: " + e.Message); continue; }
                result.Levels++;
                Check(level, movesPerLevel, rules, label, result);
                progress?.Invoke(i + 1);
            }
            return result;
        }

        static void Check(Level generated, int moves, RuleSet rules, string label, DeadlockResult result)
        {
            var problems = Validator.Validate(generated);
            if (problems.Count > 0) { result.Failures.Add(label + ": invalid: " + string.Join("; ", problems)); return; }

            // play with an unreachable score goal so only the move limit ends the session
            var level = generated with { Goals = new[] { Goal.Score(int.MaxValue / 2) }, MoveLimit = moves };
            var session = new Session(level, rules);
            var picker = new Streams(generated.Seed ^ 0x5DEECE66DUL).Bot;
            if (session.LegalMoves.Count == 0) { result.Failures.Add(label + ": no legal move at the start"); return; }
            for (int m = 0; m < moves && session.Status == SessionStatus.Playing; m++)
            {
                var (a, b) = picker.Choice(session.LegalMoves);
                MoveResult move;
                try { move = session.TrySwap(a, b); }
                catch (Exception e) { result.Failures.Add(label + ": move " + m + " threw: " + e.Message); return; }
                if (!move.Played) { result.Failures.Add(label + ": a legal move was rejected at move " + m); return; }
                result.Moves++;
                foreach (var e in move.Events) if (e is Shuffled) result.Shuffles++;
                var board = session.Board;
                foreach (var cell in board.Topology.Cells)
                    if (board.TileAt(cell) == null) { result.Failures.Add(label + ": empty cell after move " + m); return; }
                if (Match.FindRuns(board).Count > 0) { result.Failures.Add(label + ": pending match after move " + m); return; }
                if (Moves.FindLegal(board).Count == 0) { result.Failures.Add(label + ": deadlock (no legal move) after move " + m); return; }
            }
        }
    }
}
