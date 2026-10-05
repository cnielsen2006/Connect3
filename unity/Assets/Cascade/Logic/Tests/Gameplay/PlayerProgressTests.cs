using System.Collections.Generic;
using Cascade.Gameplay;
using Cascade.Tests.Support;
using NUnit.Framework;

namespace Cascade.Tests.Gameplay
{
    /// <summary>Progress-helper tests (TRD §8).</summary>
    public class PlayerProgressTests
    {
        [Test]
        public void Retry_reuses_the_saved_seed_until_won()
        {
            var s0 = new SaveData { CurrentLevel = 4 };
            var seeds = new ListSeedSource(111, 222);
            var (s1, seed) = ProgressRules.StartLevel(s0, 4, seeds);
            Assert.That(seed, Is.EqualTo(111UL));
            Assert.That(s1.CurrentSeed, Is.EqualTo(111UL));
            for (int i = 0; i < 3; i++) // losing and retrying never changes the seed
            {
                ulong again;
                (s1, again) = ProgressRules.StartLevel(s1, 4, seeds);
                Assert.That(again, Is.EqualTo(111UL));
            }
            Assert.That(seeds.Drawn, Is.EqualTo(1));
            var s2 = ProgressRules.RecordWin(s1, 4, 2, seeds);
            Assert.That(s2.CurrentLevel, Is.EqualTo(5));
            Assert.That(s2.CurrentSeed, Is.EqualTo(222UL));
            Assert.That(s2.Stars, Is.EqualTo(StarTable.From(new[] { new KeyValuePair<int, int>(4, 2) })));
            Assert.That(s1.Stars.Count, Is.EqualTo(0)); // inputs are not mutated
            Assert.That(s1.CurrentLevel, Is.EqualTo(4));
        }

        [Test]
        public void Best_stars_are_kept_and_replays_do_not_regress_progress()
        {
            var s = new SaveData { CurrentLevel = 10, CurrentSeed = 5, Stars = StarTable.Empty.With(3, 2) };
            var seeds = new ListSeedSource(77, 88);
            s = ProgressRules.RecordWin(s, 3, 1, seeds);
            Assert.That(s.Stars.Get(3), Is.EqualTo(2));
            Assert.That(s.CurrentLevel, Is.EqualTo(10));
            Assert.That(s.CurrentSeed, Is.EqualTo(5UL));
            s = ProgressRules.RecordWin(s, 3, 3, seeds);
            Assert.That(s.Stars.Get(3), Is.EqualTo(3));
            ulong seed;
            (s, seed) = ProgressRules.StartLevel(s, 3, seeds); // replay: fresh seed, nothing saved
            Assert.That(seed, Is.EqualTo(77UL));
            Assert.That(s.CurrentSeed, Is.EqualTo(5UL));
            s = ProgressRules.RecordWin(s, 10, 1, seeds);
            Assert.That(s.CurrentLevel, Is.EqualTo(11));
            Assert.That(s.CurrentSeed, Is.EqualTo(88UL));
        }

        [Test]
        public void Seed_from_another_generator_version_is_not_reused()
        {
            var old = new SaveData { CurrentLevel = 2, CurrentSeed = 5, GeneratorVersion = GeneratorVersion.Current + 1 };
            var (updated, seed) = ProgressRules.StartLevel(old, 2, new ListSeedSource(6));
            Assert.That(seed, Is.EqualTo(6UL));
            Assert.That(updated.GeneratorVersion, Is.EqualTo(GeneratorVersion.Current));
            Assert.That(updated.CurrentSeed, Is.EqualTo(6UL));
        }

        [Test]
        public void Seed_covers_the_full_64_bit_range()
        {
            var (_, seed) = ProgressRules.StartLevel(new SaveData(), 1, new ListSeedSource(ulong.MaxValue));
            Assert.That(seed, Is.EqualTo(ulong.MaxValue));
        }

        [Test]
        public void Player_progress_tracks_data_and_raises_changed_only_for_real_changes()
        {
            var progress = new PlayerProgress(new SaveData(), new ListSeedSource(10, 20, 30));
            var changes = new List<SaveData>();
            progress.Changed += changes.Add;

            Assert.That(progress.StartLevel(1), Is.EqualTo(10UL)); // stores the first seed
            Assert.That(changes.Count, Is.EqualTo(1));
            Assert.That(progress.StartLevel(1), Is.EqualTo(10UL)); // retry: nothing new
            Assert.That(changes.Count, Is.EqualTo(1));

            progress.RecordWin(1, 3);
            Assert.That(progress.CurrentLevel, Is.EqualTo(2));
            Assert.That(progress.StarsFor(1), Is.EqualTo(3));
            Assert.That(progress.TotalStars, Is.EqualTo(3));
            Assert.That(progress.Data.CurrentSeed, Is.EqualTo(20UL));
            Assert.That(progress.IsUnlocked(2));
            Assert.That(!progress.IsUnlocked(3));
            Assert.That(!progress.IsUnlocked(0));

            Assert.That(progress.StartLevel(1), Is.EqualTo(30UL)); // replaying level 1: fresh, unsaved seed
            Assert.That(progress.Data.CurrentSeed, Is.EqualTo(20UL));

            int before = changes.Count;
            progress.AdaptiveOffset = -0.05;
            progress.AdaptiveOffset = -0.05; // unchanged
            Assert.That(changes.Count, Is.EqualTo(before + 1));
            Assert.That(progress.Data.AdaptiveOffset, Is.EqualTo(-0.05));
        }

        [Test]
        public void Star_table_is_sorted_immutable_and_comparable()
        {
            var t = StarTable.Empty.With(5, 1).With(2, 3).With(9, 0);
            Assert.That(t.Levels, Is.EqualTo(new[] { 2, 5, 9 }));
            Assert.That(t.Get(2), Is.EqualTo(3));
            Assert.That(t.Get(7), Is.EqualTo(0));
            Assert.That(t.Contains(9));
            Assert.That(t.Total, Is.EqualTo(4));
            var u = t.With(5, 2);
            Assert.That(t.Get(5), Is.EqualTo(1));
            Assert.That(u.Get(5), Is.EqualTo(2));
            Assert.That(t, Is.EqualTo(StarTable.Empty.With(9, 0).With(2, 3).With(5, 1)));
            Assert.That(t, Is.Not.EqualTo(u));
        }

        [Test]
        public void Save_data_defaults_match_the_reference()
        {
            var d = new SaveData();
            Assert.That(d.SaveVersion, Is.EqualTo(1));
            Assert.That(d.GeneratorVersion, Is.EqualTo(GeneratorVersion.Current));
            Assert.That(d.CurrentLevel, Is.EqualTo(1));
            Assert.That(d.CurrentSeed, Is.Null);
            Assert.That(d.AdaptiveOffset, Is.EqualTo(0.0));
            Assert.That(d, Is.EqualTo(new SaveData()));
        }
    }
}
