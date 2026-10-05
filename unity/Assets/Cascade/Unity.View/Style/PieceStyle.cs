using System;
using UnityEngine;

namespace Cascade.Unity.View
{
    /// <summary>The seven pebble silhouettes of style K (STYLE-K-Pebbles.md section 3).</summary>
    public enum PebbleShape { Oval, Leaf, RoundedSquare, Capsule, Disc, Scallop, Kidney }

    /// <summary>What a piece looks like on top of its base pebble. Local to the view; the Core port maps onto it.</summary>
    public enum PieceSpecial { None, LineHorizontal, LineVertical, Bomb, Color }

    [Serializable]
    public sealed class PieceSwatch
    {
        public string name = "";
        public Color32 color = new Color32(255, 255, 255, 255);
        public PebbleShape shape;
        public Mesh? mesh;
        public Mesh? haloMesh;
        [Header("Special overlays draped over this silhouette")]
        public Mesh? veinHorizontal;
        public Mesh? veinVertical;
        public Mesh? holeRim;
        public Mesh? holeDisc;
        public Mesh? agate;
    }

    [Serializable]
    public sealed class PaletteParams
    {
        public PieceSwatch[] pieces = Array.Empty<PieceSwatch>();
        public Color32 bgTop = new Color32(0xef, 0xe8, 0xdb, 255);
        public Color32 bgBottom = new Color32(0xdf, 0xd4, 0xc1, 255);
        public Color32 tray = new Color32(0xd9, 0xc7, 0xa6, 255);
        public Color32 trayGrain = new Color32(0xc9, 0xb4, 0x8f, 255);
        public Color32 trayLip = new Color32(0xcd, 0xb9, 0x94, 255);
        public Color32 text = new Color32(0x4f, 0x4a, 0x46, 255);
        public Color32 accent = new Color32(0xb9, 0x84, 0x68, 255);
        public Color32 vein = new Color32(0xfb, 0xf7, 0xee, 255);
        public Color32 hole = new Color32(0x2c, 0x28, 0x25, 255);
    }

    [Serializable]
    public sealed class MaterialParams
    {
        [Tooltip("Pebble height / width.")] public float heightRatio = 0.45f;
        [Tooltip("Piece width as a fraction of a cell.")] public float size = 0.84f;
        [Tooltip("Strength of the faint top-face highlight.")] public float sheen = 0.10f;
        [Tooltip("Blinn-Phong exponent of the sun highlight (low = worn clay).")] public float shininess = 18f;
        [Tooltip("Soft rim light strength.")] public float rimStrength = 0.10f;
        public float rimPower = 3f;
        [Tooltip("Flecks per piece (baked into the fleck texture).")] public int fleckCount = 3;
        [Range(0, 1)] public float fleckDark = 0.32f;
        [Range(0, 1)] public float fleckLight = 0.50f;
        [Tooltip("Per-instance fleck shift range in UV units; PieceLook.FleckOffsetX/Y are -1..1 of this.")]
        public Vector2 fleckOffsetRange = new Vector2(0.15f, 0.07f);
        [Tooltip("Brightness is quantised so pieces share a small pool of materials (SRP batcher friendly).")]
        public float brightnessQuantum = 0.01f;
        public float fleckQuantum = 0.1f;
        [Tooltip("Cell units.")] public float selectLift = 0.15f;
        public float selectScale = 1.10f;
        [Range(0, 1)] public float grainAlpha = 0.16f;
        [Tooltip("Cell units of lift per unit of PieceView.Hover.")] public float hoverLift = 0.04f;
        [Tooltip("PieceView.Hover units a pebble under the mouse pointer rises by.")] public float hoverHighlight = 1.5f;
        [Range(0, 1), Tooltip("How much of the volume-preserving widening a squash applies (1 = full: a squash to 0.7 widens by 20%; pebbles then touch their neighbours).")]
        public float squashSpread = 0.5f;
        [Tooltip("Cell units a piece sinks below the tray at Sink = 1.")] public float sinkDepth = 0.42f;
        [Tooltip("Scale lost at Sink = 1.")] public float sinkScale = 0.55f;
        [Tooltip("Brightness multiplier of a Bomb (hag stone) pebble.")] public float bombDarken = 0.42f;
        [Tooltip("Steps of shadow/halo fade (each is a shared material).")] public int fadeLevels = 8;
        [Tooltip("Height ratio the meshes were baked with; set by the builder.")] public float bakedHeightRatio = 0.45f;
    }

