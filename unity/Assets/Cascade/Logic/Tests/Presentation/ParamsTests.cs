using Cascade.Config;
using Cascade.Platform.Abstractions;
using Cascade.Presentation;
using Cascade.Tests.Support;
using NUnit.Framework;

namespace Cascade.Tests.PresentationTests
{
    /// <summary>The parameter records the composition root builds from the dials.</summary>
    public class ParamsTests
    {
        static readonly HostInfo Desktop = new HostInfo(PlatformKind.Desktop, true, true, false, true, 96f);

        static DialRegistry Dials() =>
            DialRegistry.Load(new DictionaryDataSource().With("dials.toml", RepoFiles.ReadData("dials.toml")), Desktop, new MemoryStorage());

        [Test]
        public void TimingParams_From_the_shipped_dials_equals_the_defaults_used_by_the_planner_tests()
        {
            var t = TimingParams.From(Dials());
            Assert.That(t, Is.EqualTo(TimingParams.Default));
            Assert.That(t.SwapMs, Is.EqualTo(180));
            Assert.That(t.FallCellsPerSec, Is.EqualTo(14));
            Assert.That(t.SettleWobbles, Is.EqualTo(1));
            Assert.That(t.LandSquash, Is.EqualTo(0.2));
        }

        [Test]
        public void TimingParams_follow_live_dial_edits_and_the_animation_setting()
        {
            var d = Dials();
            d.Set("animation", "swap", 300);
            d.Set("animation", "land_squash", 0.4);
            var t = TimingParams.From(d, new AnimationSettings(2.0));
            Assert.That(t.SwapMs, Is.EqualTo(300));
            Assert.That(t.LandSquash, Is.EqualTo(0.4));
            Assert.That(t.Speed, Is.EqualTo(2.0));
            Assert.That(t.Scaled(300), Is.EqualTo(150));
        }

        [Test]
        public void Animation_speed_is_clamped_to_the_player_range()
        {
            Assert.That(new AnimationSettings(0.1).ClampedSpeed, Is.EqualTo(0.5));
            Assert.That(new AnimationSettings(9).ClampedSpeed, Is.EqualTo(3.0));
            Assert.That(new AnimationSettings(1.5).ClampedSpeed, Is.EqualTo(1.5));
            Assert.That(new AnimationSettings(double.NaN).ClampedSpeed, Is.EqualTo(1.0));
            Assert.That(TimingParams.From(Dials(), new AnimationSettings(10)).Speed, Is.EqualTo(3.0));
        }

        [Test]
        public void InputParams_read_the_mouse_dials_and_the_touch_dials()
        {
            var d = Dials();
            var mouse = InputParams.ForMouse(d);
            Assert.That(mouse.InnerFraction, Is.EqualTo(0.6));
            Assert.That(mouse.DoubleClickMs, Is.EqualTo(300));
            d.Set("input", "drag_inner_fraction", 0.7);
            Assert.That(InputParams.ForMouse(d).InnerFraction, Is.EqualTo(0.7));
            var touch = InputParams.ForTouch(d);
            Assert.That(touch.InnerFraction, Is.EqualTo(0.5));
            Assert.That(touch.DoubleClickMs, Is.EqualTo(300));
            d.Set("input", "touch_inner_fraction", 0.4);
            d.Set("input", "double_tap_ms", 250);
            Assert.That(InputParams.ForTouch(d), Is.EqualTo(new InputParams(0.4, 250)));
            touch = InputParams.ForTouch(d);
            Assert.That(InputParams.From(d, touch: true), Is.EqualTo(touch));
        }

        [Test]
        public void GestureParams_From_the_dials_has_the_documented_defaults()
        {
            var g = GestureParams.From(Dials());
            Assert.That(g.TapSlopMm, Is.EqualTo(2.0));
            Assert.That(g.PinchMinMm, Is.EqualTo(4.0));
            Assert.That(g.DoubleTapMs, Is.EqualTo(300));
            var d = Dials();
            d.Set("input", "tap_slop_mm", 3.0);
            d.Set("input", "pinch_min_mm", 5.0);
            Assert.That(GestureParams.From(d), Is.EqualTo(new GestureParams(3.0, 5.0, 300)));
        }

        [Test]
        public void CameraParams_and_PieceVariationParams_read_their_sections()
        {
            var d = Dials();
            var c = CameraParams.From(d);
            Assert.That((c.FitMargin, c.ZoomMin, c.ZoomMax, c.ZoomStep, c.PitchDeg), Is.EqualTo((0.04, 0.6, 3.0, 1.12, 25.0)));
            d.Set("camera", "pitch_deg", 30);
            Assert.That(CameraParams.From(d).PitchDeg, Is.EqualTo(30));
            var p = PieceVariationParams.From(d);
            Assert.That(p, Is.EqualTo(PieceVariationParams.Default));
            Assert.That(p.YawDeg, Is.EqualTo(13));
        }
    }
}
