using System.Collections.Generic;
using UnityEngine;

namespace Cascade.Editor
{
    /// <summary>Small indexed-triangle accumulator for the procedural meshes. Triangles are wound to match the vertex normals.</summary>
    sealed class MeshData
    {
        public readonly List<Vector3> V = new List<Vector3>();
        public readonly List<Vector3> N = new List<Vector3>();
        public readonly List<Vector2> UV = new List<Vector2>();
        public readonly List<Color32> C = new List<Color32>();
        public readonly List<int> T = new List<int>();

        public int Add(Vector3 p, Vector3 n, Vector2 uv, Color32? c = null)
        {
            V.Add(p); N.Add(n); UV.Add(uv); C.Add(c ?? new Color32(255, 255, 255, 255));
            return V.Count - 1;
        }

        public void Tri(int a, int b, int c)
        {
            Vector3 cr = Vector3.Cross(V[b] - V[a], V[c] - V[a]);
            if (cr.sqrMagnitude < 1e-14f) return;
            if (Vector3.Dot(cr, N[a] + N[b] + N[c]) < 0f) { int t = b; b = c; c = t; }
            T.Add(a); T.Add(b); T.Add(c);
        }

        public void Quad(int a, int b, int c, int d) { Tri(a, b, c); Tri(a, c, d); }

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name };
            if (V.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(V);
            m.SetNormals(N);
            m.SetUVs(0, UV);
            m.SetColors(C);
            m.SetTriangles(T, 0);
            m.RecalculateBounds();
            return m;
        }
    }
}
