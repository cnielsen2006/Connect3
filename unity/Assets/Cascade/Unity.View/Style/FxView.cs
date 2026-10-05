using UnityEngine;
using UnityEngine.Rendering;

namespace Cascade.Unity.View
{
    /// <summary>
    /// A flat translucent effect mesh: a ripple ring, the swap arrow, or a wash (a white translucent rectangle that
    /// a line wash stretches along a row or column). Alpha moves in shared-material steps.
    /// </summary>
    public sealed class FxView : MonoBehaviour
    {
        public enum Kind { Ripple, Arrow, Wash }

        PieceStyle? style;
        MeshRenderer? rend;
        Kind kind;
        int level = -1;

        public static FxView Create(PieceStyle style, Kind kind, Transform? parent)
        {
            var go = new GameObject(kind == Kind.Ripple ? "Ripple" : kind == Kind.Arrow ? "SwapArrow" : "Wash");
            if (parent != null) go.transform.SetParent(parent, false);
            var fx = go.AddComponent<FxView>();
            fx.style = style;
            fx.kind = kind;
            go.AddComponent<MeshFilter>().sharedMesh = kind == Kind.Ripple ? style.art.ripple : kind == Kind.Arrow ? style.art.arrow : (style.art.wash != null ? style.art.wash : style.art.quad);
            fx.rend = go.AddComponent<MeshRenderer>();
            fx.rend.shadowCastingMode = ShadowCastingMode.Off;
            fx.rend.receiveShadows = false;
            fx.rend.lightProbeUsage = LightProbeUsage.Off;
            fx.rend.reflectionProbeUsage = ReflectionProbeUsage.Off;
            fx.SetAlpha(1f);
            return fx;
        }

        public Kind EffectKind => kind;

        /// <summary>Ripple: radius in cells (the mesh has radius 0.5 at scale 1). Arrow: length in cells. Wash: a square of this side.</summary>
        public void Place(Vector3 position, float sizeCells, float yawDegrees = 0f)
        {
            transform.localPosition = position;
            transform.localRotation = Quaternion.Euler(0f, yawDegrees, 0f);
            float s = kind == Kind.Ripple ? sizeCells * 2f : sizeCells;
            transform.localScale = new Vector3(s, 1f, s);
        }

        /// <summary>Wash: a rectangle <paramref name="lengthX"/> by <paramref name="widthZ"/> cells (before the yaw), centred on <paramref name="position"/>.</summary>
        public void PlaceRect(Vector3 position, float lengthX, float widthZ, float yawDegrees = 0f)
        {
            transform.localPosition = position;
            transform.localRotation = Quaternion.Euler(0f, yawDegrees, 0f);
            transform.localScale = new Vector3(Mathf.Max(0.0001f, lengthX), 1f, Mathf.Max(0.0001f, widthZ));
        }

        /// <summary>0..1 opacity factor, quantised to the style's fade levels.</summary>
        public void SetAlpha(float t01)
        {
            if (style == null || rend == null) return;
            var mats = style.Materials;
            int l = mats.FadeLevel(t01);
            if (l == level) return;
            level = l;
            rend.sharedMaterial = kind == Kind.Ripple ? mats.Ripple(l) : kind == Kind.Arrow ? style.art.arrowMaterial : mats.Wash(l);
        }
    }
}
