using System;
using System.Collections.Generic;
using Cascade.Core;

namespace Cascade.Presentation
{
    /// <summary>
    /// The pose properties a tween may target. <c>PieceView</c> exposes exactly these. Scalar properties
    /// carry their value in <see cref="Float2.X"/>.
    /// </summary>
    public enum PoseProperty
    {
        /// <summary>Board-space position (world X, world Z) of the piece's middle on the tray. A <see cref="Float2"/>.</summary>
        Position,
        /// <summary>Extra lift above the resting height in world units (swap slide, hint pulse).</summary>
        Hover,
        /// <summary>0..1: how selected the piece is (lift, scale and halo are derived by the view).</summary>
        SelectT,
        /// <summary>Vertical scale factor, 1 = normal (landing squash, special pop, shuffle).</summary>
        Squash,
        /// <summary>Rock in degrees: a tilt about the horizontal axis (setR), used for the swap rock and the settling wobble after a landing.</summary>
        Roll,
        /// <summary>0..1: how far the piece has sunk into the tray (clear).</summary>
        Sink,
        /// <summary>Uniform scale factor, 1 = normal.</summary>
        Scale,
    }

    /// <summary>
    /// One thing on a <see cref="Timeline"/>. Items are sorted by <see cref="StartMs"/>; items starting at the
    /// same time keep the order the planner emitted them (<see cref="Order"/>). Times are in milliseconds
    /// from the start of the timeline and are already divided by the animation speed.
    /// </summary>
    public abstract class TimelineItem
    {
        protected TimelineItem(double startMs) { StartMs = startMs; }

        public double StartMs { get; }

        /// <summary>The emit order of the planner (unique in a timeline); the tie-break for equal start times.</summary>
        public int Order { get; internal set; }

        /// <summary>When the item is over: <see cref="StartMs"/> for an instant action.</summary>
        public virtual double EndMs => StartMs;
    }

    /// <summary>A tween of one pose property of one piece.</summary>
    public sealed class Tween : TimelineItem
    {
        public Tween(int tileId, PoseProperty property, Float2 from, Float2 to, double startMs, double durationMs,
            Easing easing, double easingParam = 0, IReadOnlyList<Float2>? path = null)
            : base(startMs)
        {
            TileId = tileId; Property = property; From = from; To = to;
            DurationMs = durationMs; Easing = easing; EasingParam = easingParam; Path = path;
        }

        public int TileId { get; }
        public PoseProperty Property { get; }
        /// <summary>Start value (scalar properties: <c>.X</c>).</summary>
        public Float2 From { get; }
        /// <summary>End value of the shape: <c>From + (To - From) * f(u)</c>. Returning shapes (<see cref="Easing.Bump"/>, <see cref="Easing.Wobble"/>) end at <c>From</c>.</summary>
        public Float2 To { get; }
        public double DurationMs { get; }
        public Easing Easing { get; }
        /// <summary>Wobble: P of sin(pi u P); Pulse: the number of pulses.</summary>
        public double EasingParam { get; }
        /// <summary>
        /// For <see cref="PoseProperty.Position"/> only: waypoints including both ends (a piece funneling or
        /// falling several cells). The position is then <c>f(u)</c> of the way along the polyline by length.
        /// Null = straight from <see cref="From"/> to <see cref="To"/>.
        /// </summary>
        public IReadOnlyList<Float2>? Path { get; }

        public override double EndMs => StartMs + DurationMs;

        /// <summary>Progress 0..1 at time <paramref name="tMs"/> (1 for a zero-length tween).</summary>
        public double Progress(double tMs)
        {
            if (DurationMs <= 0) return tMs >= StartMs ? 1.0 : 0.0;
            double u = (tMs - StartMs) / DurationMs;
            return u < 0 ? 0 : (u > 1 ? 1 : u);
        }

        /// <summary>The value at time <paramref name="tMs"/>; before the start it is <see cref="From"/>, after the end the final value.</summary>
        public Float2 ValueAt(double tMs)
        {
            double u = Progress(tMs);
            double f = Easings.Eval(Easing, u, EasingParam);
            if (Path != null && Path.Count >= 2) return AlongPath(Path, f);
            return Float2.Lerp(From, To, f);
        }

        static Float2 AlongPath(IReadOnlyList<Float2> pts, double f)
        {
            double total = 0;
            for (int i = 0; i + 1 < pts.Count; i++) total += pts[i].Distance(pts[i + 1]);
            if (total <= 0) return pts[pts.Count - 1];
            double target = f * total;
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                double s = pts[i].Distance(pts[i + 1]);
                if (target <= s || i == pts.Count - 2)
                {
                    double k = s <= 0 ? 0.0 : Math.Min(target / s, 1.0);
                    return Float2.Lerp(pts[i], pts[i + 1], k);
                }
                target -= s;
            }
            return pts[pts.Count - 1];
        }

        public override string ToString() => "tween tile " + TileId + " " + Property + " " + StartMs.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "+" + DurationMs.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>An instant action (fires once, when the clock passes <see cref="TimelineItem.StartMs"/>).</summary>
    public abstract class Instant : TimelineItem
    {
        protected Instant(double startMs) : base(startMs) { }
    }

