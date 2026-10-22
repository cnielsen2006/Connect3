using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Cascade.Config;
using Cascade.Platform.Abstractions;
using Cascade.Tests.Support;
using NUnit.Framework;

namespace Cascade.Tests.Config
{
    /// <summary>TRD §9.1, Config row: registry behavior, precedence, notifications, metadata.</summary>
    public class DialRegistryTests
    {
        static readonly HostInfo Desktop = new HostInfo(PlatformKind.Desktop, true, true, false, true, 96f);
        static readonly HostInfo Android = new HostInfo(PlatformKind.Android, false, false, true, false, 420f);

        static string RealDefaults => RepoFiles.ReadData("dials.toml");

        static DictionaryDataSource Data(string? platformAndroid = null)
        {
            var ds = new DictionaryDataSource().With("dials.toml", RealDefaults);
            if (platformAndroid != null) ds.With("dials.android.toml", platformAndroid);
            return ds;
        }

        static DialRegistry Load(HostInfo? host = null, MemoryStorage? storage = null, string? android = null) =>
            DialRegistry.Load(Data(android), host ?? Desktop, storage ?? new MemoryStorage());

        // ---- defaults

        [Test]
        public void Loads_shipped_defaults_with_native_types()
        {
            var d = Load();
            Assert.That(d.Scoring.Run3, Is.EqualTo(60));
            Assert.That(d.Scoring.CascadeStep, Is.EqualTo(0.5));
            Assert.That(d.Camera.PitchDeg, Is.EqualTo(25));
            Assert.That(d.Camera.ZoomMax, Is.EqualTo(3.0));
            Assert.That(d.Input.DragInnerFraction, Is.EqualTo(0.6));
            Assert.That(d.Generator.BotRuns, Is.EqualTo(20));
            Assert.That(d.Generator.BotWeightGoal, Is.EqualTo(10.0));
            Assert.That(d.Get("scoring", "run3"), Is.TypeOf<int>());
            Assert.That(d.Get("scoring", "cascade_step"), Is.TypeOf<double>());
        }

        [Test]
        public void Defaults_file_with_a_missing_dial_is_rejected()
        {
            var text = RealDefaults.Replace("combo_color_color = 5000", "");
            var ds = new DictionaryDataSource().With("dials.toml", text);
            var e = Assert.Throws<DialException>(() => DialRegistry.Load(ds, Desktop));
            StringAssert.Contains("scoring.combo_color_color", e!.Message);
        }

        [Test]
        public void Defaults_file_with_an_unknown_dial_or_section_is_rejected()
        {
            var ds = new DictionaryDataSource().With("dials.toml", RealDefaults + "\nbogus_dial = 1\n");
            Assert.Throws<DialException>(() => DialRegistry.Load(ds, Desktop)); // lands in [generator]
            ds = new DictionaryDataSource().With("dials.toml", RealDefaults + "\n[nonsense]\nx = 1\n");
            Assert.Throws<DialException>(() => DialRegistry.Load(ds, Desktop));
        }

        [Test]
        public void Malformed_toml_is_a_dial_exception()
        {
            var ds = new DictionaryDataSource().With("dials.toml", "[scoring\nrun3 = ");
            Assert.Throws<DialException>(() => DialRegistry.Load(ds, Desktop));
        }

        // ---- validation on Set

        [Test]
        public void Unknown_dial_is_rejected()
        {
            var d = Load();
            Assert.Throws<DialException>(() => d.Set("scoring", "run33", 1));
            Assert.Throws<DialException>(() => d.Set("nope", "run3", 1));
            Assert.Throws<DialException>(() => d.Get("scoring", "nope"));
            Assert.Throws<DialException>(() => d.Reset("scoring", "nope"));
        }

        [Test]
        public void Wrong_type_is_rejected()
        {
            var d = Load();
            Assert.Throws<DialException>(() => d.Set("scoring", "run3", "60"));
            Assert.Throws<DialException>(() => d.Set("scoring", "run3", true));
            Assert.Throws<DialException>(() => d.Set("scoring", "run3", 60.5));
            Assert.Throws<DialException>(() => d.Set("scoring", "cascade_step", "0.5"));
            Assert.Throws<DialException>(() => d.Set("scoring", "cascade_step", false));
            Assert.Throws<DialException>(() => d.Set("scoring", "run3", null!));
            Assert.That(d.Scoring.Run3, Is.EqualTo(60), "a rejected set changes nothing");
        }

