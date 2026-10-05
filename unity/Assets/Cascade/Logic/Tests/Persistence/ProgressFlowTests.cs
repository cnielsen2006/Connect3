using System.Linq;
using Cascade.Config;
using Cascade.Core;
using Cascade.Gameplay;
using Cascade.Generation;
using Cascade.Persistence;
using Cascade.Tests.Support;
using NUnit.Framework;

namespace Cascade.Tests.Persistence
{
    /// <summary>
    /// The object graph the composition root builds, end to end with test doubles: dials, rule
    /// snapshots, level source, session, progress and save service (TRD §2, §7, §8).
    /// </summary>
    public class ProgressFlowTests
    {
        [Test]
        public void Play_lose_retry_win_save_and_reload()
        {
            var storage = new MemoryStorage();
            var dials = TestDials.NoBudget(); // budget 0: levels are a pure function of (number, seed)
            var profile = TestDials.Profile();
            var seeds = new FakeSeedSource(1000);

            // first run: no save, so level 1 draws a seed and stores it
            var save = new SaveService(storage);
            var progress = new PlayerProgress(save.LoadSave(), seeds);
            progress.Changed += data => save.WriteSave(data);
            ulong seed = progress.StartLevel(1);
            Assert.That(seed, Is.EqualTo(1000UL));
            Assert.That(save.LoadSave().CurrentSeed, Is.EqualTo(1000UL), "the seed is saved before the level is played");

            // play it with a bot until it ends
            var source = new GeneratingLevelSource(dials, profile, new FakeStopwatch());
            var level = source.Get(1, seed);
            var session = PlayWithHints(level, dials);
            Assert.That(session.Status, Is.Not.EqualTo(SessionStatus.Playing));

            // a retry (after a restart: new objects, same save) gets the identical level
            var progress2 = new PlayerProgress(new SaveService(storage).LoadSave(), seeds);
            ulong again = progress2.StartLevel(1);
            Assert.That(again, Is.EqualTo(seed));
            var level2 = new GeneratingLevelSource(dials, profile, new FakeStopwatch()).Get(1, again);
            Assert.That(level2, Is.EqualTo(level));

            // a win advances the frontier, draws the next seed and keeps the stars
            progress2.Changed += data => new SaveService(storage).WriteSave(data);
            progress2.RecordWin(1, 2);
            var reloaded = new SaveService(storage).LoadSave();
            Assert.That(reloaded.CurrentLevel, Is.EqualTo(2));
            Assert.That(reloaded.CurrentSeed, Is.EqualTo(1001UL));
            Assert.That(reloaded.Stars.Get(1), Is.EqualTo(2));
            Assert.That(seeds.NextSeed(), Is.EqualTo(1002UL), "exactly one seed was drawn for the win");
        }

        static Session PlayWithHints(Level level, DialRegistry dials)
        {
            var session = new Session(level, RuleSets.From(dials), RuleSets.WeightsFrom(dials));
            while (session.Status == SessionStatus.Playing)
            {
                var hint = session.Hint();
                Assert.That(hint, Is.Not.Null);
                var result = session.TrySwap(hint!.Value.A, hint.Value.B);
                Assert.That(result.Played, "a hint is always a legal move");
                Assert.That(result.Events.OfType<Swapped>().Count(), Is.EqualTo(1));
            }
            return session;
        }

        [Test]
        public void A_session_replays_identically_from_the_same_level()
        {
            var dials = TestDials.NoBudget();
            var level = new GeneratingLevelSource(dials, TestDials.Profile(), new FakeStopwatch()).Get(8, 77);
            var a = PlayWithHints(level, dials);
            var b = PlayWithHints(level, dials);
            Assert.That(a.Score, Is.EqualTo(b.Score));
            Assert.That(a.MovesUsed, Is.EqualTo(b.MovesUsed));
            Assert.That(a.Status, Is.EqualTo(b.Status));
        }

        [Test]
        public void Settings_and_progress_are_independent_files()
        {
            var storage = new MemoryStorage();
            var save = new SaveService(storage);
            save.WriteSettings(new PlayerSettings { AnimationSpeed = 2.5 });
            Assert.That(save.LoadSave(), Is.EqualTo(new SaveData()));
            Assert.That(save.LoadSettings().AnimationSpeed, Is.EqualTo(2.5));
        }
    }
}
