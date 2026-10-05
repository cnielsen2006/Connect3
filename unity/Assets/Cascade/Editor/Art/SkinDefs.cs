using System;
using Cascade.Unity.View;
using UnityEngine;

namespace Cascade.Editor
{
    /// <summary>
    /// What differs between the art styles when <see cref="PebbleMeshBuilder"/> builds one: the palette, the silhouettes, the baked wobble and a
    /// <see cref="Configure"/> that sets the look dials of a newly created <see cref="PieceStyle"/> (an existing asset keeps its inspector tweaks).
    /// K keeps its original flat asset paths; the others build into a sub folder.
    /// </summary>
    public sealed class SkinDef
    {
        public string Id = "";
        public string StyleName = "";
        public string StyleFile = "";
        /// <summary>Sub folder of Art/Meshes and Art/Materials ("" for K, whose assets predate skins).</summary>
        public string Sub = "";
        public string[] Names = Array.Empty<string>();
        public uint[] Colors = Array.Empty<uint>();
        public PebbleShape[] Shapes = Array.Empty<PebbleShape>();
        public float[]? HeightScale;
        public float Wobble;
        public string BodyFont = "Delius-Regular.ttf", TitleFont = "DeliusSwashCaps-Regular.ttf";
        public Action<PieceStyle> Configure = _ => { };
    }

    public static class SkinDefs
    {
        static Color32 Hex(uint c) => new Color32((byte)(c >> 16), (byte)(c >> 8), (byte)c, 255);

        public static readonly SkinDef K = new SkinDef
        {
            Id = "K", StyleName = "K: Pebbles", StyleFile = "K_Pebbles.asset",
            Names = new[] { "Sand", "Sage", "Clay", "Slate", "Charcoal", "Shell", "Seaweed" },
            Colors = new uint[] { 0xcdb48d, 0x8fa08a, 0xb98468, 0x7b8794, 0x4f4a46, 0xece4d6, 0x4e5e3a },
            Shapes = new[]
            {
                PebbleShape.Oval, PebbleShape.Leaf, PebbleShape.RoundedSquare, PebbleShape.Capsule,
                PebbleShape.Disc, PebbleShape.Scallop, PebbleShape.Kidney,
            },
        };

        // I and J share silhouettes (cube, dome, triangle, puck, teardrop, star, diamond); a puck is a low disc, a dome a tall one.
        static readonly PebbleShape[] ClayShapes =
        {
            PebbleShape.Cube, PebbleShape.Dome, PebbleShape.Triangle, PebbleShape.Disc,
            PebbleShape.Teardrop, PebbleShape.Star, PebbleShape.Diamond,
        };
        static readonly float[] ClayHeights = { 1f, 1.3f, 1f, 0.7f, 1f, 1f, 1f };

        /// <summary>STYLE-I-Claymation.md: rough plasticine on kraft paper, warm daylight, the most wobble.</summary>
        public static readonly SkinDef I = new SkinDef
        {
            Id = "I", StyleName = "I: Claymation", StyleFile = "I_Claymation.asset", Sub = "I",
            Names = new[] { "Terracotta", "Ochre", "Moss", "Slate", "Mulberry", "Bone", "Umber" },
            Colors = new uint[] { 0xb8613f, 0xc89b3c, 0x6b7d46, 0x5d7a8c, 0x7d4e63, 0xe9dcc6, 0x3b2d24 },
            Shapes = ClayShapes, HeightScale = ClayHeights, Wobble = 0.05f,
            BodyFont = "ShantellSans-Regular.ttf", TitleFont = "ShantellSans-Regular.ttf",
            Configure = s =>
            {
                s.skinId = "I"; s.styleName = "I: Claymation";
                var p = s.palette;
                p.bgTop = Hex(0xe3d6bf); p.bgBottom = Hex(0xc9b89c);
                p.tray = Hex(0x8c6a4e); p.trayGrain = Hex(0x6b4e3a); p.trayLip = Hex(0x7a5a42);
                p.text = Hex(0x3b2d24); p.accent = Hex(0xb8613f);
                p.vein = Hex(0xe9dcc6); p.hole = Hex(0x2b2018);
                var m = s.material;
                m.heightRatio = 0.5f; m.bakedHeightRatio = 0.5f;
                m.sheen = 0.03f; m.shininess = 6f; m.rimStrength = 0.06f;    // matte plasticine: no specular
                m.fleckCount = 4; m.fleckDark = 0.55f; m.fleckLight = 0.30f; // thumbprints and tool marks
                m.selectScale = 1.08f; m.grainAlpha = 0.22f;
                s.fx.shadowColor = new Color(0.30f, 0.20f, 0.12f, 0.50f);
                var l = s.light;   // soft warm key from the upper left plus a gentle fill
                l.ambient = new Color(0.52f, 0.47f, 0.42f, 1f); l.sky = new Color(0.22f, 0.21f, 0.20f, 1f);
                l.sun = new Color(0.34f, 0.28f, 0.20f, 1f); l.sunSpecular = new Color(0.20f, 0.17f, 0.13f, 1f);
                l.sunHeading = -35f; l.sunPitch = -50f;
                s.camera.pitchDeg = 30f;
                var t = s.tray;
                t.lip = 0.18f; t.edgeRound = 0.08f; t.innerFillet = 0.06f; t.brightness = 1.05f;
                t.holeFloor = Hex(0x45311f);
                s.hud.buttonPause = Hex(0x6b7d46); s.hud.buttonHint = Hex(0xe9dcc6);
            },
        };

