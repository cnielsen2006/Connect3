using System.Collections.Generic;
using Cascade.Core;
using Cascade.Presentation;
using UnityEngine;
using PresentationLook = Cascade.Presentation.PieceLook;

namespace Cascade.Unity.View
{
    /// <summary>
    /// The 3D scene of one board: the tray, one <see cref="PieceView"/> per tile (a tile id to view map) and the swap
    /// arrow. It draws; it never decides and never changes logic state (TRD 12.1). The <see cref="TimelinePlayer"/>
    /// moves pieces by timeline; when a timeline ends, <see cref="Sync"/> snaps everything to the final board so an
    /// animation bug can never leave the view out of step with the logic (TRD 12.3).
    /// Place the object at the world origin: board space (X, Z) is world (X, 0, Z).
    /// </summary>
    public sealed class BoardView : MonoBehaviour
    {
        PieceStyle? style;
        PiecePool? pool;
        BoardTray? tray;
        Transform? piecesRoot, poolRoot, fxRoot;
        FxView? arrow;
        readonly Dictionary<int, PieceView> pieces = new Dictionary<int, PieceView>(); // lookup only
        readonly HashSet<int> seen = new HashSet<int>();
        readonly List<int> stale = new List<int>();
        PieceVariationParams variation = PieceVariationParams.Default;
        int hoverTile = -1;
        int agateColors = -1;

        public PieceStyle Style => style ?? throw new System.InvalidOperationException("BoardView.Init was not called");

        /// <summary>The grid of the board being shown (null before <see cref="Show"/>).</summary>
        public BoardGeometry? Geometry { get; private set; }

        public int PieceCount => pieces.Count;

        /// <summary>Parent of effects that live in the world of the board (ripples, washes).</summary>
        public Transform FxRoot => fxRoot != null ? fxRoot : transform;

        /// <summary>The tile under the pointer (the lift of <c>MaterialParams.hoverHighlight</c>), or -1.</summary>
        public int HoverTile => hoverTile;

        public void Init(PieceStyle pieceStyle)
        {
            style = pieceStyle;
            if (piecesRoot == null)
            {
                piecesRoot = Child("Pieces");
                poolRoot = Child("Pool");
                poolRoot.gameObject.SetActive(false);
                fxRoot = Child("Fx");
                var trayGo = new GameObject("Tray");
                trayGo.transform.SetParent(transform, false);
                tray = trayGo.AddComponent<BoardTray>();
            }
            pool = new PiecePool(pieceStyle, poolRoot!);
        }

        Transform Child(string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(transform, false);
            return t;
        }

        /// <summary>Shows a new level: rebuilds the tray, drops every piece and creates one for every tile of the board.</summary>
        public void Show(Board board, PieceVariationParams variationParams)
        {
            variation = variationParams;
            Clear();
            Geometry = BoardGeometry.FromTopology(board.Topology);
            agateColors = board.Colors;
            var present = new bool[Geometry.Cols * Geometry.Rows];
            foreach (var c in Geometry.Cells)
            {
                var (x, y) = Geometry.LogicalOf(c);
                present[x + y * Geometry.Cols] = true;
            }
            tray!.Build(Style, Geometry.Cols, Geometry.Rows, present);
            Sync(board);
        }

        /// <summary>The look parameters changed (a <c>[pieces]</c> dial): applies to pieces created from now on.</summary>
        public void SetVariation(PieceVariationParams variationParams) => variation = variationParams;

        /// <summary>Releases every piece and the arrow (a level ends).</summary>
        public void Clear()
        {
            foreach (var kv in pieces) pool!.Release(kv.Value);
            pieces.Clear();
            hoverTile = -1;
            HideArrow();
            for (int i = FxRoot.childCount - 1; i >= 0; i--) Destroy(FxRoot.GetChild(i).gameObject);
            arrow = null;
        }

        /// <summary>
        /// Puts the scene exactly on <paramref name="board"/>: creates missing pieces, removes extra ones, snaps every piece
        /// to its cell and resets its pose, color and special. The selection lift (<c>SelectT</c>) is the player's own
        /// state and survives; the hover lift is re-applied.
        /// </summary>
        public void Sync(Board board)
        {
            if (Geometry == null) return;
            seen.Clear();
            foreach (var (cell, tile) in board.Occupied())
            {
                seen.Add(tile.Id);
                var pos = Geometry.CellPosition(cell);
                var special = PieceMapping.ToView(tile.Special);
                if (!pieces.TryGetValue(tile.Id, out var view))
                {
                    Spawn(tile.Id, tile.Color, pos, tile.Special);
                    view = pieces[tile.Id];
                }
                else
                {
                    if (view.ColorIndex != tile.Color) view.SetColor(tile.Color);
                    if (view.Special != special) view.SetSpecial(special, agateColors);
                }
                float sel = view.SelectT;
                view.ResetPose();
                view.SelectT = sel;
                view.Hover = tile.Id == hoverTile ? Style.material.hoverHighlight : 0f;
                view.BasePosition = ToLocal(pos);
                view.ApplyPose();
            }
            stale.Clear();
            foreach (var kv in pieces) if (!seen.Contains(kv.Key)) stale.Add(kv.Key);
            foreach (int id in stale) Remove(id);
        }

