using System.Collections.Generic;

namespace Cascade.Presentation
{
    /// <summary>
    /// Small real-time timelines for things that are not event playback: the select lift and the hint pulse.
    /// They play through the same <see cref="TimelineSampler"/> as a move does but contain no
    /// <see cref="SyncToFinalBoard"/> marker.
    /// </summary>
    public static class PoseTimelines
    {
        /// <summary>
        /// Lift (or drop) a piece: <c>SelectT</c> from its current value to 1 (selected) or 0 over
        /// <c>select_lift</c>. Start a new one whenever the selection changes; stop the old one first.
        /// </summary>
        public static Timeline Select(int tileId, double currentSelectT, bool selected, TimingParams timing)
        {
            double d = timing.Scaled(timing.SelectLiftMs);
            var items = new List<TimelineItem>
            {
                new Tween(tileId, PoseProperty.SelectT, new Float2(currentSelectT, 0), new Float2(selected ? 1.0 : 0.0, 0), 0, d, Easing.SmoothStep),
            };
            return new Timeline(items, d, d, new string[0]);
        }

        /// <summary>The hint: the pieces of a suggested move bounce together (<c>Hover</c> pulses, then returns to 0).</summary>
        public static Timeline Hint(IEnumerable<int> tileIds, TimingParams timing)
        {
            double d = timing.Scaled(timing.HintMs);
            var items = new List<TimelineItem>();
            foreach (int id in tileIds)
                items.Add(new Tween(id, PoseProperty.Hover, Float2.Zero, new Float2(timing.HintHover, 0), 0, d, Easing.Pulse, timing.HintPulses));
            return new Timeline(items, d, d, new string[0]);
        }
    }
}
