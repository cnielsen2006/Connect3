using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cascade.Unity.View
{
    /// <summary>
    /// Builds the driftwood tray mesh for an arbitrary set of cells: flat top quads, a rounded lip swept along
    /// every boundary loop (outer rim and the rim around holes), and a dark floor for enclosed holes.
    /// World convention: cell (x, y), row 0 on top: X = x - (cols-1)/2, Z = -(y - (rows-1)/2), Y = 0 at the surface.
    /// Reusable: internal buffers are kept, so rebuilding for a new level does not allocate much.
    /// </summary>
    public sealed class TrayMeshBuilder
    {
        readonly List<Vector3> pos = new List<Vector3>();
        readonly List<Vector3> nrm = new List<Vector3>();
        readonly List<Vector2> uv = new List<Vector2>();
        readonly List<Color> col = new List<Color>();
        readonly List<int> triTray = new List<int>();
        readonly List<int> triFloor = new List<int>();
        readonly List<Edge> edges = new List<Edge>();
        readonly Dictionary<long, List<int>> starts = new Dictionary<long, List<int>>();
        readonly List<int> loop = new List<int>();

        struct Edge { public Vector2 a, b; public bool used; }
        struct Row { public Vector2 p, off, nrm; public float scale; }
        struct ProfilePoint { public float d, y, nd, ny; public Color color; }

        ProfilePoint[] profile = Array.Empty<ProfilePoint>();

        public static Vector2 CellCenter(int cols, int rows, int x, int y) =>
            new Vector2(x - (cols - 1) * 0.5f, -(y - (rows - 1) * 0.5f));

        /// <summary>Fills <paramref name="mesh"/>; returns whether the mesh has a hole-floor submesh (submesh 1).</summary>
        public bool Build(Mesh mesh, int cols, int rows, Func<int, int, bool> has, PieceStyle style, out Bounds bounds)
        {
            var t = style.tray;
            pos.Clear(); nrm.Clear(); uv.Clear(); col.Clear(); triTray.Clear(); triFloor.Clear(); edges.Clear(); starts.Clear();

            bool Present(int x, int y) => x >= 0 && y >= 0 && x < cols && y < rows && has(x, y);

            // ---- top quads and boundary edges ----
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < cols; x++)
                {
                    if (!Present(x, y)) continue;
                    Vector2 c = CellCenter(cols, rows, x, y);
                    minX = Mathf.Min(minX, c.x - 0.5f); maxX = Mathf.Max(maxX, c.x + 0.5f);
                    minZ = Mathf.Min(minZ, c.y - 0.5f); maxZ = Mathf.Max(maxZ, c.y + 0.5f);
                    Quad(triTray, c, 0f, Color.white);
                    if (!Present(x, y - 1)) AddEdge(new Vector2(c.x - .5f, c.y + .5f), new Vector2(c.x + .5f, c.y + .5f));
                    if (!Present(x + 1, y)) AddEdge(new Vector2(c.x + .5f, c.y + .5f), new Vector2(c.x + .5f, c.y - .5f));
                    if (!Present(x, y + 1)) AddEdge(new Vector2(c.x + .5f, c.y - .5f), new Vector2(c.x - .5f, c.y - .5f));
                    if (!Present(x - 1, y)) AddEdge(new Vector2(c.x - .5f, c.y - .5f), new Vector2(c.x - .5f, c.y + .5f));
                }
            }

            // ---- lip swept along every boundary loop ----
            BuildProfile(style);
            for (int i = 0; i < edges.Count; i++)
            {
                if (edges[i].used) continue;
                TraceLoop(i);
                SweepLoop(t.cornerSegments);
            }

            // ---- floors of enclosed holes ----
            bool floor = BuildFloors(cols, rows, Present, t.depth);

            if (triTray.Count == 0)
            {
                mesh.Clear();
                bounds = new Bounds(Vector3.zero, Vector3.zero);
                return false;
            }

            mesh.Clear();
            mesh.indexFormat = pos.Count > 60000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(pos);
            mesh.SetNormals(nrm);
            mesh.SetUVs(0, uv);
            mesh.SetColors(col);
            mesh.subMeshCount = floor ? 2 : 1;
            mesh.SetTriangles(triTray, 0, false);
            if (floor) mesh.SetTriangles(triFloor, 1, false);
            bounds = new Bounds();
            bounds.SetMinMax(new Vector3(minX - t.lip, -t.depth, minZ - t.lip),
                new Vector3(maxX + t.lip, t.lipHeight + style.material.heightRatio, maxZ + t.lip));
            mesh.bounds = bounds;
            return floor;
        }

        // ---- geometry helpers ----

        void Quad(List<int> tris, Vector2 c, float y, Color color)
        {
            int b = pos.Count;
            for (int k = 0; k < 4; k++)
            {
                float sx = (k == 0 || k == 3) ? -0.5f : 0.5f;
                float sz = (k < 2) ? -0.5f : 0.5f;
                pos.Add(new Vector3(c.x + sx, y, c.y + sz));
                nrm.Add(Vector3.up);
                uv.Add(new Vector2(c.x + sx, c.y + sz));
                col.Add(color);
            }
            Tri(tris, b, b + 1, b + 2);
            Tri(tris, b, b + 2, b + 3);
        }

        void Tri(List<int> tris, int a, int b, int c)
        {
            Vector3 cr = Vector3.Cross(pos[b] - pos[a], pos[c] - pos[a]);
            if (cr.sqrMagnitude < 1e-12f) return;
            if (Vector3.Dot(cr, nrm[a] + nrm[b] + nrm[c]) < 0f) { int tmp = b; b = c; c = tmp; }
            tris.Add(a); tris.Add(b); tris.Add(c);
        }

        void AddEdge(Vector2 a, Vector2 b)
        {
            int idx = edges.Count;
            edges.Add(new Edge { a = a, b = b });
            long k = Key(a);
            if (!starts.TryGetValue(k, out var l)) starts[k] = l = new List<int>(2);
            l.Add(idx);
        }

        static long Key(Vector2 p) => ((long)Mathf.RoundToInt(p.x * 2f) << 32) ^ (uint)Mathf.RoundToInt(p.y * 2f);

        static Vector2 Dir(in Edge e) => (e.b - e.a).normalized;
        static Vector2 Left(Vector2 d) => new Vector2(-d.y, d.x);
        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        void TraceLoop(int first)
        {
            loop.Clear();
            int cur = first;
            while (true)
            {
                var e = edges[cur];
                e.used = true;
                edges[cur] = e;
                loop.Add(cur);
                long endKey = Key(e.b);
                if (Key(edges[first].a) == endKey) break;
                int next = -1;
                float best = float.MaxValue;
                if (starts.TryGetValue(endKey, out var cands))
                {
                    foreach (int c in cands)
                    {
                        if (edges[c].used) continue;
                        float turn = Cross(Dir(e), Dir(edges[c])); // most negative = tightest right turn
                        if (turn < best) { best = turn; next = c; }
                    }
                }
                if (next < 0) break;
                cur = next;
            }
        }

        void BuildProfile(PieceStyle style)
        {
            var t = style.tray;
            float W = t.lip, H = t.lipHeight, D = t.depth, R = Mathf.Min(t.edgeRound, Mathf.Min(W, H) * 0.9f);
            float F = Mathf.Min(t.innerFillet, W - R);
            var pts = new List<Vector2>();
            const int fillet = 4, round = 5;
            for (int k = 0; k <= fillet; k++)
            {
                float a = k / (float)fillet * Mathf.PI * 0.5f;
                pts.Add(new Vector2(F * (1f - Mathf.Cos(a)), H * Mathf.Sin(a)));
            }
            for (int k = 0; k <= round; k++)
            {
                float a = k / (float)round * Mathf.PI * 0.5f;
                pts.Add(new Vector2(W - R + R * Mathf.Sin(a), H - R + R * Mathf.Cos(a)));
            }
            pts.Add(new Vector2(W, -D * 0.5f));
            pts.Add(new Vector2(W - 0.01f, -D));
            // drop consecutive duplicates
            for (int i = pts.Count - 1; i > 0; i--)
                if ((pts[i] - pts[i - 1]).sqrMagnitude < 1e-8f) pts.RemoveAt(i);

            Color lipRatio = new Color(
                Ratio(style.palette.trayLip.r, style.palette.tray.r),
                Ratio(style.palette.trayLip.g, style.palette.tray.g),
                Ratio(style.palette.trayLip.b, style.palette.tray.b), 1f);
            profile = new ProfilePoint[pts.Count];
            for (int i = 0; i < pts.Count; i++)
            {
                Vector2 tan = (pts[Mathf.Min(i + 1, pts.Count - 1)] - pts[Mathf.Max(i - 1, 0)]).normalized;
                Vector2 n = new Vector2(-tan.y, tan.x); // profile normal (nd, ny)
                float lipK = Mathf.Clamp01(pts[i].y / Mathf.Max(1e-4f, H * 0.8f));
                Color c = Color.Lerp(Color.white, lipRatio, Mathf.Max(lipK, pts[i].x > F ? 1f : 0f));
                if (pts[i].y < 0f)
                {
                    float s = Mathf.Lerp(1f, t.edgeShade, Mathf.Clamp01(-pts[i].y / D));
                    c = new Color(lipRatio.r * s, lipRatio.g * s, lipRatio.b * s, 1f);
                }
                profile[i] = new ProfilePoint { d = pts[i].x, y = pts[i].y, nd = n.x, ny = n.y, color = c };
            }
        }

        static float Ratio(byte a, byte b) => Mathf.Clamp(a / Mathf.Max(1f, (float)b), 0.2f, 1.6f);

        int MakeRow(in Row r)
        {
            int b = pos.Count;
            foreach (var pp in profile)
            {
                Vector2 xz = r.p + r.off * (r.scale * pp.d);
                pos.Add(new Vector3(xz.x, pp.y, xz.y));
                nrm.Add(new Vector3(r.nrm.x * pp.nd, pp.ny, r.nrm.y * pp.nd).normalized);
                uv.Add(xz);
                col.Add(pp.color);
            }
            return b;
        }

        void Strip(int r1, int r2)
        {
            int m = profile.Length;
            for (int j = 0; j < m - 1; j++)
            {
                Tri(triTray, r1 + j, r2 + j, r2 + j + 1);
                Tri(triTray, r1 + j, r2 + j + 1, r1 + j + 1);
            }
        }

        void SweepLoop(int cornerSegments)
        {
            int m = loop.Count;
            int prevEnd = -1;   // row index of the previous edge's end (for chaining)
            int firstStart = -1;
            for (int i = 0; i < m; i++)
            {
                var e = edges[loop[i]];
                var ePrev = edges[loop[(i + m - 1) % m]];
                var eNext = edges[loop[(i + 1) % m]];
                Vector2 d = Dir(e), dp = Dir(ePrev), dn = Dir(eNext);
                Vector2 n = Left(d);

                float turnStart = Cross(dp, d);
                float turnEnd = Cross(d, dn);

                Row start = new Row { p = e.a, off = n, nrm = n, scale = 1f };
                if (turnStart > 0.001f) start = Miter(e.a, n, Left(dp));
                Row end = new Row { p = e.b, off = n, nrm = n, scale = 1f };
                if (turnEnd > 0.001f) end = Miter(e.b, n, Left(dn));

                int rs = MakeRow(start);
                int re = MakeRow(end);
                Strip(rs, re);
                if (i == 0) firstStart = rs;

                // connect to the previous edge's end (same corner) unless it was a concave jump
                if (prevEnd >= 0 && turnStart <= 0.001f)
                {
                    // convex corners: arc rows; straight: direct
                    if (turnStart < -0.001f)
                    {
                        int prevRow = prevEnd;
                        Vector2 nPrev = Left(dp);
                        for (int k = 1; k < cornerSegments; k++)
                        {
                            float ang = -90f * k / cornerSegments;
                            Vector2 dir = Rot(nPrev, ang);
                            int rr = MakeRow(new Row { p = e.a, off = dir, nrm = dir, scale = 1f });
                            Strip(prevRow, rr);
                            prevRow = rr;
                        }
                        Strip(prevRow, rs);
                    }
                    else Strip(prevEnd, rs);
                }
                prevEnd = re;
            }
            // close the loop: last end -> first start
            {
                var e0 = edges[loop[0]];
                var eL = edges[loop[m - 1]];
                Vector2 d0 = Dir(e0), dL = Dir(eL);
                float turn = Cross(dL, d0);
                if (turn <= 0.001f)
                {
                    if (turn < -0.001f)
                    {
                        int prevRow = prevEnd;
                        Vector2 nPrev = Left(dL);
                        for (int k = 1; k < cornerSegments; k++)
                        {
                            Vector2 dir = Rot(nPrev, -90f * k / cornerSegments);
                            int rr = MakeRow(new Row { p = e0.a, off = dir, nrm = dir, scale = 1f });
                            Strip(prevRow, rr);
                            prevRow = rr;
                        }
                        Strip(prevRow, firstStart);
                    }
                    else Strip(prevEnd, firstStart);
                }
            }
        }

        static Row Miter(Vector2 p, Vector2 own, Vector2 other)
        {
            Vector2 m = (own + other).normalized;
            float s = 1f / Mathf.Max(0.3f, Vector2.Dot(m, own));
            return new Row { p = p, off = m, scale = s, nrm = own };
        }

        static Vector2 Rot(Vector2 v, float deg)
        {
            float r = deg * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        bool BuildFloors(int cols, int rows, Func<int, int, bool> present, float depth)
        {
            // Absent cells not reachable from outside the grid (4-neighbourhood) are enclosed holes.
            int w = cols + 2, h = rows + 2;
            var outside = new bool[w * h];
            var stack = new Stack<int>();
            stack.Push(0);
            outside[0] = true;
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                int x = i % w, y = i / w;
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = y + (k == 2 ? 1 : k == 3 ? -1 : 0);
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    int j = ny * w + nx;
                    if (outside[j]) continue;
                    if (present(nx - 1, ny - 1)) continue;
                    outside[j] = true;
                    stack.Push(j);
                }
            }
            bool any = false;
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < cols; x++)
                    if (!present(x, y) && !outside[(y + 1) * w + x + 1])
                    {
                        Quad(triFloor, CellCenter(cols, rows, x, y), -depth, Color.white);
                        any = true;
                    }
            return any;
        }
    }
}