    [Serializable]
    public sealed class SpecialParams
    {
        [Tooltip("Pebble-width units; clamped to fit narrow silhouettes.")] public float veinLength = 0.50f;
        public float veinThickness = 0.085f;
        public float veinHeight = 0.06f;
        public float bombHoleRadius = 0.105f;
        public float bombRimRadius = 0.15f;
        public float bombRimTube = 0.032f;
        [Tooltip("Outer edge of the agate as a fraction of the pebble outline (the bands follow the silhouette).")]
        public float agateExtent = 0.78f;
        [Tooltip("Overlays sit this far above the pebble surface (pebble units).")] public float shellOffset = 0.006f;
        [Tooltip("Number of concentric bands of the agate; colours repeat if fewer are active.")]
        public int agateBands = 5;
        [Tooltip("Default number of palette colours the agate shows.")] public int agateColors = 5;
    }

    [Serializable]
    public sealed class FxParams
    {
        public float shadowSize = 1.05f;
        public Color shadowColor = new Color(0.25f, 0.20f, 0.15f, 0.55f);
        public float shadowY = 0.004f;
        public float haloY = 0.012f;
        [Range(0, 1)] public float haloAlpha = 0.9f;
        public Color haloColor = new Color(1f, 1f, 1f, 1f);
        public Color rippleColor = new Color(1f, 1f, 1f, 0.8f);
        public Color arrowColor = new Color(1f, 1f, 1f, 0.9f);
        [Tooltip("Colour of the wave that washes across a line special's row or column.")]
        public Color washColor = new Color(1f, 0.99f, 0.95f, 0.6f);
        [Header("Placement of the play-time effects (cell units above the tray)")]
        [Tooltip("Height of a clear ripple.")] public float rippleY = 0.02f;
        [Tooltip("Height of line, bomb and colour washes: above the pebbles so they read over them.")] public float washY = 0.5f;
        [Tooltip("Width of a line wash in cells.")] public float washWidth = 0.8f;
        [Tooltip("Height of the swap arrow (above a lifted pebble).")] public float arrowY = 0.58f;
        [Tooltip("Length of the swap arrow in cells.")] public float arrowLength = 0.9f;
    }

    [Serializable]
    public sealed class LightParams
    {
        [Tooltip("Flat ambient (overcast sky bounce). Raw display-space values.")]
        public Color ambient = new Color(0.50f, 0.51f, 0.54f, 1f);
        [Tooltip("Soft sky light straight from above.")]
        public Color sky = new Color(0.26f, 0.26f, 0.27f, 1f);
        [Tooltip("Low warm sun from the upper left.")]
        public Color sun = new Color(0.30f, 0.27f, 0.21f, 1f);
        public Color sunSpecular = new Color(0.60f, 0.55f, 0.45f, 1f);
        [Tooltip("Degrees. Convention: (negative = light travels towards +X).")]
        public float sunHeading = -35f;
        public float sunPitch = -55f;
    }

    [Serializable]
    public sealed class CameraParams
    {
        [Tooltip("Degrees from vertical. The camera.pitch_deg dial overrides it at runtime.")]
        public float pitchDeg = 25f;
        public float fovDeg = 24f;
        [Tooltip("Fraction of the view kept as margin when fitting a board.")]
        public float fitMargin = 0.04f;
        public float near = 0.3f;
        public float far = 80f;
        [Tooltip("Draw the board only inside the layout's board slot, so a zoomed board never runs under the HUD text.")]
        public bool clipToSlot = true;
    }

    [Serializable]
    public sealed class TrayParams
    {
        public float lip = 0.16f;
        public float lipHeight = 0.09f;
        public float depth = 0.25f;
        public float edgeRound = 0.05f;
        public float innerFillet = 0.035f;
        [Range(0, 1)] public float edgeShade = 0.74f;
        public float brightness = 1.14f;
        [Tooltip("Texture tiles per world unit (u across the grain, v along it).")]
        public Vector2 grainTiling = new Vector2(0.083f, 0.25f);
        public int cornerSegments = 6;
        [Tooltip("Colour of the ground seen through an enclosed hole of the tray (lit like the pebbles, so it comes out a little darker).")]
        public Color32 holeFloor = new Color32(0x8c, 0x7a, 0x5f, 255);
    }

    [Serializable]
    public sealed class HudTheme
    {
        public Color32 buttonPause = new Color32(0x8f, 0xa0, 0x8a, 255);
        public Color32 buttonHint = new Color32(0xcd, 0xb4, 0x8d, 255);
        public Color32 buttonText = new Color32(255, 255, 255, 255);
        public Font? titleFont;
        public Font? bodyFont;
    }

    [Serializable]
    public sealed class ArtRefs
    {
        [Header("Meshes")]
        public Mesh? quad;
        public Mesh? arrow;
        public Mesh? ripple;
        public Mesh? haloCircle;
        [Tooltip("Soft-edged strip that line washes stretch along a row or column.")] public Mesh? wash;
        [Header("Materials (templates; pieces use pooled copies)")]
        public Material? clay;
        public Material? veinMaterial;
        public Material? holeRimMaterial;
        public Material? holeDiscMaterial;
        public Material? agateMaterial;
        public Material? trayMaterial;
        public Material? trayHoleMaterial;
        public Material? shadow;
        public Material? halo;
        public Material? rippleMaterial;
        public Material? arrowMaterial;
        public Material? background;
        public Material? grain;
    }

