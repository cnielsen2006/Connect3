using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Cascade.Platform.Abstractions;
using Tomlyn;
using Tomlyn.Model;

namespace Cascade.Config
{
    /// <summary>
    /// The dial registry (TRD §5): typed, validated, with metadata and change notifications.
    /// Layers, lowest to highest precedence: <c>dials.toml</c> (through <see cref="IDataSource"/>),
    /// <c>dials.&lt;kind&gt;.toml</c> for the host's platform (optional; may only change existing dials),
    /// then <c>dev_dials.toml</c> (through <see cref="IStorage"/>, optional). Any unknown section or key,
    /// wrong type or out-of-range value in any layer throws <see cref="DialException"/>.
    /// <para>
    /// Not thread-safe: change dials from the main thread. Generation reads an immutable settings
    /// snapshot built from the dials (Gameplay), not the registry. An <see cref="IDataSource"/> must throw
    /// <see cref="KeyNotFoundException"/> for a key it does not have (the platform file is optional).
    /// </para>
    /// </summary>
    public sealed class DialRegistry
    {
        public const string DefaultsFile = "dials.toml";
        public const string DevOverridesFile = "dev_dials.toml";

        enum Layer { Defaults, Platform, Dev }

        readonly Dictionary<string, Dial> byPath = new Dictionary<string, Dial>(StringComparer.Ordinal);
        readonly List<Dial> ordered = new List<Dial>();
        readonly HashSet<string> sections = new HashSet<string>(StringComparer.Ordinal);
        readonly IStorage? storage;

        DialRegistry(IStorage? storage)
        {
            this.storage = storage;
            var meta = new List<DialMeta>();
            foreach (var s in DialCatalog.Specs)
            {
                var m = new DialMeta(s.Section, s.Key, s.Type, s.Min, s.Max, s.Step, s.Description);
                var d = new Dial(m);
                byPath.Add(m.Path, d);
                ordered.Add(d);
                sections.Add(m.Section);
                meta.Add(m);
            }
            Metadata = meta;
            Scoring = new ScoringDials(this);
            Specials = new SpecialsDials(this);
            Stars = new StarsDials(this);
            Input = new InputDials(this);
            Render = new RenderDials(this);
            Animation = new AnimationDials(this);
            Celebration = new CelebrationDials(this);
            Audio = new AudioDials(this);
            Camera = new CameraDials(this);
            Pieces = new PiecesDials(this);
            Planner = new PlannerDials(this);
            Generator = new GeneratorDials(this);
        }

        // ---- typed sections (values are read live through the registry)
        public ScoringDials Scoring { get; }
        public SpecialsDials Specials { get; }
        public StarsDials Stars { get; }
        public InputDials Input { get; }
        public RenderDials Render { get; }
        public AnimationDials Animation { get; }
        public CelebrationDials Celebration { get; }
        public AudioDials Audio { get; }
        public CameraDials Camera { get; }
        public PiecesDials Pieces { get; }
        public PlannerDials Planner { get; }
        public GeneratorDials Generator { get; }

        /// <summary>Every dial, ordinal by section then key. The dev page is generated from this.</summary>
        public IReadOnlyList<DialMeta> Metadata { get; }

        /// <summary>Raised after a dial's value actually changed (never for a no-op set).</summary>
        public event Action<DialChange>? Changed;

        /// <summary>The platform file name for a host kind: <c>dials.android.toml</c>.</summary>
        public static string PlatformFileName(PlatformKind kind) =>
            "dials." + kind.ToString().ToLowerInvariant() + ".toml";

        /// <summary>
        /// Builds a registry. <paramref name="storage"/> may be null (no dev overrides, nothing can be saved).
        /// </summary>
        public static DialRegistry Load(IDataSource data, HostInfo host, IStorage? storage = null)
        {
            var reg = new DialRegistry(storage);
            reg.Apply(Layer.Defaults, DefaultsFile, data.ReadText(DefaultsFile));

            string? platformText = null;
            var platformFile = PlatformFileName(host.Kind);
            try { platformText = data.ReadText(platformFile); }
            catch (KeyNotFoundException) { }
            if (platformText != null) reg.Apply(Layer.Platform, platformFile, platformText);

            var dev = storage?.TryRead(DevOverridesFile);
            if (dev != null)
            {
                var text = Encoding.UTF8.GetString(dev).TrimStart('﻿');
                reg.Apply(Layer.Dev, DevOverridesFile, text);
            }
            return reg;
        }

        // ---- reading

        /// <summary>The current value, a boxed int or double.</summary>
        public object Get(string section, string key) => Find(section, key).Value;

        public int GetInt(string section, string key)
        {
            var d = Find(section, key);
            if (d.Meta.Type != DialType.Int) throw new DialException($"dial {d.Meta.Path} is a double, not an int");
            return d.Int;
        }

        public double GetDouble(string section, string key)
        {
            var d = Find(section, key);
            return d.Meta.Type == DialType.Int ? d.Int : d.Dbl;
        }

        public DialMeta GetMeta(string section, string key) => Find(section, key).Meta;

        public bool TryGetMeta(string section, string key, out DialMeta meta)
        {
            if (byPath.TryGetValue(section + "." + key, out var d)) { meta = d.Meta; return true; }
            meta = null!;
            return false;
        }

        /// <summary>True if the dial differs from its default (the value Reset returns to).</summary>
        public bool IsChanged(string section, string key) => Find(section, key).IsChanged;

        // ---- editing

        /// <summary>
        /// Changes a dial live and notifies subscribers. Accepts int/long for an int dial, and any
        /// integer or float for a double dial; anything else, or a value outside [Min, Max], throws.
        /// </summary>
        public void Set(string section, string key, object value)
        {
            var d = Find(section, key);
            Coerce(d.Meta, value, out var i, out var dv);
            Assign(d, i, dv, notify: true);
        }

