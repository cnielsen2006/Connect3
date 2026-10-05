using System;
using System.Collections.Generic;
using Cascade.Unity.View;
using UnityEngine;

namespace Cascade.Editor
{
    /// <summary>
    /// Procedural geometry for style K: pebble silhouettes extruded into soft domes, the special-piece overlays
    /// (quartz vein, hag-stone hole, banded agate) and the flat FX meshes. Everything is in "pebble units":
    /// the outline spans about 1 x 1 and the pebble is <c>heightRatio</c> tall; PieceView scales by the style size.
    /// Look and parameters follow the art direction.
    /// </summary>
    static class PebbleGeometry
    {
        public const int OutlinePoints = 72;

        // ---- silhouettes (2D, x right, y = +Z), roughly inside a 1x1 box, centred ----

        /// <summary>Hand-pressed irregularity (fraction of the radius) the builder bakes into every outline of the style being built; 0 for K.</summary>
        public static float Wobble;

        public static List<Vector2> Outline(PebbleShape shape, int n = OutlinePoints)
        {
            var pts = BaseOutline(shape, n);
            if (Wobble <= 0f) return pts;
            // low-frequency, seeded per shape, closed: a sum of whole-number harmonics of the angle
            float p2 = (int)shape * 1.7f, p3 = (int)shape * 2.9f + 1f;
            for (int i = 0; i < pts.Count; i++)
            {
                float t = 2f * Mathf.PI * i / pts.Count;
                float k = 1f + Wobble * (0.6f * Mathf.Sin(2f * t + p2) + 0.4f * Mathf.Sin(3f * t + p3));
                pts[i] *= k;
            }
            return pts;
        }

