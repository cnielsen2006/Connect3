using System.IO;
using Cascade.Unity.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Cascade.Editor
{
    /// <summary>
    /// Builds every generated asset of style K: Pebbles (meshes, textures, materials, the PieceStyle asset) and
    /// adds the grain Renderer Feature to the project's URP renderer. Idempotent: existing assets are updated in
    /// place (GUIDs stay valid) and inspector tweaks of an existing style asset are kept; only references are refreshed.
    ///   Unity -batchmode -quit -projectPath unity -executeMethod Cascade.Editor.PebbleMeshBuilder.BuildAll
    /// </summary>
    public static class PebbleMeshBuilder
    {
        const string Root = "Assets/Cascade";
        public const string StylePath = Root + "/Styles/K_Pebbles.asset";
        const string MeshDir = Root + "/Art/Meshes";
        const string TexDir = Root + "/Art/Textures";
        const string MatDir = Root + "/Art/Materials";
        const string ShaderDir = Root + "/Art/Shaders";
        const string RendererPath = Root + "/Settings/Cascade_URP_Renderer.asset";
        const string FontDir = Root + "/UI/Fonts";

        static readonly string[] Names = { "Sand", "Sage", "Clay", "Slate", "Charcoal", "Shell", "Seaweed" };
        static readonly uint[] Colors = { 0xcdb48d, 0x8fa08a, 0xb98468, 0x7b8794, 0x4f4a46, 0xece4d6, 0x4e5e3a };
        static readonly PebbleShape[] Shapes =
        {
            PebbleShape.Oval, PebbleShape.Leaf, PebbleShape.RoundedSquare, PebbleShape.Capsule,
            PebbleShape.Disc, PebbleShape.Scallop, PebbleShape.Kidney,
        };

        [MenuItem("Cascade/Build Pebble Assets")]
        public static void BuildAll()
        {
            Folder(Root + "/Styles"); Folder(MeshDir); Folder(MeshDir + "/Specials"); Folder(TexDir); Folder(MatDir);

            var style = LoadOrCreateStyle();
            var a = style.art;
            float h = style.material.heightRatio;
            style.material.bakedHeightRatio = h;

            // ---- textures ----
            var fleck = ArtTextures.Save(ArtTextures.Fleck(128, style.material.fleckCount, 11), TexDir + "/Fleck.png", false, TextureWrapMode.Repeat, true);
            var grain = ArtTextures.Save(ArtTextures.TrayGrain(style, 256, 2024), TexDir + "/TrayGrain.png", true, TextureWrapMode.Repeat, true);
            var blob = ArtTextures.Save(ArtTextures.Blob(64, 1.6f), TexDir + "/BlobShadow.png", false, TextureWrapMode.Clamp, true);

            // ---- meshes ----
            for (int i = 0; i < style.palette.pieces.Length; i++)
            {
                var sw = style.palette.pieces[i];
                sw.mesh = Save(PebbleGeometry.Pebble(sw.shape, h), MeshDir + "/Pebble_" + sw.name + ".asset");
                sw.haloMesh = Save(PebbleGeometry.HaloFor(PebbleGeometry.Outline(sw.shape), "Halo_" + sw.name), MeshDir + "/Halo_" + sw.name + ".asset");
                string sd = MeshDir + "/Specials";
                sw.veinHorizontal = Save(PebbleGeometry.Vein(style, sw.shape, h, false), sd + "/VeinH_" + sw.name + ".asset");
                sw.veinVertical = Save(PebbleGeometry.Vein(style, sw.shape, h, true), sd + "/VeinV_" + sw.name + ".asset");
                sw.holeRim = Save(PebbleGeometry.HoleRim(style, sw.shape, h), sd + "/HoleRim_" + sw.name + ".asset");
                sw.holeDisc = Save(PebbleGeometry.HoleDisc(style, sw.shape, h), sd + "/HoleDisc_" + sw.name + ".asset");
                sw.agate = Save(PebbleGeometry.Agate(style, sw.shape, h), sd + "/Agate_" + sw.name + ".asset");
            }
            a.haloCircle = Save(PebbleGeometry.HaloCircle(), MeshDir + "/Halo_Circle.asset");
            a.quad = Save(PebbleGeometry.Quad(), MeshDir + "/Quad.asset");
            a.arrow = Save(PebbleGeometry.Arrow(), MeshDir + "/SwapArrow.asset");
            a.ripple = Save(PebbleGeometry.Ripple(), MeshDir + "/Ripple.asset");
            a.wash = Save(PebbleGeometry.Wash(), MeshDir + "/Wash.asset");

            // ---- materials ----
            var clay = LoadShader(ShaderDir + "/Clay.shader");
            var tray = LoadShader(ShaderDir + "/Tray.shader");
            var fx = LoadShader(ShaderDir + "/FxUnlit.shader");
            var m = style.material;

            a.clay = Mat("Clay", clay, mt =>
            {
                ClayDefaults(mt, m, fleck, Color.white, 1f, new Vector4(0, 0, m.fleckDark, m.fleckLight));
            });
            a.veinMaterial = Mat("Vein", clay, mt =>
                ClayDefaults(mt, m, fleck, Rgb(style.palette.vein), 1f, Vector4.zero, sheen: 0.28f, shininess: 28f));
            a.holeRimMaterial = Mat("HoleRim", clay, mt =>
                ClayDefaults(mt, m, fleck, Rgb(style.palette.vein) * 0.92f, 1f, Vector4.zero, sheen: 0.2f, shininess: 24f));
            a.holeDiscMaterial = Mat("HoleDisc", fx, mt =>
            {
                Fx(mt, Rgb(style.palette.hole), null, opaque: true);
                mt.renderQueue = 2002;
            });
            a.agateMaterial = Mat("Agate", clay, mt =>
            {
                ClayDefaults(mt, m, fleck, Color.white, 1f, Vector4.zero, sheen: 0.16f, shininess: 24f);
                mt.SetFloat("_UseBands", 1f);
                mt.EnableKeyword("_BANDS");
            });
            a.trayMaterial = Mat("Tray", tray, mt =>
            {
                mt.SetTexture("_MainTex", grain);
                mt.SetColor("_BaseColor", Color.white);
                mt.SetFloat("_Brightness", style.tray.brightness);
                mt.SetVector("_GrainTiling", new Vector4(style.tray.grainTiling.x, style.tray.grainTiling.y, 0, 0));
                mt.SetFloat("_Sheen", 0.03f);
            });
            a.trayHoleMaterial = Mat("TrayHole", clay, mt =>
                ClayDefaults(mt, m, fleck, Rgb(style.tray.holeFloor), 1f, Vector4.zero, sheen: 0f));
            a.shadow = Mat("Shadow", fx, mt => Fx(mt, style.fx.shadowColor, blob, opaque: false));
            a.halo = Mat("Halo", fx, mt => Fx(mt, style.fx.haloColor, null, opaque: false, offset: -2f));
            a.rippleMaterial = Mat("Ripple", fx, mt => Fx(mt, style.fx.rippleColor, null, opaque: false, offset: -2f));
            a.arrowMaterial = Mat("SwapArrow", fx, mt => Fx(mt, style.fx.arrowColor, null, opaque: false, offset: -2f));
            a.background = Mat("Background", LoadShader(ShaderDir + "/Background.shader"), mt =>
            {
                mt.SetColor("_TopColor", Rgb(style.palette.bgTop));
                mt.SetColor("_BottomColor", Rgb(style.palette.bgBottom));
            });
            a.grain = Mat("Grain", LoadShader(ShaderDir + "/Grain.shader"), mt =>
                mt.SetFloat("_GrainAlpha", m.grainAlpha));

            // ---- fonts (UI Toolkit reads the TTFs; see StyleApplier) ----
            style.hud.bodyFont = AssetDatabase.LoadAssetAtPath<Font>(FontDir + "/Delius-Regular.ttf");
            style.hud.titleFont = AssetDatabase.LoadAssetAtPath<Font>(FontDir + "/DeliusSwashCaps-Regular.ttf");

            EditorUtility.SetDirty(style);
            AssetDatabase.SaveAssets();

            EnsureGrainFeature(a.grain);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Cascade] Pebble assets built: " + StylePath);
        }

        // ---- style asset ----

        static PieceStyle LoadOrCreateStyle()
        {
            var style = AssetDatabase.LoadAssetAtPath<PieceStyle>(StylePath);
            if (style == null)
            {
                style = ScriptableObject.CreateInstance<PieceStyle>();
                AssetDatabase.CreateAsset(style, StylePath);
            }
            if (style.palette.pieces.Length == 0)
            {
                style.palette.pieces = new PieceSwatch[Names.Length];
                for (int i = 0; i < Names.Length; i++)
                    style.palette.pieces[i] = new PieceSwatch
                    {
                        name = Names[i],
                        color = new Color32((byte)(Colors[i] >> 16), (byte)(Colors[i] >> 8), (byte)Colors[i], 255),
                        shape = Shapes[i],
                    };
            }
            return style;
        }

        // ---- asset helpers ----

        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)!.Replace('\\', '/');
            Folder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static T Save<T>(T fresh, string path) where T : Object
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(fresh, path);
                return fresh;
            }
            EditorUtility.CopySerialized(fresh, existing);
            Object.DestroyImmediate(fresh);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        static Shader LoadShader(string path) =>
            AssetDatabase.LoadAssetAtPath<Shader>(path) ?? throw new FileNotFoundException("Shader missing: " + path);

        static Material Mat(string name, Shader shader, System.Action<Material> setup)
        {
            string path = MatDir + "/" + name + ".mat";
            var mt = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mt == null)
            {
                mt = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(mt, path);
            }
            mt.shader = shader;
            setup(mt);
            EditorUtility.SetDirty(mt);
            return mt;
        }

        static Color Rgb(Color32 c) => new Color(c.r / 255f, c.g / 255f, c.b / 255f, 1f);

        static void ClayDefaults(Material mt, MaterialParams m, Texture fleck, Color baseColor, float brightness, Vector4 fleckParams,
            float? sheen = null, float? shininess = null)
        {
            mt.SetColor("_BaseColor", baseColor);
            mt.SetFloat("_Brightness", brightness);
            mt.SetTexture("_FleckTex", fleck);
            mt.SetVector("_FleckParams", fleckParams);
            mt.SetFloat("_Sheen", sheen ?? m.sheen);
            mt.SetFloat("_Shininess", shininess ?? m.shininess);
            mt.SetFloat("_RimStrength", m.rimStrength);
            mt.SetFloat("_RimPower", m.rimPower);
        }

        static void Fx(Material mt, Color color, Texture? tex, bool opaque, float offset = -1f)
        {
            mt.SetColor("_Color", color);
            if (tex != null) mt.SetTexture("_MainTex", tex);
            mt.SetFloat("_SrcBlend", opaque ? (float)BlendMode.One : (float)BlendMode.SrcAlpha);
            mt.SetFloat("_DstBlend", opaque ? (float)BlendMode.Zero : (float)BlendMode.OneMinusSrcAlpha);
            mt.SetFloat("_ZWrite", opaque ? 1f : 0f);
            mt.SetFloat("_OffsetFactor", offset);
            mt.SetFloat("_OffsetUnits", offset);
            if (!opaque) mt.renderQueue = 3000;
        }

        // ---- grain renderer feature ----

        const string FeatureName = "CascadeGrain";

        /// <summary>Adds (or refreshes) the full-screen grain pass on the project's URP renderer.</summary>
        public static void EnsureGrainFeature(Material grain)
        {
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (data == null) { Debug.LogWarning("[Cascade] URP renderer asset not found; run Cascade/Apply Project Setup first."); return; }

            FullScreenPassRendererFeature? feature = null;
            foreach (var f in data.rendererFeatures)
                if (f is FullScreenPassRendererFeature fs && fs.name == FeatureName) feature = fs;

            if (feature == null)
            {
                feature = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
                feature.name = FeatureName;
                AssetDatabase.AddObjectToAsset(feature, data);
                AssetDatabase.SaveAssets();
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);
                var so = new SerializedObject(data);
                var features = so.FindProperty("m_RendererFeatures");
                var map = so.FindProperty("m_RendererFeatureMap");
                features.arraySize++;
                features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
                map.arraySize++;
                map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            feature.passMaterial = grain;
            feature.passIndex = 0;
            feature.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.AfterRenderingPostProcessing;
            feature.fetchColorBuffer = true;
            feature.requirements = ScriptableRenderPassInput.None;
            feature.SetActive(true);
            EditorUtility.SetDirty(feature);
            EditorUtility.SetDirty(data);
        }
    }
}
