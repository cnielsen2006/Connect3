using System;
using System.Collections.Generic;
using System.Linq;
using Cascade.Core;
using NUnit.Framework;

namespace Cascade.Tests.CoreTests
{
    /// <summary>Tests for Core/Rng plus extras (TRD §9.1 Core/Rng).</summary>
    public class RngTests
    {
        [Test]
        public void Pcg32_reference_vectors()
        {
            // pcg32_srandom(42, 54) from the pcg-random.org reference demo (inc = (54 << 1) | 1 = 109)
            var r = new Pcg32(42, 109);
            var got = Enumerable.Range(0, 6).Select(_ => r.NextU32()).ToArray();
            Assert.That(got, Is.EqualTo(new uint[] { 0xA15C02B7, 0x7B47F409, 0xBA1D3330, 0x83D2F293, 0xBFA4784B, 0xCBED606E }));
        }

        [Test]
        public void Hash_vectors()
        {
            Assert.That(Rng.Fnv1a64(new byte[0]), Is.EqualTo(0xCBF29CE484222325UL));
            Assert.That(Rng.Fnv1a64(new byte[] { (byte)'a' }), Is.EqualTo(0xAF63DC4C8601EC8CUL));
            Assert.That(Rng.Fnv1a64("a"), Is.EqualTo(0xAF63DC4C8601EC8CUL));
            Assert.That(Rng.SplitMix64(0), Is.EqualTo(0xE220A8397B1DCDAFUL));
        }

        [Test]
        public void Substream_known_answers()
        {
            var s = Rng.Substream(12345, "tiles");
            var got = Enumerable.Range(0, 10).Select(_ => s.NextU32()).ToArray();
            Assert.That(got, Is.EqualTo(new uint[]
            {
                3568103722, 2923933164, 1763756212, 1165160585, 863915936,
                466246160, 3851713883, 3051233004, 3177557926, 3872502686,
            }));
        }

        [Test]
        public void Streams_are_independent_and_deterministic()
        {
            var a = new Streams(7);
            var b = new Streams(7);
            var xs = Enumerable.Range(0, 5).Select(_ => a.Get("tiles").NextU32()).ToArray();
            var ys = Enumerable.Range(0, 5).Select(_ => b.Get("tiles").NextU32()).ToArray();
            Assert.That(xs, Is.EqualTo(ys));
            Assert.That(Rng.Substream(7, "tiles").NextU32(), Is.Not.EqualTo(Rng.Substream(7, "spawn").NextU32()));
            var c = a.Copy();
            Assert.That(a.Get("tiles").NextU32(), Is.EqualTo(c.Get("tiles").NextU32()));
        }

        [Test]
        public void Named_streams_match_their_string_names()
        {
            var s = new Streams(99);
            Assert.That(s.Tiles, Is.SameAs(s.Get("tiles")));
            Assert.That(s.Spawn(new SourceId(3)), Is.SameAs(s.Get("spawn.3")));
            Assert.That(StreamNames.All, Is.EqualTo(new[] { "layout", "tiles", "goals", "spawn", "specials", "shuffle", "bot" }));
            Assert.That(new Streams(99).Bot.NextU32(), Is.EqualTo(Rng.Substream(99, "bot").NextU32()));
        }

        [Test]
        public void RandBelow_range_and_uniformity()
        {
            var r = Rng.Substream(1, "bot");
            var counts = new int[6];
            for (int i = 0; i < 6000; i++) counts[r.RandBelow(6)]++;
            Assert.That(counts.All(c => c > 800 && c < 1200), Is.True);
            Assert.That(r.RandBelow(1), Is.EqualTo(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => r.RandBelow(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => r.RandBelow((1L << 32) + 1));
        }

        [Test]
        public void Shuffle_is_a_permutation_and_deterministic()
        {
            var items = Enumerable.Range(0, 20).ToList();
            var a = new List<int>(items);
            Rng.Substream(3, "shuffle").Shuffle(a);
            var b = new List<int>(items);
            Rng.Substream(3, "shuffle").Shuffle(b);
            Assert.That(a, Is.EqualTo(b));
            Assert.That(a.OrderBy(x => x).ToList(), Is.EqualTo(items));
            Assert.That(a, Is.Not.EqualTo(items));
        }

        [Test]
        public void Choice_and_random()
        {
            var r = Rng.Substream(5, "bot");
            Assert.Throws<ArgumentException>(() => r.Choice(new List<int>()));
            for (int i = 0; i < 100; i++)
            {
                double d = r.Random();
                Assert.That(d, Is.GreaterThanOrEqualTo(0.0).And.LessThan(1.0));
            }
            Assert.That(new Pcg32(1, 2).Choice(new[] { 7 }), Is.EqualTo(7));
        }

        [Test]
        public void Copy_continues_identically()
        {
            var r = new Pcg32(11, 13);
            r.NextU32();
            var c = r.Copy();
            Assert.That(Enumerable.Range(0, 5).Select(_ => c.NextU32()).ToArray(),
                Is.EqualTo(Enumerable.Range(0, 5).Select(_ => r.NextU32()).ToArray()));
        }

        [Test]
        public void FloorDiv_and_FloorMod_follow_floor_semantics_on_negatives()
        {
            Assert.That(MathX.FloorDiv(-7, 2), Is.EqualTo(-4));
            Assert.That(MathX.FloorMod(-7, 2), Is.EqualTo(1));
            Assert.That(MathX.FloorDiv(7, -2), Is.EqualTo(-4));
            Assert.That(MathX.FloorMod(7, -2), Is.EqualTo(-1));
            Assert.That(MathX.FloorMod(-6, 3), Is.EqualTo(0));
            Assert.That(MathX.FloorDiv(-6L, 3L), Is.EqualTo(-2L));
            Assert.That(MathX.FloorMod(-1L, 8L), Is.EqualTo(7L));
        }

        [Test]
        public void RoundHalfEven_rounds_half_to_even()
        {
            Assert.That(MathX.RoundHalfEven(0.5), Is.EqualTo(0));
            Assert.That(MathX.RoundHalfEven(1.5), Is.EqualTo(2));
            Assert.That(MathX.RoundHalfEven(2.5), Is.EqualTo(2));
            Assert.That(MathX.RoundHalfEven(-0.5), Is.EqualTo(0));
            Assert.That(MathX.RoundHalfEven(150.0), Is.EqualTo(150));
        }
    }
}