        [Test]
        public void Out_of_range_is_rejected()
        {
            var d = Load();
            var m = d.GetMeta("scoring", "run3");
            Assert.Throws<DialException>(() => d.Set("scoring", "run3", (int)m.Max + 1));
            Assert.Throws<DialException>(() => d.Set("scoring", "run3", (int)m.Min - 1));
            Assert.Throws<DialException>(() => d.Set("scoring", "cascade_step", double.NaN));
            Assert.Throws<DialException>(() => d.Set("scoring", "cascade_step", double.PositiveInfinity));
            Assert.Throws<DialException>(() => d.Set("specials", "incoming_chance", 1.01));
            d.Set("specials", "incoming_chance", 1.0);
            d.Set("scoring", "run3", (int)m.Max);
            Assert.That(d.Scoring.Run3, Is.EqualTo((int)m.Max));
        }

        [Test]
        public void An_integer_is_accepted_for_a_double_dial_and_stays_a_double()
        {
            var d = Load();
            d.Set("scoring", "cascade_step", 1);
            Assert.That(d.Scoring.CascadeStep, Is.EqualTo(1.0));
            Assert.That(d.Get("scoring", "cascade_step"), Is.TypeOf<double>());
            d.Set("scoring", "cascade_step", 2L);
            Assert.That(d.Scoring.CascadeStep, Is.EqualTo(2.0));

            var viaToml = Load(android: "[scoring]\ncascade_step = 1\n", host: Android);
            Assert.That(viaToml.Scoring.CascadeStep, Is.EqualTo(1.0));
        }

        [Test]
        public void An_ints_dial_rejects_a_float_in_a_toml_file()
        {
            Assert.Throws<DialException>(() => Load(Android, android: "[scoring]\nrun3 = 60.0\n"));
        }

        // ---- precedence

        [Test]
        public void Precedence_is_defaults_then_platform_then_dev()
        {
            var storage = new MemoryStorage();
            var android = "[camera]\npitch_deg = 40\n[scoring]\nrun3 = 70\n";
            storage.PutText("dev_dials.toml", "[scoring]\nrun3 = 80\n");

            var desktop = Load(Desktop, new MemoryStorage(), android);
            Assert.That(desktop.Camera.PitchDeg, Is.EqualTo(25), "android file is ignored on desktop");
            Assert.That(desktop.Scoring.Run3, Is.EqualTo(60));

            var phone = Load(Android, new MemoryStorage(), android);
            Assert.That(phone.Camera.PitchDeg, Is.EqualTo(40));
            Assert.That(phone.Scoring.Run3, Is.EqualTo(70));
            Assert.That(phone.Scoring.Run4, Is.EqualTo(120), "untouched dials keep the default");

            var dev = Load(Android, storage, android);
            Assert.That(dev.Scoring.Run3, Is.EqualTo(80), "dev beats platform");
            Assert.That(dev.Camera.PitchDeg, Is.EqualTo(40), "platform survives where dev is silent");
        }

        [Test]
        public void Reset_returns_to_the_platform_value_not_the_file_default()
        {
            var storage = new MemoryStorage();
            storage.PutText("dev_dials.toml", "[scoring]\nrun3 = 80\n");
            var d = Load(Android, storage, "[scoring]\nrun3 = 70\n");
            Assert.That(d.IsChanged("scoring", "run3"));
            Assert.That(d.GetMeta("scoring", "run3").Default, Is.EqualTo(70));
            d.Reset("scoring", "run3");
            Assert.That(d.Scoring.Run3, Is.EqualTo(70));
            Assert.That(d.IsChanged("scoring", "run3"), Is.False);
        }

        [Test]
        public void Missing_platform_file_is_fine()
        {
            Assert.DoesNotThrow(() => Load(Android));
        }

        [Test]
        public void Unknown_key_in_a_platform_file_is_rejected()
        {
            var e = Assert.Throws<DialException>(() => Load(Android, android: "[scoring]\nrun33 = 1\n"));
            StringAssert.Contains("dials.android.toml", e!.Message);
            Assert.Throws<DialException>(() => Load(Android, android: "[nonsense]\nx = 1\n"));
            Assert.Throws<DialException>(() => Load(Android, android: "top = 1\n"));
        }

        [Test]
        public void Bad_values_in_platform_and_dev_files_are_rejected()
        {
            Assert.Throws<DialException>(() => Load(Android, android: "[scoring]\nrun3 = \"x\"\n"));
            Assert.Throws<DialException>(() => Load(Android, android: "[scoring]\nrun3 = 99999999\n"));
            var storage = new MemoryStorage();
            storage.PutText("dev_dials.toml", "[scoring]\nrun33 = 1\n");
            Assert.Throws<DialException>(() => Load(Desktop, storage));
            storage.PutText("dev_dials.toml", "[scoring]\nrun3 = 99999999\n");
            Assert.Throws<DialException>(() => Load(Desktop, storage));
            storage.PutText("dev_dials.toml", "[scoring]\nrun3 = \"60\"\n");
            Assert.Throws<DialException>(() => Load(Desktop, storage));
        }

