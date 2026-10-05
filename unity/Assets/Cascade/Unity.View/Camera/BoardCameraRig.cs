using Cascade.Config;
using Cascade.Presentation;
using UnityEngine;
using UnityEngine.UIElements;
using PresentationCameraParams = Cascade.Presentation.CameraParams;

namespace Cascade.Unity.View
{
    /// <summary>
    /// Owns the <see cref="BoardCamera"/> and copies its pose to the Unity camera (TRD 12.2). Every frame it measures the
    /// layout's <c>BoardSlot</c> element in screen pixels (origin top left, the convention of Presentation) and hands slot
    /// and screen size to <see cref="BoardCamera.SetViewport"/>, which re-fits the board unless the player zoomed. The
    /// Unity camera is only touched when the pose actually changes. It also hosts the style: clear color, light rig and
    /// grain come from <see cref="StyleApplier"/>.
    /// </summary>
    public sealed class BoardCameraRig : MonoBehaviour
    {
        Camera? cam;
        VisualElement? slot;
        PieceStyle? style;
        PresentationCameraParams cameraParams = PresentationCameraParams.Default;
        BoardCamera? board;
        CameraPose? applied;
        RectF slotPx;
        bool haveSlot;

        /// <summary>The camera logic (zoom, pan, pick, project); null until <see cref="SetBoard"/>.</summary>
        public BoardCamera? Board => board;

        public Camera UnityCamera => cam != null ? cam : throw new System.InvalidOperationException("BoardCameraRig.Init was not called");

        /// <summary>The board slot in screen pixels (top-left origin), as last measured.</summary>
        public RectF SlotPx => slotPx;

        public bool HasSlot => haveSlot;

        Vector2Int? sizeOverride;

        /// <summary>
        /// The size in pixels of what the camera renders to: the screen, unless <see cref="OverrideScreenSize"/> says the view
        /// renders to a texture of another size (tests and tools that take screenshots without a window).
        /// </summary>
        public Vector2Int ScreenSize => sizeOverride ?? ScreenSizeSource?.Invoke() ?? new Vector2Int(Screen.width, Screen.height);

        /// <summary>Where the size of the rendered area comes from (the app's lifecycle bridge, so one override serves camera and UI); null: the screen.</summary>
        public System.Func<Vector2Int>? ScreenSizeSource { get; set; }

        public void OverrideScreenSize(Vector2Int? size) => sizeOverride = size;

        /// <summary>Camera parameters of a style and the dials: fov, tray lip and depth from the style, the rest from <c>[camera]</c>.</summary>
        public static PresentationCameraParams ParamsFrom(PieceStyle style, DialRegistry dials) =>
            PresentationCameraParams.From(dials) with
            {
                FovDeg = style.camera.fovDeg,
                TrayLip = style.tray.lip,
                TrayDepth = style.tray.depth,
            };

        /// <param name="boardSlot">The layout's empty <c>BoardSlot</c> element; its resolved rectangle is where the board is fitted.</param>
        public void Init(Camera camera, VisualElement boardSlot, PieceStyle pieceStyle, PresentationCameraParams parameters)
        {
            cam = camera;
            slot = boardSlot;
            style = pieceStyle;
            cameraParams = parameters;
            if (board != null) board.Params = parameters;
        }

        /// <summary>The player switched art style: its fov, tray lip and depth and slot clipping apply from now on.</summary>
        public void SetStyle(PieceStyle pieceStyle, PresentationCameraParams parameters)
        {
            style = pieceStyle;
            SetParams(parameters);
        }

        /// <summary>A camera dial changed.</summary>
        public void SetParams(PresentationCameraParams parameters)
        {
            cameraParams = parameters;
            if (board != null) board.Params = parameters;
        }

        /// <summary>A new level: show this board auto-fitted.</summary>
        public void SetBoard(BoardGeometry geometry)
        {
            if (board == null) board = new BoardCamera(geometry, cameraParams);
            else board.SetBoard(geometry);
            applied = null;
            Refresh();
        }

        float baseFov;
        float punchAmount, punchMs, punchStart = float.NegativeInfinity;

        /// <summary>
        /// A short camera punch (PRD 5.4.1): the view zooms in by <paramref name="amount"/> (a fraction of the field of view) and
        /// settles back over <paramref name="durationMs"/>. A stronger punch replaces a weaker one that is still running.
        /// </summary>
        public void Punch(float amount, float durationMs)
        {
            if (durationMs <= 0f) return;
            float running = Time.unscaledTime - punchStart < punchMs / 1000f ? punchAmount : 0f;
            if (amount < running) return;
            punchAmount = amount; punchMs = durationMs; punchStart = Time.unscaledTime;
        }

        void LateUpdate()
        {
            Refresh();
            ApplyPunch();
        }

