using System;
using System.Collections.Generic;

namespace Cascade.Presentation
{
    /// <summary>What <see cref="GestureRecognizer"/> recognized. Positions are screen pixels (origin top left).</summary>
    public abstract record GestureEvent
    {
        /// <summary>A finger went down and up without moving more than the tap slop.</summary>
        public sealed record Tap(double X, double Y) : GestureEvent;
        /// <summary>The second tap of a double tap (sent right after that tap's <see cref="Tap"/>).</summary>
        public sealed record DoubleTap(double X, double Y) : GestureEvent;
        /// <summary>One finger moved beyond the tap slop; X, Y is where it went down.</summary>
        public sealed record DragStart(double X, double Y) : GestureEvent;
        /// <summary>The dragging finger moved to X, Y (the move that crossed the slop is the first one).</summary>
        public sealed record DragMove(double X, double Y) : GestureEvent;
        public sealed record DragEnd(double X, double Y) : GestureEvent;
        /// <summary>Two fingers changed their spread: multiply the zoom by <c>Factor</c> around the midpoint (CenterX, CenterY).</summary>
        public sealed record PinchZoom(double Factor, double CenterX, double CenterY) : GestureEvent;
        /// <summary>Two fingers moved together: move the view content by (Dx, Dy) pixels.</summary>
        public sealed record Pan(double Dx, double Dy) : GestureEvent;
        /// <summary>The single-finger gesture in progress was abandoned (a second finger came, or the system cancelled it). No tap or drag end follows.</summary>
        public sealed record Cancel : GestureEvent;
    }

    /// <summary>
    /// Touch gesture recognition (TRD 13.5), pure: timestamped pointer events in, <see cref="GestureEvent"/>s out.
    /// Thresholds are millimetres from <see cref="GestureParams"/> converted with the screen DPI.
    ///
    /// <list type="bullet">
    /// <item>One finger, moved less than <c>TapSlopMm</c> and lifted: <see cref="GestureEvent.Tap"/>. If the finger went
    /// down within <c>DoubleTapMs</c> of the previous tap's release and within <c>DoubleTapDistanceMm</c> of it, the
    /// tap is followed by <see cref="GestureEvent.DoubleTap"/>.</item>
    /// <item>One finger moved beyond the slop: <see cref="GestureEvent.DragStart"/>, <see cref="GestureEvent.DragMove"/>s,
    /// <see cref="GestureEvent.DragEnd"/>.</item>
    /// <item>A second finger while the first is down: <see cref="GestureEvent.Cancel"/> (the first finger's gesture is
    /// abandoned: feed the <see cref="InputStateMachine"/> a <c>SecondFingerDown</c>), then view gestures:
    /// <see cref="GestureEvent.PinchZoom"/> once the spread has changed by <c>PinchMinMm</c> (the first factor catches up
    /// with the whole change, later ones are per move) and <see cref="GestureEvent.Pan"/> once the midpoint has moved
    /// by the tap slop. Lifting either finger ends the gesture; the other finger does nothing until everyone is up.</item>
    /// <item>A third finger is ignored.</item>
    /// </list>
    /// The adapter also forwards the first finger's raw events to the <see cref="InputStateMachine"/>; the recognizer
    /// only adds what a single pointer cannot express (double tap, pinch, pan, cancel).
    /// </summary>
    public sealed class GestureRecognizer
    {
        enum Mode { Idle, Single, Drag, Multi, Suppressed }

        struct Finger { public int Id; public double X, Y; }

        readonly List<Finger> _fingers = new List<Finger>(2);
        Mode _mode = Mode.Idle;
        double _startX, _startY, _downMs;
        double _lastTapUpMs = double.NegativeInfinity, _lastTapX, _lastTapY;
        bool _haveLastTap;

        // two-finger state
        double _spread0, _lastSpread, _centerX0, _centerY0, _lastCenterX, _lastCenterY;
        bool _pinching, _panning;

        public GestureRecognizer(GestureParams parameters, double dpi)
        {
            Params = parameters;
            Dpi = dpi;
        }

        public GestureParams Params { get; set; }
        /// <summary>Pixels per inch of the screen (<c>HostInfo.ScreenDpi</c>).</summary>
        public double Dpi { get; set; }

        double Px(double mm) => mm * Dpi / 25.4;

        public IReadOnlyList<GestureEvent> PointerDown(int id, double x, double y, double tMs)
        {
            var o = new List<GestureEvent>();
            if (IndexOf(id) >= 0) return o; // duplicate down
            if (_fingers.Count >= 2) return o; // a third finger is ignored
            _fingers.Add(new Finger { Id = id, X = x, Y = y });
            switch (_mode)
            {
                case Mode.Idle:
                    _mode = Mode.Single;
                    _startX = x; _startY = y; _downMs = tMs;
                    break;
                case Mode.Single:
                case Mode.Drag:
                    o.Add(new GestureEvent.Cancel());
                    BeginMulti();
                    break;
                default: break; // Multi / Suppressed: nothing new
            }
            return o;
        }

