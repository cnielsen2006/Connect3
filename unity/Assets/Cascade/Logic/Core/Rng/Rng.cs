using System.Text;

namespace Cascade.Core
{
    /// <summary>Hashing and seeding primitives of the deterministic RNG (TRD §4.1).</summary>
    public static class Rng
    {
        const ulong Golden = 0x9E3779B97F4A7C15UL;
        const ulong FnvOffset = 0xCBF29CE484222325UL;
        const ulong FnvPrime = 0x100000001B3UL;

        /// <summary>SplitMix64 mixing step.</summary>
        public static ulong SplitMix64(ulong x)
        {
            unchecked
            {
                ulong z = x + Golden;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        /// <summary>FNV-1a 64-bit hash of a byte sequence.</summary>
        public static ulong Fnv1a64(byte[] data)
        {
            unchecked
            {
                ulong h = FnvOffset;
                foreach (byte b in data) h = (h ^ b) * FnvPrime;
                return h;
            }
        }

        /// <summary>FNV-1a 64-bit hash of the UTF-8 bytes of <paramref name="text"/>.</summary>
        public static ulong Fnv1a64(string text) => Fnv1a64(Encoding.UTF8.GetBytes(text));

        /// <summary>The named PCG32 sub-stream of a level seed.</summary>
        public static Pcg32 Substream(ulong levelSeed, string name)
        {
            ulong h = Fnv1a64(name);
            ulong s = SplitMix64(levelSeed ^ h);
            ulong inc = SplitMix64(s);
            return new Pcg32(s, inc | 1UL);
        }
    }
}
