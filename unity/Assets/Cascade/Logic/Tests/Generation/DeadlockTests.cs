using Cascade.Gameplay;
using Cascade.Generation;
using Cascade.Tests.Support;
using NUnit.Framework;

namespace Cascade.Tests.Generation
{
    /// <summary>
    /// A small, fast variant of the MRD §5 deadlock check (the 10,000-level run is
    /// <c>dotnet run --project tools/Cascade.Tools -- deadlock-check</c>).
    /// </summary>
    public class DeadlockTests
    {
        [Test]
        public void No_generated_level_deadlocks_over_300_levels()
        {
            var dials = TestDials.Fast();
            var profile = TestDials.Profile();
            var settings = RuleSets.GeneratorFrom(dials);
            var result = DeadlockCheck.Run(300, 25, RuleSets.From(dials),
                (n, seed) => LevelGenerator.MakeCandidate(profile.Evaluate(n), seed, settings));
            Assert.That(result.Failures, Is.Empty);
            Assert.That(result.Levels, Is.EqualTo(300));
            Assert.That(result.Moves, Is.GreaterThan(300 * 10));
        }

        [Test]
        public void Tuned_levels_from_the_real_pipeline_also_never_deadlock()
        {
            var dials = TestDials.Fast();
            var source = new GeneratingLevelSource(dials, TestDials.Profile(), new FakeStopwatch());
            var result = DeadlockCheck.Run(30, 20, RuleSets.From(dials), (n, seed) => source.Get(n, seed));
            Assert.That(result.Failures, Is.Empty);
        }

        [Test]
        public void The_checker_reports_a_broken_level()
        {
            var dials = TestDials.Fast();
            var profile = TestDials.Profile();
            var settings = RuleSets.GeneratorFrom(dials);
            var result = DeadlockCheck.Run(2, 5, RuleSets.From(dials), (n, seed) =>
                LevelGenerator.MakeCandidate(profile.Evaluate(n), seed, settings) with { MoveLimit = 0 });
            Assert.That(result.Clean, Is.False);
            Assert.That(result.Failures.Count, Is.EqualTo(2));
        }
    }
}
