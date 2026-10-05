#if UNITY_EDITOR || DEVELOPMENT_BUILD || CASCADE_DEV
// Dev builds only (like DevAutoPlay): the planner's "ghost" overlay is not compiled into a release player.
using System.Collections.Generic;
using Cascade.Core;
using Cascade.Unity.View;
using UnityEngine;

namespace Cascade.App
{
    /// <summary>
    /// The planner's research made visible (TRD 7.1): a line of moves drawn over the board as translucent washes on the cells
    /// each swap touches, brightest for the first move and fainter for the later ones, like the ghost of a best lap. A line
    /// that is only being tried (a probe) shows the washes and the arrow of its first move; the chosen line also shows an
    /// arrow for every move. It only draws: it reads the board's geometry and never touches the session (TRD 12.1).
    /// </summary>
    public sealed class PlanGhost
    {
        readonly BoardView view;
        readonly Transform root;
        readonly List<FxView> washes = new List<FxView>();
        readonly List<FxView> arrows = new List<FxView>();

        /// <summary>Each later move is this much fainter than the one before.</summary>
        const float PlyFade = 0.7f;

        public PlanGhost(BoardView boardView)
        {
            view = boardView;
            root = new GameObject("PlanGhost").transform;
            root.SetParent(boardView.transform, false);
        }

        /// <summary>True while any part of a line is showing.</summary>
        public bool Visible { get; private set; }

        /// <summary>Shows <paramref name="moves"/>; <paramref name="strength"/> (0..1) scales the whole line, <paramref name="arrowsForAll"/> adds an arrow per move.</summary>
        public void Show(IReadOnlyList<(CellId A, CellId B)> moves, float strength, bool arrowsForAll)
        {
            var geometry = view.Geometry;
            if (geometry == null) { Hide(); return; }
            HideAll();
            int w = 0, a = 0;
            float fade = strength;
            for (int ply = 0; ply < moves.Count; ply++, fade *= PlyFade)
            {
                var (cellA, cellB) = moves[ply];
                var pa = geometry.CellPosition(cellA);
                var pb = geometry.CellPosition(cellB);
                var fx = view.Style.fx;
                float y = fx.arrowY - 0.06f;
                foreach (var p in new[] { pa, pb })
                {
                    var wash = Next(washes, FxView.Kind.Wash, w++);
                    wash.Place(new Vector3((float)p.X, y, (float)p.Y), 0.95f);
                    wash.SetAlpha(fade);
                }
                if (ply > 0 && !arrowsForAll) continue;
                double dx = pb.X - pa.X, dz = pb.Y - pa.Y;
                float dist = Mathf.Sqrt((float)(dx * dx + dz * dz));
                var dir = new Vector3((float)dx, 0f, (float)dz) / Mathf.Max(1e-4f, dist);
                float yaw = -Mathf.Atan2((float)dz, (float)dx) * Mathf.Rad2Deg;
                var arrow = Next(arrows, FxView.Kind.Arrow, a++);
                arrow.Place(new Vector3((float)pa.X, fx.arrowY + 0.01f * ply, (float)pa.Y) + dir * (dist * 0.5f - fx.arrowLength * 0.5f), fx.arrowLength, yaw);
            }
            Visible = true;
        }

        /// <summary>Removes the overlay.</summary>
        public void Hide()
        {
            if (!Visible) return;
            HideAll();
            Visible = false;
        }

        FxView Next(List<FxView> pool, FxView.Kind kind, int index)
        {
            if (index == pool.Count) pool.Add(FxView.Create(view.Style, kind, root));
            var fx = pool[index];
            fx.gameObject.SetActive(true);
            return fx;
        }

        void HideAll()
        {
            foreach (var fx in washes) fx.gameObject.SetActive(false);
            foreach (var fx in arrows) fx.gameObject.SetActive(false);
        }
    }
}
#endif
