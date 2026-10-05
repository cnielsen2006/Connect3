using System;
using System.Collections.Generic;
using Cascade.Config;
using Cascade.Core;
using Cascade.Gameplay;
using Cascade.Presentation;
using UnityEngine;

namespace Cascade.Unity.View
{
    /// <summary>
    /// The glue of a playing level, with no rules of its own: pointer input goes through the pure
    /// <see cref="InputStateMachine"/> (press / drag / click, PRD 2.2), its intents drive the view (selection lift, arrow,
    /// hover), a swap goes to the <see cref="Session"/>, the resulting events are planned by <see cref="TimelinePlanner"/>
    /// and played by the <see cref="TimelinePlayer"/>. A swap requested while the board animates waits in the one-slot
    /// <see cref="SwapQueue"/> (TRD 12.3). The adapters (<c>MouseAdapter</c>, later <c>TouchAdapter</c>) only translate a
    /// device into the pointer calls here. The view never changes logic state except through <see cref="Session.TrySwap"/>.
    /// </summary>
    public sealed class BoardController
    {
        readonly BoardView view;
        readonly TimelinePlayer player;
        readonly BoardCameraRig rig;
        readonly SwapQueue queue = new SwapQueue();
        InputStateMachine? machine;
        Session? session;
        TimingParams timing = TimingParams.Default;
        InputParams inputParams = InputParams.Default;
        PieceVariationParams variation = PieceVariationParams.Default;
        bool inputEnabled = true;
        bool paused;
        bool hintPending;

        public const int HintKey = -1000000;

        /// <summary>
        /// Where a swap goes. The shell sets <c>GameController.TrySwap</c> here, so a win is recorded, saved and announced by the
        /// game; null plays on the <see cref="Session"/> directly (tests, tools). It returns null when the game does not accept input now.
        /// </summary>
        public Func<CellId, CellId, MoveResult?>? SwapHandler { get; set; }

        public BoardController(BoardView view, TimelinePlayer player, BoardCameraRig rig)
        {
            this.view = view;
            this.player = player;
            this.rig = rig;
        }

        /// <summary>The level being played (null between levels).</summary>
        public Session? Session => session;

        public bool IsAnimating => player.IsAnimating;

        /// <summary>The cell the player has selected, if any.</summary>
        public CellId? Selected => machine?.Selected;

        public bool HasPendingSwap => queue.HasPending;

        /// <summary>When false pointer events are ignored (a banner or screen is open).</summary>
        public bool InputEnabled
        {
            get => inputEnabled;
            set
            {
                inputEnabled = value;
                if (machine != null) machine.Enabled = value && !paused;
                if (!value) view.HideArrow();
            }
        }

        /// <summary>Freezes the animation clocks and blocks input (the pause menu).</summary>
        public bool Paused
        {
            get => paused;
            set
            {
                paused = value;
                player.Paused = value;
                if (machine != null) machine.Enabled = inputEnabled && !value;
                if (value) view.HideArrow();
            }
        }

        /// <summary>The animation durations for the next timelines (changing the speed setting applies to the next move).</summary>
        public TimingParams Timing { get => timing; set => timing = value; }

        public InputParams Input
        {
            get => inputParams;
            set { inputParams = value; if (machine != null) machine.Params = value; }
        }

        public PieceVariationParams Variation
        {
            get => variation;
            set { variation = value; view.SetVariation(value); }
        }

        /// <summary>A move was requested and answered by the session (before it animates): update the moves counter.</summary>
        public event Action<MoveResult>? MoveStarted;

        /// <summary>The points of one match group reach the HUD (points, depth, combo multiplier), timed by the animation.</summary>
        public event Action<int, int, double>? ScoreApplied;

        /// <summary>A move has finished animating and the view is synced to the session's board.</summary>
        public event Action<MoveResult>? MoveSettled;

        /// <summary>Starts a level: builds the board, fits the camera, clears selection, queue and animations.</summary>
        public void Begin(Session newSession)
        {
            player.StopAll();
            queue.Clear();
            hintPending = false;
            session = newSession;
            view.Show(newSession.Board, variation);
            rig.SetBoard(view.Geometry!);
            var topo = newSession.Board.Topology;
            machine = new InputStateMachine(
                c => topo.Neighbors(c),
                c => session!.Board.TileAt(c)?.Id,
                inputParams,
                hoverEnabled: true,
                cellPosition: c => view.Geometry!.CellPosition(c));
            machine.Enabled = inputEnabled && !paused;
            player.Paused = paused;
            player.ScoreApplied -= OnScoreApplied;
            player.ScoreApplied += OnScoreApplied;
            player.CameraPunched -= OnCameraPunched;
            player.CameraPunched += OnCameraPunched;
        }

        /// <summary>Leaves the level (back to a menu): stops animations and drops the board.</summary>
        public void End()
        {
            player.StopAll();
            player.ScoreApplied -= OnScoreApplied;
            player.CameraPunched -= OnCameraPunched;
            queue.Clear();
            view.Clear();
            machine = null;
            session = null;
        }

        void OnScoreApplied(int points, int depth, double multiplier) => ScoreApplied?.Invoke(points, depth, multiplier);

        void OnCameraPunched(double amount, double ms) => rig.Punch((float)amount, (float)ms);

        // ---- pointer (from the adapters; positions are screen pixels with the origin at the TOP LEFT) ----

        public void PointerDown(Float2 px, double timeMs)
        {
            if (machine == null || !inputEnabled || paused || !CameraReady) return;
            var pick = rig.Board!.Pick(px);
            Apply(machine.PointerDown(pick?.Cell, pick?.Fx ?? 0.0, pick?.Fy ?? 0.0, timeMs, rig.Board!.PointAt(px)));
        }

