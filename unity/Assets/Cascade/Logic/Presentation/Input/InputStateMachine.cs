using System;
using System.Collections.Generic;
using Cascade.Core;

namespace Cascade.Presentation
{
    /// <summary>
    /// What the pointer did, as the view reports it. <c>Cell</c> is the cell under the pointer (from
    /// <see cref="BoardCamera.Pick"/>), null over empty space or a hole; <c>Fx, Fy</c> are the offsets in cell
    /// units from the cell's middle (-0.5..0.5, Fx to the right, Fy downward) that the pick returned.
    /// </summary>
    public abstract record InputEvent
    {
        /// <summary>Primary button or the first finger went down. <c>TimeMs</c> is any monotonic clock in milliseconds (for the double click).</summary>
        public sealed record PointerDown(CellId? Cell, double Fx, double Fy, double TimeMs, Float2? World = null) : InputEvent;
        /// <summary>The pointer moved (mouse: always; touch: only while the finger is down). <c>World</c> is the board-space point under the pointer even off the board (needed for the drag direction).</summary>
        public sealed record PointerMove(CellId? Cell, double Fx, double Fy, Float2? World = null) : InputEvent;
        /// <summary>Released.</summary>
        public sealed record PointerUp(CellId? Cell, double Fx, double Fy) : InputEvent;
        /// <summary>A second finger touched the screen: the drag is cancelled (no swap, the piece stays selected) and a view gesture takes over.</summary>
        public sealed record SecondFingerDown : InputEvent;
        /// <summary>The gesture was interrupted (focus lost, a pan or pinch started, a screen opened): same effect as <see cref="SecondFingerDown"/>.</summary>
        public sealed record Cancel : InputEvent;
    }

    /// <summary>What the view should do in response (apply in order).</summary>
    public abstract record InputIntent
    {
        /// <summary>Select the piece at this cell (lift and halo). Preceded by a <see cref="Deselect"/> when another piece was selected.</summary>
        public sealed record Select(CellId Cell) : InputIntent;
        /// <summary>Drop the selection of the piece at this cell.</summary>
        public sealed record Deselect(CellId Cell) : InputIntent;
        /// <summary>Ask the session to swap these two neighboring cells. The session may reject it; while the board animates, queue it in a <see cref="SwapQueue"/>.</summary>
        public sealed record Swap(CellId A, CellId B) : InputIntent;
        /// <summary>Show the swap arrow from the pressed piece toward its neighbor (replaces a previous arrow).</summary>
        public sealed record ShowArrow(CellId From, CellId To) : InputIntent;
        public sealed record HideArrow : InputIntent;
        /// <summary>Double click on empty space: <c>BoardCamera.Reset()</c>.</summary>
        public sealed record ResetView : InputIntent;
        /// <summary>The hovered piece changed (null = none). Mouse only.</summary>
        public sealed record Hover(CellId? Cell) : InputIntent;
    }

    /// <summary>
    /// The press / drag / click rules of PRD 2.2, pure (the board-input state machine). Feed it
    /// pointer events, apply the intents it returns. It holds the selection and the arrow; it never talks to the
    /// session or the view.
    ///
    /// <list type="table">
    /// <item><term>Press on a piece</term><description>nothing selected: select it. The selected piece: remember it was already selected. A neighbor of the selected piece: keep the selection (a click will swap). Any other piece: move the selection to it.</description></item>
    /// <item><term>Drag</term><description>nothing changes until the drag points at a neighbor of the pressed piece: then <see cref="InputIntent.ShowArrow"/>; pointing elsewhere gives <see cref="InputIntent.HideArrow"/>. With world positions (the normal case) the neighbor is the one best aligned with the vector from the press point to the pointer (within <c>DragMinCos</c>), reached once the pointer is past the inner zone's near edge, and overshooting the neighbor still counts. Without them: the pointer must be inside the neighbor's inner zone (the central <c>InnerFraction</c> of the cell).</description></item>
    /// <item><term>Release with the arrow</term><description>deselect, then <see cref="InputIntent.Swap"/>(pressed, target).</description></item>
    /// <item><term>Release without the arrow</term><description>a click on the pressed piece: a pressed neighbor of the selection swaps with it; the selected piece pressed again is deselected; otherwise the selection stays. If the pointer left the pressed piece it is no click at all and nothing changes.</description></item>
    /// <item><term>Second finger / cancel</term><description>drop the press and the arrow; the selection stays.</description></item>
    /// <item><term>Double click on empty space</term><description>two presses on empty space within <c>DoubleClickMs</c>: <see cref="InputIntent.ResetView"/>.</description></item>
    /// </list>
    /// Selecting is allowed while the board animates (the selection refers to the final board); the swap is queued by the caller.
    /// </summary>
    public sealed class InputStateMachine
    {
        readonly Func<CellId, IReadOnlyList<CellId>> _neighbors;
        readonly Func<CellId, int?> _tileAt;
        readonly Func<CellId, Float2>? _cellPosition;