        public IReadOnlyList<GestureEvent> PointerMove(int id, double x, double y, double tMs)
        {
            var o = new List<GestureEvent>();
            int i = IndexOf(id);
            if (i < 0) return o;
            _fingers[i] = new Finger { Id = id, X = x, Y = y };
            switch (_mode)
            {
                case Mode.Single:
                    if (Dist(x, y, _startX, _startY) > Px(Params.TapSlopMm))
                    {
                        _mode = Mode.Drag;
                        _haveLastTap = false; // a drag breaks a double-tap chain
                        o.Add(new GestureEvent.DragStart(_startX, _startY));
                        o.Add(new GestureEvent.DragMove(x, y));
                    }
                    break;
                case Mode.Drag:
                    o.Add(new GestureEvent.DragMove(x, y));
                    break;
                case Mode.Multi:
                    if (_fingers.Count == 2) Multi(o);
                    break;
            }
            return o;
        }

        public IReadOnlyList<GestureEvent> PointerUp(int id, double x, double y, double tMs)
        {
            var o = new List<GestureEvent>();
            int i = IndexOf(id);
            if (i < 0) return o;
            _fingers.RemoveAt(i);
            switch (_mode)
            {
                case Mode.Single:
                    EmitTap(x, y, tMs, o);
                    _mode = Mode.Idle;
                    break;
                case Mode.Drag:
                    o.Add(new GestureEvent.DragEnd(x, y));
                    _mode = Mode.Idle;
                    break;
                case Mode.Multi:
                    _mode = Mode.Suppressed;
                    break;
            }
            if (_fingers.Count == 0) _mode = Mode.Idle;
            return o;
        }

        /// <summary>The system cancelled a touch (a palm, a notification): <see cref="GestureEvent.Cancel"/> if a gesture was in progress.</summary>
        public IReadOnlyList<GestureEvent> PointerCancel(int id)
        {
            var o = new List<GestureEvent>();
            int i = IndexOf(id);
            if (i < 0) return o;
            _fingers.RemoveAt(i);
            if (_mode == Mode.Single || _mode == Mode.Drag || _mode == Mode.Multi)
            {
                o.Add(new GestureEvent.Cancel());
                _mode = Mode.Suppressed;
            }
            if (_fingers.Count == 0) _mode = Mode.Idle;
            return o;
        }

        /// <summary>Drop everything (the app lost focus): a <see cref="GestureEvent.Cancel"/> if a gesture was in progress.</summary>
        public IReadOnlyList<GestureEvent> CancelAll()
        {
            var o = new List<GestureEvent>();
            if (_mode == Mode.Single || _mode == Mode.Drag || _mode == Mode.Multi) o.Add(new GestureEvent.Cancel());
            _fingers.Clear();
            _mode = Mode.Idle;
            _haveLastTap = false;
            return o;
        }

        // ---- internals ------------------------------------------------------------------------------------

        void EmitTap(double x, double y, double tMs, List<GestureEvent> o)
        {
            o.Add(new GestureEvent.Tap(x, y));
            bool second = _haveLastTap
                && _downMs - _lastTapUpMs <= Params.DoubleTapMs
                && Dist(x, y, _lastTapX, _lastTapY) <= Px(Params.DoubleTapDistanceMm);
            if (second)
            {
                o.Add(new GestureEvent.DoubleTap(x, y));
                _haveLastTap = false; // a third tap starts a new chain
            }
            else
            {
                _haveLastTap = true;
                _lastTapUpMs = tMs; _lastTapX = x; _lastTapY = y;
            }
        }

        void BeginMulti()
        {
            _mode = Mode.Multi;
            _haveLastTap = false;
            var a = _fingers[0]; var b = _fingers[1];
            _spread0 = _lastSpread = Dist(a.X, a.Y, b.X, b.Y);
            _centerX0 = _lastCenterX = (a.X + b.X) / 2;
            _centerY0 = _lastCenterY = (a.Y + b.Y) / 2;
            _pinching = _panning = false;
        }

        void Multi(List<GestureEvent> o)
        {
            var a = _fingers[0]; var b = _fingers[1];
            double spread = Dist(a.X, a.Y, b.X, b.Y);
            double cx = (a.X + b.X) / 2, cy = (a.Y + b.Y) / 2;

            if (!_pinching && Math.Abs(spread - _spread0) >= Px(Params.PinchMinMm)) _pinching = true;
            if (_pinching && _lastSpread > 0 && spread > 0)
            {
                double factor = spread / _lastSpread;
                _lastSpread = spread;
                if (Math.Abs(factor - 1.0) > 1e-12) o.Add(new GestureEvent.PinchZoom(factor, cx, cy));
            }

            if (!_panning && Dist(cx, cy, _centerX0, _centerY0) >= Px(Params.TapSlopMm)) _panning = true;
            if (_panning)
            {
                double dx = cx - _lastCenterX, dy = cy - _lastCenterY;
                _lastCenterX = cx; _lastCenterY = cy;
                if (dx != 0 || dy != 0) o.Add(new GestureEvent.Pan(dx, dy));
            }
        }

        int IndexOf(int id)
        {
            for (int i = 0; i < _fingers.Count; i++) if (_fingers[i].Id == id) return i;
            return -1;
        }

        static double Dist(double x0, double y0, double x1, double y1)
        {
            double dx = x1 - x0, dy = y1 - y0;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
