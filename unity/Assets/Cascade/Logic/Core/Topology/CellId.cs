using System;
using System.Globalization;

namespace Cascade.Core
{
    /// <summary>
    /// Identifies one cell of a <see cref="Topology"/>. Logic only compares and stores ids; the view
    /// maps them to world space through <see cref="Topology.Pos"/>. Ids of a topology are not
    /// necessarily contiguous (holes are absent). Implicitly convertible from <see cref="int"/>.
    /// </summary>
    public readonly struct CellId : IEquatable<CellId>, IComparable<CellId>
    {
        public CellId(int value) { Value = value; }

        /// <summary>The raw id (an index into per-cell arrays of size <see cref="Topology.CellCapacity"/>).</summary>
        public int Value { get; }

        public bool Equals(CellId other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is CellId o && Value == o.Value;
        public override int GetHashCode() => Value;
        public int CompareTo(CellId other) => Value.CompareTo(other.Value);
        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(CellId a, CellId b) => a.Value == b.Value;
        public static bool operator !=(CellId a, CellId b) => a.Value != b.Value;
        public static bool operator <(CellId a, CellId b) => a.Value < b.Value;
        public static bool operator >(CellId a, CellId b) => a.Value > b.Value;
        public static bool operator <=(CellId a, CellId b) => a.Value <= b.Value;
        public static bool operator >=(CellId a, CellId b) => a.Value >= b.Value;

        public static implicit operator CellId(int value) => new CellId(value);
        public static explicit operator int(CellId id) => id.Value;
    }

    /// <summary>Identifies a <see cref="Source"/> (a place that spawns new tiles).</summary>
    public readonly struct SourceId : IEquatable<SourceId>, IComparable<SourceId>
    {
        public SourceId(int value) { Value = value; }

        public int Value { get; }

        public bool Equals(SourceId other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is SourceId o && Value == o.Value;
        public override int GetHashCode() => Value;
        public int CompareTo(SourceId other) => Value.CompareTo(other.Value);
        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(SourceId a, SourceId b) => a.Value == b.Value;
        public static bool operator !=(SourceId a, SourceId b) => a.Value != b.Value;

        public static implicit operator SourceId(int value) => new SourceId(value);
        public static explicit operator int(SourceId id) => id.Value;
    }

    /// <summary>
    /// Logical coordinates of a cell, for the view only (rules never read them). On a grid,
    /// <c>Y = 0</c> is the top row.
    /// </summary>
    public readonly struct Pos : IEquatable<Pos>
    {
        public Pos(int layer, int x, int y) { Layer = layer; X = x; Y = y; }

        public int Layer { get; }
        public int X { get; }
        public int Y { get; }

        public bool Equals(Pos other) => Layer == other.Layer && X == other.X && Y == other.Y;
        public override bool Equals(object? obj) => obj is Pos o && Equals(o);
        public override int GetHashCode() { unchecked { return (Layer * 31 + X) * 31 + Y; } }
        public override string ToString() => "(" + Layer + ", " + X + ", " + Y + ")";

        public static bool operator ==(Pos a, Pos b) => a.Equals(b);
        public static bool operator !=(Pos a, Pos b) => !a.Equals(b);

        public void Deconstruct(out int layer, out int x, out int y) { layer = Layer; x = X; y = Y; }
    }
}