    /// <summary>Create the piece for a new tile at a board-space position (above the board for spawns). Fired before the piece's first tween.</summary>
    public sealed class SpawnPiece : Instant
    {
        public SpawnPiece(int tileId, int color, Float2 position, double startMs) : base(startMs)
        { TileId = tileId; Color = color; Position = position; }
        public int TileId { get; }
        public int Color { get; }
        public Float2 Position { get; }
    }

    /// <summary>Change a piece's color (shuffle).</summary>
    public sealed class SetColor : Instant
    {
        public SetColor(int tileId, int color, double startMs) : base(startMs) { TileId = tileId; Color = color; }
        public int TileId { get; }
        public int Color { get; }
    }

    /// <summary>Make a piece special (the marker appears).</summary>
    public sealed class SetSpecial : Instant
    {
        public SetSpecial(int tileId, Special special, double startMs) : base(startMs) { TileId = tileId; Special = special; }
        public int TileId { get; }
        public Special Special { get; }
    }

    /// <summary>Remove a piece (return it to the pool): its clear finished.</summary>
    public sealed class RemovePiece : Instant
    {
        public RemovePiece(int tileId, double startMs) : base(startMs) { TileId = tileId; }
        public int TileId { get; }
    }

    /// <summary>The sounds the game makes (TRD 12.4). The view maps each to a clip.</summary>
    public enum SoundCue { Swap, Reject, Clear, Land, SpecialCreate, LineFire, BombFire, ColorFire, Shuffle, Win, Lose, Stinger }

    /// <summary>Play a sound cue now. <see cref="Depth"/> is the cascade depth (0 = the player's move); only <see cref="SoundCue.Clear"/> uses it (pitch).</summary>
    public sealed class PlaySound : Instant
    {
        public PlaySound(SoundCue cue, int depth, double startMs) : base(startMs) { Cue = cue; Depth = depth; }
        public SoundCue Cue { get; }
        public int Depth { get; }
    }

    /// <summary>Call-back marker: this resolve iteration's points reach the HUD now. The screen applies the points.</summary>
    public sealed class ScoreApplied : Instant
    {
        public ScoreApplied(int points, int depth, double startMs, double multiplier = 1.0) : base(startMs) { Points = points; Depth = depth; Multiplier = multiplier; }
        public int Points { get; }
        public int Depth { get; }
        /// <summary>The combo multiplier these points were scored at (the HUD shows it until the move settles).</summary>
        public double Multiplier { get; }
    }

    /// <summary>
    /// A short camera punch (zoom in and settle back) when a combo reaches a celebration tier. <see cref="Amount"/> is the
    /// zoom-in as a fraction of the view; the view runs it over <see cref="DurationMs"/>.
    /// </summary>
    public sealed class CameraPunch : Instant
    {
        public CameraPunch(double amount, double durationMs, double startMs) : base(startMs) { Amount = amount; DurationMs = durationMs; }
        public double Amount { get; }
        public double DurationMs { get; }
    }

    /// <summary>
    /// The last item of every timeline, at <see cref="Timeline.TotalMs"/>: put the scene exactly on the final
    /// board (create missing pieces, remove extra ones, snap every piece to its cell, reset the pose, color
    /// and special). A bug in an animation can never leave the view out of step with the logic (TRD 12.3).
    /// </summary>
    public sealed class SyncToFinalBoard : Instant
    {
        public SyncToFinalBoard(double startMs) : base(startMs) { }
    }

    /// <summary>The kinds of effect a <see cref="ShowFx"/> can show.</summary>
    public enum FxKind
    {
        /// <summary>A ring spreading from a cleared pebble (the tray rippling where it sank).</summary>
        Ripple,
        /// <summary>A wave washing along a row or column: <see cref="ShowFx.Axis"/>, <see cref="ShowFx.Length"/>.</summary>
        LineWash,
        /// <summary>A wave spreading from a bomb: <see cref="ShowFx.Radius"/>.</summary>
        BombWash,
        /// <summary>A ripple at every hit cell, nearest first: <see cref="ShowFx.Cells"/> (already ordered by distance).</summary>
        ColorWash,
        /// <summary>A floating "+points" label at <see cref="ShowFx.Position"/>, billboarded, rising and fading.</summary>
        ScorePopup,
        /// <summary>The combo multiplier ("x2.5") popping over the match: <see cref="ShowFx.Tier"/>, <see cref="ShowFx.Size"/>.</summary>
        ComboBadge,
        /// <summary>A burst of <see cref="ShowFx.Count"/> pebble-dust particles flying out to <see cref="ShowFx.Radius"/> (screen-facing).</summary>
        Burst,
    }

    /// <summary>
    /// A transient effect from <see cref="TimelineItem.StartMs"/> for <see cref="DurationMs"/>. The view creates
    /// it when the clock passes the start and removes it at the end; while it lives it asks
    /// <see cref="ScaleAt"/>, <see cref="AlphaAt"/> and <see cref="ElevationAt"/> with the progress u in 0..1.
    /// </summary>
    public sealed class ShowFx : TimelineItem
    {
        public ShowFx(int id, FxKind kind, Float2 position, double startMs, double durationMs)
            : base(startMs) { Id = id; Kind = kind; Position = position; DurationMs = durationMs; }

