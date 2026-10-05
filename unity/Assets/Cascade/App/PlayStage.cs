using System;
using Cascade.Config;
using Cascade.Presentation;
using Cascade.Presentation.Shell;
using Cascade.Unity.View;
using UnityEngine;

namespace Cascade.App
{
    /// <summary>
    /// The 3D board of a playing level: <see cref="BoardView"/> with its pieces and tray, the <see cref="TimelinePlayer"/>, the
    /// <see cref="BoardCameraRig"/> that fits the board into the Play screen's <c>BoardSlot</c>, the pointer adapter and the
    /// <see cref="BoardController"/> that ties them together. It exists (is active) only while the flow is on the Play page: <see cref="Begin"/>
    /// builds the board of a session and switches the objects on, <see cref="End"/> drops the board and switches them off. The objects are
    /// created once, so a new level only rebuilds the board. The Unity camera itself stays on in every screen because it draws the sand backdrop.
    /// </summary>
    public sealed class PlayStage
    {
        readonly GameObject root;
        readonly DialRegistry dials;
        readonly PieceStyle style;

        public PlayStage(Transform parent, PieceStyle pieceStyle, Camera camera, HudScreen hud, DialRegistry dialRegistry, Func<Vector2Int> screenSize, SoundPlayer? sound = null)
        {
            style = pieceStyle;
            dials = dialRegistry;
            root = new GameObject("Board");
            root.transform.SetParent(parent, false);
            View = root.AddComponent<BoardView>();
            View.Init(style);
            Player = root.AddComponent<TimelinePlayer>();
            Rig = root.AddComponent<BoardCameraRig>();
            Rig.ScreenSizeSource = screenSize;
            Player.Init(View, new FxPresenter(style, View.FxRoot, hud.PopupLayer, Rig.WorldToPanel), sound);
            Rig.Init(camera, hud.BoardSlot, style, BoardCameraRig.ParamsFrom(style, dials));
            Controller = new BoardController(View, Player, Rig);
            Mouse = root.AddComponent<MouseAdapter>();
            Mouse.Bind(Controller, Rig);
            Touch = root.AddComponent<TouchAdapter>();
            Touch.Bind(Controller, Rig);
            root.SetActive(false);
        }

        public BoardView View { get; }
        public TimelinePlayer Player { get; }
        public BoardCameraRig Rig { get; }
        public BoardController Controller { get; }
        public MouseAdapter Mouse { get; }
        public TouchAdapter Touch { get; }

        /// <summary>The board exists and takes input.</summary>
        public bool Active => root.activeSelf;

        /// <summary>Shows the board of this game's session. Swaps go through <see cref="GameController.TrySwap"/>.</summary>
        public void Begin(GameController game, TimingParams timing)
        {
            var session = game.Session ?? throw new InvalidOperationException("no session to show");
            root.SetActive(true);
            Controller.SwapHandler = game.TrySwap;
            Controller.Paused = false;
            Controller.InputEnabled = true;
            ApplyDials(timing);
            Controller.Begin(session);
        }

        /// <summary>Drops the board and switches the stage off.</summary>
        public void End()
        {
            if (!root.activeSelf) return;
            Controller.End();
            root.SetActive(false);
        }

        /// <summary>Re-reads the dials the board uses: animation timings (with the player's speed), input, piece variation, camera.</summary>
        public void ApplyDials(TimingParams timing)
        {
            Controller.Timing = timing;
            Controller.Input = InputParams.ForMouse(dials);
            Touch.SetParams(InputParams.ForTouch(dials), GestureParams.From(dials));
            Controller.Variation = PieceVariationParams.From(dials);
            Rig.SetParams(BoardCameraRig.ParamsFrom(style, dials));
        }
    }
}
