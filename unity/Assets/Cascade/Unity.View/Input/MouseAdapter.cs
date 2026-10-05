using System;
using Cascade.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Cascade.Unity.View
{
    /// <summary>
    /// Mouse and keyboard to the board (PRD 2.2, 5.3; TRD 13.5), through the Unity Input System (<c>Mouse.current</c>,
    /// <c>Keyboard.current</c>). It translates devices into the pointer calls of <see cref="BoardController"/> and contains no
    /// rule: the left button is the pointer (press, drag, click go to the <see cref="InputStateMachine"/>), the wheel zooms
    /// toward the cursor, right and middle drag pan, a double click on empty space resets the view (the state machine
    /// decides). Esc and Android Back are not its business (<c>LifecycleBridge</c> raises <c>BackPressed</c>). Presses that start outside the board slot are not the board's.
    /// The public <c>Feed*</c> methods are the same entry points the polling uses, so tests and tools can drive the adapter
    /// without a device.
    /// </summary>
    public sealed class MouseAdapter : MonoBehaviour
    {
        BoardController? controller;
        BoardCameraRig? rig;
        bool leftOnBoard;
        bool panning;
        Float2 panLast;
        Vector2 lastPosition = new Vector2(float.NaN, float.NaN);

        [SerializeField, Tooltip("Scroll units the Input System reports for one wheel notch (Windows: 120).")]
        float scrollUnitsPerNotch = 120f;

        public void Bind(BoardController boardController, BoardCameraRig cameraRig)
        {
            controller = boardController;
            rig = cameraRig;
        }

        void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null || controller == null) return;

            Vector2 raw = mouse.position.ReadValue();
            double timeMs = Time.unscaledTimeAsDouble * 1000.0;

            if (mouse.leftButton.wasPressedThisFrame) FeedPress(raw, timeMs);
            else if (raw != lastPosition) FeedMove(raw);
            if (mouse.leftButton.wasReleasedThisFrame) FeedRelease(raw);

            bool panHeld = mouse.rightButton.isPressed || mouse.middleButton.isPressed;
            bool panStart = mouse.rightButton.wasPressedThisFrame || mouse.middleButton.wasPressedThisFrame;
            if (panStart) FeedPanStart(raw);
            else if (panHeld) FeedPanMove(raw);
            if (!panHeld) panning = false;

            float scroll = mouse.scroll.ReadValue().y;
            if (scroll != 0f) FeedScroll(raw, scroll / Mathf.Max(1f, scrollUnitsPerNotch));

            lastPosition = raw;
        }

        // ---- entry points (Unity screen coordinates: pixels, origin bottom left) ----

        Float2 ToTopLeft(Vector2 unityScreen) => rig != null ? rig.ToTopLeft(unityScreen) : new Float2(unityScreen.x, Screen.height - unityScreen.y);

        bool OverBoard(Float2 px) => rig != null && rig.HasSlot && rig.SlotPx.Contains(px);

        public void FeedPress(Vector2 unityScreen, double timeMs)
        {
            if (controller == null) return;
            var px = ToTopLeft(unityScreen);
            leftOnBoard = OverBoard(px);
            if (leftOnBoard) controller.PointerDown(px, timeMs);
        }

        public void FeedMove(Vector2 unityScreen)
        {
            if (controller == null) return;
            controller.PointerMove(ToTopLeft(unityScreen));
        }

        public void FeedRelease(Vector2 unityScreen)
        {
            if (controller == null) return;
            if (leftOnBoard) controller.PointerUp(ToTopLeft(unityScreen));
            leftOnBoard = false;
        }

        public void FeedPanStart(Vector2 unityScreen)
        {
            var px = ToTopLeft(unityScreen);
            panning = OverBoard(px);
            panLast = px;
        }

        public void FeedPanMove(Vector2 unityScreen)
        {
            if (controller == null || !panning) return;
            var px = ToTopLeft(unityScreen);
            if (px != panLast) controller.Pan(panLast, px);
            panLast = px;
        }

        /// <summary>Wheel notches (positive = zoom in) over the board.</summary>
        public void FeedScroll(Vector2 unityScreen, float notches)
        {
            if (controller == null) return;
            var px = ToTopLeft(unityScreen);
            if (OverBoard(px)) controller.ZoomNotches(notches, px);
        }
    }
}
