using System;
using System.Collections.Generic;
using Cascade.Presentation.Shell;
using UnityEngine;
using UnityEngine.UIElements;

namespace Cascade.Unity.View
{
    /// <summary>
    /// Binder of <c>Map.uxml</c> to <see cref="MapViewModel"/>: a scrolling path of level pebbles, level 1 at the bottom, with the stars
    /// earned under each, the current level highlighted and locked levels dimmed. Tap or click an unlocked pebble to start it. The wheel and
    /// a drag scroll the path smoothly; the view-model only exposes a window of levels around its center, so when the view has scrolled
    /// away from the center the screen asks it to move the window (<see cref="MapViewModel.ScrollBy"/>).
    /// <para>Distances (level spacing, the width of the lane the path wanders in, wheel speed, drag slop) are read from invisible metric
    /// elements whose size is set in <c>Map.uss</c>, so the wide and tall layouts can differ without code. The node look is USS classes
    /// (<c>map-node--current</c>, <c>--completed</c>, <c>--locked</c>, <c>--shape0..2</c>).</para>
    /// </summary>
    public sealed class MapScreen : IScreenView, IDisposable
    {
        const int DotsPerSegment = 3;

        readonly MapViewModel vm;
        readonly TouchSizing sizing;
        readonly VisualElement viewport, content, metricSpacing, metricLane, metricWheel, metricSlop;
        readonly Button back;
        readonly Label title, totalStars;
        readonly Dictionary<int, VisualElement> nodes = new Dictionary<int, VisualElement>();
        readonly Dictionary<int, Button> buttons = new Dictionary<int, Button>();
        readonly List<(VisualElement Dot, int Level, float T)> dots = new List<(VisualElement, int, float)>();

        float center;
        bool pressed, dragging;
        int pointerId = -1;
        float startY, lastY;
        bool rebuilding;

        public MapScreen(VisualElement root, MapViewModel viewModel, TouchSizing touchSizing)
        {
            vm = viewModel;
            sizing = touchSizing;
            viewport = Ui.Get<VisualElement>(root, "map-viewport");
            content = Ui.Get<VisualElement>(root, "map-content");
            metricSpacing = Ui.Get<VisualElement>(root, "map-metric-spacing");
            metricLane = Ui.Get<VisualElement>(root, "map-metric-lane");
            metricWheel = Ui.Get<VisualElement>(root, "map-metric-wheel");
            metricSlop = Ui.Get<VisualElement>(root, "map-metric-slop");
            back = Ui.Get<Button>(root, "map-back");
            title = Ui.Get<Label>(root, "map-title");
            totalStars = Ui.Get<Label>(root, "map-stars");
            back.clicked += () => vm.Back();

            viewport.RegisterCallback<GeometryChangedEvent>(_ => Place());
            viewport.RegisterCallback<WheelEvent>(OnWheel);
            viewport.RegisterCallback<PointerDownEvent>(OnDown, TrickleDown.TrickleDown);
            viewport.RegisterCallback<PointerMoveEvent>(OnMove, TrickleDown.TrickleDown);
            viewport.RegisterCallback<PointerUpEvent>(OnUp, TrickleDown.TrickleDown);
            viewport.RegisterCallback<PointerCaptureOutEvent>(_ => { pressed = false; dragging = false; });

            center = vm.CurrentLevel;
            vm.Changed += OnChanged;
            OnChanged();
        }

        public Button BackButton => back;

        /// <summary>The level number at the middle of the viewport; fractional while scrolling.</summary>
        public float Center => center;

        /// <summary>The button of a level pebble currently built, or null.</summary>
        public Button? NodeButton(int level) => buttons.TryGetValue(level, out var b) ? b : null;

        /// <summary>The element (pebble and stars) of a level currently built, or null.</summary>
        public VisualElement? NodeElement(int level) => nodes.TryGetValue(level, out var n) ? n : null;

        public IReadOnlyCollection<int> BuiltLevels => nodes.Keys;

        // ---- content ----

        void OnChanged()
        {
            Ui.SetText(title, vm.Title);
            Ui.SetText(back, vm.BackLabel);
            Ui.SetText(totalStars, vm.TotalStarsText);
            Rebuild();
        }

        void Rebuild()
        {
            if (rebuilding) return;
            rebuilding = true;
            content.Clear();
            nodes.Clear();
            buttons.Clear();
            dots.Clear();

            var list = vm.Nodes;
            // dots first, so the pebbles draw over them
            for (int i = 0; i + 1 < list.Count; i++)
                for (int k = 1; k <= DotsPerSegment; k++)
                {
                    var dot = new VisualElement { pickingMode = PickingMode.Ignore };
                    dot.AddToClassList("map-dot");
                    content.Add(dot);
                    dots.Add((dot, list[i].Number, k / (float)(DotsPerSegment + 1)));
                }
            foreach (var node in list) content.Add(MakeNode(node));
            sizing.Apply(content);
            rebuilding = false;
            Place();
        }