        /// <summary>Creates the piece of a tile at a board-space position (a spawn is above the board).</summary>
        public PieceView Spawn(int tileId, int color, Float2 position, Special? special)
        {
            if (pieces.TryGetValue(tileId, out var existing)) return existing;
            var look = PieceMapping.ToView(PieceVariation.For(tileId, variation));
            var view = pool!.Get(piecesRoot!, color, PieceMapping.ToView(special), look, agateColors);
            view.name = "Piece " + tileId;
            view.BasePosition = ToLocal(position);
            view.ApplyPose();
            pieces[tileId] = view;
            return view;
        }

        public void Remove(int tileId)
        {
            if (!pieces.TryGetValue(tileId, out var view)) return;
            pieces.Remove(tileId);
            if (hoverTile == tileId) hoverTile = -1;
            pool!.Release(view);
        }

        public PieceView? Get(int tileId) => pieces.TryGetValue(tileId, out var v) ? v : null;

        public bool Has(int tileId) => pieces.ContainsKey(tileId);

        /// <summary>Every tile id that has a piece (for tests and tools).</summary>
        public IEnumerable<int> TileIds => pieces.Keys;

        /// <summary>
        /// Compares the scene with a board: same tiles, each at its cell with its color and special, no extra pieces.
        /// Returns null when they agree, otherwise a description of the first difference (for tests and the self-test).
        /// </summary>
        public string? Verify(Board board)
        {
            if (Geometry == null) return "no board is shown";
            int expected = 0;
            foreach (var (cell, tile) in board.Occupied())
            {
                expected++;
                var view = Get(tile.Id);
                if (view == null) return "tile " + tile.Id + " at cell " + cell + " has no piece";
                var want = ToLocal(Geometry.CellPosition(cell));
                if ((view.BasePosition - want).sqrMagnitude > 1e-6f)
                    return "tile " + tile.Id + " is at " + view.BasePosition + ", expected " + want + " (cell " + cell + ")";
                if (view.ColorIndex != tile.Color) return "tile " + tile.Id + " has color " + view.ColorIndex + ", expected " + tile.Color;
                if (view.Special != PieceMapping.ToView(tile.Special))
                    return "tile " + tile.Id + " has special " + view.Special + ", expected " + PieceMapping.ToView(tile.Special);
            }
            if (pieces.Count != expected) return pieces.Count + " pieces for " + expected + " tiles";
            return null;
        }

        /// <summary>The world position of a board-space point on the tray surface.</summary>
        public Vector3 ToLocal(Float2 p) => new Vector3((float)p.X, 0f, (float)p.Y);

        /// <summary>Lifts the piece under the pointer a little (mouse hover); -1 clears it.</summary>
        public void SetHover(int tileId)
        {
            if (tileId == hoverTile) return;
            if (hoverTile >= 0 && pieces.TryGetValue(hoverTile, out var old)) { old.Hover = 0f; old.ApplyPose(); }
            hoverTile = tileId;
            if (tileId >= 0 && pieces.TryGetValue(tileId, out var now)) { now.Hover = Style.material.hoverHighlight; now.ApplyPose(); }
        }

        // ---- swap arrow ----

        /// <summary>Shows the arrow from the pressed piece's cell toward its neighbor (replaces the previous one).</summary>
        public void ShowArrow(CellId from, CellId to)
        {
            if (Geometry == null) return;
            var a = Geometry.CellPosition(from);
            var b = Geometry.CellPosition(to);
            double dx = b.X - a.X, dz = b.Y - a.Y;
            if (arrow == null) arrow = FxView.Create(Style, FxView.Kind.Arrow, FxRoot);
            arrow.gameObject.SetActive(true);
            float yaw = -Mathf.Atan2((float)dz, (float)dx) * Mathf.Rad2Deg; // Unity turns +X toward -Z for a positive yaw
            var fx = Style.fx;
            // Centred between the two cells: it spans the middle <arrowLength> of the way from one piece to the other.
            float len = fx.arrowLength;
            float dist = Mathf.Sqrt((float)(dx * dx + dz * dz));
            var dir = new Vector3((float)dx, 0f, (float)dz) / Mathf.Max(1e-4f, dist);
            arrow.Place(new Vector3((float)a.X, fx.arrowY, (float)a.Y) + dir * (dist * 0.5f - len * 0.5f), len, yaw);
            arrow.SetAlpha(1f);
        }

        public void HideArrow()
        {
            if (arrow != null) arrow.gameObject.SetActive(false);
        }

        public bool ArrowVisible => arrow != null && arrow.gameObject.activeSelf;
    }
}
