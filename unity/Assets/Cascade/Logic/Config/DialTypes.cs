using System;

namespace Cascade.Config
{
    /// <summary>A dial file or a dial change was rejected (unknown dial, wrong type, out of range, bad TOML).</summary>
    public sealed class DialException : Exception
    {
        public DialException(string message) : base(message) { }
    }

    public enum DialType { Int, Double }

    /// <summary>A dial changed. <see cref="Value"/> is the new value (boxed int or double).</summary>
    public readonly struct DialChange
    {
        public DialChange(string section, string key, object value)
        {
            Section = section; Key = key; Value = value;
        }

        public string Section { get; }
        public string Key { get; }
        public object Value { get; }
    }

    /// <summary>
    /// Everything a generated dev page needs for one dial (TRD §5, §12.2). <see cref="Default"/> is the
    /// value Reset returns to: dials.toml with the platform file applied (boxed int or double).
    /// </summary>
    public sealed class DialMeta
    {
        internal DialMeta(string section, string key, DialType type, double min, double max, double step, string description)
        {
            Section = section; Key = key; Type = type; Min = min; Max = max; Step = step; Description = description;
            Default = type == DialType.Int ? (object)0 : 0.0;
        }

        public string Section { get; }
        public string Key { get; }
        public DialType Type { get; }
        public object Default { get; internal set; }
        public double Min { get; }
        public double Max { get; }
        public double Step { get; }
        public string Description { get; }
        public string Path => Section + "." + Key;
    }

    /// <summary>The live value of one dial. Typed accessors hold these so reads are a field load.</summary>
    internal sealed class Dial
    {
        public Dial(DialMeta meta) { Meta = meta; }

        public DialMeta Meta { get; }
        public int Int;
        public double Dbl;
        public int BaseInt;
        public double BaseDbl;

        public bool IsChanged => Meta.Type == DialType.Int ? Int != BaseInt : Dbl != BaseDbl;
        public object Value => Meta.Type == DialType.Int ? (object)Int : Dbl;
        public object BaseValue => Meta.Type == DialType.Int ? (object)BaseInt : BaseDbl;
    }
}