        public void PointerMove(Float2 px)
        {
            if (machine == null || !inputEnabled || paused || !CameraReady) return;
            var pick = rig.Board!.Pick(px);
            Apply(machine.PointerMove(pick?.Cell, pick?.Fx ?? 0.0, pick?.Fy ?? 0.0, rig.Board!.PointAt(px)));
        }

        public void PointerUp(Float2 px)
        {
            if (machine == null || !inputEnabled || paused || !CameraReady) return;
            var pick = rig.Board!.Pick(px);
            Apply(machine.PointerUp(pick?.Cell, pick?.Fx ?? 0.0, pick?.Fy ?? 0.0));
        }

        /// <summary>The gesture was interrupted (focus lost, a second finger): no swap, the selection stays.</summary>
        public void CancelPointer()
        {
            if (machine == null) return;
            Apply(machine.Cancel());
        }

        /// <summary>Wheel zoom toward a pixel (notches &gt; 0 zoom in).</summary>
        public void ZoomNotches(double notches, Float2 aroundPx)
        {
            if (!inputEnabled || paused || !CameraReady) return;
            rig.Board!.ZoomNotches(notches, aroundPx);
        }

        public void ZoomBy(double factor, Float2 aroundPx)
        {
            if (!inputEnabled || paused || !CameraReady) return;
            rig.Board!.ZoomBy(factor, aroundPx);
        }

        /// <summary>Drag the view so the world point under <paramref name="fromPx"/> follows the pointer to <paramref name="toPx"/>.</summary>
        public void Pan(Float2 fromPx, Float2 toPx)
        {
            if (!inputEnabled || paused || !CameraReady) return;
            rig.Board!.Pan(fromPx, toPx);
        }

        /// <summary>False until the rig has measured the BoardSlot and given the camera a viewport (the first frames of Play, or a layout in flux): pointer input is ignored then rather than throwing.</summary>
        bool CameraReady => rig.Board != null && rig.HasSlot;

        public void ResetView() { if (CameraReady) rig.Board!.Reset(); }

        // ---- intents ----

        void Apply(IReadOnlyList<InputIntent> intents)
        {
            for (int i = 0; i < intents.Count; i++)
            {
                switch (intents[i])
                {
                    case InputIntent.Select s: SetSelected(s.Cell, true); break;
                    case InputIntent.Deselect d: SetSelected(d.Cell, false); break;
                    case InputIntent.ShowArrow a: view.ShowArrow(a.From, a.To); break;
                    case InputIntent.HideArrow _: view.HideArrow(); break;
                    case InputIntent.ResetView _: ResetView(); break;
                    case InputIntent.Hover h: view.SetHover(h.Cell is CellId hc ? TileIdAt(hc) : -1); break;
                    case InputIntent.Swap sw: RequestSwap(sw.A, sw.B); break;
                }
            }
        }

        int TileIdAt(CellId cell) => session?.Board.TileAt(cell)?.Id ?? -1;

        void SetSelected(CellId cell, bool selected)
        {
            int id = TileIdAt(cell);
            if (id < 0) return;
            var piece = view.Get(id);
            if (piece == null) return; // a tile that has not appeared yet: RefreshSelection picks it up when the board settles
            player.PlayPose(PoseTimelines.Select(id, piece.SelectT, selected, timing), id);
        }

        /// <summary>After a sync: the selected piece is lifted (it may have just spawned), the others are not.</summary>
        void RefreshSelection()
        {
            if (machine?.Selected is CellId c)
            {
                int id = TileIdAt(c);
                var p = id >= 0 ? view.Get(id) : null;
                if (p != null && p.SelectT < 1f) { p.SelectT = 1f; p.ApplyPose(); }
            }
        }

        // ---- moves ----

        /// <summary>Plays a swap now, or queues it (one slot, last wins) while the board is animating.</summary>
        public void RequestSwap(CellId a, CellId b)
        {
            if (session == null) return;
            var play = queue.Request(a, b, player.IsAnimating);
            if (play != null) PlaySwap(play.Value.A, play.Value.B);
        }

        void PlaySwap(CellId a, CellId b)
        {
            var s = session;
            if (s == null) return;
            var r = SwapHandler != null ? SwapHandler(a, b) : s.TrySwap(a, b);
            if (r == null || r.Outcome == MoveOutcome.Ignored) return;
            var geometry = view.Geometry!;
            var timeline = TimelinePlanner.Plan(r.Before, r.Events, timing, geometry);
            MoveStarted?.Invoke(r);
            player.PlayMove(timeline, r.After, () => OnSettled(r));
        }

        void OnSettled(MoveResult r)
        {
            RefreshSelection();
            MoveSettled?.Invoke(r);
            if (session == null) return;
            if (session.Status != SessionStatus.Playing)
            {
                queue.Clear();
                hintPending = false;
                return;
            }
            var next = queue.TakePending();
            if (next != null) PlaySwap(next.Value.A, next.Value.B);
            else if (hintPending) { hintPending = false; ShowHint(); }
        }

        /// <summary>Pulses the pieces of the session's suggested move (waits until the board has settled).</summary>
        public bool ShowHint()
        {
            if (session == null || !session.IsPlaying) return false;
            if (player.IsAnimating) { hintPending = true; return true; }
            var move = session.Hint();
            if (move == null) return false;
            var ids = new List<int>(2);
            int ia = TileIdAt(move.Value.A), ib = TileIdAt(move.Value.B);
            if (ia >= 0) ids.Add(ia);
            if (ib >= 0) ids.Add(ib);
            player.PlayPose(PoseTimelines.Hint(ids, timing), HintKey);
            return true;
        }
    }
}