        public void Reset(string section, string key)
        {
            var d = Find(section, key);
            Assign(d, d.BaseInt, d.BaseDbl, notify: true);
        }

        public void ResetAll()
        {
            foreach (var d in ordered) Assign(d, d.BaseInt, d.BaseDbl, notify: true);
        }

        /// <summary>
        /// Writes only the dials that differ from their defaults to <c>dev_dials.toml</c> through
        /// <see cref="IStorage.WriteAtomic"/>. With no changes the file is written empty (comment only).
        /// </summary>
        public void SaveDevOverrides()
        {
            if (storage == null) throw new InvalidOperationException("This registry was built without storage.");
            storage.WriteAtomic(DevOverridesFile, Encoding.UTF8.GetBytes(ToDevToml()));
        }

        /// <summary>The TOML text <see cref="SaveDevOverrides"/> writes (invariant culture).</summary>
        public string ToDevToml()
        {
            var sb = new StringBuilder("# Dev dial overrides (generated by the dev settings page).\n");
            string? current = null;
            foreach (var d in ordered)
            {
                if (!d.IsChanged) continue;
                if (current != d.Meta.Section)
                {
                    current = d.Meta.Section;
                    sb.Append('\n').Append('[').Append(current).Append("]\n");
                }
                sb.Append(d.Meta.Key).Append(" = ").Append(Format(d)).Append('\n');
            }
            return sb.ToString();
        }

        // ---- internals

        internal Dial Handle(string section, string key) => Find(section, key);

        Dial Find(string section, string key)
        {
            if (byPath.TryGetValue(section + "." + key, out var d)) return d;
            throw new DialException($"unknown dial: {section}.{key}");
        }

        void Assign(Dial d, int i, double dv, bool notify)
        {
            bool same = d.Meta.Type == DialType.Int ? d.Int == i : d.Dbl == dv;
            if (same) return;
            d.Int = i;
            d.Dbl = dv;
            if (notify) Changed?.Invoke(new DialChange(d.Meta.Section, d.Meta.Key, d.Value));
        }

        void Apply(Layer layer, string source, string text)
        {
            TomlTable model;
            try { model = Toml.ToModel(text); }
            catch (TomlException e) { throw new DialException($"{source}: {e.Message}"); }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var sectionPair in model)
            {
                if (!(sectionPair.Value is TomlTable table))
                    throw new DialException($"{source}: '{sectionPair.Key}' is not a section");
                if (!sections.Contains(sectionPair.Key))
                    throw new DialException($"{source}: unknown dial section [{sectionPair.Key}]");
                foreach (var pair in table)
                {
                    var path = sectionPair.Key + "." + pair.Key;
                    if (!byPath.TryGetValue(path, out var d))
                        throw new DialException($"{source}: unknown dial {path}");
                    try { Coerce(d.Meta, pair.Value, out var i, out var dv); ApplyLoaded(layer, d, i, dv); }
                    catch (DialException e) { throw new DialException($"{source}: {e.Message}"); }
                    seen.Add(path);
                }
            }

            if (layer != Layer.Defaults) return;
            foreach (var d in ordered)
                if (!seen.Contains(d.Meta.Path))
                    throw new DialException($"{source}: missing dial {d.Meta.Path}");
        }

        static void ApplyLoaded(Layer layer, Dial d, int i, double dv)
        {
            d.Int = i;
            d.Dbl = dv;
            if (layer == Layer.Dev) return;
            d.BaseInt = i;
            d.BaseDbl = dv;
            d.Meta.Default = d.Value;
        }

        /// <summary>Checks type and range; ints stay ints, and an integer is accepted for a double dial.</summary>
        static void Coerce(DialMeta m, object? raw, out int i, out double dv)
        {
            i = 0; dv = 0;
            double number;
            bool isInteger = TryInteger(raw, out var l);
            if (m.Type == DialType.Int)
            {
                if (!isInteger) throw new DialException($"dial {m.Path}: expected an integer, got {Describe(raw)}");
                number = l;
            }
            else if (isInteger) number = l;
            else if (raw is double d) number = d;
            else if (raw is float f) number = f;
            else throw new DialException($"dial {m.Path}: expected a number, got {Describe(raw)}");

            if (double.IsNaN(number) || double.IsInfinity(number) || number < m.Min || number > m.Max)
                throw new DialException(
                    $"dial {m.Path}: {Format(number)} is out of range [{Format(m.Min)}, {Format(m.Max)}]");
            if (m.Type == DialType.Int) i = (int)l; else dv = number;
        }

        static bool TryInteger(object? raw, out long value)
        {
            switch (raw)
            {
                case int a: value = a; return true;
                case long b: value = b; return true;
                case short c: value = c; return true;
                case byte e: value = e; return true;
                case sbyte f: value = f; return true;
                case ushort g: value = g; return true;
                case uint h: value = h; return true;
                default: value = 0; return false;
            }
        }

        static string Describe(object? raw) =>
            raw == null ? "null" : raw.GetType().Name + " " + Convert.ToString(raw, CultureInfo.InvariantCulture);

        static string Format(Dial d) => d.Meta.Type == DialType.Int
            ? d.Int.ToString(CultureInfo.InvariantCulture)
            : Format(d.Dbl);

        /// <summary>Shortest round-trip text, always recognisable as a TOML float (has '.' or an exponent).</summary>
        static string Format(double v)
        {
            var s = v.ToString("R", CultureInfo.InvariantCulture);
            return s.IndexOfAny(new[] { '.', 'E', 'e' }) >= 0 ? s : s + ".0";
        }
    }
}
