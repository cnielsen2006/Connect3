using System;
using Cascade.Core;

namespace Cascade.Presentation
{
    /// <summary>
    /// The board camera as the view uses it: auto-fit into the layout's board slot, zoom (wheel, pinch), pan
    /// (right/middle drag, two fingers), reset, picking and projection. Pure and deterministic; the Unity camera
    /// rig copies <see cref="Pose"/> into the camera after every call that changes it (conventions: see
    /// <see cref="CameraPose"/>). All screen positions are pixels with the origin at the top left.
    ///
    /// <list type="bullet">
    /// <item><b>Auto-fit</b> (zoom 1, no pan): the board fills the slot minus <c>fit_margin</c>.</item>
    /// <item><b>Zoom</b> is relative to auto-fit and clamped to <c>zoom_min..zoom_max</c>; the world point under the
    /// zoom center stays under it.</item>
    /// <item><b>Pan</b> keeps the world point under the finger under it; the view center is clamped to within half a
    /// board of the fit center, so the board stays partly visible.</item>
    /// <item><b>Slot change</b> (<see cref="SetViewport"/>: resize, rotation, split screen): the fit is recomputed.
    /// Zoom and pan are kept as factors relative to the new fit; if the user never zoomed or panned the board simply
    /// re-fits.</item>
    /// </list>
    /// </summary>
    public sealed class BoardCamera
    {
        BoardGeometry _geometry;
        CameraParams _params;
        RectF _slot;
        Float2 _screen;
        bool _haveViewport;

        CameraPose? _fit;
        Float2 _pan = Float2.Zero;
        double _zoom = 1.0;
        CameraPose? _pose;

        public BoardCamera(BoardGeometry geometry, CameraParams parameters)
        {
            _geometry = geometry;
            _params = parameters;
        }

        public BoardGeometry Geometry => _geometry;

        /// <summary>Live-editable (a camera dial changed): refits.</summary>
        public CameraParams Params
        {
            get => _params;
            set { _params = value; Refit(); }
        }

        /// <summary>True once the user zoomed or panned (until <see cref="Reset"/>).</summary>
        public bool UserZoomed { get; private set; }

        /// <summary>Zoom relative to auto-fit (1 = auto-fit).</summary>
        public double Zoom => _zoom;

        /// <summary>Pan offset in world units (board-space X, Z) from the fit center.</summary>
        public Float2 PanOffset => _pan;

        /// <summary>The auto-fit pose for the current board and slot (zoom 1, no pan).</summary>
        public CameraPose FitPose
        {
            get { RequireViewport(); return _fit!; }
        }

        /// <summary>The current pose: apply it to the Unity camera.</summary>
        public CameraPose Pose
        {
            get
            {
                RequireViewport();
                return _pose ??= CameraPose.Create(_fit!.Focus + _pan, _fit.Distance / _zoom, _slot, _screen, _params.FovDeg, _params.PitchDeg);
            }
        }

        /// <summary>A new level (another board): shows it auto-fitted.</summary>
        public void SetBoard(BoardGeometry geometry)
        {
            _geometry = geometry;
            _pan = Float2.Zero;
            _zoom = 1.0;
            UserZoomed = false;
            Refit();
        }

        /// <summary>
        /// The slot rect (where the layout wants the board) and the screen size changed, or are known for the first time.
        /// </summary>
        public void SetViewport(RectF slotPx, Float2 screenPx)
        {
            if (_haveViewport && slotPx == _slot && screenPx == _screen) return;
            _slot = slotPx;
            _screen = screenPx;
            _haveViewport = true;
            Refit();
        }

        /// <summary>Back to auto-fit (double click or double tap on empty space).</summary>
        public void Reset()
        {
            _pan = Float2.Zero;
            _zoom = 1.0;
            UserZoomed = false;
            _pose = null;
        }

        /// <summary>Zoom by wheel notches (positive = in): the factor is <c>zoom_step ^ notches</c>. Around the pixel <paramref name="aroundPx"/>.</summary>
        public void ZoomNotches(double notches, Float2 aroundPx) => ZoomBy(Math.Pow(_params.ZoomStep, notches), aroundPx);

        /// <summary>Zoom by a factor (&gt; 1 = in; a pinch's <c>Factor</c>) keeping the world point under <paramref name="aroundPx"/> in place.</summary>
        public void ZoomBy(double factor, Float2 aroundPx)
        {
            RequireViewport();
            if (!(factor > 0) || double.IsInfinity(factor)) return;
            var before = Pose.GroundPoint(aroundPx, _params.PickHeight);
            _zoom = Math.Min(_params.ZoomMax, Math.Max(_params.ZoomMin, _zoom * factor));
            UserZoomed = true;
            _pose = null;
            var after = Pose.GroundPoint(aroundPx, _params.PickHeight);
            if (before != null && after != null)
            {
                _pan = _pan + (before.Value - after.Value);
                ClampPan();
                _pose = null;
            }
        }

        /// <summary>Move the view so the world point under <paramref name="fromPx"/> ends up under <paramref name="toPx"/> (a drag).</summary>
        public void Pan(Float2 fromPx, Float2 toPx)
        {
            RequireViewport();
            var a = Pose.GroundPoint(fromPx, _params.PickHeight);
            var b = Pose.GroundPoint(toPx, _params.PickHeight);
            if (a == null || b == null) return;
            _pan = _pan + (a.Value - b.Value);
            UserZoomed = true;
            ClampPan();
            _pose = null;
        }

        /// <summary>Pan by a pixel delta of the content (a <see cref="GestureEvent.Pan"/>): measured at the slot's center.</summary>
        public void PanBy(Float2 deltaPx)
        {
            RequireViewport();
            var c = _slot.Center;
            Pan(c, c + deltaPx);
        }

        /// <summary>The cell under a screen pixel (see <see cref="CameraFit.Pick"/>); null outside the slot, over a hole or off the board.</summary>
        public CellPick? Pick(Float2 px) { RequireViewport(); return CameraFit.Pick(Pose, _geometry, px, _params); }

        /// <summary>The board-space point under a pixel at the picking height, even off the board or outside the slot (for the drag direction); null if the ray misses the plane.</summary>
        public Float2? PointAt(Float2 px) { RequireViewport(); return Pose.GroundPoint(px, _params.PickHeight); }

        /// <summary>Screen pixel of a world point.</summary>
        public Float2 Project(Float3 world) { RequireViewport(); return Pose.Project(world); }

        /// <summary>Screen pixel of a cell's middle at the picking height (for placing the arrow or a tooltip).</summary>
        public Float2 ProjectCell(CellId cell) { RequireViewport(); return Pose.Project(_geometry.CellPosition(cell), _params.PickHeight); }

        void Refit()
        {
            _pose = null;
            if (!_haveViewport) return;
            _fit = CameraFit.Fit(_geometry, _slot, _screen, _params);
            _zoom = Math.Min(_params.ZoomMax, Math.Max(_params.ZoomMin, _zoom));
            ClampPan();
        }

        void ClampPan()
        {
            var (x0, z0, x1, z1) = _geometry.WorldBounds();
            double lx = (x1 - x0) / 2, lz = (z1 - z0) / 2;
            _pan = new Float2(Math.Max(-lx, Math.Min(lx, _pan.X)), Math.Max(-lz, Math.Min(lz, _pan.Y)));
        }

        void RequireViewport()
        {
            if (!_haveViewport) throw new InvalidOperationException("call SetViewport(slot, screen) before using the camera");
        }
    }
}