    /// <summary>
    /// One art style (TRD 12.1): palette, material/light/camera/tray dials, HUD theme and the generated
    /// meshes and materials. Another style is another asset. Defaults here are style K: Pebbles; every number
    /// is editable in the inspector, nothing is hard-coded in the view scripts.
    /// </summary>
    [CreateAssetMenu(menuName = "Cascade/Piece Style", fileName = "PieceStyle")]
    public sealed class PieceStyle : ScriptableObject
    {
        public string styleName = "K: Pebbles";
        public PaletteParams palette = new PaletteParams();
        public MaterialParams material = new MaterialParams();
        public SpecialParams specials = new SpecialParams();
        public FxParams fx = new FxParams();
        public LightParams light = new LightParams();
        public CameraParams camera = new CameraParams();
        public TrayParams tray = new TrayParams();
        public HudTheme hud = new HudTheme();
        public ArtRefs art = new ArtRefs();

        [NonSerialized] PieceMaterials? runtimeMaterials;
        [NonSerialized] bool clipPublished;

        static readonly int AmbientId = Shader.PropertyToID("_CascadeAmbient");
        static readonly int SkyId = Shader.PropertyToID("_CascadeSky");
        static readonly int SunColorId = Shader.PropertyToID("_CascadeSunColor");
        static readonly int SunSpecId = Shader.PropertyToID("_CascadeSunSpec");
        static readonly int SunDirId = Shader.PropertyToID("_CascadeSunDir");

        public int PieceCount => palette.pieces.Length;

        int Wrap(int i)
        {
            int n = palette.pieces.Length;
            if (n == 0) throw new InvalidOperationException("PieceStyle '" + name + "' has no pieces.");
            return ((i % n) + n) % n;
        }

        public Color32 PieceColor(int colorIndex) => palette.pieces[Wrap(colorIndex)].color;

        public Mesh PieceMesh(int colorIndex) =>
            palette.pieces[Wrap(colorIndex)].mesh ?? throw new InvalidOperationException("Piece mesh missing: run Cascade/Build Pebble Assets.");

        public Mesh HaloMesh(int colorIndex) =>
            palette.pieces[Wrap(colorIndex)].haloMesh ?? art.haloCircle ?? throw new InvalidOperationException("Halo mesh missing.");

        public Mesh? VeinMesh(int colorIndex, bool vertical) =>
            vertical ? palette.pieces[Wrap(colorIndex)].veinVertical : palette.pieces[Wrap(colorIndex)].veinHorizontal;
        public Mesh? HoleRimMesh(int colorIndex) => palette.pieces[Wrap(colorIndex)].holeRim;
        public Mesh? HoleDiscMesh(int colorIndex) => palette.pieces[Wrap(colorIndex)].holeDisc;
        public Mesh? AgateMesh(int colorIndex) => palette.pieces[Wrap(colorIndex)].agate;

        public PebbleShape PieceShape(int colorIndex) => palette.pieces[Wrap(colorIndex)].shape;

        /// <summary>Shared per-style pool of runtime materials (colour, brightness, fleck variants).</summary>
        public PieceMaterials Materials => runtimeMaterials ??= new PieceMaterials(this);

        /// <summary>Destroys runtime-created materials (call when switching style).</summary>
        public void ReleaseRuntime()
        {
            runtimeMaterials?.Dispose();
            runtimeMaterials = null;
        }

        /// <summary>Unit vector pointing from the scene towards the sun, in Unity world space.</summary>
        public Vector3 SunDirectionToLight()
        {
            // Authored heading/pitch (Z-up) -> Unity forward of the light, then negated.
            Vector3 forward = Quaternion.Euler(-light.sunPitch, -light.sunHeading, 0f) * Vector3.forward;
            return -forward;
        }

        /// <summary>The board shaders draw everywhere (no slot clip). The default whenever no board is on screen.</summary>
        public static void ClipOff()
        {
            Shader.SetGlobalVector(Shader.PropertyToID("_CascadeClip"), new Vector4(-1000f, -1000f, 1000f, 1000f));
            Shader.SetGlobalMatrix(Shader.PropertyToID("_CascadeClipVP"), Matrix4x4.identity);
        }

        /// <summary>Publishes the light rig to the clay/tray shaders (globals; no per-material cost).</summary>
        public void ApplyLightGlobals()
        {
            Shader.SetGlobalVector(AmbientId, light.ambient);
            Shader.SetGlobalVector(SkyId, light.sky);
            Shader.SetGlobalVector(SunColorId, light.sun);
            Shader.SetGlobalVector(SunSpecId, light.sunSpecular);
            Shader.SetGlobalVector(SunDirId, SunDirectionToLight());
            if (!clipPublished) { ClipOff(); clipPublished = true; }
        }
    }
}
