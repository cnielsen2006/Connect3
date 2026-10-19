using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Cascade.Editor
{
    /// <summary>
    /// Points Player Settings at the app icon PNGs in Assets/Cascade/Art/Icon (made by tools/icon/make_icon.py):
    /// the default icon (Windows exe, desktop), and the Android adaptive (background + foreground), round and legacy icons.
    /// Also sets the startup splash: Unity's "Made with Unity" logo is off and the Cascade logo (splash-logo.png) shows instead.
    /// Idempotent; ProjectSetup.Apply calls both so every build carries them.
    /// </summary>
    public static class IconSetup
    {
        const string Dir = "Assets/Cascade/Art/Icon";

        [MenuItem("Cascade/Apply App Icon")]
        public static void Apply()
        {
            var master = Import("icon-1024.png");
            if (master == null)
            {
                Debug.LogWarning($"IconSetup: {Dir}/icon-1024.png not found, leaving the default icon (run tools/icon/make_icon.py).");
                return;
            }

            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { master }, IconKind.Application);

            var background = Import("icon-adaptive-bg-432.png");
            var foreground = Import("icon-adaptive-fg-432.png");
            var round = Import("icon-round-192.png");
            var legacy = Import("icon-legacy-192.png");

            foreach (var kind in PlayerSettings.GetSupportedIconKinds(NamedBuildTarget.Android))
            {
                string name = kind.ToString();
                var icons = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
                foreach (var icon in icons)
                {
                    if (name.Contains("Adaptive"))
                    {
                        icon.SetTexture(background, 0);
                        icon.SetTexture(foreground, 1);
                    }
                    else if (name.Contains("Round")) icon.SetTexture(round, 0);
                    else if (name.Contains("Legacy")) icon.SetTexture(legacy, 0);
                }
                PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, kind, icons);
            }
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Cascade/Apply Splash Screen")]
        public static void ApplySplash()
        {
            var logo = ImportSprite("splash-logo.png");
            if (logo == null)
            {
                Debug.LogWarning($"IconSetup: {Dir}/splash-logo.png not found, leaving the splash as is (run tools/icon/make_icon.py).");
                return;
            }

            PlayerSettings.SplashScreen.show = true;
            PlayerSettings.SplashScreen.showUnityLogo = false;
            PlayerSettings.SplashScreen.drawMode = PlayerSettings.SplashScreen.DrawMode.AllSequential;
            PlayerSettings.SplashScreen.animationMode = PlayerSettings.SplashScreen.AnimationMode.Static;
            PlayerSettings.SplashScreen.backgroundColor = new Color32(0xe8, 0xdf, 0xce, 0xff); // pale sand, as the title screen
            PlayerSettings.SplashScreen.logos = new[] { PlayerSettings.SplashScreenLogo.Create(2f, logo) };
            AssetDatabase.SaveAssets();
        }

        static Texture2D? Import(string file)
        {
            string path = $"{Dir}/{file}";
            if (!File.Exists(path)) return null;
            if (AssetImporter.GetAtPath(path) is TextureImporter ti)
            {
                // Icons stay crisp: no mips, no NPOT rescale, no compression.
                if (ti.mipmapEnabled || ti.npotScale != TextureImporterNPOTScale.None
                    || ti.textureCompression != TextureImporterCompression.Uncompressed || !ti.alphaIsTransparency)
                {
                    ti.textureType = TextureImporterType.Default;
                    ti.mipmapEnabled = false;
                    ti.npotScale = TextureImporterNPOTScale.None;
                    ti.textureCompression = TextureImporterCompression.Uncompressed;
                    ti.alphaIsTransparency = true;
                    ti.SaveAndReimport();
                }
            }
            else AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Sprite? ImportSprite(string file)
        {
            string path = $"{Dir}/{file}";
            if (!File.Exists(path)) return null;
            AssetDatabase.ImportAsset(path);
            if (AssetImporter.GetAtPath(path) is TextureImporter ti
                && (ti.textureType != TextureImporterType.Sprite || ti.mipmapEnabled || ti.textureCompression != TextureImporterCompression.Uncompressed))
            {
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.mipmapEnabled = false;
                ti.alphaIsTransparency = true;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
    }
}
