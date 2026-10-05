using System;
using System.Collections.Generic;
using Cascade.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Cascade.Unity.View
{
    /// <summary>
    /// Draws the transient effects of a <see cref="Timeline"/> (<see cref="ShowFx"/> items): ripples, line, bomb and
    /// color washes as flat translucent meshes in the world of the board, and score popups as UI Toolkit labels (class
    /// <c>popup</c>, so they use the style font) positioned over the board by projecting the popup's world point.
    /// It only follows what the player tells it: <see cref="Begin"/> when an effect starts, <see cref="Tick"/> with its
    /// progress while it lives, <see cref="End"/> when it is over.
    /// </summary>
    public sealed class FxPresenter
    {
        sealed class Active
        {
            public ShowFx Fx = null!;
            public readonly List<FxView> Views = new List<FxView>();
            public Label? Popup;
            public readonly List<VisualElement> Sparks = new List<VisualElement>();
        }

        readonly PieceStyle style;
        readonly Transform parent;
        readonly VisualElement? popupLayer;
        readonly Func<Vector3, Vector2?>? projector;
        readonly Dictionary<ShowFx, Active> active = new Dictionary<ShowFx, Active>(); // lookup only
        readonly List<ShowFx> endAll = new List<ShowFx>();

        /// <param name="popupLayer">An element covering the screen that popups are added to (null: no popups are drawn).</param>
        /// <param name="projector">World point to panel units (<see cref="BoardCameraRig.WorldToPanel"/>), to place a popup.</param>
        public FxPresenter(PieceStyle style, Transform parent, VisualElement? popupLayer, Func<Vector3, Vector2?>? projector)
        {
            this.style = style;
            this.parent = parent;
            this.popupLayer = popupLayer;
            this.projector = projector;
        }

        public int ActiveCount => active.Count;

        public void Begin(ShowFx fx)
        {
            if (active.ContainsKey(fx)) return;
            var a = new Active { Fx = fx };
            active[fx] = a;
            switch (fx.Kind)
            {
                case FxKind.Ripple:
                case FxKind.BombWash:
                    a.Views.Add(FxView.Create(style, FxView.Kind.Ripple, parent));
                    break;
                case FxKind.LineWash:
                    a.Views.Add(FxView.Create(style, FxView.Kind.Wash, parent));
                    break;
                case FxKind.ColorWash:
                    for (int i = 0; i < fx.Cells.Count; i++) a.Views.Add(FxView.Create(style, FxView.Kind.Ripple, parent));
                    break;
                case FxKind.ScorePopup:
                    if (popupLayer != null)
                    {
                        var label = new Label(fx.Text) { pickingMode = PickingMode.Ignore };
                        label.AddToClassList("popup");
                        label.AddToClassList("popup--depth" + Mathf.Min(fx.Depth, 4));
                        popupLayer.Add(label);
                        a.Popup = label;
                    }
                    break;
                case FxKind.ComboBadge:
                    if (popupLayer != null)
                    {
                        var badge = new Label(fx.Text.Replace('x', '\u00D7')) { pickingMode = PickingMode.Ignore };
                        badge.AddToClassList("combo-badge");
                        badge.AddToClassList("combo-badge--tier" + Mathf.Clamp(fx.Tier, 0, 4));
                        popupLayer.Add(badge);
                        a.Popup = badge;
                    }
                    break;
                case FxKind.Burst:
                    if (popupLayer != null)
                        for (int i = 0; i < fx.Count; i++)
                        {
                            var spark = new VisualElement { pickingMode = PickingMode.Ignore };
                            spark.AddToClassList("spark");
                            spark.AddToClassList("spark--tier" + Mathf.Clamp(fx.Tier, 0, 4));
                            spark.AddToClassList("spark--v" + (i % 3));
                            popupLayer.Add(spark);
                            a.Sparks.Add(spark);
                        }
                    break;
            }
            Tick(fx, 0.0);
        }

        /// <summary>Positions and fades the effect for progress <paramref name="u"/> (0..1).</summary>
        public void Tick(ShowFx fx, double u)
        {
            if (!active.TryGetValue(fx, out var a)) return;
            var f = style.fx;
            float peak = (float)fx.AlphaAt(0.0);
            float alpha = peak > 1e-4f ? (float)(fx.AlphaAt(u) / peak) : 0f;
            switch (fx.Kind)
            {
                case FxKind.Ripple:
                    a.Views[0].Place(new Vector3((float)fx.Position.X, f.rippleY, (float)fx.Position.Y), (float)fx.ScaleAt(u) * 0.5f);
                    a.Views[0].SetAlpha(alpha);
                    break;
                case FxKind.BombWash:
                    a.Views[0].Place(new Vector3((float)fx.Position.X, f.washY, (float)fx.Position.Y), (float)fx.ScaleAt(u) * 0.5f);
                    a.Views[0].SetAlpha(alpha);
                    break;
                case FxKind.LineWash:
                {
                    float len = (float)fx.ScaleAt(u);
                    var pos = new Vector3((float)fx.Position.X, f.washY, (float)fx.Position.Y);
                    if (fx.Axis == 0) a.Views[0].PlaceRect(pos, len, f.washWidth);
                    else a.Views[0].PlaceRect(pos, f.washWidth, len);
                    a.Views[0].SetAlpha(alpha);
                    break;
                }
                case FxKind.ColorWash:
                    for (int i = 0; i < a.Views.Count; i++)
                    {
                        double cu = fx.CellProgress(i, u);
                        var c = fx.Cells[i];
                        bool live = cu > 0.0 && cu < 1.0;
                        if (a.Views[i].gameObject.activeSelf != live) a.Views[i].gameObject.SetActive(live);
                        if (!live) continue;
                        a.Views[i].Place(new Vector3((float)c.X, f.washY, (float)c.Y), (float)fx.ScaleAt(cu) * 0.5f);
                        a.Views[i].SetAlpha((float)(fx.AlphaAt(cu) / peak));
                    }
                    break;
                case FxKind.ScorePopup:
                case FxKind.ComboBadge:
                    TickPopup(a, u);
                    break;
                case FxKind.Burst:
                    TickBurst(a, u);
                    break;
            }
        }

        /// <summary>Particles fly out from the burst's point; each gets a fixed angle and speed from its index (no random state).</summary>
        void TickBurst(Active a, double u)
        {
            if (projector == null || a.Sparks.Count == 0 || a.Sparks[0].panel == null) return;
            var fx = a.Fx;
            var world = new Vector3((float)fx.Position.X, (float)fx.ElevationAt(u), (float)fx.Position.Y);
            var c = projector(world);
            var unit = projector(world + Vector3.right);
            if (c == null || unit == null) return;
            float pxPerUnit = Vector2.Distance(c.Value, unit.Value);
            float reach = (float)fx.ScaleAt(u) * pxPerUnit;
            float alpha = (float)fx.AlphaAt(u);
            for (int i = 0; i < a.Sparks.Count; i++)
            {
                uint h = (uint)(fx.Id * 73856093) ^ (uint)((i + 1) * 19349663);
                h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
                float angle = (i + (h & 0xFF) / 255f) * (2f * Mathf.PI / a.Sparks.Count);
                float speed = 0.55f + 0.45f * ((h >> 8) & 0xFF) / 255f;
                float fall = 0.35f * reach * (float)u * (float)u; // gravity-like droop
                var s = a.Sparks[i];
                s.style.left = c.Value.x + Mathf.Cos(angle) * reach * speed;
                s.style.top = c.Value.y + Mathf.Sin(angle) * reach * speed + fall;
                s.style.opacity = alpha;
            }
        }

        void TickPopup(Active a, double u)
        {
            var label = a.Popup;
            if (label == null || projector == null || label.panel == null) return;
            var fx = a.Fx;
            var world = new Vector3((float)fx.Position.X, (float)fx.ElevationAt(u), (float)fx.Position.Y);
            var p = projector(world);
            if (p == null) return;
            label.style.left = p.Value.x;
            label.style.top = p.Value.y;
            label.style.opacity = (float)fx.AlphaAt(u);
            float s = (float)fx.ScaleAt(u);
            label.style.scale = new Scale(new Vector3(s, s, 1f));
        }

        public void End(ShowFx fx)
        {
            if (!active.TryGetValue(fx, out var a)) return;
            active.Remove(fx);
            foreach (var v in a.Views) if (v != null) UnityEngine.Object.Destroy(v.gameObject);
            a.Popup?.RemoveFromHierarchy();
            foreach (var s in a.Sparks) s.RemoveFromHierarchy();
        }

        /// <summary>Ends every live effect (a level ends, the player is stopped).</summary>
        public void Clear()
        {
            endAll.Clear();
            foreach (var kv in active) endAll.Add(kv.Key);
            foreach (var fx in endAll) End(fx);
        }
    }
}
