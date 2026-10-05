using System;
using System.Collections.Generic;

namespace Cascade.Generation
{
    /// <summary>The curve shapes of the difficulty profile (TRD §6).</summary>
    public enum CurveShape { Linear, EaseOut, Step }

    /// <summary>
    /// Maps a level number through sorted control points <c>(n_i, v_i)</c>; constant outside them
    /// (TRD §6). Immutable.
    /// </summary>
    public sealed class Curve
    {
        readonly double[] _xs;
        readonly double[] _vs;

        /// <summary>Validates the shape's points: at least one, strictly increasing levels.</summary>
        public Curve(CurveShape shape, IReadOnlyList<(double N, double Value)> points)
        {
            if (points.Count == 0) throw new ArgumentException("a curve needs at least one control point");
            _xs = new double[points.Count];
            _vs = new double[points.Count];
            for (int i = 0; i < points.Count; i++)
            {
                _xs[i] = points[i].N;
                _vs[i] = points[i].Value;
                if (i > 0 && _xs[i] <= _xs[i - 1])
                    throw new ArgumentException("curve control points must have strictly increasing levels");
            }
            Shape = shape;
        }

        public CurveShape Shape { get; }

        /// <summary>Number of control points.</summary>
        public int PointCount => _xs.Length;

        /// <summary>The curve's name in the TOML file: "linear", "ease_out" or "step".</summary>
        public static string ShapeName(CurveShape shape) =>
            shape == CurveShape.Linear ? "linear" : shape == CurveShape.EaseOut ? "ease_out" : "step";

        /// <summary>Parses a shape name; false if unknown.</summary>
        public static bool TryParseShape(string name, out CurveShape shape)
        {
            switch (name)
            {
                case "linear": shape = CurveShape.Linear; return true;
                case "ease_out": shape = CurveShape.EaseOut; return true;
                case "step": shape = CurveShape.Step; return true;
                default: shape = CurveShape.Linear; return false;
            }
        }

        /// <summary>The curve's value at level <paramref name="n"/>.</summary>
        public double At(double n)
        {
            int last = _xs.Length - 1;
            if (n <= _xs[0]) return _vs[0];
            if (n >= _xs[last]) return _vs[last];
            for (int i = 0; i < last; i++)
            {
                double n0 = _xs[i], n1 = _xs[i + 1], v0 = _vs[i], v1 = _vs[i + 1];
                if (n0 <= n && n < n1)
                {
                    if (Shape == CurveShape.Step) return v0;
                    double t = (n - n0) / (n1 - n0);
                    if (Shape == CurveShape.EaseOut) t = 1 - (1 - t) * (1 - t); // a product, not Pow (TRD §6)
                    return v0 + (v1 - v0) * t;
                }
            }
            return _vs[last]; // unreachable
        }
    }

    /// <summary>The pressure/release difficulty function (PRD §3.2, TRD §6).</summary>
    public static class Difficulty
    {
        /// <summary>
        /// -R at phase 0 (release), then <c>((p-1)/(P-2))^K</c> up to 1.0 at p = P-1 (the pinch).
        /// This is the one place <see cref="Math.Pow"/> is allowed (TRD §4.3).
        /// </summary>
        public static double Wave(int phase, int cycleLength, double rampPower, double releaseDepth)
        {
            if (phase == 0) return -releaseDepth;
            return Math.Pow((phase - 1) / (double)(cycleLength - 2), rampPower);
        }

        /// <summary>Position in the cycle, 0 .. P-1: <c>(n - 1) mod P</c>.</summary>
        public static int Phase(int n, int cycleLength) => Cascade.Core.MathX.FloorMod(n - 1, cycleLength);

        /// <summary>A pinch level: <c>n mod P == 0</c>.</summary>
        public static bool IsPinch(int n, int cycleLength) => Cascade.Core.MathX.FloorMod(n, cycleLength) == 0;

        /// <summary>A release level: phase 0 after the first level.</summary>
        public static bool IsRelease(int n, int cycleLength) => Phase(n, cycleLength) == 0 && n > 1;

        /// <summary><c>clamp(trend(n) + amplitude(n) * wave(phase(n)), 0, 1)</c>.</summary>
        public static double At(int n, Curve trend, Curve amplitude, int cycleLength, double rampPower, double releaseDepth)
        {
            double w = Wave(Phase(n, cycleLength), cycleLength, rampPower, releaseDepth);
            return Math.Min(1.0, Math.Max(0.0, trend.At(n) + amplitude.At(n) * w));
        }
    }
}
