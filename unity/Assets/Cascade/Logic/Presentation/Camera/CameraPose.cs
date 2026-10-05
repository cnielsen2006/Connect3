using System;

namespace Cascade.Presentation
{
    /// <summary>
    /// A perspective camera over the board, in the world convention of TRD 12 (the board lies in the XZ plane,
    /// Y up, Z up the screen, one cell = one unit, the tray surface at Y = 0).
    ///
    /// <para><b>How Unity applies it:</b> <c>cam.transform.position = Position</c>;
    /// <c>cam.transform.rotation = Quaternion.Euler(EulerDegrees)</c> (X = 90 - pitch, Y = Z = 0);
    /// <c>cam.fieldOfView = FovDeg</c> (VERTICAL field of view, Unity's convention);
    /// the camera renders the whole screen, so its aspect is <c>ScreenPx.X / ScreenPx.Y</c> (Unity's automatic aspect);
    /// no lens shift and no viewport rect. <see cref="NearClip"/> / <see cref="FarClip"/> are suggestions.</para>
    ///
    /// <para><b>Screen space</b> is pixels with the origin at the top left, Y down, over the full screen
    /// (<see cref="ScreenPx"/>); Unity's mouse and touch positions have the origin at the bottom left, so flip Y
    /// (<c>y = ScreenPx.Y - unityY</c>) before calling anything here. The <b>slot rect</b> is where the layout wants the board:
    /// the board is centered in it by moving the camera (the point <see cref="Focus"/> of the tray shows exactly at the
    /// slot's center pixel), not by shifting the lens.</para>
    ///
    /// <para>The camera looks along <c>(0, -cos(pitch), +sin(pitch))</c>: tilted <c>pitch</c> degrees from straight
    /// down toward +Z (three-quarter view); screen right is +X and screen up is <c>(0, sin(pitch), cos(pitch))</c>.</para>
    /// </summary>
    public sealed class CameraPose
    {
        CameraPose(Float2 focus, double distance, RectF slot, Float2 screen, double fovDeg, double pitchDeg)
        {
            Focus = focus; Distance = distance; SlotPx = slot; ScreenPx = screen; FovDeg = fovDeg; PitchDeg = pitchDeg;
            double p = pitchDeg * Math.PI / 180.0;
            Right = new Float3(1, 0, 0);
            Up = new Float3(0, Math.Sin(p), Math.Cos(p));
            Forward = new Float3(0, -Math.Cos(p), Math.Sin(p));
            EulerDegrees = new Float3(90.0 - pitchDeg, 0, 0);
            var through = RayDirection(slot.Center);
            Position = new Float3(focus.X, 0, focus.Y) - through * distance;
            NearClip = Math.Max(0.05, distance * 0.1);
            FarClip = distance * 3.0 + 20.0;
        }

        /// <summary>
        /// Builds the pose that shows the tray point <paramref name="focus"/> (world X, Z at Y = 0) at the center pixel
        /// of <paramref name="slot"/>, <paramref name="distance"/> away along the ray through that pixel.
        /// </summary>
        public static CameraPose Create(Float2 focus, double distance, RectF slot, Float2 screen, double fovDeg, double pitchDeg)
        {
            if (screen.X <= 0 || screen.Y <= 0) throw new ArgumentException("screen size must be positive", nameof(screen));
            if (slot.Width <= 0 || slot.Height <= 0) throw new ArgumentException("slot size must be positive", nameof(slot));
            return new CameraPose(focus, distance, slot, screen, fovDeg, pitchDeg);
        }

        /// <summary>World position of the camera.</summary>
        public Float3 Position { get; }
        /// <summary>Unity Euler angles in degrees: (90 - pitch, 0, 0).</summary>
        public Float3 EulerDegrees { get; }
        /// <summary>Vertical field of view in degrees.</summary>
        public double FovDeg { get; }
        /// <summary>Tilt from straight down, degrees.</summary>
        public double PitchDeg { get; }
        /// <summary>The tray point (world X, Z at Y = 0) shown at the slot's center pixel.</summary>
        public Float2 Focus { get; }
        /// <summary>Distance from the camera to <see cref="Focus"/> along the view ray through the slot center.</summary>
        public double Distance { get; }
        public RectF SlotPx { get; }
        public Float2 ScreenPx { get; }

        public Float3 Right { get; }
        public Float3 Up { get; }
        public Float3 Forward { get; }
        public double NearClip { get; }
        public double FarClip { get; }

        public double Aspect => ScreenPx.X / ScreenPx.Y;

        double TanHalf => Math.Tan(FovDeg * Math.PI / 360.0);

        /// <summary>Screen pixel of a world point; both components NaN when the point is behind the camera.</summary>
        public Float2 Project(Float3 world)
        {
            var v = world - Position;
            double z = v.Dot(Forward);
            if (z <= 1e-6) return new Float2(double.NaN, double.NaN);
            double t = TanHalf;
            double nx = v.Dot(Right) / (z * t * Aspect);
            double ny = v.Dot(Up) / (z * t);
            return new Float2((nx + 1) / 2 * ScreenPx.X, (1 - ny) / 2 * ScreenPx.Y);
        }

        /// <summary>Screen pixel of board-space (X, Z) at height <paramref name="y"/>.</summary>
        public Float2 Project(Float2 boardXZ, double y = 0) => Project(new Float3(boardXZ.X, y, boardXZ.Y));

        /// <summary>Unit direction in world space of the view ray through a screen pixel.</summary>
        public Float3 RayDirection(Float2 px)
        {
            double nx = px.X / ScreenPx.X * 2 - 1;
            double ny = 1 - px.Y / ScreenPx.Y * 2;
            double t = TanHalf;
            var d = Right * (nx * t * Aspect) + Up * (ny * t) + Forward;
            return d * (1.0 / d.Length);
        }

        /// <summary>
        /// Where the ray through a screen pixel meets the horizontal plane at height <paramref name="y"/>,
        /// as board-space (world X, Z); null if the ray does not point down to it.
        /// </summary>
        public Float2? GroundPoint(Float2 px, double y)
        {
            var d = RayDirection(px);
            if (d.Y >= -1e-9) return null;
            double t = (y - Position.Y) / d.Y;
            if (t <= 0) return null;
            return new Float2(Position.X + d.X * t, Position.Z + d.Z * t);
        }
    }
}
