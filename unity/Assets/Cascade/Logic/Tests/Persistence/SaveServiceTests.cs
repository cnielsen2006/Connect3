using System;
using System.Collections.Generic;
using System.Linq;
using Cascade.Gameplay;
using Cascade.Persistence;
using Cascade.Platform.Abstractions;
using Cascade.Tests.Support;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Cascade.Tests.Persistence
{
    /// <summary>Tests (TRD §9.1 Persistence, §8).</summary>
    public class SaveServiceTests
    {
        /// <summary>A storage whose writes can be made to fail, like a full disk.</summary>
        sealed class FlakyStorage : IStorage
        {
            public readonly MemoryStorage Inner = new MemoryStorage();
            public bool FailWrites;
            public bool FailMoves;
            public bool FailReads;

            public byte[]? TryRead(string name) => FailReads ? throw new InvalidOperationException("unreadable") : Inner.TryRead(name);

            public void WriteAtomic(string name, byte[] bytes)
            {
                if (FailWrites) throw new InvalidOperationException("disk on fire"); // nothing was written
                Inner.WriteAtomic(name, bytes);
            }

            public void Move(string name, string newName)
            {
                if (FailMoves) throw new InvalidOperationException("cannot move");
                Inner.Move(name, newName);
            }

            public void Delete(string name) => Inner.Delete(name);
        }

        static SaveData Sample() => new SaveData
        {
            CurrentLevel = 42,
            CurrentSeed = ulong.MaxValue,
            Stars = StarTable.Empty.With(1, 3).With(2, 2),
            AdaptiveOffset = -0.05,
        };

        [Test]
        public void Round_trip()
        {
            var storage = new MemoryStorage();
            var svc = new SaveService(storage);
            Assert.That(svc.LoadSave(), Is.EqualTo(new SaveData())); // missing file -> defaults
            var data = Sample();
            svc.WriteSave(data);
            Assert.That(svc.LoadSave(), Is.EqualTo(data));
            var raw = JObject.Parse(storage.GetText("save.json")!);
            Assert.That(raw["stars"]!.ToString(Newtonsoft.Json.Formatting.None), Is.EqualTo("{\"1\":3,\"2\":2}")); // TRD §8 layout
            Assert.That((int)raw["save_version"]!, Is.EqualTo(1));
            Assert.That((int)raw["generator_version"]!, Is.EqualTo(GeneratorVersion.Current));
            Assert.That(storage.Files.Keys.ToArray(), Is.EqualTo(new[] { "save.json" })); // no temp files left behind
        }

        [Test]
        public void Seed_is_written_as_a_decimal_string_and_survives_the_full_64_bit_range()
        {
            var storage = new MemoryStorage();
            var svc = new SaveService(storage);
            foreach (ulong seed in new[] { 0UL, 1UL, (1UL << 53) + 1, 9182736455463728190UL, ulong.MaxValue })
            {
                svc.WriteSave(new SaveData { CurrentSeed = seed });
                var raw = JObject.Parse(storage.GetText("save.json")!);
                Assert.That(raw["current_seed"]!.Type, Is.EqualTo(JTokenType.String));
                Assert.That((string)raw["current_seed"]!, Is.EqualTo(seed.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                Assert.That(svc.LoadSave().CurrentSeed, Is.EqualTo(seed));
            }
            svc.WriteSave(new SaveData());
            Assert.That(JObject.Parse(storage.GetText("save.json")!)["current_seed"]!.Type, Is.EqualTo(JTokenType.Null));
            Assert.That(svc.LoadSave().CurrentSeed, Is.Null);
        }

        [Test]
        public void The_documented_example_loads()
        {
            var storage = new MemoryStorage();
            storage.PutText("save.json", "{ \"save_version\": 1, \"generator_version\": 1, \"current_level\": 42, " +
                "\"current_seed\": \"9182736455463728190\", \"stars\": {\"1\": 3, \"2\": 2}, \"adaptive_offset\": -0.05 }");
            var d = new SaveService(storage).LoadSave();
            Assert.That(d.CurrentLevel, Is.EqualTo(42));
            Assert.That(d.CurrentSeed, Is.EqualTo(9182736455463728190UL));
            Assert.That(d.Stars.Get(1), Is.EqualTo(3));
            Assert.That(d.Stars.Get(2), Is.EqualTo(2));
            Assert.That(d.AdaptiveOffset, Is.EqualTo(-0.05));
            Assert.That(storage.Files.ContainsKey("save.json.corrupt"), Is.False);
        }

        [Test]
        public void Optional_fields_may_be_missing_and_unknown_fields_are_ignored()
        {
            var storage = new MemoryStorage();
            storage.PutText("save.json", "{\"save_version\": 1, \"generator_version\": " + GeneratorVersion.Current + ", \"current_level\": 3, \"extra\": [1, 2]}");
            var d = new SaveService(storage).LoadSave();
            Assert.That(d, Is.EqualTo(new SaveData { CurrentLevel = 3 }));
            Assert.That(storage.Files.ContainsKey("save.json.corrupt"), Is.False);
        }

        [Test]
        public void A_numeric_seed_in_range_is_accepted()
        {
            var storage = new MemoryStorage();
            storage.PutText("save.json", "{\"save_version\": 1, \"generator_version\": 1, \"current_level\": 3, \"current_seed\": 777}");
            Assert.That(new SaveService(storage).LoadSave().CurrentSeed, Is.EqualTo(777UL));
        }

        [Test]
        public void Writes_are_atomic_the_old_file_survives_a_failed_write()
        {
            var storage = new FlakyStorage();
            var svc = new SaveService(storage);
            var good = new SaveData { CurrentLevel = 5, CurrentSeed = 9 };
            svc.WriteSave(good);
            storage.FailWrites = true;
            Assert.Throws<InvalidOperationException>(() => svc.WriteSave(new SaveData { CurrentLevel = 6 }));
            storage.FailWrites = false;
            Assert.That(svc.LoadSave(), Is.EqualTo(good)); // the old file is untouched
            Assert.That(storage.Inner.Files.Keys.ToArray(), Is.EqualTo(new[] { "save.json" }));
        }

        [Test]
        public void TryWrite_reports_failure_and_logs_instead_of_throwing()
        {
            var storage = new FlakyStorage();
            var log = new ListLog();
            var svc = new SaveService(storage, log);
            Assert.That(svc.TryWriteSave(new SaveData()), Is.True);
            storage.FailWrites = true;
            Assert.That(svc.TryWriteSave(new SaveData { CurrentLevel = 2 }), Is.False);
            Assert.That(svc.TryWriteSettings(new PlayerSettings()), Is.False);
            Assert.That(log.Lines.Count(l => l.StartsWith("Error: ")), Is.EqualTo(2));
        }

        static readonly string[] CorruptSaves =
        {
            "{not json",
            "",
            "   ",
            "[]",
            "null",
            "42",
            "{\"save_version\": 1, \"generator_version\": 1, \"current_level\": 3} trailing",
            "{\"save_version\": 99, \"generator_version\": 1, \"current_level\": 3}",
            "{\"save_version\": 0, \"generator_version\": 1, \"current_level\": 3}",
            "{\"generator_version\": 1, \"current_level\": 3}",
            "{\"save_version\": 1, \"generator_version\": 1, \"current_level\": 0}",
            "{\"save_version\": 1, \"generator_version\": 1}",
            "{\"save_version\": 1, \"generator_version\": 1, \"current_level\": 3, \"stars\": {\"x\": 1}}",
            "{\"save_version\": 1, \"generator_version\": 1, \"current_level\": 3, \"stars\": {\"1\": 7}}",
            "{\"save_version\": 1, \"generator_version\": 1, \"current_level\": 3, \"stars\": [1]}",
            "{\"save_version\": 1, \"generator_version\": 1, \"current_level\": 3, \"current_seed\": -4}",
            "{\"save_version\": 1, \"generator_version\": 1, \"current_level\": 3, \"current_seed\": \"-4\"}",
            "{\"save_version\": 1, \"generator_version\": 1, \"current_level\": 3, \"current_seed\": \"18446744073709551616\"}",
            "{\"save_version\": 1, \"generator_version\": 1, \"current_level\": 3, \"current_seed\": 18446744073709551616}",
            "{\"save_version\": 1, \"generator_version\": 1, \"current_level\": 3, \"current_seed\": 1.5}",
            "{\"save_version\": 1, \"generator_version\": 1, \"current_level\": true}",
            "{\"save_version\": 1, \"generator_version\": 1, \"current_level\": 3.0}",
            "{\"save_version\": 1, \"generator_version\": 1, \"current_level\": 3, \"adaptive_offset\": \"x\"}",
        };

        [TestCaseSource(nameof(CorruptSaves))]
        public void Corrupt_save_gives_defaults_and_is_moved_aside(string text)
        {
            var storage = new MemoryStorage();
            var log = new ListLog();
            var svc = new SaveService(storage, log);
            storage.PutText("save.json", text);
            Assert.That(svc.LoadSave(), Is.EqualTo(new SaveData()));
            Assert.That(storage.Files.ContainsKey("save.json"), Is.False);
            Assert.That(storage.GetText("save.json.corrupt"), Is.EqualTo(text));
            Assert.That(log.Lines.Any(l => l.StartsWith("Warn: ")));
            svc.WriteSave(new SaveData { CurrentLevel = 2 }); // and the game can carry on
            Assert.That(svc.LoadSave().CurrentLevel, Is.EqualTo(2));
        }

        [Test]
        public void Binary_garbage_is_tolerated()
        {
            var storage = new MemoryStorage();
            storage.Files["save.json"] = new byte[] { 0xFF, 0xFE, 0x00, (byte)'g', (byte)'a', (byte)'r' };
            Assert.That(new SaveService(storage).LoadSave(), Is.EqualTo(new SaveData()));
            Assert.That(storage.Files.ContainsKey("save.json.corrupt"));
        }

        [Test]
        public void A_second_corrupt_file_replaces_the_first_corrupt_copy()
        {
            var storage = new MemoryStorage();
            var svc = new SaveService(storage);
            storage.PutText("save.json", "first garbage");
            svc.LoadSave();
            storage.PutText("save.json", "second garbage");
            svc.LoadSave();
            Assert.That(storage.GetText("save.json.corrupt"), Is.EqualTo("second garbage"));
        }

        [Test]
        public void A_failing_move_aside_or_read_never_throws()
        {
            var storage = new FlakyStorage { FailMoves = true };
            storage.Inner.PutText("save.json", "{garbage");
            var svc = new SaveService(storage, new ListLog());
            Assert.That(svc.LoadSave(), Is.EqualTo(new SaveData()));
            storage.FailReads = true;
            Assert.That(svc.LoadSave(), Is.EqualTo(new SaveData()));
            Assert.That(svc.LoadSettings(), Is.EqualTo(new PlayerSettings()));
        }

        [Test]
        public void Fps_and_skin_settings_round_trip_and_bad_values_fall_back()
        {
            var storage = new MemoryStorage();
            var svc = new SaveService(storage);
            Assert.That(svc.LoadSettings().TargetFps, Is.EqualTo(60));
            Assert.That(svc.LoadSettings().Skin, Is.EqualTo("K"));
            svc.WriteSettings(new PlayerSettings { TargetFps = 30, Skin = "J" });
            Assert.That(svc.LoadSettings(), Is.EqualTo(new PlayerSettings { TargetFps = 30, Skin = "J" }));
            storage.PutText("settings.json", "{\"target_fps\": 45, \"skin\": \"Z\"}");
            Assert.That(svc.LoadSettings(), Is.EqualTo(new PlayerSettings()));
        }

        [Test]
        public void Settings_defaults_round_trip_and_tolerance()
        {
            var storage = new MemoryStorage();
            var svc = new SaveService(storage);
            Assert.That(svc.LoadSettings(), Is.EqualTo(new PlayerSettings { Sound = true, AnimationSpeed = 1.0, Adaptive = true }));
            svc.WriteSettings(new PlayerSettings { Sound = false, AnimationSpeed = 2.0, Adaptive = false });
            Assert.That(svc.LoadSettings(), Is.EqualTo(new PlayerSettings { Sound = false, AnimationSpeed = 2.0, Adaptive = false }));
            var raw = JObject.Parse(storage.GetText("settings.json")!);
            Assert.That(raw.Properties().Select(p => p.Name).ToArray(), Is.EqualTo(new[] { "sound", "animation_speed", "adaptive", "target_fps", "skin" }));

            storage.PutText("settings.json", "{\"sound\": \"yes\", \"animation_speed\": -3, \"adaptive\": false}");
            // bad fields fall back one by one
            Assert.That(svc.LoadSettings(), Is.EqualTo(new PlayerSettings { Sound = true, AnimationSpeed = 1.0, Adaptive = false }));
            storage.PutText("settings.json", "nope");
            Assert.That(svc.LoadSettings(), Is.EqualTo(new PlayerSettings()));
            Assert.That(storage.Files.ContainsKey("settings.json.corrupt"));
        }

        [TestCase("0.5", 0.5)]
        [TestCase("3.0", 3.0)]
        [TestCase("3", 3.0)]
        [TestCase("2.25", 2.25)]
        [TestCase("0.49", 1.0)]
        [TestCase("3.01", 1.0)]
        [TestCase("0", 1.0)]
        [TestCase("100", 1.0)]
        [TestCase("\"fast\"", 1.0)]
        [TestCase("true", 1.0)]
        [TestCase("null", 1.0)]
        public void Animation_speed_is_limited_to_its_range(string json, double expected)
        {
            var storage = new MemoryStorage();
            storage.PutText("settings.json", "{\"animation_speed\": " + json + "}");
            Assert.That(new SaveService(storage).LoadSettings().AnimationSpeed, Is.EqualTo(expected));
        }

        [Test]
        public void Settings_that_are_not_an_object_give_defaults_without_moving_the_file()
        {
            var storage = new MemoryStorage();
            storage.PutText("settings.json", "[1, 2]");
            Assert.That(new SaveService(storage).LoadSettings(), Is.EqualTo(new PlayerSettings()));
            Assert.That(storage.Files.ContainsKey("settings.json"));
        }

        [Test]
        public void Save_and_settings_live_in_separate_files()
        {
            var storage = new MemoryStorage();
            var svc = new SaveService(storage);
            svc.WriteSave(Sample());
            svc.WriteSettings(new PlayerSettings { Sound = false });
            Assert.That(storage.Files.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray(), Is.EqualTo(new[] { "save.json", "settings.json" }));
            Assert.That(storage.GetText("save.json"), Does.Not.Contain("sound"));
            Assert.That(storage.GetText("save.json")!.EndsWith("\n"));
            Assert.That(storage.GetText("save.json"), Does.Not.Contain("\r"));
        }
    }
}
