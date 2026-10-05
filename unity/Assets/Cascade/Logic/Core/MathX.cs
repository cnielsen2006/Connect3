using System;

namespace Cascade.Core
{
    /// <summary>
    /// Integer and rounding helpers whose behavior is floor-based (TRD §4.3). C# truncates on
    /// division and remainder, these helpers floor: use them wherever an operand may be negative.
    /// </summary>
    public static class MathX
    {
        /// <summary>Floor division (<c>a // b</c> semantics).</summary>
        public static int FloorDiv(int a, int b)
        {
            int q = a / b;
            if ((a % b != 0) && ((a < 0) != (b < 0))) q--;
            return q;
        }

        /// <summary>Floor modulo (<c>a % b</c> floor semantics): the result has the sign of <paramref name="b"/>.</summary>
        public static int FloorMod(int a, int b)
        {
            int r = a % b;
            if (r != 0 && ((r < 0) != (b < 0))) r += b;
            return r;
        }

        /// <summary>Floor division on <see cref="long"/>.</summary>
        public static long FloorDiv(long a, long b)
        {
            long q = a / b;
            if ((a % b != 0) && ((a < 0) != (b < 0))) q--;
            return q;
        }

        /// <summary>Floor modulo on <see cref="long"/>.</summary>
        public static long FloorMod(long a, long b)
        {
            long r = a % b;
            if (r != 0 && ((r < 0) != (b < 0))) r += b;
            return r;
        }

        /// <summary>Round half to even (<c>round(x)</c> semantics), returning an <see cref="int"/>.</summary>
        public static int RoundHalfEven(double x) => (int)Math.Round(x, MidpointRounding.ToEven);
    }
}
