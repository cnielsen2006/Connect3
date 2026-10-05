using System;
using System.Collections.Generic;
using Cascade.Core;
using Cascade.Presentation;
using UnityEngine;

namespace Cascade.Unity.View
{
    /// <summary>
    /// Plays <see cref="Timeline"/>s (TRD 12.1): each frame it advances the clock of every running timeline by the
    /// unscaled frame time, asks its <see cref="TimelineSampler"/> what to apply and applies it in the sampler's order
    /// (Fired, FxStarted, FxEnded, Poses, Fx). It contains no timing of its own: durations and the player's animation
    /// speed are already in the timeline, so changing the speed only affects the next timeline planned. Pause freezes
    /// every clock (<see cref="Paused"/>).
    ///
    /// Two kinds of timeline run side by side:
    /// <list type="bullet">
    /// <item>a <b>move</b> (<see cref="PlayMove"/>): the animation of one swap. It ends with a
    /// <see cref="SyncToFinalBoard"/> that calls <see cref="BoardView.Sync"/> with the board the move led to, then
    /// <see cref="Settled"/> is raised. <see cref="IsAnimating"/> is true until then.</item>
    /// <item>a <b>pose timeline</b> (<see cref="PlayPose"/>): the selection lift or the hint pulse (<see cref="PoseTimelines"/>).
    /// A new one with the same key replaces the old one.</item>
    /// </list>
    /// </summary>
    public sealed class TimelinePlayer : MonoBehaviour
    {
        sealed class Layer
        {
            public TimelineSampler Sampler = null!;
            public double TimeMs;
            public Board? Final;
            public bool Move;
            public int Key;
            public bool SettledFired;
            public Action? OnSettled;
        }

        BoardView? board;
        FxPresenter? fx;
        SoundPlayer? sound;
        readonly List<Layer> layers = new List<Layer>();
        readonly List<Layer> done = new List<Layer>();

        [SerializeField, Tooltip("A frame longer than this advances the clocks by this much only (a hitch or a debugger pause must not skip an animation).")]
        float maxStepMs = 100f;

        /// <summary>Tools and tests only: when true the clock does not run by itself; callers advance it with <see cref="Advance"/> (deterministic frame sequences).</summary>
        public bool ManualClock { get; set; }

        /// <summary>Freezes every running timeline (pause menu, app in the background).</summary>
        public bool Paused { get; set; }

        /// <summary>True from <see cref="PlayMove"/> until the board has settled (the final sync fired).</summary>
        public bool IsAnimating
        {
            get
            {
                foreach (var l in layers) if (l.Move && !l.SettledFired) return true;
                return false;
            }
        }

        /// <summary>Number of timelines running (moves and pose timelines).</summary>
        public int LayerCount => layers.Count;

        /// <summary>A match group's (or bonus's) points reach the HUD now (points, cascade depth, combo multiplier).</summary>
        public event Action<int, int, double>? ScoreApplied;

        /// <summary>A combo reached a celebration tier: zoom the camera in by this fraction and back over this many ms.</summary>
        public event Action<double, double>? CameraPunched;

        /// <summary>A move's board has settled and the view was synced to it (raised after the per-move callback).</summary>
        public event Action? Settled;

        public void SetStyle(PieceStyle style) => fx?.SetStyle(style);

        public void Init(BoardView boardView, FxPresenter presenter, SoundPlayer? soundPlayer = null)
        {
            board = boardView;
            fx = presenter;
            sound = soundPlayer;
        }

        /// <summary>
        /// Plays the animation of one move. <paramref name="finalBoard"/> is the board the scene is synced to when the
        /// timeline ends (the session's board after the move). <paramref name="onSettled"/> runs right after that sync.
        /// </summary>
        public void PlayMove(Timeline timeline, Board finalBoard, Action? onSettled = null)
        {
            var layer = new Layer { Sampler = new TimelineSampler(timeline), Final = finalBoard, Move = true, OnSettled = onSettled };
            layers.Add(layer);
            Step(layer, 0.0); // t = 0 happens now: no frame of delay between the click and the first motion
            Reap();
        }