        /// <summary>Unique within the timeline.</summary>
        public int Id { get; }
        public FxKind Kind { get; }
        /// <summary>Board-space (X, Z): the pebble that sank, the bomb, the center of the line, the popup anchor.</summary>
        public Float2 Position { get; }
        public double DurationMs { get; }
        public override double EndMs => StartMs + DurationMs;

        /// <summary>LineWash: 0 = along world X (a row), 1 = along world Z (a column).</summary>
        public int Axis { get; init; }
        /// <summary>LineWash: length of the wash in world units (the hit cells plus one cell).</summary>
        public double Length { get; init; }
        /// <summary>BombWash: distance from the bomb to the farthest hit cell edge, in world units.</summary>
        public double Radius { get; init; }
        /// <summary>ColorWash: board-space positions of the hit cells, nearest the origin first.</summary>
        public IReadOnlyList<Float2> Cells { get; init; } = Array.Empty<Float2>();
        /// <summary>ScorePopup: the label, e.g. "+60".</summary>
        public string Text { get; init; } = "";
        /// <summary>ScorePopup: cascade depth (the label grows a little with it).</summary>
        public int Depth { get; init; }
        /// <summary>ScorePopup, ComboBadge: the combo multiplier this effect celebrates.</summary>
        public double Multiplier { get; init; } = 1.0;
        /// <summary>ComboBadge, Burst: the celebration tier (0 to 4).</summary>
        public int Tier { get; init; }
        /// <summary>ComboBadge: size factor (<see cref="CelebrationParams.BadgeScale"/>).</summary>
        public double Size { get; init; } = 1.0;
        /// <summary>Burst: number of particles.</summary>
        public int Count { get; init; }

        /// <summary>
        /// Size at progress u. Ripple / BombWash / ColorWash / ScorePopup: a multiplier of the natural size of
        /// a ring (or label) mesh, as elsewhere. LineWash: the length of the wash so far in world
        /// units (its width stays at a fixed 0.8). For ColorWash call it with <c>CellProgress(i, u)</c>.
        /// </summary>
        public double ScaleAt(double u)
        {
            u = Clamp01(u);
            switch (Kind)
            {
                case FxKind.Ripple: return 0.5 + 1.3 * u;
                case FxKind.LineWash: return Length * Math.Min(1.0, u * 1.6);
                case FxKind.BombWash: return 1.0 + (Math.Max(2.0, 2.5 * Radius) - 1.0) * u;
                case FxKind.ColorWash: return 0.6 + 1.2 * u;
                case FxKind.ScorePopup: return (0.5 + 0.1 * Math.Min(Depth, 4)) * (0.7 + 0.3 * Math.Min(1.0, u * 5));
                case FxKind.ComboBadge:
                {
                    // pop in with overshoot, settle, then drift smaller
                    double k = u < 0.18 ? 0.3 + 1.0 * (u / 0.18) : u < 0.36 ? 1.3 - 0.3 * ((u - 0.18) / 0.18) : 1.0 - 0.1 * ((u - 0.36) / 0.64);
                    return Size * 0.5 * k;
                }
                case FxKind.Burst: return Radius * (1.0 - (1.0 - u) * (1.0 - u));
                default: return 1.0;
            }
        }

        /// <summary>Opacity 0..1 at progress u (for ColorWash use <c>CellProgress(i, u)</c>).</summary>
        public double AlphaAt(double u)
        {
            u = Clamp01(u);
            switch (Kind)
            {
                case FxKind.Ripple: return 0.7 * (1 - u);
                case FxKind.LineWash: return 0.75 * (1 - u);
                case FxKind.BombWash: return 0.85 * (1 - u);
                case FxKind.ColorWash: return 0.8 * (1 - u);
                case FxKind.ScorePopup: return 1.0 - Math.Max(0.0, (u - 0.55) / 0.45);
                case FxKind.ComboBadge: return 1.0 - Math.Max(0.0, (u - 0.7) / 0.3);
                case FxKind.Burst: return 1.0 - u * u;
                default: return 1.0;
            }
        }

        /// <summary>Height above the tray in world units (only the score popup rises: 0.6 to 1.5).</summary>
        public double ElevationAt(double u) => Kind == FxKind.ScorePopup ? 0.6 + 0.9 * Clamp01(u)
            : Kind == FxKind.ComboBadge ? 1.7 + 0.3 * Clamp01(u)
            : Kind == FxKind.Burst ? 0.8 : 0.0;

        /// <summary>ColorWash: the local progress of cell <paramref name="index"/> of <see cref="Cells"/> (nearer cells start first; each runs for half the duration).</summary>
        public double CellProgress(int index, double u)
        {
            int n = Math.Max(Cells.Count, 1);
            return Clamp01((Clamp01(u) - 0.5 * index / n) / 0.5);
        }

        static double Clamp01(double u) => u < 0 ? 0 : (u > 1 ? 1 : u);
    }
}