        struct Press
        {
            public CellId Origin;
            public bool WasSelected;
            public bool NeighborOfSelected;
            public Float2? StartWorld;
        }

        Press? _press;
        double? _lastEmptyPressMs;

        /// <param name="neighbors">The cells a cell can swap with (<c>Topology.Neighbors</c>).</param>
        /// <param name="tileAt">The tile id at a cell on the board the player sees (the final board), or null if the cell is empty.</param>
        /// <param name="parameters"><see cref="InputParams.ForMouse"/> or <see cref="InputParams.ForTouch"/>.</param>
        /// <param name="hoverEnabled">Report <see cref="InputIntent.Hover"/> (mouse yes, touch no).</param>
        /// <param name="cellPosition">Board-space middle of a cell (<c>BoardGeometry.CellPosition</c>); enables the drag-direction rule.</param>
        public InputStateMachine(Func<CellId, IReadOnlyList<CellId>> neighbors, Func<CellId, int?> tileAt, InputParams parameters, bool hoverEnabled = true, Func<CellId, Float2>? cellPosition = null)
        {
            _cellPosition = cellPosition;
            _neighbors = neighbors;
            _tileAt = tileAt;
            Params = parameters;
            HoverEnabled = hoverEnabled;
        }

        /// <summary>Live-editable (a dial changed).</summary>
        public InputParams Params { get; set; }
        public bool HoverEnabled { get; set; }

        /// <summary>The selected cell, if any.</summary>
        public CellId? Selected { get; private set; }
        /// <summary>The hovered cell (mouse, no button down), if any.</summary>
        public CellId? Hovered { get; private set; }
        /// <summary>The cell the swap arrow points at, if shown.</summary>
        public CellId? ArrowTarget { get; private set; }
        /// <summary>The cell the current press started on, if a press is in progress.</summary>
        public CellId? PressOrigin => _press?.Origin;
        public bool IsPressed => _press != null;

