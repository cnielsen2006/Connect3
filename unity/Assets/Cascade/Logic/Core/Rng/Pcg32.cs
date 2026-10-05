using System;
using System.Collections.Generic;

namespace Cascade.Core
{
    /// <summary>
    /// PCG32 (PCG-XSH-RR, 64-bit state, 32-bit output; TRD §4.1). Mutable: the state advances with
    /// every draw. Sequences are bit-identical to the reference.
    /// </summary>
    public sealed class Pcg32
    {
        const ulong Mult = 6364136223846793005UL;

        ulong _state;
        ulong _inc;

        /// <summary>
        /// Follows the reference <c>pcg32_srandom</c>, except that the stream increment is given
        /// directly (and forced odd) instead of being <c>(initseq &lt;&lt; 1) | 1</c>.
        /// </summary>
        public Pcg32(ulong initState, ulong inc)
        {
            _inc = inc | 1UL;
            _state = 0;
            NextU32();
            unchecked { _state += initState; }
            NextU32();
        }

        Pcg32() { }

        /// <summary>The next 32 random bits.</summary>
        public uint NextU32()
        {
            unchecked
            {
                ulong old = _state;
                _state = old * Mult + _inc;
                uint xorshifted = (uint)(((old >> 18) ^ old) >> 27);
                int rot = (int)(old >> 59);
                return (xorshifted >> rot) | (xorshifted << ((-rot) & 31));
            }
        }

        /// <summary>Uniform in [0, n) by rejection sampling (no modulo bias). 1 &lt;= n &lt;= 2^32.</summary>
        public long RandBelow(long n)
        {
            if (n < 1 || n > (1L << 32))
                throw new ArgumentOutOfRangeException(nameof(n), "randbelow bound out of range: " + n);
            ulong un = (ulong)n;
            ulong threshold = ((1UL << 32) - un) % un;
            while (true)
            {
                ulong r = NextU32();
                if (r >= threshold) return (long)(r % un);
            }
        }

        /// <summary>Uniform in [0, n) by rejection sampling. n &gt;= 1.</summary>
        public int RandBelow(int n) => (int)RandBelow((long)n);

        /// <summary>Uniform double in [0, 1): <c>NextU32() / 2^32</c>.</summary>
        public double Random() => NextU32() / 4294967296.0;

        /// <summary>A uniformly chosen element. Throws on an empty list.</summary>
        public T Choice<T>(IReadOnlyList<T> items)
        {
            if (items.Count == 0) throw new ArgumentException("choice from empty sequence");
            return items[RandBelow(items.Count)];
        }

        /// <summary>Fisher-Yates, in place.</summary>
        public void Shuffle<T>(IList<T> items)
        {
            for (int i = items.Count - 1; i > 0; i--)
            {
                int j = RandBelow(i + 1);
                T tmp = items[i];
                items[i] = items[j];
                items[j] = tmp;
            }
        }

        /// <summary>An independent generator in the same state.</summary>
        public Pcg32 Copy() => new Pcg32 { _state = _state, _inc = _inc };

        /// <summary>The raw 64-bit state (for tests and diagnostics).</summary>
        public ulong State => _state;
    }
}
