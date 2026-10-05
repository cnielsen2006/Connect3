using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cascade.Unity.View
{
    /// <summary>
    /// Runtime material pool for one style. Pieces of the same colour, quantised brightness and quantised
    /// fleck offset share a material, so every renderer stays SRP-batcher compatible (no MaterialPropertyBlock)
    /// and re-colouring a piece is just swapping a shared material reference.
    /// </summary>
    public sealed class PieceMaterials : IDisposable
    {
        readonly PieceStyle style;
        readonly Dictionary<long, Material> clay = new Dictionary<long, Material>();
        readonly Dictionary<int, Material> agate = new Dictionary<int, Material>();
        readonly Dictionary<int, Texture2D> ramps = new Dictionary<int, Texture2D>();
        Material?[] shadowLevels = Array.Empty<Material?>();
        Material?[] haloLevels = Array.Empty<Material?>();
        Material?[] rippleLevels = Array.Empty<Material?>();
        Material?[] washLevels = Array.Empty<Material?>();

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int BrightnessId = Shader.PropertyToID("_Brightness");
        static readonly int FleckParamsId = Shader.PropertyToID("_FleckParams");
        static readonly int BandTexId = Shader.PropertyToID("_BandTex");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        public PieceMaterials(PieceStyle style) { this.style = style; }

        static Material Copy(Material template, string name) =>
            new Material(template) { name = name, hideFlags = HideFlags.HideAndDontSave };

        /// <summary>Pooled clay material. Brightness and fleck offsets are quantised by the style dials.</summary>
        public Material Clay(int color, bool bomb, float brightness, float fleckX, float fleckY)
        {
            var m = style.material;
            int qb = Mathf.RoundToInt((brightness - 1f) / Mathf.Max(1e-4f, m.brightnessQuantum));
            int qx = Mathf.RoundToInt(fleckX / Mathf.Max(1e-4f, m.fleckQuantum));
            int qy = Mathf.RoundToInt(fleckY / Mathf.Max(1e-4f, m.fleckQuantum));
            int ci = ((color % style.PieceCount) + style.PieceCount) % style.PieceCount;
            long key = ((((long)ci * 2 + (bomb ? 1 : 0)) * 4096L + (qb + 2048)) * 256L + (qx + 128)) * 256L + (qy + 128);
            if (clay.TryGetValue(key, out var mat)) return mat;

            var tmpl = style.art.clay ?? throw new InvalidOperationException("PieceStyle.art.clay missing.");
            mat = Copy(tmpl, "Clay_" + ci + (bomb ? "_bomb" : "") + "_" + qb + "_" + qx + "_" + qy);
            var spec = new ClaySpec(ci, bomb, qb, qx, qy);
            ConfigureClay(mat, spec);
            clay[key] = mat;
            claySpecs[key] = spec;
            return mat;
        }

        readonly struct ClaySpec
        {
            public readonly int Color, Qb, Qx, Qy;
            public readonly bool Bomb;
            public ClaySpec(int color, bool bomb, int qb, int qx, int qy) { Color = color; Bomb = bomb; Qb = qb; Qx = qx; Qy = qy; }
        }

        readonly Dictionary<long, ClaySpec> claySpecs = new Dictionary<long, ClaySpec>();

        void ConfigureClay(Material mat, ClaySpec s)
        {
            var m = style.material;
            Color32 c = style.PieceColor(s.Color);
            mat.SetColor(BaseColorId, new Color(c.r / 255f, c.g / 255f, c.b / 255f, 1f));
            float b = (1f + s.Qb * m.brightnessQuantum) * (s.Bomb ? m.bombDarken : 1f);
            mat.SetFloat(BrightnessId, b);
            mat.SetFloat("_Sheen", m.sheen);
            mat.SetFloat("_Shininess", m.shininess);
            mat.SetFloat("_RimStrength", m.rimStrength);
            mat.SetFloat("_RimPower", m.rimPower);
            mat.SetVector(FleckParamsId, new Vector4(s.Qx * m.fleckQuantum * m.fleckOffsetRange.x,
                s.Qy * m.fleckQuantum * m.fleckOffsetRange.y, m.fleckDark, m.fleckLight));
        }

        /// <summary>
        /// Re-applies the style's current values to the materials already handed out, in place (renderers keep their references).
        /// Called when the style asset is edited while the scene runs, so a change shows at once. Template materials are re-read too.
        /// </summary>
        public void Refresh()
        {
            if (!Application.isPlaying) return;
            foreach (var kv in clay)
            {
                if (style.art.clay != null) kv.Value.CopyPropertiesFromMaterial(style.art.clay);
                ConfigureClay(kv.Value, claySpecs[kv.Key]);
            }
            foreach (var kv in agate)
            {
                if (style.art.agateMaterial != null) { kv.Value.CopyPropertiesFromMaterial(style.art.agateMaterial); kv.Value.SetTexture(BandTexId, ramps[kv.Key]); }
                FillRamp(ramps[kv.Key], kv.Key);
            }
            RefreshLevels(shadowLevels, style.art.shadow, style.fx.shadowColor);
            Color halo = style.fx.haloColor;
            halo.a *= style.fx.haloAlpha;
            RefreshLevels(haloLevels, style.art.halo, halo);
            RefreshLevels(washLevels, style.art.halo, style.fx.washColor);
            RefreshLevels(rippleLevels, style.art.rippleMaterial, style.fx.rippleColor);
            if (tray != null)
            {
                if (style.art.trayMaterial != null) tray.CopyPropertiesFromMaterial(style.art.trayMaterial);
                ConfigureTray(tray);
            }
            if (trayHole != null)
            {
                if (style.art.trayHoleMaterial != null) trayHole.CopyPropertiesFromMaterial(style.art.trayHoleMaterial);
                ConfigureTrayHole(trayHole);
            }
        }

        void RefreshLevels(Material?[] cache, Material? template, Color color)
        {
            for (int level = 0; level < cache.Length; level++)
            {
                var m = cache[level];
                if (m == null) continue;
                if (template != null) m.CopyPropertiesFromMaterial(template);
                Color c = color;
                c.a *= (level + 1f) / cache.Length;
                m.SetColor(ColorId, c);
            }
        }

        /// <summary>Banded agate material showing the first <paramref name="colors"/> palette colours.</summary>
        public Material Agate(int colors)
        {
            colors = Mathf.Clamp(colors, 1, style.PieceCount);
            if (agate.TryGetValue(colors, out var mat)) return mat;
            var tmpl = style.art.agateMaterial ?? throw new InvalidOperationException("PieceStyle.art.agateMaterial missing.");
            mat = Copy(tmpl, "Agate_" + colors);
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
                name = "AgateRamp_" + colors,
            };
            FillRamp(tex, colors);
            ramps[colors] = tex;
            mat.SetTexture(BandTexId, tex);
            agate[colors] = mat;
            return mat;
        }

        void FillRamp(Texture2D tex, int colors)
        {
            int bands = Mathf.Max(1, Mathf.Max(style.specials.agateBands, colors)); // every active colour gets a band
            if (tex.width != bands) tex.Reinitialize(bands, 1);
            for (int i = 0; i < bands; i++) tex.SetPixel(i, 0, style.PieceColor(i % colors));
            tex.Apply(false, false);
        }

        Material Level(ref Material?[] cache, Material? template, Color color, int level, string name)
        {
            int n = Mathf.Max(2, style.material.fadeLevels);
            if (cache.Length != n) cache = new Material?[n];
            level = Mathf.Clamp(level, 0, n - 1);
            var m = cache[level];
            if (m != null) return m;
            m = Copy(template ?? throw new InvalidOperationException("PieceStyle.art." + name + " missing."), name + "_" + level);
            Color c = color;
            c.a *= (level + 1f) / n;
            m.SetColor(ColorId, c);
            cache[level] = m;
            return m;
        }

        /// <summary>Fade level for a 0..1 alpha factor: 0 is invisible-ish, FadeLevels-1 is full.</summary>
        public int FadeLevel(float t01) =>
            Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(t01) * (Mathf.Max(2, style.material.fadeLevels) - 1)), 0, Mathf.Max(2, style.material.fadeLevels) - 1);

        public Material Shadow(int level) => Level(ref shadowLevels, style.art.shadow, style.fx.shadowColor, level, "shadow");

        public Material Halo(int level)
        {
            Color c = style.fx.haloColor;
            c.a *= style.fx.haloAlpha;
            return Level(ref haloLevels, style.art.halo, c, level, "halo");
        }

        Material? tray, trayHole;

        /// <summary>Tray material with the style's tiling and brightness dials applied.</summary>
        public Material Tray()
        {
            if (tray != null) return tray;
            tray = Copy(style.art.trayMaterial ?? throw new InvalidOperationException("PieceStyle.art.trayMaterial missing."), "Tray");
            ConfigureTray(tray);
            return tray;
        }

        void ConfigureTray(Material m)
        {
            var t = style.tray;
            m.SetVector("_GrainTiling", new Vector4(t.grainTiling.x, t.grainTiling.y, 0f, 0f));
            m.SetFloat(BrightnessId, t.brightness);
        }

        void ConfigureTrayHole(Material m)
        {
            Color32 c = style.tray.holeFloor;
            m.SetColor(BaseColorId, new Color(c.r / 255f, c.g / 255f, c.b / 255f, 1f));
        }

        /// <summary>Dark material of the floor visible through holes in the tray.</summary>
        public Material TrayHole()
        {
            if (trayHole != null) return trayHole;
            trayHole = Copy(style.art.trayHoleMaterial ?? throw new InvalidOperationException("PieceStyle.art.trayHoleMaterial missing."), "TrayHole");
            ConfigureTrayHole(trayHole);
            return trayHole;
        }

        public Material Wash(int level) => Level(ref washLevels, style.art.halo, style.fx.washColor, level, "wash");

        public Material Ripple(int level) => Level(ref rippleLevels, style.art.rippleMaterial, style.fx.rippleColor, level, "ripple");

        public void Dispose()
        {
            foreach (var m in clay.Values) Destroy(m);
            foreach (var m in agate.Values) Destroy(m);
            foreach (var t in ramps.Values) Destroy(t);
            foreach (var m in shadowLevels) if (m != null) Destroy(m);
            foreach (var m in haloLevels) if (m != null) Destroy(m);
            foreach (var m in rippleLevels) if (m != null) Destroy(m);
            foreach (var m in washLevels) if (m != null) Destroy(m);
            if (tray != null) Destroy(tray);
            if (trayHole != null) Destroy(trayHole);
            tray = trayHole = null;
            clay.Clear(); claySpecs.Clear(); agate.Clear(); ramps.Clear();
            shadowLevels = Array.Empty<Material?>();
            haloLevels = Array.Empty<Material?>();
            rippleLevels = Array.Empty<Material?>();
            washLevels = Array.Empty<Material?>();
        }

        static void Destroy(UnityEngine.Object o)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
