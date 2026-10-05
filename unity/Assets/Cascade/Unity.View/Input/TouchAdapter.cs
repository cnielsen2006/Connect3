using System;
using Cascade.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

namespace Cascade.Unity.View
{
    /// <summary>
    /// Touch to the board (PRD 2.2, TRD 13.5), through <c>Touchscreen.current</c>. Like <see cref="MouseAdapter"/> it contains no
    /// rule: the first finger on the board is the pointer (press, drag, release go to the same <see cref="InputStateMachine"/>,
    /// with the touch inner fraction); a second finger cancels any drag (no swap, the piece stays selected) and starts a view
    /// gesture: pinch zooms around the midpoint once the spread changed by <c>pinch_min_mm</c>, a two-finger drag pans. A double
    /// tap on empty space resets the view (the state machine decides, with <c>double_tap_ms</c>). Presses that start outside the
    /// board slot are not the board's.
    /// </summary>
    public sealed class TouchAdapter : MonoBehaviour
    {
        BoardController? controller;
        BoardCameraRig? rig;
        InputParams inputParams = InputParams.Default;
        GestureParams gesture = GestureParams.Default;

        int primaryId = -1;
        Float2 primaryLast;
        bool viewGesture;
        bool pinching;
        double startSpread, prevSpread;
        Float2 prevMid;

        public void Bind(BoardController boardController, BoardCameraRig cameraRig)
        {
            controller = boardController;
            rig = cameraRig;
        }

        /// <summary>The touch dials, applied from the next press on (and used to tell the controller to use the touch inner fraction).</summary>
        public void SetParams(InputParams input, GestureParams gestureParams)
        {
            inputParams = input;
            gesture = gestureParams;
        }

        void OnDisable() => Release();

        void Update()
        {
            var screen = Touchscreen.current;
            if (screen == null || controller == null) return;

            double timeMs = Time.unscaledTimeAsDouble * 1000.0;
            int active = 0;
            int idA = -1, idB = -1;
            Float2 posA = default, posB = default;
            bool primaryPresent = false, primaryEnded = false;
            Float2 primaryPos = primaryLast;

            var touches = screen.touches;
            for (int i = 0; i < touches.Count; i++)
            {
                var t = touches[i];
                var phase = t.phase.ReadValue();
                if (phase == UnityEngine.InputSystem.TouchPhase.None) continue;
                int id = t.touchId.ReadValue();
                var p = ToTopLeft(t.position.ReadValue());
                bool ended = phase == UnityEngine.InputSystem.TouchPhase.Ended || phase == UnityEngine.InputSystem.TouchPhase.Canceled;
                if (id == primaryId) { primaryPresent = true; primaryEnded = ended; primaryPos = p; }
                if (ended) continue;
                active++;
                if (idA < 0) { idA = id; posA = p; }
                else if (idB < 0) { idB = id; posB = p; }
            }

            // a second finger turns the gesture into a view gesture and cancels the press
            if (active >= 2)
            {
                if (primaryId >= 0) { controller.CancelPointer(); primaryId = -1; }
                if (!viewGesture) { viewGesture = true; pinching = false; startSpread = prevSpread = Dist(posA, posB); prevMid = Mid(posA, posB); }
                else ViewGesture(posA, posB);
                return;
            }
            if (viewGesture)
            {
                if (active == 0) viewGesture = false; // all fingers up; one finger left over is ignored until then
                return;
            }

            if (primaryId >= 0)
            {
                if (!primaryPresent) { Release(); return; } // vanished without an Ended phase
                if (primaryEnded) { controller.PointerUp(primaryPos); primaryId = -1; return; }
                if (primaryPos != primaryLast) controller.PointerMove(primaryPos);
                primaryLast = primaryPos;
                return;
            }

            if (active == 1)
            {
                // a finger that is new this frame
                var t0 = FindById(touches, idA);
                if (t0 != null && t0.press.wasPressedThisFrame && OverBoard(posA))
                {
                    controller.Input = inputParams;
                    primaryId = idA;
                    primaryLast = posA;
                    controller.PointerDown(posA, timeMs);
                }
            }
        }

        void ViewGesture(Float2 a, Float2 b)
        {
            if (controller == null) return;
            var mid = Mid(a, b);
            double spread = Dist(a, b);
            double pinchMinPx = gesture.PinchMinMm / 25.4 * Dpi();
            if (!pinching && Math.Abs(spread - startSpread) >= pinchMinPx) { pinching = true; prevSpread = spread; }
            if (pinching && prevSpread > 1e-6 && spread > 1e-6)
            {
                controller.ZoomBy(spread / prevSpread, mid);
                prevSpread = spread;
            }
            if (mid != prevMid) controller.Pan(prevMid, mid);
            prevMid = mid;
        }

        void Release()
        {
            if (primaryId >= 0 && controller != null) controller.CancelPointer();
            primaryId = -1;
            viewGesture = false;
        }

        static TouchControl? FindById(UnityEngine.InputSystem.Utilities.ReadOnlyArray<TouchControl> touches, int id)
        {
            for (int i = 0; i < touches.Count; i++)
                if (touches[i].touchId.ReadValue() == id && touches[i].phase.ReadValue() != UnityEngine.InputSystem.TouchPhase.None) return touches[i];
            return null;
        }

        static float Dpi() => Screen.dpi > 0 ? Screen.dpi : 160f;
        static double Dist(Float2 a, Float2 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        static Float2 Mid(Float2 a, Float2 b) => new Float2((a.X + b.X) / 2, (a.Y + b.Y) / 2);
        Float2 ToTopLeft(Vector2 unityScreen) => rig != null ? rig.ToTopLeft(unityScreen) : new Float2(unityScreen.x, Screen.height - unityScreen.y);
        bool OverBoard(Float2 px) => rig != null && rig.HasSlot && rig.SlotPx.Contains(px);
    }
}
