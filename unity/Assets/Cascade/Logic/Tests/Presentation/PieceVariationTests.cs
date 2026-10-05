using System.Linq;
using Cascade.Presentation;
using NUnit.Framework;

namespace Cascade.Tests.PresentationTests
{
    /// <summary>TRD 12.1: per-instance variation is a pure function of the tile id and the [pieces] dials.</summary>
    public class PieceVariationTests
    {
        static readonly PieceVariationParams P = PieceVariationParams.Default;

        [Test]
        public void Same_tile_id_gives_the_same_look_every_time()
        {
            foreach (int id in new[] { 0, 1, 7, 1000, 123456 })
            {
                var a = PieceVariation.For(id, P);
                var b = PieceVariation.For(id, P);
                Assert.That(b.YawDegrees, Is.EqualTo(a.YawDegrees));
                Assert.That(b.Squash, Is.EqualTo(a.Squash));
                Assert.That(b.SquashOnX, Is.EqualTo(a.SquashOnX));
                Assert.That(b.Brightness, Is.EqualTo(a.Brightness));
                Assert.That(b.FleckVariant, Is.EqualTo(a.FleckVariant));
                Assert.That(b.FleckOffsetX, Is.EqualTo(a.FleckOffsetX));
                Assert.That(b.FleckOffsetY, Is.EqualTo(a.FleckOffsetY));
            }
        }

        [Test]
        public void Matches_reference_values_for_known_tile_ids()
        {
            // reference values: PCG32(tile_id * 2654435761 + 12345, 54)
            var cases = new (int Id, double Yaw, double Squash, double Bright, bool OnX, int Fleck, double Ox, double Oy)[]
            {
                (0, -7.676584162283689, 0.960013889730908, 1.0288779125269503, true, 7, 0.9479654936585575, 0.94920350657776),
                (1, 10.809095103293657, 0.8729438933683559, 0.9437297729589045, false, 5, 0.24527588509954512, 0.8780525932088494),
                (7, -5.5321273086592555, 0.9391674751089886, 1.0554197722114622, true, 6, 0.41039778804406524, 0.4905328657478094),
                (1000, 5.997620542533696, 0.9706254147691652, 0.9898279674258084, true, 2, 0.5867770162876695, 0.5667917695827782),
                (123456, -12.86928422562778, 0.9323845663992688, 1.0563933173008264, false, 0, 0.9475850139278919, 0.9839820612687618),
            };
            foreach (var c in cases)
            {
                var l = PieceVariation.For(c.Id, P);
                Assert.That(l.YawDegrees, Is.EqualTo(c.Yaw).Within(1e-12), "yaw " + c.Id);
                Assert.That(l.Squash, Is.EqualTo(c.Squash).Within(1e-12), "squash " + c.Id);
                Assert.That(l.Brightness, Is.EqualTo(c.Bright).Within(1e-12), "brightness " + c.Id);
                Assert.That(l.SquashOnX, Is.EqualTo(c.OnX), "flip " + c.Id);
                Assert.That(l.FleckVariant, Is.EqualTo(c.Fleck), "fleck " + c.Id);
                Assert.That(l.FleckOffsetX, Is.EqualTo(c.Ox).Within(1e-12));
                Assert.That(l.FleckOffsetY, Is.EqualTo(c.Oy).Within(1e-12));
            }
        }

        [Test]
        public void Values_stay_inside_the_dial_ranges_for_many_ids()
        {
            var p = new PieceVariationParams(YawDeg: 20, SquashMin: 0.7, Brightness: 0.1);
            for (int id = 0; id < 5000; id++)
            {
                var l = PieceVariation.For(id, p);
                Assert.That(l.YawDegrees, Is.InRange(-20.0, 20.0));
                Assert.That(l.Squash, Is.InRange(0.7, 1.0));
                Assert.That(l.Brightness, Is.InRange(0.9, 1.1));
                Assert.That(l.FleckVariant, Is.InRange(0, PieceVariation.FleckVariants - 1));
                Assert.That(l.FleckOffsetX, Is.InRange(0.0, 1.0));
                Assert.That(l.FleckOffsetY, Is.InRange(0.0, 1.0));
            }
        }

        [Test]
        public void Variation_is_real_not_constant_and_both_squash_axes_and_all_flecks_occur()
        {
            var looks = Enumerable.Range(0, 400).Select(i => PieceVariation.For(i, P)).ToList();
            Assert.That(looks.Select(l => l.YawDegrees).Distinct().Count(), Is.GreaterThan(300));
            Assert.That(looks.Select(l => l.SquashOnX).Distinct().Count(), Is.EqualTo(2));
            Assert.That(looks.Select(l => l.FleckVariant).Distinct().Count(), Is.EqualTo(PieceVariation.FleckVariants));
            Assert.That(looks.Min(l => l.YawDegrees), Is.LessThan(-10));
            Assert.That(looks.Max(l => l.YawDegrees), Is.GreaterThan(10));
        }

        [Test]
        public void Zero_variation_dials_give_a_neutral_look()
        {
            var l = PieceVariation.For(42, new PieceVariationParams(0, 1.0, 0));
            Assert.That(l.YawDegrees, Is.EqualTo(0));
            Assert.That(l.Squash, Is.EqualTo(1.0));
            Assert.That(l.Brightness, Is.EqualTo(1.0));
        }

        [Test]
        public void Dials_scale_the_same_underlying_draws()
        {
            var narrow = PieceVariation.For(5, new PieceVariationParams(5, 0.82, 0.06));
            var wide = PieceVariation.For(5, new PieceVariationParams(10, 0.82, 0.06));
            Assert.That(wide.YawDegrees, Is.EqualTo(narrow.YawDegrees * 2).Within(1e-12));
            Assert.That(wide.FleckVariant, Is.EqualTo(narrow.FleckVariant));
        }
    }
}
