using UnityEngine;
using UnityEngine.UIElements;

namespace Cascade.Unity.View
{
    /// <summary>
    /// A star, drawn with the vector painter in the element's USS <c>color</c>, with softened points so it reads as a stone
    /// rather than a sticker. Size and colors are USS (<c>.star</c>, <c>.star--earned</c> in Theme.uss).
    /// </summary>
    public sealed class StarElement : VisualElement
    {
        public const string StyleClass = "star";
        public const string EarnedClass = "star--earned";

        bool earned;

        public StarElement(bool earned = false)
        {
            AddToClassList(StyleClass);
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            Earned = earned;
        }

        public bool Earned
        {
            get => earned;
            set
            {
                earned = value;
                EnableInClassList(EarnedClass, value);
                MarkDirtyRepaint();
            }
        }

        void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            if (r.width < 1f || r.height < 1f) return;
            var p = ctx.painter2D;
            var color = resolvedStyle.color;
            float radius = Mathf.Min(r.width, r.height) * 0.5f;
            float outer = radius * 0.80f, inner = outer * 0.48f, soften = radius * 0.16f;
            var c = r.center;
            p.fillColor = color;
            p.strokeColor = color;
            p.lineWidth = soften * 2f;
            p.lineJoin = LineJoin.Round;
            p.BeginPath();
            for (int i = 0; i < 10; i++)
            {
                float a = (-90f + i * 36f) * Mathf.Deg2Rad;
                float d = (i & 1) == 0 ? outer : inner;
                var pt = new Vector2(c.x + Mathf.Cos(a) * d, c.y + Mathf.Sin(a) * d + radius * 0.04f);
                if (i == 0) p.MoveTo(pt); else p.LineTo(pt);
            }
            p.ClosePath();
            p.Fill();
            p.Stroke();
        }
    }
}
