using UnityEngine;
using UnityEngine.Rendering;

namespace Cascade.Unity.View
{
    /// <summary>
    /// One tile in the scene. The animation player writes the pose fields and calls <see cref="ApplyPose"/>;
    /// nothing here allocates per frame. Hierarchy (built once by <see cref="Init"/>):
    /// root (BasePosition) / Shadow, Halo, Pose (lift, scale, roll) / Body (yaw, squash), Vein, HoleRim, HoleDisc, Agate.
    /// </summary>
    public sealed class PieceView : MonoBehaviour
    {
        // ---- pose, written by the animation player ----
        /// <summary>0..1 selection (lift, scale, halo).</summary>
        public float SelectT;
        /// <summary>Extra lift in units of MaterialParams.hoverLift.</summary>
        public float Hover;
        /// <summary>Vertical landing squash factor, 1 = none (volume preserving: width scales by 1/sqrt).</summary>
        public float Squash = 1f;
        /// <summary>Rock about the world Z axis, degrees (the "nudged stone" wobble).</summary>
        public float Roll;
        /// <summary>0..1 sinking into the sand: drops below the tray, shrinks, shadow fades.</summary>
        public float Sink;
        /// <summary>Extra uniform scale (spawn pop, clear shrink).</summary>
        public float Scale = 1f;
        /// <summary>Resting position of the piece on the tray surface, in the parent's space.</summary>
        public Vector3 BasePosition;

        public PieceStyle? Style { get; private set; }
        public int ColorIndex { get; private set; }
        public PieceSpecial Special { get; private set; }
        public PieceLook Look { get; private set; }
        public int AgateColors { get; private set; } = -1;

        Transform? pose, body, shadow, halo, vein, holeRim, holeDisc, agate;
        MeshFilter? bodyFilter, haloFilter, veinFilter, holeRimFilter, holeDiscFilter, agateFilter;
        MeshRenderer? bodyRenderer, shadowRenderer, haloRenderer, veinRenderer, holeRimRenderer, holeDiscRenderer, agateRenderer;
        int shadowLevel = -1, haloLevel = -1;
        bool built;

        /// <summary>Builds the child objects on first use, then sets colour, special-free look and resets the pose.</summary>
        public void Init(PieceStyle style, int color, PieceLook look)
        {
            if (!built || style != Style)
            {
                Style = style;
                Build();
            }
            Special = PieceSpecial.None;
            AgateColors = -1;
            ColorIndex = color;
            Look = look;
            ResetPose();
            RefreshMesh();
            RefreshLook();
            RefreshSpecial();
            ApplyPose();
        }

        public void SetColor(int color)
        {
            if (Style == null) return;
            ColorIndex = color;
            RefreshMesh();
            RefreshLook();
        }

        public void SetLook(PieceLook look)
        {
            Look = look;
            RefreshLook();
        }

        /// <param name="agateColors">For PieceSpecial.Color: how many palette colours the bands show (-1 = style default).</param>
        public void SetSpecial(PieceSpecial special, int agateColors = -1)
        {
            Special = special;
            AgateColors = agateColors;
            RefreshLook();
            RefreshSpecial();
        }

        public void ResetPose()
        {
            SelectT = 0f; Hover = 0f; Squash = 1f; Roll = 0f; Sink = 0f; Scale = 1f;
        }