        static List<Vector2> BaseOutline(PebbleShape shape, int n)
        {
            if (shape == PebbleShape.Capsule) return Capsule(0.25f, 0.25f, n);
            var pts = new List<Vector2>(n);
            for (int i = 0; i < n; i++)
            {
                float t = 2f * Mathf.PI * i / n;
                float c = Mathf.Cos(t), s = Mathf.Sin(t);
                switch (shape)
                {
                    case PebbleShape.Oval:
                        pts.Add(new Vector2(0.5f * c, 0.35f * s));
                        break;
                    case PebbleShape.Leaf:
                    {
                        // vesica: two circular arcs meeting in points at (+-0.5, 0)
                        float x = 0.5f * c;
                        const float k = 0.2667f, r = 0.5667f;
                        float h = Mathf.Sqrt(Mathf.Max(r * r - x * x, 0f)) - k;
                        pts.Add(new Vector2(x, s >= 0f ? h : -h));
                        break;
                    }
                    case PebbleShape.RoundedSquare:
                    {
                        const float e = 4f;
                        pts.Add(new Vector2(0.40f * Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), 2f / e),
                                            0.40f * Mathf.Sign(s) * Mathf.Pow(Mathf.Abs(s), 2f / e)));
                        break;
                    }
                    case PebbleShape.Disc:
                        pts.Add(new Vector2(0.45f * c, 0.45f * s));
                        break;
                    case PebbleShape.Cube:
                    {
                        // a squat, puffy cube: a squarer superellipse than the rounded square
                        const float e = 3f;
                        pts.Add(new Vector2(0.43f * Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), 2f / e),
                                            0.43f * Mathf.Sign(s) * Mathf.Pow(Mathf.Abs(s), 2f / e)));
                        break;
                    }
                    case PebbleShape.Dome:
                        pts.Add(new Vector2(0.42f * c, 0.42f * s));
                        break;
                    case PebbleShape.Triangle:
                    {
                        // a triangle with a vertex up the screen, corners rounded by pulling towards a circle
                        float a = Mathf.Repeat(t - 0.5f * Mathf.PI + Mathf.PI / 3f, 2f * Mathf.PI / 3f) - Mathf.PI / 3f;
                        float r = Mathf.Lerp(0.26f / Mathf.Cos(a), 0.38f, 0.35f);
                        pts.Add(new Vector2(r * c, r * s - 0.04f));
                        break;
                    }
                    case PebbleShape.Teardrop:
                    {
                        // round end at -X, point at +X
                        pts.Add(new Vector2(0.5f * c, 0.42f * s * Mathf.Sin(0.5f * t)));
                        break;
                    }
                    case PebbleShape.Star:
                    {
                        float r = 0.38f * (1f + 0.22f * Mathf.Cos(5f * (t - 0.5f * Mathf.PI)));
                        pts.Add(new Vector2(r * c, r * s));
                        break;
                    }
                    case PebbleShape.Diamond:
                    {
                        const float e = 1.6f;
                        pts.Add(new Vector2(0.48f * Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), 2f / e),
                                            0.48f * Mathf.Sign(s) * Mathf.Pow(Mathf.Abs(s), 2f / e)));
                        break;
                    }
                    case PebbleShape.Scallop:
                    {
                        // eight gentle lobes, like a scalloped shell edge
                        float r = 0.44f * (1f + 0.065f * Mathf.Cos(8f * t));
                        pts.Add(new Vector2(r * c, r * s));
                        break;
                    }
                    case PebbleShape.Kidney:
                    {
                        float a = Mathf.Repeat(t - 0.5f * Mathf.PI + Mathf.PI, 2f * Mathf.PI) - Mathf.PI;
                        float dent = 0.17f * Mathf.Exp(-(a * a) / 0.35f);
                        float r = 0.44f - dent;
                        pts.Add(new Vector2(r * c * 1.05f, r * s * 0.85f));
                        break;
                    }
                    default:
                        throw new ArgumentOutOfRangeException(nameof(shape));
                }
            }
            return pts;
        }

        /// <summary>Stadium outline along X: two semicircles of radius <paramref name="r"/> joined by straight edges.</summary>
        public static List<Vector2> Capsule(float r, float half, int n)
        {
            int arc = n * 11 / 36, edge = (n - 2 * arc) / 2;
            var pts = new List<Vector2>(n);
            for (int i = 0; i < arc; i++)   // right arc, bottom to top
            {
                float a = -Mathf.PI / 2 + Mathf.PI * (i + 0.5f) / arc;
                pts.Add(new Vector2(half + r * Mathf.Cos(a), r * Mathf.Sin(a)));
            }
            for (int i = 0; i < edge; i++)  // top edge, right to left
                pts.Add(new Vector2(Mathf.Lerp(half, -half, (i + 0.5f) / edge), r));
            for (int i = 0; i < arc; i++)   // left arc, top to bottom
            {
                float a = Mathf.PI / 2 + Mathf.PI * (i + 0.5f) / arc;
                pts.Add(new Vector2(-half + r * Mathf.Cos(a), r * Mathf.Sin(a)));
            }
            while (pts.Count < n)           // bottom edge, left to right
            {
                int k = pts.Count - 2 * arc - edge;
                pts.Add(new Vector2(Mathf.Lerp(-half, half, (k + 0.5f) / (n - 2 * arc - edge)), -r));
            }
            return pts;
        }

        // ---- domes ----

        /// <summary>
        /// A rounded, flattened dome over an outline: bottom rim at y = yOffset, apex at yOffset + height.
        /// Ring k scales the outline by r_k = cos(a)^power; <paramref name="soft"/> (optional) is blended in towards the top
        /// so lobed silhouettes stay lobed at the rim but smooth on top. <paramref name="firstDeg"/> starts the dome higher up
        /// (used for shells that only cover the top face).
        /// </summary>
        public static MeshData Dome(IReadOnlyList<Vector2> pts, float height, int rings, float power, float startDeg, float yOffset = 0f,
            bool cap = true, IReadOnlyList<Vector2>? soft = null, float? firstDeg = null, float[]? ringR = null)
        {
            int n = pts.Count;
            var md = new MeshData();
            float lo = Mathf.Sin(startDeg * Mathf.Deg2Rad);
            float a0 = firstDeg ?? startDeg;
            var grid = new Vector3[rings, n];
            for (int k = 0; k < rings; k++)
            {
                float a = a0 + (90f - a0) * k / (rings - 1);
                float r = a < 90f ? Mathf.Pow(Mathf.Cos(a * Mathf.Deg2Rad), power) : 0f;
                if (ringR != null) ringR[k] = r;
                float y = height * (Mathf.Sin(a * Mathf.Deg2Rad) - lo) / (1f - lo) + yOffset;
                float w = soft == null ? 0f : Mathf.Pow(k / (float)(rings - 1), 1.3f);
                for (int i = 0; i < n; i++)
                {
                    Vector2 q = soft == null ? pts[i] : Vector2.Lerp(pts[i], soft[i], w);
                    grid[k, i] = new Vector3(q.x * r, y, q.y * r);
                }
            }
            Vector3 center = new Vector3(0f, yOffset + height * 0.2f, 0f);
            var idx = new int[rings - 1, n];
            for (int k = 0; k < rings - 1; k++)
            {
                for (int i = 0; i < n; i++)
                {
                    Vector3 p = grid[k, i];
                    Vector3 tr = grid[k, (i + 1) % n] - grid[k, (i + n - 1) % n];
                    Vector3 tp = grid[Mathf.Min(k + 1, rings - 1), i] - grid[Mathf.Max(k - 1, 0), i];
                    Vector3 nrm = Vector3.Cross(tr, tp);
                    if (nrm.sqrMagnitude < 1e-12f) nrm = new Vector3(p.x, 0f, p.z);
                    nrm.Normalize();
                    if (Vector3.Dot(nrm, p - center) < 0f) nrm = -nrm;
                    idx[k, i] = md.Add(p, nrm, new Vector2(p.x + 0.5f, p.z + 0.5f));
                }
            }
            int apex = md.Add(new Vector3(0f, yOffset + height, 0f), Vector3.up, new Vector2(0.5f, 0.5f));
            for (int k = 0; k < rings - 2; k++)
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    md.Quad(idx[k, i], idx[k, j], idx[k + 1, j], idx[k + 1, i]);
                }
            for (int i = 0; i < n; i++) md.Tri(idx[rings - 2, i], idx[rings - 2, (i + 1) % n], apex);

            if (cap)
            {
                int c0 = md.Add(new Vector3(0f, yOffset, 0f), Vector3.down, new Vector2(0.5f, 0.5f));
                var rim = new int[n];
                for (int i = 0; i < n; i++)
                    rim[i] = md.Add(grid[0, i], Vector3.down, new Vector2(grid[0, i].x + 0.5f, grid[0, i].z + 0.5f));
                for (int i = 0; i < n; i++) md.Tri(c0, rim[i], rim[(i + 1) % n]);
            }
            return md;
        }

        const float Power = 0.55f, StartDeg = -18f;

        static List<Vector2>? SoftOutline(PebbleShape shape)
        {
            if (shape != PebbleShape.Scallop) return null;
            var pts = new List<Vector2>(OutlinePoints);
            for (int i = 0; i < OutlinePoints; i++)
            {
                float t = 2f * Mathf.PI * i / OutlinePoints;
                float r = 0.44f * (1f + 0.01f * Mathf.Cos(8f * t));
                pts.Add(new Vector2(r * Mathf.Cos(t), r * Mathf.Sin(t)));
            }
            return pts;
        }

        /// <summary>Height of the pebble surface above the tray at (x, z), so overlays can be draped over any silhouette.</summary>
        public sealed class Surface
        {
            const int Bins = 720;
            readonly float[] rho = new float[Bins + 1];
            readonly float height, lo, edgeR;

            public Surface(IReadOnlyList<Vector2> pts, float height)
            {
                this.height = height;
                lo = Mathf.Sin(StartDeg * Mathf.Deg2Rad);
                edgeR = Mathf.Pow(Mathf.Cos(StartDeg * Mathf.Deg2Rad), Power);
                for (int b = 0; b <= Bins; b++)
                {
                    float th = 2f * Mathf.PI * b / Bins;
                    Vector2 d = new Vector2(Mathf.Cos(th), Mathf.Sin(th));
                    float best = 0f;
                    for (int i = 0; i < pts.Count; i++)
                    {
                        Vector2 A = pts[i], B = pts[(i + 1) % pts.Count], e = B - A;
                        float den = d.x * e.y - d.y * e.x;
                        if (Mathf.Abs(den) < 1e-9f) continue;
                        float t = (A.x * e.y - A.y * e.x) / den;
                        float u = (A.x * d.y - A.y * d.x) / den;
                        if (t > 0f && u >= 0f && u <= 1f) best = Mathf.Max(best, t);
                    }
                    rho[b] = best;
                }
            }

            /// <summary>Distance from the centre to the outline along an angle (radians, from +X towards +Z).</summary>
            public float Rho(float angle)
            {
                float f = Mathf.Repeat(angle, 2f * Mathf.PI) / (2f * Mathf.PI) * Bins;
                int i = Mathf.Min((int)f, Bins - 1);
                return Mathf.Lerp(rho[i], rho[i + 1], f - i);
            }

            public float Height(float x, float z)
            {
                float d = Mathf.Sqrt(x * x + z * z);
                if (d < 1e-6f) return height;
                float s = Mathf.Min(d / Mathf.Max(1e-4f, Rho(Mathf.Atan2(z, x))), edgeR);
                float a = Mathf.Acos(Mathf.Pow(s, 1f / Power));
                return height * (Mathf.Sin(a) - lo) / (1f - lo);
            }
        }

        static void Drape(MeshData md, Surface surf, float lift)
        {
            for (int i = 0; i < md.V.Count; i++)
            {
                Vector3 p = md.V[i];
                md.V[i] = new Vector3(p.x, p.y + surf.Height(p.x, p.z) + lift, p.z);
            }
        }

        public static Mesh Pebble(PebbleShape shape, float heightRatio) =>
            Dome(Outline(shape), heightRatio, 14, Power, StartDeg, 0f, true, SoftOutline(shape)).ToMesh("Pebble_" + shape);

        // ---- flat rings following an outline (selection halo) ----

        public static Mesh HaloFor(IReadOnlyList<Vector2> pts, string name)
        {
            // scale steps from the outline centre: hidden under the pebble, solid band, soft outer edge
            // white band with a soft warm-sand edge: the edge keeps the ring readable on the pale driftwood
            float[] s = { 1.00f, 1.05f, 1.13f, 1.19f, 1.27f };
            byte[] a = { 90, 255, 255, 170, 0 };
            Color32[] col = { new Color32(255, 255, 255, 0), new Color32(255, 255, 255, 0), new Color32(255, 255, 255, 0), new Color32(176, 148, 104, 0), new Color32(176, 148, 104, 0) };
            var md = new MeshData();
            int n = pts.Count;
            var rows = new int[s.Length][];
            for (int k = 0; k < s.Length; k++)
            {
                rows[k] = new int[n];
                for (int i = 0; i < n; i++)
                    rows[k][i] = md.Add(new Vector3(pts[i].x * s[k], 0f, pts[i].y * s[k]), Vector3.up, Vector2.zero, new Color32(col[k].r, col[k].g, col[k].b, a[k]));
            }
            for (int k = 0; k < s.Length - 1; k++)
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    md.Quad(rows[k][i], rows[k][j], rows[k + 1][j], rows[k + 1][i]);
                }
            return md.ToMesh(name);
        }

        public static Mesh HaloCircle()
        {
            var pts = new List<Vector2>();
            for (int i = 0; i < OutlinePoints; i++)
            {
                float t = 2f * Mathf.PI * i / OutlinePoints;
                pts.Add(new Vector2(0.45f * Mathf.Cos(t), 0.45f * Mathf.Sin(t)));
            }
            return HaloFor(pts, "Halo_Circle");
        }

        /// <summary>Ripple ring of radius 0.5 with soft inner and outer edges (scale by 2 x radius in cells).</summary>
        public static Mesh Ripple()
        {
            // a pale crest with a darker sand trough outside it, so the ring reads on pale driftwood and on the dark floor of a hole
            float[] r = { 0.33f, 0.40f, 0.45f, 0.50f };
            byte[] a = { 0, 255, 200, 0 };
            Color32[] col = { new Color32(255, 255, 255, 0), new Color32(255, 255, 255, 0), new Color32(190, 160, 115, 0), new Color32(190, 160, 115, 0) };
            var md = new MeshData();
            const int n = 64;
            var rows = new int[r.Length][];
            for (int k = 0; k < r.Length; k++)
            {
                rows[k] = new int[n];
                for (int i = 0; i < n; i++)
                {
                    float t = 2f * Mathf.PI * i / n;
                    rows[k][i] = md.Add(new Vector3(r[k] * Mathf.Cos(t), 0f, r[k] * Mathf.Sin(t)), Vector3.up, Vector2.zero, new Color32(col[k].r, col[k].g, col[k].b, a[k]));
                }
            }
            for (int k = 0; k < r.Length - 1; k++)
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    md.Quad(rows[k][i], rows[k][j], rows[k + 1][j], rows[k + 1][i]);
                }
            return md.ToMesh("Ripple");
        }

        /// <summary>
        /// A unit XZ strip for line washes: alpha ramps to zero along both ends and both sides, so a wave that sweeps a row or
        /// a column has soft edges (the placement scales it to length x width).
        /// </summary>
        public static Mesh Wash()
        {
            float[] x = { -0.5f, -0.42f, 0.42f, 0.5f };
            float[] z = { -0.5f, -0.26f, 0.26f, 0.5f };
            byte[] ax = { 0, 255, 255, 0 };
            byte[] az = { 0, 255, 255, 0 };
            var md = new MeshData();
            var id = new int[4, 4];
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 4; j++)
                {
                    byte al = (byte)(ax[i] * az[j] / 255);
                    id[i, j] = md.Add(new Vector3(x[i], 0f, z[j]), Vector3.up, new Vector2(i / 3f, j / 3f), new Color32(255, 255, 255, al));
                }
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    md.Quad(id[i, j], id[i, j + 1], id[i + 1, j + 1], id[i + 1, j]);
            return md.ToMesh("Wash");
        }

        /// <summary>Unit XZ quad, normal up, UV 0..1, centred at the origin (blob shadow, backdrop).</summary>
        public static Mesh Quad()
        {
            var md = new MeshData();
            int a = md.Add(new Vector3(-0.5f, 0f, -0.5f), Vector3.up, new Vector2(0, 0));
            int b = md.Add(new Vector3(-0.5f, 0f, 0.5f), Vector3.up, new Vector2(0, 1));
            int c = md.Add(new Vector3(0.5f, 0f, 0.5f), Vector3.up, new Vector2(1, 1));
            int d = md.Add(new Vector3(0.5f, 0f, -0.5f), Vector3.up, new Vector2(1, 0));
            md.Quad(a, b, c, d);
            return md.ToMesh("Quad");
        }

        /// <summary>Flat arrow pointing along +X from the origin, length 1, width 0.34, soft rounded corners.</summary>
        public static Mesh Arrow()
        {
            const float length = 1f, width = 0.34f;
            float sh = width * 0.28f, hl = length * 0.36f;
            var pts = new[]
            {
                new Vector2(0f, -sh), new Vector2(length - hl, -sh), new Vector2(length - hl, -width / 2),
                new Vector2(length, 0f), new Vector2(length - hl, width / 2), new Vector2(length - hl, sh), new Vector2(0f, sh),
            };
            var md = new MeshData();
            var ids = new int[pts.Length];
            for (int i = 0; i < pts.Length; i++) ids[i] = md.Add(new Vector3(pts[i].x, 0f, pts[i].y), Vector3.up, pts[i]);
            for (int i = 1; i < ids.Length - 1; i++) md.Tri(ids[0], ids[i], ids[i + 1]);
            return md.ToMesh("Arrow");
        }

        // ---- special overlays: draped over each silhouette so they sit on the surface of any pebble ----
        // Pebble units; parented to the pose (never yawed), so a Line's axis is exact.

        /// <summary>White quartz vein: a low domed capsule along X (or Z), inlaid in the pebble top.</summary>
        public static Mesh Vein(PieceStyle st, PebbleShape shape, float pebbleHeight, bool vertical)
        {
            var pts = Outline(shape);
            var surf = new Surface(pts, pebbleHeight);
            float rho = surf.Rho(vertical ? Mathf.PI / 2f : 0f);
            float r = st.specials.veinThickness * 0.5f;
            float length = Mathf.Min(st.specials.veinLength, rho * 1.6f);
            var outline = Capsule(r, Mathf.Max(0.01f, length * 0.5f - r), 48);
            if (vertical) for (int i = 0; i < outline.Count; i++) outline[i] = new Vector2(outline[i].y, outline[i].x);
            var md = Dome(outline, st.specials.veinHeight, 6, 0.6f, -10f, 0f, false);
            Drape(md, surf, -0.012f);
            return md.ToMesh("Vein" + (vertical ? "V_" : "H_") + shape);
        }

        /// <summary>Worn rim of the hag-stone hole: a torus lying flat, half sunk into the pebble top.</summary>
        public static Mesh HoleRim(PieceStyle st, PebbleShape shape, float pebbleHeight)
        {
            var surf = new Surface(Outline(shape), pebbleHeight);
            float R = st.specials.bombRimRadius, r = st.specials.bombRimTube;
            var md = new MeshData();
            const int major = 40, minor = 10;
            var g = new int[major, minor];
            for (int i = 0; i < major; i++)
            {
                float u = 2f * Mathf.PI * i / major;
                Vector3 dir = new Vector3(Mathf.Cos(u), 0f, Mathf.Sin(u));
                for (int j = 0; j < minor; j++)
                {
                    float v = 2f * Mathf.PI * j / minor;
                    Vector3 nrm = dir * Mathf.Cos(v) + Vector3.up * Mathf.Sin(v);
                    g[i, j] = md.Add(dir * R + nrm * r, nrm, Vector2.zero);
                }
            }
            for (int i = 0; i < major; i++)
                for (int j = 0; j < minor; j++)
                    md.Quad(g[i, j], g[(i + 1) % major, j], g[(i + 1) % major, (j + 1) % minor], g[i, (j + 1) % minor]);
            Drape(md, surf, -r * 0.2f);
            return md.ToMesh("HoleRim_" + shape);
        }

        /// <summary>Dark centre of the hole: a disc following the pebble surface.</summary>
        public static Mesh HoleDisc(PieceStyle st, PebbleShape shape, float pebbleHeight)
        {
            var surf = new Surface(Outline(shape), pebbleHeight);
            var md = new MeshData();
            const int n = 40, rings = 3;
            int c = md.Add(Vector3.zero, Vector3.up, new Vector2(0.5f, 0.5f));
            var prev = new[] { c };
            for (int k = 1; k <= rings; k++)
            {
                var cur = new int[n];
                float rad = st.specials.bombHoleRadius * k / rings;
                for (int i = 0; i < n; i++)
                {
                    float t = 2f * Mathf.PI * i / n;
                    cur[i] = md.Add(new Vector3(rad * Mathf.Cos(t), 0f, rad * Mathf.Sin(t)), Vector3.up, new Vector2(0.5f, 0.5f));
                }
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    if (k == 1) md.Tri(c, cur[i], cur[j]);
                    else md.Quad(prev[i], prev[j], cur[j], cur[i]);
                }
                prev = cur;
            }
            Drape(md, surf, 0.004f);
            return md.ToMesh("HoleDisc_" + shape);
        }

        /// <summary>
        /// Banded agate: a thin shell over the pebble top whose bands follow the silhouette (UV.x = 0 at the rim to 1 at the apex;
        /// the colour of each band comes from the band ramp texture). Works on any outline because it is the pebble's own dome.
        /// </summary>
        public static Mesh Agate(PieceStyle st, PebbleShape shape, float pebbleHeight)
        {
            const int rings = 22;
            float rMax = Mathf.Clamp(st.specials.agateExtent, 0.2f, 0.98f);
            float first = Mathf.Acos(Mathf.Pow(rMax, 1f / Power)) * Mathf.Rad2Deg;
            var ringR = new float[rings];
            var md = Dome(Outline(shape), pebbleHeight, rings, Power, StartDeg, 0f, false, SoftOutline(shape), first, ringR);
            // vertices were added ring by ring (n per ring) followed by the apex vertex
            int n = OutlinePoints;
            for (int i = 0; i < md.V.Count; i++)
            {
                int k = Mathf.Min(i / n, rings - 1);
                float u = i == md.V.Count - 1 ? 1f : 1f - ringR[k] / rMax;
                md.UV[i] = new Vector2(Mathf.Clamp01(u), 0.5f);
                md.V[i] += md.N[i] * st.specials.shellOffset;
            }
            return md.ToMesh("Agate_" + shape);
        }
    }
}
