using System;
using System.Collections.Generic;
using Cascade.Core;
using Cascade.Gameplay;
using Cascade.Platform.Abstractions;

namespace Cascade.Generation
{
    /// <summary>The outcome of one bot playthrough.</summary>
    public sealed record BotRun(bool Won, int Score, int Moves);

    /// <summary>The runs of one measurement (a batch of bot playthroughs of one level).</summary>
    public sealed class Measurement
    {
        public List<BotRun> Runs { get; } = new List<BotRun>();

        public Measurement() { }
        public Measurement(IEnumerable<BotRun> runs) { Runs.AddRange(runs); }

        /// <summary>Fraction of runs won; 0 with no runs.</summary>
        public double WinRate
        {
            get
            {
                if (Runs.Count == 0) return 0.0;
                int wins = 0;
                foreach (var r in Runs) if (r.Won) wins++;
                return (double)wins / Runs.Count;
            }
        }

        /// <summary>Scores of the won runs (the score at the moment the level was won), ascending.</summary>
        public List<int> WinScores
        {
            get
            {
                var list = new List<int>();
                foreach (var r in Runs) if (r.Won) list.Add(r.Score);
                list.Sort();
                return list;
            }
        }

        /// <summary>Scores of all runs, ascending.</summary>
        public List<int> AllScores
        {
            get
            {
                var list = new List<int>();
                foreach (var r in Runs) list.Add(r.Score);
                list.Sort();
                return list;
            }
        }

        /// <summary>Total moves played over all runs.</summary>
        public int Moves
        {
            get
            {
                int n = 0;
                foreach (var r in Runs) n += r.Moves;
                return n;
            }
        }
    }

    /// <summary>
    /// Auto-playtester (TRD §7 step 5): a greedy, shallow-lookahead player that drives the real
    /// <see cref="Session"/>. Each run uses fresh streams derived from the level seed and the run
    /// index, so runs differ while the initial board stays the same.
    /// </summary>
    public static class Bot
    {
        /// <summary>Play streams for bot run <paramref name="runIndex"/>: unrelated to the level's own streams.</summary>
        public static Streams RunStreams(ulong levelSeed, int runIndex)
            => new Streams(Rng.SplitMix64(levelSeed ^ Rng.SplitMix64((ulong)(runIndex + 1))));

        /// <summary>The level's initial board (identical for every run).</summary>
        public static Board StartBoard(Level level) => level.BuildBoard(new Streams(level.Seed));

        /// <summary>One bot playthrough of <paramref name="level"/>. <paramref name="baseBoard"/> is copied, never changed.</summary>
        public static BotRun Play(Level level, RuleSet rules, GeneratorSettings settings, int runIndex, Board? baseBoard = null)
        {
            var board = (baseBoard ?? StartBoard(level)).Copy();
            var streams = RunStreams(level.Seed, runIndex);
            var session = new Session(level, rules, settings.Weights, streams, board);
            var rng = streams.Bot;
            double chance = settings.BotRandomChance;
            while (session.Status == SessionStatus.Playing)
            {
                IReadOnlyList<(CellId A, CellId B)> choices = rng.Random() < chance ? session.LegalMoves : session.BestMoves();
                if (choices.Count == 0) break;
                var (a, b) = rng.Choice(choices);
                session.TrySwap(a, b);
            }
            return new BotRun(session.Status == SessionStatus.Won, session.Score, session.MovesUsed);
        }

        /// <summary>
        /// Plays up to <paramref name="runs"/> times. Stops early once <paramref name="deadlineMs"/> (on
        /// <paramref name="clock"/>) has passed, but never before <c>MinBotRuns</c> runs.
        /// </summary>
        public static Measurement Measure(Level level, RuleSet rules, GeneratorSettings settings, int runs,
            Board? baseBoard = null, double? deadlineMs = null, IStopwatch? clock = null, int firstRun = 0)
        {
            var baseline = baseBoard ?? StartBoard(level);
            var outcome = new Measurement();
            int minimum = Math.Min(runs, settings.MinBotRuns);
            for (int i = 0; i < runs; i++)
            {
                if (deadlineMs.HasValue && outcome.Runs.Count >= minimum && clock != null && clock.ElapsedMilliseconds > deadlineMs.Value)
                    break;
                outcome.Runs.Add(Play(level, rules, settings, firstRun + i, baseline));
            }
            return outcome;
        }
    }
}