        /// <summary>Composes position, scale, squash, yaw and roll from the pose fields. No allocation.</summary>
        public void ApplyPose()
        {
            if (Style == null || pose == null || body == null) return;
            var m = Style.material;
            transform.localPosition = BasePosition;

            float lift = SelectT * m.selectLift + Hover * m.hoverLift - Sink * m.sinkDepth;
            float sel = 1f + SelectT * (m.selectScale - 1f);
            float u = m.size * sel * (1f - m.sinkScale * Sink) * Scale;
            float sq = Mathf.Max(0.05f, Squash);
            float xz = 1f + (1f / Mathf.Sqrt(sq) - 1f) * m.squashSpread; // a landing or shuffle squash widens the pebble, but never into its neighbours
            float hr = m.heightRatio / Mathf.Max(1e-4f, m.bakedHeightRatio);

            pose.localPosition = new Vector3(0f, lift, 0f);
            pose.localScale = new Vector3(u * xz, u * sq * hr, u * xz);
            pose.localRotation = Quaternion.Euler(0f, 0f, Roll);

            body.localRotation = Quaternion.Euler(0f, Look.YawDegrees, 0f);
            body.localScale = Look.SquashAlongZ ? new Vector3(1f, 1f, Look.Squash) : new Vector3(Look.Squash, 1f, 1f);

            // Shadow: stays on the tray, shrinks and fades while sinking. Halo: follows the pebble footprint.
            float fade = 1f - Sink;
            float ss = Style.fx.shadowSize * (0.5f + 0.5f * fade);
            shadow!.localScale = new Vector3(ss, 1f, ss);
            int sl = Style.Materials.FadeLevel(fade);
            if (sl != shadowLevel)
            {
                shadowLevel = sl;
                shadowRenderer!.sharedMaterial = Style.Materials.Shadow(sl);
            }

            bool haloOn = SelectT > 0.01f;
            if (halo!.gameObject.activeSelf != haloOn) halo.gameObject.SetActive(haloOn);
            if (haloOn)
            {
                halo.localPosition = new Vector3(0f, Style.fx.haloY + SelectT * m.selectLift, 0f);
                float hs = m.size * sel * (1f + 0.03f * SelectT);
                halo.localScale = new Vector3(hs * (Look.SquashAlongZ ? 1f : Look.Squash), 1f, hs * (Look.SquashAlongZ ? Look.Squash : 1f));
                halo.localRotation = Quaternion.Euler(0f, Look.YawDegrees, 0f);
                int hl = Style.Materials.FadeLevel(SelectT);
                if (hl != haloLevel)
                {
                    haloLevel = hl;
                    haloRenderer!.sharedMaterial = Style.Materials.Halo(hl);
                }
            }
        }

        // ---- internals ----

        void Build()
        {
            var s = Style!;
            // Clean up when re-initialised with another style.
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
            }

            shadow = Child("Shadow", transform, s.art.quad, out _, out shadowRenderer);
            shadow.localPosition = new Vector3(0f, s.fx.shadowY, 0f);
            halo = Child("Halo", transform, s.art.haloCircle, out haloFilter, out haloRenderer);
            halo.localPosition = new Vector3(0f, s.fx.haloY, 0f);
            halo.gameObject.SetActive(false);
            pose = new GameObject("Pose").transform;
            pose.SetParent(transform, false);
            body = Child("Body", pose, null, out bodyFilter, out bodyRenderer);
            vein = Child("Vein", pose, null, out veinFilter, out veinRenderer);
            veinRenderer!.sharedMaterial = s.art.veinMaterial;
            holeRim = Child("HoleRim", body, null, out holeRimFilter, out holeRimRenderer);
            holeRimRenderer!.sharedMaterial = s.art.holeRimMaterial;
            holeDisc = Child("HoleDisc", body, null, out holeDiscFilter, out holeDiscRenderer);
            holeDiscRenderer!.sharedMaterial = s.art.holeDiscMaterial;
            agate = Child("Agate", body, null, out agateFilter, out agateRenderer);
            shadowLevel = haloLevel = -1;
            built = true;
        }

        static Transform Child(string name, Transform parent, Mesh? mesh, out MeshFilter filter, out MeshRenderer renderer)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            renderer = go.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return go.transform;
        }

        void RefreshMesh()
        {
            bodyFilter!.sharedMesh = Style!.PieceMesh(ColorIndex);
            haloFilter!.sharedMesh = Style.HaloMesh(ColorIndex);
            agateFilter!.sharedMesh = Style.AgateMesh(ColorIndex);
            holeRimFilter!.sharedMesh = Style.HoleRimMesh(ColorIndex);
            holeDiscFilter!.sharedMesh = Style.HoleDiscMesh(ColorIndex);
            veinFilter!.sharedMesh = Style.VeinMesh(ColorIndex, Special == PieceSpecial.LineVertical);
        }

        void RefreshLook()
        {
            bodyRenderer!.sharedMaterial = Style!.Materials.Clay(ColorIndex, Special == PieceSpecial.Bomb,
                Look.Brightness, Look.FleckOffsetX, Look.FleckOffsetY);
        }

        void RefreshSpecial()
        {
            var s = Style!;
            vein!.gameObject.SetActive(Special == PieceSpecial.LineHorizontal || Special == PieceSpecial.LineVertical);
            veinFilter!.sharedMesh = s.VeinMesh(ColorIndex, Special == PieceSpecial.LineVertical);
            bool bomb = Special == PieceSpecial.Bomb;
            holeRim!.gameObject.SetActive(bomb);
            holeDisc!.gameObject.SetActive(bomb);
            bool color = Special == PieceSpecial.Color;
            agate!.gameObject.SetActive(color);
            if (color)
            {
                int n = AgateColors > 0 ? AgateColors : s.specials.agateColors;
                agateRenderer!.sharedMaterial = s.Materials.Agate(n);
            }
        }
    }
}
