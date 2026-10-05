using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace Cascade.Unity.View
{
    /// <summary>
    /// Applies a <see cref="PieceStyle"/> to the running scene: camera (clear colour, FOV, pitch, simple fit),
    /// the light rig (sun object, ambient, shader globals; no hard shadows), the pale-sand backdrop quad and the
    /// grain overlay strength. Put one in the scene and call <see cref="Apply"/>; it can be called again to
    /// switch style or react to dial changes.
    /// </summary>
    public sealed class StyleApplier : MonoBehaviour
    {
        public PieceStyle? Style { get; private set; }
        public Camera? Camera { get; private set; }

        Light? sun;
        MeshRenderer? backdrop;
        Material? backdropMaterial;

        static readonly int TopColorId = Shader.PropertyToID("_TopColor");
        static readonly int BottomColorId = Shader.PropertyToID("_BottomColor");
        static readonly int GrainAlphaId = Shader.PropertyToID("_GrainAlpha");

        public void Apply(PieceStyle style, Camera camera, float? pitchDegOverride = null)
        {
            Style = style;
            Camera = camera;
            ApplyLight();
            ApplyBackdrop();
            ApplyCamera(pitchDegOverride);
            SetGrainAlpha(style.material.grainAlpha);
        }

        // ---- camera ----

        /// <summary>Clear colour, FOV and clip planes; pitch from the style or the camera.pitch_deg dial.</summary>
        public void ApplyCamera(float? pitchDegOverride = null)
        {
            if (Style == null || Camera == null) return;
            var c = Style.camera;
            Camera.orthographic = false;
            Camera.fieldOfView = c.fovDeg;
            Camera.nearClipPlane = c.near;
            Camera.farClipPlane = c.far;
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Color32 bg = Style.palette.bgBottom;
            Camera.backgroundColor = new Color32(bg.r, bg.g, bg.b, 255);
        }

        /// <summary>
        /// Simple distance fit: places the camera at <c>pitchDeg</c> from vertical so the world-space
        /// <paramref name="board"/> bounds fill the view minus the margin, centred. The real fit (screen slot,
        /// zoom, pan) comes from Presentation.CameraFit later; this is the placeholder and the showcase.
        /// </summary>
        public void FitCamera(Bounds board, float? pitchDegOverride = null, float? marginOverride = null)
        {
            if (Style == null || Camera == null) return;
            Fit(Camera, board, pitchDegOverride ?? Style.camera.pitchDeg, marginOverride ?? Style.camera.fitMargin);
        }

        public static void Fit(Camera cam, Bounds board, float pitchDeg, float margin)
        {
            float p = pitchDeg * Mathf.Deg2Rad;
            Vector3 fwd = new Vector3(0f, -Mathf.Cos(p), Mathf.Sin(p));
            Quaternion rot = Quaternion.LookRotation(fwd, Vector3.forward);
            Quaternion inv = Quaternion.Inverse(rot);
            float tanV = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float tanH = tanV * cam.aspect;
            Vector3 target = board.center;
            float limit = 1f - Mathf.Clamp(margin, 0f, 0.5f);
            float dist = 10f;
            for (int iter = 0; iter < 6; iter++)
            {
                float lo = 0.5f, hi = 500f;
                for (int k = 0; k < 40; k++)
                {
                    float mid = (lo + hi) * 0.5f;
                    Project(board, target - fwd * mid, inv, tanH, tanV, out _, out _, out _, out _, out bool ok);
                    Vector2 ex = Extent(board, target - fwd * mid, inv, tanH, tanV);
                    if (ok && Mathf.Max(ex.x, ex.y) <= limit) hi = mid; else lo = mid;
                }
                dist = hi;
                Project(board, target - fwd * dist, inv, tanH, tanV, out float minX, out float maxX, out float minY, out float maxY, out _);
                float cx = (minX + maxX) * 0.5f, cy = (minY + maxY) * 0.5f;
                target += rot * new Vector3(cx * tanH * dist, cy * tanV * dist, 0f);
            }
            cam.transform.SetPositionAndRotation(target - fwd * dist, rot);
        }

        static void Project(Bounds b, Vector3 camPos, Quaternion inv, float tanH, float tanV,
            out float minX, out float maxX, out float minY, out float maxY, out bool ok)
        {
            minX = minY = float.MaxValue; maxX = maxY = float.MinValue; ok = true;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = b.min + new Vector3((i & 1) != 0 ? b.size.x : 0f, (i & 2) != 0 ? b.size.y : 0f, (i & 4) != 0 ? b.size.z : 0f);
                Vector3 cs = inv * (c - camPos);
                if (cs.z <= 0.05f) { ok = false; continue; }
                float nx = cs.x / (cs.z * tanH), ny = cs.y / (cs.z * tanV);
                minX = Mathf.Min(minX, nx); maxX = Mathf.Max(maxX, nx);
                minY = Mathf.Min(minY, ny); maxY = Mathf.Max(maxY, ny);
            }
        }

        static Vector2 Extent(Bounds b, Vector3 camPos, Quaternion inv, float tanH, float tanV)
        {
            Project(b, camPos, inv, tanH, tanV, out float minX, out float maxX, out float minY, out float maxY, out _);
            // Half-extent of the projection around its own centre, so centring is solved separately.
            return new Vector2((maxX - minX) * 0.5f, (maxY - minY) * 0.5f);
        }

        // ---- light ----

        public void ApplyLight()
        {
            if (Style == null) return;
            Style.ApplyLightGlobals();
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Style.light.ambient;
            RenderSettings.fog = false;
            if (sun == null)
            {
                var go = new GameObject("Sun");
                go.transform.SetParent(transform, false);
                sun = go.AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.shadows = LightShadows.None;
            }
            sun.color = Style.light.sun;
            sun.intensity = 1f;
            sun.transform.rotation = Quaternion.Euler(-Style.light.sunPitch, -Style.light.sunHeading, 0f);
        }

        // ---- backdrop and grain ----

        void ApplyBackdrop()
        {
            if (Style == null) return;
            if (backdrop == null)
            {
                var go = new GameObject("Backdrop");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = Style.art.quad;
                backdrop = go.AddComponent<MeshRenderer>();
                backdrop.shadowCastingMode = ShadowCastingMode.Off;
                backdrop.receiveShadows = false;
                // The shader writes clip-space positions, so the renderer must never be frustum culled.
                backdrop.localBounds = new Bounds(Vector3.zero, new Vector3(1e5f, 1e5f, 1e5f));
            }
            if (backdropMaterial == null && Style.art.background != null)
                backdropMaterial = new Material(Style.art.background) { hideFlags = HideFlags.HideAndDontSave };
            if (backdropMaterial != null)
            {
                backdropMaterial.SetColor(TopColorId, (Color)(Color32)Style.palette.bgTop);
                backdropMaterial.SetColor(BottomColorId, (Color)(Color32)Style.palette.bgBottom);
                backdrop.sharedMaterial = backdropMaterial;
            }
        }

        /// <summary>Film grain strength (the style's grain_alpha); settable at runtime.</summary>
        public void SetGrainAlpha(float alpha)
        {
            var m = Style != null ? Style.art.grain : null;
            if (m != null) m.SetFloat(GrainAlphaId, Mathf.Clamp01(alpha));
        }

        // ---- fonts ----

        /// <summary>
        /// Sets the body font (Delius) on a UI Toolkit root; the title uses <see cref="TitleFont"/>
        /// (Delius Swash Caps), e.g. <c>titleLabel.style.unityFontDefinition = StyleApplier.TitleFont(style)</c>.
        /// In USS the same fonts are <c>-unity-font: url("project:///Assets/Cascade/UI/Fonts/Delius-Regular.ttf")</c>.
        /// </summary>
        public void ApplyFonts(VisualElement root)
        {
            if (Style?.hud.bodyFont != null)
                root.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(Style.hud.bodyFont));
        }

        public static StyleFontDefinition TitleFont(PieceStyle style) =>
            style.hud.titleFont != null ? new StyleFontDefinition(FontDefinition.FromFont(style.hud.titleFont)) : new StyleFontDefinition(StyleKeyword.Null);

        void OnDestroy()
        {
            if (backdropMaterial == null) return;
            if (Application.isPlaying) Destroy(backdropMaterial); else DestroyImmediate(backdropMaterial);
        }
    }
}
