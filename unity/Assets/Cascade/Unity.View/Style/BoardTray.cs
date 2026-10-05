using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cascade.Unity.View
{
    /// <summary>
    /// The driftwood tray under a board. Rebuild with <see cref="Build"/> whenever the level changes; the cell
    /// set may be any shape (holes simply have no tray, pieces sit loose, there are no sockets).
    /// Place this object at the board origin: cell (x, y) is at <see cref="CellToLocal"/>.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class BoardTray : MonoBehaviour
    {
        readonly TrayMeshBuilder builder = new TrayMeshBuilder();
        Mesh? mesh;
        MeshFilter? filter;
        MeshRenderer? rend;
        Func<int, int, bool>? presence;

        public int Cols { get; private set; }
        public int Rows { get; private set; }
        /// <summary>Local-space bounds of the tray including the lip, for camera fitting.</summary>
        public Bounds LocalBounds { get; private set; }

        /// <summary>World-convention position of cell (x, y): row 0 is the top row, +Z is up on screen.</summary>
        public static Vector3 CellToLocal(int cols, int rows, int x, int y) =>
            new Vector3(x - (cols - 1) * 0.5f, 0f, -(y - (rows - 1) * 0.5f));

        public bool HasCell(int x, int y) => presence != null && x >= 0 && y >= 0 && x < Cols && y < Rows && presence(x, y);

        /// <param name="present">Row-major cell presence, index = x + y * cols; null means every cell.</param>
        public void Build(PieceStyle style, int cols, int rows, bool[]? present)
        {
            if (present == null) Build(style, cols, rows, (x, y) => true);
            else Build(style, cols, rows, (x, y) => present[x + y * cols]);
        }

        public void Build(PieceStyle style, int cols, int rows, Func<int, int, bool> present)
        {
            filter = filter != null ? filter : GetComponent<MeshFilter>();
            rend = rend != null ? rend : GetComponent<MeshRenderer>();
            if (mesh == null) { mesh = new Mesh { name = "Tray", hideFlags = HideFlags.HideAndDontSave }; mesh.MarkDynamic(); }
            Cols = cols; Rows = rows; presence = present;
            bool floor = builder.Build(mesh, cols, rows, present, style, out var bounds);
            LocalBounds = bounds;
            filter.sharedMesh = mesh;
            rend.shadowCastingMode = ShadowCastingMode.Off;
            rend.receiveShadows = false;
            var mats = style.Materials;
            rend.sharedMaterials = floor
                ? new[] { mats.Tray(), mats.TrayHole() }
                : new[] { mats.Tray() };
        }

        /// <summary>Rebuilds the tray of the current cells (the style's tray dials changed).</summary>
        public void Rebuild(PieceStyle style)
        {
            if (presence != null) Build(style, Cols, Rows, presence);
        }

        void OnDestroy()
        {
            if (mesh == null) return;
            if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh);
        }
    }
}