        /// <summary>
        /// When false (a screen is open, input is blocked) events are ignored and a press in progress is
        /// dropped. Not used for "the board is animating": selecting stays allowed then.
        /// </summary>
        public bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled && !value) { _press = null; ArrowTarget = null; Hovered = null; }
                _enabled = value;
            }
        }
        bool _enabled = true;

        public IReadOnlyList<InputIntent> Handle(InputEvent e)
        {
            switch (e)
            {
                case InputEvent.PointerDown d: return PointerDown(d.Cell, d.Fx, d.Fy, d.TimeMs, d.World);
                case InputEvent.PointerMove m: return PointerMove(m.Cell, m.Fx, m.Fy, m.World);
                case InputEvent.PointerUp u: return PointerUp(u.Cell, u.Fx, u.Fy);
                case InputEvent.SecondFingerDown _:
                case InputEvent.Cancel _: return Cancel();
                default: throw new ArgumentException("unknown input event " + e);
            }
        }

        public IReadOnlyList<InputIntent> PointerDown(CellId? cell, double fx, double fy, double timeMs, Float2? world = null)
        {
            var o = new List<InputIntent>();
            if (!_enabled) return o;
            if (cell == null || _tileAt(cell.Value) == null)
            {
                // empty space: a second press soon after the first is a double click
                if (_lastEmptyPressMs != null && timeMs - _lastEmptyPressMs.Value <= Params.DoubleClickMs)
                {
                    o.Add(new InputIntent.ResetView());
                    _lastEmptyPressMs = null;
                }
                else _lastEmptyPressMs = timeMs;
                _press = null;
                return o;
            }
            _lastEmptyPressMs = null;
            var c = cell.Value;
            if (Selected == null)
            {
                SetSelected(c, o);
                _press = new Press { Origin = c, StartWorld = world };
            }
            else if (c == Selected.Value) _press = new Press { Origin = c, WasSelected = true, StartWorld = world };
            else if (Contains(_neighbors(Selected.Value), c)) _press = new Press { Origin = c, NeighborOfSelected = true, StartWorld = world };
            else
            {
                SetSelected(c, o);
                _press = new Press { Origin = c, StartWorld = world };
            }
            SetHover(null, o);
            return o;
        }

        public IReadOnlyList<InputIntent> PointerMove(CellId? cell, double fx, double fy, Float2? world = null)
        {
            var o = new List<InputIntent>();
            if (!_enabled) return o;
            if (_press == null)
            {
                SetHover(cell != null && _tileAt(cell.Value) != null ? cell : null, o);
                return o;
            }
            SetHover(null, o);
            var origin = _press.Value.Origin;
            CellId? target = null;
            if (_cellPosition != null && world != null && _press.Value.StartWorld != null)
                target = DragTarget(origin, _press.Value.StartWorld.Value, world.Value);
            else if (cell != null && cell.Value != origin && Contains(_neighbors(origin), cell.Value) && InInnerZone(fx, fy))
                target = cell;
            if (target != ArrowTarget)
            {
                ArrowTarget = target;
                if (target == null) o.Add(new InputIntent.HideArrow());
                else o.Add(new InputIntent.ShowArrow(origin, target.Value));
            }
            return o;
        }

        public IReadOnlyList<InputIntent> PointerUp(CellId? cell, double fx, double fy)
        {
            var o = new List<InputIntent>();
            if (!_enabled) return o;
            var press = _press;
            var target = ArrowTarget;
            _press = null;
            if (ArrowTarget != null) { ArrowTarget = null; o.Add(new InputIntent.HideArrow()); }
            if (HoverEnabled) SetHover(cell != null && _tileAt(cell.Value) != null ? cell : null, o);
            if (press == null) return o;
            var p = press.Value;
            if (target != null)
            {
                SetSelected(null, o);
                o.Add(new InputIntent.Swap(p.Origin, target.Value));
                return o;
            }
            bool overOrigin = cell != null && cell.Value == p.Origin;
            if (p.NeighborOfSelected)
            {
                if (overOrigin && Selected != null)
                {
                    var a = Selected.Value;
                    SetSelected(null, o);
                    o.Add(new InputIntent.Swap(a, p.Origin));
                }
            }
            else if (p.WasSelected && overOrigin) SetSelected(null, o);
            return o;
        }

        /// <summary>A second finger went down, or the gesture was interrupted: no swap, the selection stays.</summary>
        public IReadOnlyList<InputIntent> Cancel()
        {
            var o = new List<InputIntent>();
            _press = null;
            if (ArrowTarget != null) { ArrowTarget = null; o.Add(new InputIntent.HideArrow()); }
            return o;
        }

        /// <summary>The board changed under the selection (a new level, a swap played, the screen closed): drop selection, press, arrow and hover.</summary>
        public IReadOnlyList<InputIntent> Reset()
        {
            var o = new List<InputIntent>();
            _press = null;
            if (ArrowTarget != null) { ArrowTarget = null; o.Add(new InputIntent.HideArrow()); }
            SetSelected(null, o);
            SetHover(null, o);
            _lastEmptyPressMs = null;
            return o;
        }

        /// <summary>Deselect from outside (for example after the session played the swap the player queued).</summary>
        public IReadOnlyList<InputIntent> Deselect()
        {
            var o = new List<InputIntent>();
            SetSelected(null, o);
            return o;
        }

        /// <summary>
        /// The neighbor the drag from <paramref name="start"/> to <paramref name="now"/> points at: the one whose
        /// direction from the origin is best aligned with the drag (cosine at least <c>DragMinCos</c>), once the
        /// pointer is past the near edge of that neighbor's inner zone along its direction. Unbounded beyond.
        /// </summary>
        CellId? DragTarget(CellId origin, Float2 start, Float2 now)
        {
            var drag = now - start;
            double dragLen = drag.Length;
            if (dragLen < 1e-9) return null;
            var from = _cellPosition!(origin);
            CellId? best = null;
            double bestCos = Params.DragMinCos;
            foreach (var n in _neighbors(origin))
            {
                if (_tileAt(n) == null) continue;
                var dir = _cellPosition(n) - from;
                double len = dir.Length;
                if (len < 1e-9) continue;
                double cos = (drag.X * dir.X + drag.Y * dir.Y) / (dragLen * len);
                if (cos < bestCos) continue;
                var rel = now - from;
                double along = (rel.X * dir.X + rel.Y * dir.Y) / len; // distance from the origin's middle toward n
                if (along < len * (1 - Params.InnerFraction / 2)) continue;
                best = n; bestCos = cos;
            }
            return best;
        }

        bool InInnerZone(double fx, double fy)
        {
            double half = Params.InnerFraction / 2;
            return Math.Abs(fx) <= half && Math.Abs(fy) <= half;
        }

        void SetSelected(CellId? cell, List<InputIntent> o)
        {
            if (cell == Selected) return;
            if (Selected != null) o.Add(new InputIntent.Deselect(Selected.Value));
            Selected = cell;
            if (cell != null) o.Add(new InputIntent.Select(cell.Value));
        }

        void SetHover(CellId? cell, List<InputIntent> o)
        {
            if (!HoverEnabled) cell = null;
            if (cell == Hovered) return;
            Hovered = cell;
            o.Add(new InputIntent.Hover(cell));
        }

        static bool Contains(IReadOnlyList<CellId> list, CellId c)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] == c) return true;
            return false;
        }
    }
}