        VisualElement MakeNode(MapNode node)
        {
            var wrap = new VisualElement { pickingMode = PickingMode.Ignore, name = "map-node-" + node.Number };
            wrap.AddToClassList("map-node");
            wrap.AddToClassList("map-node--shape" + (node.Number % 3));
            wrap.AddToClassList(node.State == MapNodeState.Current ? "map-node--current"
                : node.State == MapNodeState.Completed ? "map-node--completed" : "map-node--locked");

            var button = new Button { text = node.Label, name = "map-button-" + node.Number };
            button.AddToClassList("map-node__button");
            int level = node.Number;
            button.clicked += () => vm.Select(level);
            wrap.Add(button);

            if (node.Stars > 0)
            {
                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.AddToClassList("map-node__stars");
                for (int i = 1; i <= 3; i++)
                {
                    var star = new StarElement(i <= node.Stars);
                    star.AddToClassList("star--small");
                    row.Add(star);
                }
                wrap.Add(row);
            }
            nodes[node.Number] = wrap;
            buttons[node.Number] = button;
            return wrap;
        }

        // ---- layout and scrolling ----

        static bool Good(float v) => !float.IsNaN(v) && v > 0f;

        float Spacing => metricSpacing.resolvedStyle.height;

        /// <summary>Positions every node and dot for the current <see cref="Center"/>.</summary>
        void Place()
        {
            float vw = viewport.resolvedStyle.width, vh = viewport.resolvedStyle.height;
            float spacing = Spacing, lane = metricLane.resolvedStyle.width;
            if (!Good(vw) || !Good(vh) || !Good(spacing) || !Good(lane)) return;

            foreach (var kv in nodes)
            {
                var p = PointOf(kv.Key, vw, vh, spacing, lane);
                kv.Value.style.left = p.x;
                kv.Value.style.top = p.y;
            }
            foreach (var (dot, level, t) in dots)
            {
                var a = PointOf(level, vw, vh, spacing, lane);
                var b = PointOf(level + 1, vw, vh, spacing, lane);
                var p = Vector2.Lerp(a, b, t);
                dot.style.left = p.x;
                dot.style.top = p.y;
            }
        }

        Vector2 PointOf(int level, float vw, float vh, float spacing, float lane)
        {
            double pathX = vm.NodeFor(level).PathX; // -1..1, from the view-model
            return new Vector2(vw * 0.5f + (float)pathX * lane * 0.5f, vh * 0.5f + (center - level) * spacing);
        }

        /// <summary>Scrolls by a number of levels (positive shows higher levels).</summary>
        public void ScrollLevels(float levels)
        {
            SetCenter(center + levels);
        }

        /// <summary>Scrolls so that this (fractional) level is at the middle.</summary>
        public void SetCenter(float level)
        {
            center = Mathf.Clamp(level, 1f, Mathf.Max(1, vm.HighestShown));
            // keep the view-model's window around what is on screen
            int want = Mathf.RoundToInt(center);
            if (Mathf.Abs(want - vm.CenterLevel) >= 3) vm.ScrollBy(want - vm.CenterLevel); // raises Changed, which rebuilds and places
            else Place();
        }

        void OnWheel(WheelEvent e)
        {
            float spacing = Spacing, wheel = metricWheel.resolvedStyle.height;
            if (!Good(spacing) || !Good(wheel)) return;
            ScrollLevels(-e.delta.y * wheel / spacing);
            e.StopPropagation();
        }

        void OnDown(PointerDownEvent e)
        {
            if (e.button != 0 && e.pointerType == "mouse") return;
            pressed = true; dragging = false;
            pointerId = e.pointerId;
            startY = lastY = e.position.y;
        }

        void OnMove(PointerMoveEvent e)
        {
            if (!pressed || e.pointerId != pointerId) return;
            float y = e.position.y;
            if (!dragging && Mathf.Abs(y - startY) > Mathf.Max(1f, metricSlop.resolvedStyle.height))
            {
                dragging = true;
                viewport.CapturePointer(pointerId); // the pebble under the finger no longer gets a click
            }
            if (dragging && Good(Spacing)) ScrollLevels((y - lastY) / Spacing);
            lastY = y;
        }

        void OnUp(PointerUpEvent e)
        {
            if (e.pointerId != pointerId) return;
            if (dragging && viewport.HasPointerCapture(pointerId)) viewport.ReleasePointer(pointerId);
            pressed = false; dragging = false;
        }

        // ---- IScreenView ----

        public void OnShown()
        {
            center = vm.CurrentLevel;
            vm.ScrollToCurrent();
            Rebuild();
        }

        public void OnHidden() { }

        public void Dispose() => vm.Changed -= OnChanged;
    }
}
