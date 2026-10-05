using System;
using System.IO;
using Cascade.Unity.View;
using UnityEditor;
using UnityEngine;

namespace Cascade.Editor
{
    /// <summary>Procedural textures of style K, written as PNG assets so they can be inspected and replaced.</summary>
    static class ArtTextures
    {
        /// <summary>
        /// Fleck map: R = dark fleck multiplier (1 = none), G = light speck amount. A small cluster of
        /// <paramref name="count"/> flecks (each a dark dot with a lighter speck beside it) around the upper left,
        /// kept inside the pebble for the per-instance shift range.
        /// </summary>
        public static Texture2D Fleck(int size, int count, int seed)
        {
            var r = new float[size * size];
            var g = new float[size * size];
            for (int i = 0; i < r.Length; i++) r[i] = 1f;
            var rng = new System.Random(seed);
            float F() => (float)rng.NextDouble();
            for (int f = 0; f < count; f++)
            {
                float cu = 0.42f + (F() * 2f - 1f) * 0.10f;
                float cv = 0.56f + (F() * 2f - 1f) * 0.07f;
                float rd = 0.016f + F() * 0.012f;
                Splat(r, size, cu, cv, rd, true);
                Splat(g, size, cu - 0.032f, cv + 0.026f, rd * 0.6f, false);
            }
            var px = new Color[size * size];
            for (int i = 0; i < px.Length; i++) px[i] = new Color(r[i], g[i], 0f, 1f);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true) { name = "Fleck" };
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        static void Splat(float[] ch, int size, float cu, float cv, float rad, bool dark)
        {
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float du = (x + 0.5f) / size - cu, dv = (y + 0.5f) / size - cv;
                    float d = Mathf.Sqrt(du * du + dv * dv);
                    float t = Mathf.Clamp01((d - rad * 0.45f) / (rad * 0.55f));
                    float c = 1f - t * t * (3f - 2f * t);
                    if (c <= 0f) continue;
                    int i = y * size + x;
                    ch[i] = dark ? Mathf.Min(ch[i], 1f - c) : Mathf.Max(ch[i], c);
                }
        }

        /// <summary>
        /// Tileable driftwood: fine grain lines running along v (world Z) with a slow wobble.
        /// Grain texture with the lines turned to run up the screen like the K mockup.
        /// </summary>
        public static Texture2D TrayGrain(PieceStyle style, int size, int seed)
        {
            Color b = style.palette.tray, d = style.palette.trayGrain;
            var rng = new System.Random(seed);
            var cols = new float[size];
            for (int i = 0; i < size; i++) cols[i] = (float)rng.NextDouble();
            var px = new Color[size * size];
            for (int x = 0; x < size; x++)
            {
                float r = cols[x] * 0.5f + cols[(x + 1) % size] * 0.25f + cols[(x + size - 1) % size] * 0.25f;
                for (int y = 0; y < size; y++)
                {
                    float wob = 0.5f + 0.5f * Mathf.Sin(y / (float)size * Mathf.PI * 2f * 2f + cols[x] * 6f);
                    float k = Mathf.Clamp01((r - 0.40f) * 2.4f) * (0.45f + 0.5f * wob);
                    px[y * size + x] = Color.Lerp(b, d, k);
                }
            }
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, false) { name = "TrayGrain" };
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        /// <summary>Soft round falloff in alpha (contact shadow).</summary>
        public static Texture2D Blob(int size, float power)
        {
            var px = new Color[size * size];
            float c = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                    px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Pow(Mathf.Max(0f, 1f - d), power));
                }
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, false) { name = "BlobShadow" };
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        /// <summary>Writes the PNG, (re)imports it and applies the importer settings. Returns the imported asset.</summary>
        public static Texture2D Save(Texture2D tex, string assetPath, bool srgb, TextureWrapMode wrap, bool mips)
        {
            File.WriteAllBytes(assetPath, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            imp.textureType = TextureImporterType.Default;
            imp.sRGBTexture = srgb;
            imp.wrapMode = wrap;
            imp.mipmapEnabled = mips;
            imp.filterMode = FilterMode.Bilinear;
            imp.anisoLevel = mips ? 4 : 0;
            imp.alphaSource = TextureImporterAlphaSource.FromInput;
            imp.alphaIsTransparency = false;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }
    }
}