        /// <summary>STYLE-J-StudioSet.md: a stop-motion set at night, one hard key light, spotlight and vignette, film grain.</summary>
        public static readonly SkinDef J = new SkinDef
        {
            Id = "J", StyleName = "J: Studio Set", StyleFile = "J_StudioSet.asset", Sub = "J",
            Names = new[] { "Rust", "Mustard", "Olive", "Teal", "Aubergine", "Bone", "CharcoalBlue" },
            Colors = new uint[] { 0xa3492d, 0xc9a227, 0x7a7a3a, 0x3f7a78, 0x5e3a5a, 0xd8cbb2, 0x2f3b4a },
            Shapes = ClayShapes, HeightScale = ClayHeights, Wobble = 0.03f,
            BodyFont = "PatrickHand-Regular.ttf", TitleFont = "PatrickHandSC-Regular.ttf",
            Configure = s =>
            {
                s.skinId = "J"; s.styleName = "J: Studio Set";
                var p = s.palette;
                p.bgTop = Hex(0x4a3a30); p.bgBottom = Hex(0x0e0a09); p.vignette = 0.55f;
                p.tray = Hex(0xa87d4f); p.trayGrain = Hex(0x9c7146); p.trayLip = Hex(0x8f6a3f);
                p.text = Hex(0xefe6d6); p.accent = Hex(0xc9a227);
                p.vein = Hex(0xd8cbb2); p.hole = Hex(0x120d0b);
                var m = s.material;
                m.heightRatio = 0.5f; m.bakedHeightRatio = 0.5f;
                m.sheen = 0.05f; m.shininess = 10f;
                m.rimStrength = 0.38f; m.rimPower = 2.4f;                    // stands in for the per-piece coloured rim light
                m.fleckCount = 3; m.fleckDark = 0.45f; m.fleckLight = 0.35f; // fingerprint
                m.selectScale = 1.08f; m.grainAlpha = 0.35f;
                s.fx.shadowColor = new Color(0f, 0f, 0f, 0.78f); s.fx.shadowSize = 1.12f;
                s.fx.haloColor = new Color(0.98f, 0.96f, 0.90f, 1f);          // paper-white outline ring
                var l = s.light;   // one hard key from the upper right, about 40 degrees above the board, very low fill
                l.ambient = new Color(0.22f, 0.18f, 0.16f, 1f); l.sky = new Color(0.08f, 0.08f, 0.10f, 1f);
                l.sun = new Color(0.66f, 0.56f, 0.42f, 1f); l.sunSpecular = new Color(0.30f, 0.26f, 0.20f, 1f);
                l.sunHeading = 40f; l.sunPitch = -40f;
                s.camera.pitchDeg = 35f;
                var t = s.tray;
                t.lip = 0.2f; t.lipHeight = 0.12f; t.edgeRound = 0.04f; t.brightness = 1.0f;
                t.holeFloor = Hex(0x3a2a1c);
                s.hud.buttonPause = Hex(0x3f7a78); s.hud.buttonHint = Hex(0x7a7a3a);
            },
        };

        public static readonly SkinDef[] All = { K, I, J };
    }
}
