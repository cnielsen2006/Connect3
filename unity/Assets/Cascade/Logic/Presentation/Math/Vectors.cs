using System;
using System.Globalization;

namespace Cascade.Presentation
{
    /// <summary>
    /// A 2D vector of doubles. In board space (the world convention of TRD §12) <c>X</c> is world X and
    /// <c>Y</c> is world <b>Z</b> (the board lies in the XZ plane; Z is up on the screen). In screen space
    /// it is a pixel position with the origin at the top left and Y growing downwards.
    /// </summary>
    public readonly struct Float2 : IEquatable<Float2>
    {
        public Float2(double x, double y) { X = x; Y = y; }

        public double X { get; }
        public double Y { get; }

        public static readonly Float2 Zero = new Float2(0, 0);

        public double Length => Math.Sqrt(X * X + Y * Y);
        public double Distance(Float2 o) => (this - o).Length;

        public static Float2 Lerp(Float2 a, Float2 b, double t) => new Float2(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

        public static Float2 operator +(Float2 a, Float2 b) => new Float2(a.X + b.X, a.Y + b.Y);
        public static Float2 operator -(Float2 a, Float2 b) => new Float2(a.X - b.X, a.Y - b.Y);
        public static Float2 operator *(Float2 a, double k) => new Float2(a.X * k, a.Y * k);
        public static Float2 operator *(double k, Float2 a) => new Float2(a.X * k, a.Y * k);
        public static Float2 operator -(Float2 a) => new Float2(-a.X, -a.Y);

        /// <summary>True if both components differ from <paramref name="o"/> by at most <paramref name="eps"/>.</summary>
        public bool Near(Float2 o, double eps = 1e-9) => Math.Abs(X - o.X) <= eps && Math.Abs(Y - o.Y) <= eps;

        public bool Equals(Float2 o) => X == o.X && Y == o.Y;
        public override bool Equals(object? obj) => obj is Float2 o && Equals(o);
        public override int GetHashCode() { unchecked { return X.GetHashCode() * 397 ^ Y.GetHashCode(); } }
        public static bool operator ==(Float2 a, Float2 b) => a.Equals(b);
        public static bool operator !=(Float2 a, Float2 b) => !a.Equals(b);
        public override string ToString() => "(" + X.ToString("0.###", CultureInfo.InvariantCulture) + ", " + Y.ToString("0.###", CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>A 3D vector of doubles in world space (X right, Y up, Z up-the-screen on the board).</summary>
    public readonly struct Float3 : IEquatable<Float3>
    {
        public Float3(double x, double y, double z) { X = x; Y = y; Z = z; }

        public double X { get; }
        public double Y { get; }
        public double Z { get; }

        public static readonly Float3 Zero = new Float3(0, 0, 0);

        public double Dot(Float3 o) => X * o.X + Y * o.Y + Z * o.Z;
        public double Length => Math.Sqrt(Dot(this));

        public static Float3 operator +(Float3 a, Float3 b) => new Float3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Float3 operator -(Float3 a, Float3 b) => new Float3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Float3 operator *(Float3 a, double k) => new Float3(a.X * k, a.Y * k, a.Z * k);
        public static Float3 operator *(double k, Float3 a) => new Float3(a.X * k, a.Y * k, a.Z * k);

        public bool Near(Float3 o, double eps = 1e-9) => Math.Abs(X - o.X) <= eps && Math.Abs(Y - o.Y) <= eps && Math.Abs(Z - o.Z) <= eps;

        public bool Equals(Float3 o) => X == o.X && Y == o.Y && Z == o.Z;
        public override bool Equals(object? obj) => obj is Float3 o && Equals(o);
        public override int GetHashCode() { unchecked { return (X.GetHashCode() * 397 ^ Y.GetHashCode()) * 397 ^ Z.GetHashCode(); } }
        public static bool operator ==(Float3 a, Float3 b) => a.Equals(b);
        public static bool operator !=(Float3 a, Float3 b) => !a.Equals(b);
        public override string ToString() => "(" + X.ToString("0.###", CultureInfo.InvariantCulture) + ", " + Y.ToString("0.###", CultureInfo.InvariantCulture) + ", " + Z.ToString("0.###", CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>A rectangle in screen pixels: origin at the top left, Y down. <c>X, Y</c> is the top-left corner.</summary>
    public readonly struct RectF : IEquatable<RectF>
    {
        public RectF(double x, double y, double width, double height) { X = x; Y = y; Width = width; Height = height; }

        public double X { get; }
        public double Y { get; }
        public double Width { get; }
        public double Height { get; }

        public double Right => X + Width;
        public double Bottom => Y + Height;
        public Float2 Center => new Float2(X + Width / 2, Y + Height / 2);
        public Float2 Size => new Float2(Width, Height);

        /// <summary>True if the point is inside (edges included).</summary>
        public bool Contains(Float2 p) => p.X >= X && p.X <= Right && p.Y >= Y && p.Y <= Bottom;

        public bool Equals(RectF o) => X == o.X && Y == o.Y && Width == o.Width && Height == o.Height;
        public override bool Equals(object? obj) => obj is RectF o && Equals(o);
        public override int GetHashCode() { unchecked { return ((X.GetHashCode() * 397 ^ Y.GetHashCode()) * 397 ^ Width.GetHashCode()) * 397 ^ Height.GetHashCode(); } }
        public static bool operator ==(RectF a, RectF b) => a.Equals(b);
        public static bool operator !=(RectF a, RectF b) => !a.Equals(b);
        public override string ToString() => "[" + X.ToString("0.#", CultureInfo.InvariantCulture) + ", " + Y.ToString("0.#", CultureInfo.InvariantCulture) + " " + Width.ToString("0.#", CultureInfo.InvariantCulture) + "x" + Height.ToString("0.#", CultureInfo.InvariantCulture) + "]";
    }

    /// <summary>A linear RGBA color, each component 0..1.</summary>
    public readonly struct ColorF : IEquatable<ColorF>
    {
        public ColorF(double r, double g, double b, double a = 1.0) { R = r; G = g; B = b; A = a; }

        public double R { get; }
        public double G { get; }
        public double B { get; }
        public double A { get; }

        public static readonly ColorF White = new ColorF(1, 1, 1, 1);

        public ColorF WithAlpha(double a) => new ColorF(R, G, B, a);
        public static ColorF Lerp(ColorF a, ColorF b, double t) =>
            new ColorF(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t, a.A + (b.A - a.A) * t);

        public bool Equals(ColorF o) => R == o.R && G == o.G && B == o.B && A == o.A;
        public override bool Equals(object? obj) => obj is ColorF o && Equals(o);
        public override int GetHashCode() { unchecked { return ((R.GetHashCode() * 397 ^ G.GetHashCode()) * 397 ^ B.GetHashCode()) * 397 ^ A.GetHashCode(); } }
        public static bool operator ==(ColorF a, ColorF b) => a.Equals(b);
        public static bool operator !=(ColorF a, ColorF b) => !a.Equals(b);
        public override string ToString() => "rgba(" + R.ToString("0.##", CultureInfo.InvariantCulture) + ", " + G.ToString("0.##", CultureInfo.InvariantCulture) + ", " + B.ToString("0.##", CultureInfo.InvariantCulture) + ", " + A.ToString("0.##", CultureInfo.InvariantCulture) + ")";
    }
}