        [Test]
        public void Platform_file_name_follows_the_host_kind()
        {
            Assert.That(DialRegistry.PlatformFileName(PlatformKind.Android), Is.EqualTo("dials.android.toml"));
            Assert.That(DialRegistry.PlatformFileName(PlatformKind.Desktop), Is.EqualTo("dials.desktop.toml"));
            Assert.That(DialRegistry.PlatformFileName(PlatformKind.Web), Is.EqualTo("dials.web.toml"));
        }

        // ---- change notifications, reset

        [Test]
        public void Set_notifies_with_section_key_and_value_and_typed_accessors_see_it()
        {
            var d = Load();
            var seen = new List<DialChange>();
            d.Changed += seen.Add;
            d.Set("animation", "swap", 100);
            d.Set("camera", "zoom_max", 4);
            Assert.That(seen.Count, Is.EqualTo(2));
            Assert.That((seen[0].Section, seen[0].Key, seen[0].Value), Is.EqualTo(("animation", "swap", (object)100)));
            Assert.That(seen[1].Value, Is.EqualTo(4.0));
            Assert.That(d.Animation.Swap, Is.EqualTo(100));
            Assert.That(d.Camera.ZoomMax, Is.EqualTo(4.0));
        }

        [Test]
        public void Setting_the_same_value_does_not_notify_and_a_rejected_set_does_not_either()
        {
            var d = Load();
            int count = 0;
            d.Changed += _ => count++;
            d.Set("animation", "swap", 180);
            Assert.Throws<DialException>(() => d.Set("animation", "swap", 99999));
            Assert.That(count, Is.EqualTo(0));
        }

        [Test]
        public void Reset_and_ResetAll_restore_defaults_and_notify_only_changed_dials()
        {
            var d = Load();
            d.Set("animation", "swap", 100);
            d.Set("scoring", "run3", 61);
            d.Set("camera", "zoom_max", 5);
            var seen = new List<string>();
            d.Changed += c => seen.Add(c.Section + "." + c.Key);

            d.Reset("scoring", "run3");
            Assert.That(d.Scoring.Run3, Is.EqualTo(60));
            d.Reset("scoring", "run3");
            Assert.That(seen, Is.EqualTo(new[] { "scoring.run3" }));

            seen.Clear();
            d.ResetAll();
            Assert.That(seen, Is.EqualTo(new[] { "animation.swap", "camera.zoom_max" }));
            Assert.That(d.Animation.Swap, Is.EqualTo(180));
            Assert.That(d.Camera.ZoomMax, Is.EqualTo(3.0));
        }

        // ---- dev overrides persistence

        [Test]
        public void Dev_overrides_round_trip_and_only_changed_dials_are_written()
        {
            var storage = new MemoryStorage();
            var d = Load(Desktop, storage);
            d.Set("scoring", "run3", 65);
            d.Set("camera", "zoom_max", 4);
            d.Set("generator", "bot_weight_points", 0.025);
            d.Set("animation", "swap", 180); // same as default: not written
            d.SaveDevOverrides();

            var text = storage.GetText("dev_dials.toml")!;
            Assert.That(text, Does.Contain("run3 = 65"));
            Assert.That(text, Does.Contain("zoom_max = 4.0"));
            Assert.That(text, Does.Contain("bot_weight_points = 0.025"));
            Assert.That(text, Does.Not.Contain("swap"));
            Assert.That(text, Does.Not.Contain("run4"));

            var again = Load(Desktop, storage);
            Assert.That(again.Scoring.Run3, Is.EqualTo(65));
            Assert.That(again.Camera.ZoomMax, Is.EqualTo(4.0));
            Assert.That(again.Generator.BotWeightPoints, Is.EqualTo(0.025));
            Assert.That(again.Animation.Swap, Is.EqualTo(180));
            Assert.That(again.IsChanged("scoring", "run3"));
            Assert.That(again.ToDevToml(), Is.EqualTo(text));
        }

        [Test]
        public void Saving_with_no_changes_writes_no_overrides_and_reload_gives_defaults()
        {
            var storage = new MemoryStorage();
            var d = Load(Desktop, storage);
            d.Set("scoring", "run3", 65);
            d.SaveDevOverrides();
            d.ResetAll();
            d.SaveDevOverrides();
            var again = Load(Desktop, storage);
            Assert.That(again.Scoring.Run3, Is.EqualTo(60));
            Assert.That(storage.Writes, Is.EqualTo(2));
        }

        [Test]
        public void Saved_text_is_culture_invariant()
        {
            var old = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                var storage = new MemoryStorage();
                var d = Load(Desktop, storage);
                d.Set("scoring", "cascade_step", 0.75);
                d.SaveDevOverrides();
                Assert.That(storage.GetText("dev_dials.toml"), Does.Contain("cascade_step = 0.75"));
                Assert.That(Load(Desktop, storage).Scoring.CascadeStep, Is.EqualTo(0.75));
            }
            finally { CultureInfo.CurrentCulture = old; }
        }

