using System.Collections.Generic;
using System.Linq;
using Cascade.Config;
using Cascade.Core;
using Cascade.Generation;
using Cascade.Gameplay;
using Cascade.Tests.Support;
using NUnit.Framework;

namespace Cascade.Tests.Gameplay
{
    /// <summary>Planner tests (TRD §7.1): an oracle beam search over real session copies.</summary>
    public class PlannerTests
    {
        static readonly DialRegistry Dials = TestDials.Fast();
        static readonly RuleSet Rules = RuleSets.From(Dials);
        static readonly GeneratorSettings Gen = RuleSets.GeneratorFrom(Dials);
        static readonly PlannerSettings Settings = RuleSets.PlannerFrom(Dials);
        static readonly Profile Prof = TestDials.Profile();

        static Session Start(int levelNumber, ulong seed)
        {
            var level = new GeneratingLevelSource(Dials, Prof, new FakeStopwatch()).Get(levelNumber, seed);
            return new Session(level, Rules, RuleSets.WeightsFrom(Dials), new Streams(level.Seed), Bot.StartBoard(level));
        }

        static string Fingerprint(Session s) =>
            s.Score + "|" + s.MovesLeft + "|" + string.Join(",", s.Progress.ToArray()) + "|" +
            string.Join(",", s.Board.Topology.Cells.Select(c => s.Board.TileAt(c)?.Color ?? -1));

        [Test]
        public void Plan_does_not_change_the_session()
        {
            var s = Start(5, 1000005);
            string before = Fingerprint(s);
            var plan = Planner.Find(s, Settings, recordProbes: true);
            Assert.That(plan, Is.Not.Null);
            Assert.That(Fingerprint(s), Is.EqualTo(before));
        }

        [Test]
        public void Plan_is_deterministic()
        {
            var a = Planner.Find(Start(7, 1000007), Settings)!;
            var b = Planner.Find(Start(7, 1000007), Settings)!;
            Assert.That(a.Moves, Is.EqualTo(b.Moves));
            Assert.That(a.Value, Is.EqualTo(b.Value));
        }

        [Test]
        public void Plan_is_an_oracle_the_real_session_reaches_the_value_the_plan_promised()
        {
            // The copies carry the RNG streams, so playing the planned line on the real session
            // ends exactly where the search said it would (refills included).
            for (int n = 3; n <= 9; n++)
            {
                var s = Start(n, 1000000UL + (ulong)n);
                var plan = Planner.Find(s, Settings)!;
                Assert.That(plan.Moves.Count, Is.GreaterThan(0), "level " + n);
                foreach (var (a, b) in plan.Moves)
                    Assert.That(s.TrySwap(a, b).Outcome, Is.EqualTo(MoveOutcome.Played), "level " + n);
                Assert.That(Planner.Evaluate(s, Settings), Is.EqualTo(plan.Value).Within(1e-9), "level " + n);
            }
        }

        [Test]
        public void First_planned_move_is_legal()
        {
            var s = Start(4, 1000004);
            var plan = Planner.Find(s, Settings)!;
            Assert.That(s.LegalMoves, Does.Contain(plan.Moves[0]));
        }

        [Test]
        public void Finished_session_has_no_plan()
        {
            var s = Start(2, 1000002);
            while (s.IsPlaying)
            {
                var m = s.Hint();
                if (m == null) break;
                s.TrySwap(m.Value.A, m.Value.B);
            }
            Assert.That(Planner.Find(s, Settings), Is.Null);
        }

        [Test]
        public void Depth_one_plans_one_move_and_node_budget_keeps_the_last_completed_depth()
        {
            var s = Start(6, 1000006);
            var one = Planner.Find(s, Settings with { Depth = 1 })!;
            Assert.That(one.Moves.Count, Is.EqualTo(1));
            Assert.That(one.Depth, Is.EqualTo(1));
            // a budget too small for depth 2 still answers with the depth 1 line
            var tight = Planner.Find(s, Settings with { Depth = 4, MaxNodes = 1 })!;
            Assert.That(tight.Depth, Is.EqualTo(1));
        }

        [Test]
        public void Probes_are_recorded_only_on_request_and_capped()
        {
            var s = Start(8, 1000008);
            Assert.That(Planner.Find(s, Settings)!.Probes, Is.Empty);
            var plan = Planner.Find(s, Settings with { MaxProbes = 10 }, recordProbes: true)!;
            Assert.That(plan.Probes.Count, Is.InRange(1, 10));
            Assert.That(plan.Probes.All(p => p.Moves.Count > 0));
        }

        [Test]
        public void Planning_ahead_wins_at_least_as_often_as_the_greedy_hint()
        {
            int planWins = 0, hintWins = 0;
            var levels = new List<(int, ulong)>();
            for (int n = 4; n <= 30; n++) levels.Add((n, 1000000UL + (ulong)n));
            foreach (var (n, seed) in levels)
            {
                planWins += Play(Start(n, seed), usePlanner: true) ? 1 : 0;
                hintWins += Play(Start(n, seed), usePlanner: false) ? 1 : 0;
            }
            TestContext.Out.WriteLine($"plan {planWins} vs hint {hintWins} of {levels.Count}");
            Assert.That(planWins, Is.GreaterThanOrEqualTo(hintWins), $"plan {planWins} vs hint {hintWins} of {levels.Count}");
        }

        static bool Play(Session s, bool usePlanner)
        {
            while (s.IsPlaying)
            {
                (CellId A, CellId B)? move = usePlanner ? Planner.Find(s, Settings)?.Moves[0] : s.Hint();
                if (move == null) break;
                s.TrySwap(move.Value.A, move.Value.B);
            }
            return s.Status == SessionStatus.Won;
        }
    }
}
