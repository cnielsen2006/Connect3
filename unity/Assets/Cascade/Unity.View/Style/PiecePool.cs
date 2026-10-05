using System.Collections.Generic;
using UnityEngine;

namespace Cascade.Unity.View
{
    /// <summary>Builds <see cref="PieceView"/> objects for a style.</summary>
    public static class PieceFactory
    {
        public static PieceView Create(PieceStyle style, Transform? parent, int color, PieceSpecial special, PieceLook look, int agateColors = -1)
        {
            var go = new GameObject("Piece");
            if (parent != null) go.transform.SetParent(parent, false);
            var view = go.AddComponent<PieceView>();
            view.Init(style, color, look);
            if (special != PieceSpecial.None) view.SetSpecial(special, agateColors);
            return view;
        }
    }

    /// <summary>
    /// Pool of pieces: Cleared pieces are released here instead of destroyed, and Spawned pieces are taken
    /// from it. Released pieces are deactivated and parked under the pool root.
    /// </summary>
    public sealed class PiecePool
    {
        readonly PieceStyle style;
        readonly Stack<PieceView> free = new Stack<PieceView>();
        readonly Transform root;
        int created;

        public PiecePool(PieceStyle style, Transform root)
        {
            this.style = style;
            this.root = root;
        }

        public int FreeCount => free.Count;
        public int CreatedCount => created;

        /// <summary>Gets a piece, re-initialised with the given colour, special and look, at pose rest.</summary>
        public PieceView Get(Transform parent, int color, PieceSpecial special, PieceLook look, int agateColors = -1)
        {
            PieceView view;
            if (free.Count > 0)
            {
                view = free.Pop();
                view.transform.SetParent(parent, false);
                view.gameObject.SetActive(true);
                view.Init(style, color, look);
                if (special != PieceSpecial.None) view.SetSpecial(special, agateColors);
            }
            else
            {
                view = PieceFactory.Create(style, parent, color, special, look, agateColors);
                created++;
            }
            view.ApplyPose();
            return view;
        }

        public void Release(PieceView view)
        {
            view.ResetPose();
            view.gameObject.SetActive(false);
            view.transform.SetParent(root, false);
            free.Push(view);
        }

        /// <summary>Creates <paramref name="count"/> pieces up front so the first spawn does not hitch.</summary>
        public void Prewarm(int count)
        {
            var tmp = new List<PieceView>(count);
            for (int i = 0; i < count; i++) tmp.Add(Get(root, i, PieceSpecial.None, PieceLook.Neutral));
            foreach (var v in tmp) Release(v);
        }
    }
}