        void ApplyPunch()
        {
            if (cam == null || baseFov <= 0f) return;
            float u = (Time.unscaledTime - punchStart) / (punchMs / 1000f);
            float k = u >= 0f && u < 1f ? punchAmount * Mathf.Sin(Mathf.PI * u) : 0f;
            float fov = baseFov * (1f - k);
            if (!Mathf.Approximately(cam.fieldOfView, fov)) cam.fieldOfView = fov;
        }

        /// <summary>Re-measures the slot and applies the pose if it changed. Called every frame; public for tests.</summary>
        public void Refresh()
        {
            if (board == null || cam == null) return;
            if (!MeasureSlot()) return;
            var size = ScreenSize;
            board.SetViewport(slotPx, new Float2(size.x, size.y));
            var pose = board.Pose;
            if (ReferenceEquals(pose, applied)) { PublishClip(); return; }
            applied = pose;
            var t = cam.transform;
            t.SetPositionAndRotation(
                new Vector3((float)pose.Position.X, (float)pose.Position.Y, (float)pose.Position.Z),
                Quaternion.Euler((float)pose.EulerDegrees.X, (float)pose.EulerDegrees.Y, (float)pose.EulerDegrees.Z));
            baseFov = (float)pose.FovDeg;
            cam.fieldOfView = baseFov;
            cam.nearClipPlane = (float)pose.NearClip;
            cam.farClipPlane = (float)pose.FarClip;
            PublishClip();
        }

        static readonly int ClipId = Shader.PropertyToID("_CascadeClip");
        static readonly int ClipVpId = Shader.PropertyToID("_CascadeClipVP");

        /// <summary>
        /// Tells the board shaders to draw only inside the BoardSlot (style <c>camera.clipToSlot</c>): a zoomed or panned board, or a
        /// piece dropping in from above the tray, never runs under the HUD text. The rectangle is the slot in the camera's logical NDC.
        /// </summary>
        void PublishClip()
        {
            if (cam == null || style == null || !haveSlot) return;
            if (!style.camera.clipToSlot) { PieceStyle.ClipOff(); return; }
            var size = ScreenSize;
            if (size.x < 1 || size.y < 1) return;
            float x0 = (float)(2.0 * slotPx.X / size.x - 1.0), x1 = (float)(2.0 * (slotPx.X + slotPx.Width) / size.x - 1.0);
            float yTop = (float)(1.0 - 2.0 * slotPx.Y / size.y), yBottom = (float)(1.0 - 2.0 * (slotPx.Y + slotPx.Height) / size.y);
            Shader.SetGlobalVector(ClipId, new Vector4(x0, yBottom, x1, yTop));
            Shader.SetGlobalMatrix(ClipVpId, cam.projectionMatrix * cam.worldToCameraMatrix);
        }

        void OnDisable() => PieceStyle.ClipOff();

        /// <summary>The slot's resolved rectangle in screen pixels; false while the layout has no size yet.</summary>
        bool MeasureSlot()
        {
            if (slot == null || slot.panel == null) return false;
            var root = slot.panel.visualTree;
            float rw = root.layout.width, rh = root.layout.height;
            var wb = slot.worldBound; // panel units, top-left origin
            if (!(rw > 1f) || !(rh > 1f) || !(wb.width > 1f) || !(wb.height > 1f)) return false;
            var size = ScreenSize;
            float sx = size.x / rw, sy = size.y / rh;
            slotPx = new RectF(wb.x * sx, wb.y * sy, wb.width * sx, wb.height * sy);
            haveSlot = true;
            return true;
        }

        /// <summary>
        /// A world point as a position in the UI panel's units (origin top left), for labels that float over the board.
        /// Uses the same camera math and screen size as the board slot, so it is right on a window, a notched phone or a
        /// texture. Null before the layout is measured or when the point is behind the camera.
        /// </summary>
        public Vector2? WorldToPanel(Vector3 world)
        {
            if (board == null || slot == null || slot.panel == null || !haveSlot) return null;
            var px = board.Project(new Float3(world.x, world.y, world.z));
            if (double.IsNaN(px.X) || double.IsNaN(px.Y)) return null;
            var root = slot.panel.visualTree;
            var size = ScreenSize;
            return new Vector2((float)px.X * root.layout.width / size.x, (float)px.Y * root.layout.height / size.y);
        }

        /// <summary>Screen position from Unity's convention (origin bottom left) to the top-left origin of Presentation.</summary>
        public Float2 ToTopLeft(Vector2 unityScreen) => new Float2(unityScreen.x, ScreenSize.y - unityScreen.y);

        /// <summary>Sets the style's camera values on the Unity camera (clear color, clip planes); the pose then moves it.</summary>
        public void ApplyStyle(StyleApplier applier)
        {
            if (style == null || cam == null) return;
            applier.Apply(style, cam);
        }
    }
}