        /// <summary>Plays a small real-time timeline (selection lift, hint pulse). Replaces a running one with the same key.</summary>
        public void PlayPose(Timeline timeline, int key)
        {
            for (int i = layers.Count - 1; i >= 0; i--)
                if (!layers[i].Move && layers[i].Key == key) layers.RemoveAt(i);
            var layer = new Layer { Sampler = new TimelineSampler(timeline), Key = key };
            layers.Add(layer);
            Step(layer, 0.0);
            Reap();
        }

        /// <summary>Stops everything without syncing (a new level starts, the scene is rebuilt).</summary>
        public void StopAll()
        {
            layers.Clear();
            fx?.Clear();
        }

        void Update()
        {
            if (Paused || ManualClock || layers.Count == 0) return;
            Advance(Math.Min(Time.unscaledDeltaTime * 1000.0, maxStepMs));
        }

        /// <summary>Advances every running timeline by <paramref name="dtMs"/> (called each frame; public for tests and tools).</summary>
        public void Advance(double dtMs)
        {
            if (Paused) return;
            // Callbacks may start new timelines: iterate by index over what is there now.
            int n = layers.Count;
            for (int i = 0; i < n && i < layers.Count; i++) Step(layers[i], dtMs);
            Reap();
        }

        void Step(Layer layer, double dtMs)
        {
            layer.TimeMs += dtMs;
            var frame = layer.Sampler.Advance(layer.TimeMs);
            Apply(layer, frame);
            if (frame.Settled && !layer.SettledFired)
            {
                layer.SettledFired = true;
                if (layer.Move)
                {
                    layer.OnSettled?.Invoke();
                    Settled?.Invoke();
                }
            }
            if (frame.Finished) done.Add(layer);
        }

        void Reap()
        {
            if (done.Count == 0) return;
            foreach (var l in done) layers.Remove(l);
            done.Clear();
        }

        void Apply(Layer layer, SampleFrame frame)
        {
            var b = board;
            if (b == null) return;

            var fired = frame.Fired;
            for (int i = 0; i < fired.Count; i++)
            {
                switch (fired[i])
                {
                    case SpawnPiece s:
                        b.Spawn(s.TileId, s.Color, s.Position, null);
                        break;
                    case SetColor c:
                        b.Get(c.TileId)?.SetColor(c.Color);
                        break;
                    case SetSpecial sp:
                        b.Get(sp.TileId)?.SetSpecial(PieceMapping.ToView(sp.Special), b.Style.PieceCount);
                        break;
                    case RemovePiece r:
                        b.Remove(r.TileId);
                        break;
                    case PlaySound ps:
                        sound?.Play(ps.Cue, ps.Depth);
                        break;
                    case ScoreApplied sc:
                        ScoreApplied?.Invoke(sc.Points, sc.Depth, sc.Multiplier);
                        break;
                    case CameraPunch cp:
                        CameraPunched?.Invoke(cp.Amount, cp.DurationMs);
                        break;
                    case SyncToFinalBoard _:
                        if (layer.Final != null) b.Sync(layer.Final);
                        break;
                }
            }

            if (fx != null)
            {
                for (int i = 0; i < frame.FxStarted.Count; i++) fx.Begin(frame.FxStarted[i]);
                for (int i = 0; i < frame.FxEnded.Count; i++) fx.End(frame.FxEnded[i]);
            }

            float hoverLift = Mathf.Max(1e-4f, b.Style.material.hoverLift);
            var poses = frame.Poses;
            for (int i = 0; i < poses.Count; i++)
            {
                var p = poses[i];
                var view = b.Get(p.TileId);
                if (view == null) continue;
                switch (p.Property)
                {
                    case PoseProperty.Position: view.BasePosition = b.ToLocal(p.Value); break;
                    case PoseProperty.Hover: view.Hover = (float)p.Scalar / hoverLift; break; // world units to PieceView.Hover units
                    case PoseProperty.SelectT: view.SelectT = (float)p.Scalar; break;
                    case PoseProperty.Squash: view.Squash = (float)p.Scalar; break;
                    case PoseProperty.Roll: view.Roll = (float)p.Scalar; break;
                    case PoseProperty.Sink: view.Sink = (float)p.Scalar; break;
                    case PoseProperty.Scale: view.Scale = (float)p.Scalar; break;
                }
                view.ApplyPose();
            }

            if (fx != null)
            {
                var live = frame.Fx;
                for (int i = 0; i < live.Count; i++) fx.Tick(live[i].Fx, live[i].U);
            }
        }
    }
}