        [Test]
        public void Saving_without_storage_throws_and_loading_without_storage_works()
        {
            var d = DialRegistry.Load(Data(), Desktop);
            Assert.Throws<InvalidOperationException>(() => d.SaveDevOverrides());
        }

        // ---- metadata

        [Test]
        public void Metadata_covers_exactly_the_keys_in_dials_toml()
        {
            var fromFile = new List<string>();
            string section = "";
            foreach (var raw in RealDefaults.Split('\n'))
            {
                var line = raw.Trim();
                var head = Regex.Match(line, @"^\[([a-z_0-9]+)\]");
                if (head.Success) { section = head.Groups[1].Value; continue; }
                var kv = Regex.Match(line, @"^([a-z_0-9]+)\s*=");
                if (kv.Success) fromFile.Add(section + "." + kv.Groups[1].Value);
            }
            var meta = Load().Metadata.Select(m => m.Section + "." + m.Key).ToList();
            Assert.That(fromFile, Is.Not.Empty);
            Assert.That(meta, Is.EquivalentTo(fromFile));
        }

        [Test]
        public void Metadata_is_complete_ordinal_and_sane()
        {
            var d = Load();
            var m = d.Metadata;
            var paths = m.Select(x => x.Section + "." + x.Key).ToList();
            Assert.That(paths.Distinct().Count(), Is.EqualTo(paths.Count));
            var sorted = m.OrderBy(x => x.Section, StringComparer.Ordinal).ThenBy(x => x.Key, StringComparer.Ordinal).ToList();
            Assert.That(m, Is.EqualTo(sorted), "ordinal by section then key");
            foreach (var x in m)
            {
                Assert.That(x.Description, Is.Not.Empty, x.Path);
                Assert.That(x.Step, Is.GreaterThan(0), x.Path);
                Assert.That(x.Min, Is.LessThan(x.Max), x.Path);
                var def = Convert.ToDouble(x.Default, CultureInfo.InvariantCulture);
                Assert.That(def, Is.InRange(x.Min, x.Max), x.Path + " default inside its range");
                Assert.That(x.Default, Is.TypeOf(x.Type == DialType.Int ? typeof(int) : typeof(double)), x.Path);
                if (x.Type == DialType.Int)
                    Assert.That(x.Step, Is.EqualTo(Math.Floor(x.Step)), x.Path + " int step is whole");
            }
        }

        [Test]
        public void Dev_page_style_editing_works_from_metadata_alone()
        {
            var d = Load();
            foreach (var m in d.Metadata)
            {
                // A page sets "default + one step" (or minus when at the top) through the generic API.
                var def = Convert.ToDouble(m.Default, CultureInfo.InvariantCulture);
                var next = def + m.Step <= m.Max ? def + m.Step : def - m.Step;
                object v = m.Type == DialType.Int ? (object)(int)Math.Round(next) : next;
                Assert.DoesNotThrow(() => d.Set(m.Section, m.Key, v), m.Path);
                Assert.That(d.IsChanged(m.Section, m.Key), m.Path);
            }
            d.ResetAll();
            Assert.That(d.Metadata.All(m => !d.IsChanged(m.Section, m.Key)));
        }

        [Test]
        public void Every_dial_has_a_typed_accessor_that_reads_the_registry_value()
        {
            var d = Load();
            var props = typeof(DialRegistry).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.PropertyType.Name.EndsWith("Dials", StringComparison.Ordinal))
                .ToDictionary(p => p.Name.ToLowerInvariant(), p => p.GetValue(d)!);
            int checkedCount = 0;
            foreach (var m in d.Metadata)
            {
                Assert.That(props.TryGetValue(m.Section, out var section), m.Section + " section accessor");
                var name = string.Concat(m.Key.Split('_').Select(w => char.ToUpperInvariant(w[0]) + w.Substring(1)));
                var prop = section!.GetType().GetProperty(name);
                Assert.That(prop, Is.Not.Null, m.Path + " -> " + name);
                Assert.That(prop!.PropertyType, Is.EqualTo(m.Type == DialType.Int ? typeof(int) : typeof(double)), m.Path);
                Assert.That(prop.GetValue(section), Is.EqualTo(d.Get(m.Section, m.Key)), m.Path);
                checkedCount++;
            }
            Assert.That(checkedCount, Is.EqualTo(d.Metadata.Count));
            Assert.That(props.Values.Sum(s => s.GetType().GetProperties().Length), Is.EqualTo(d.Metadata.Count),
                "no accessor without metadata");
        }
    }
}
