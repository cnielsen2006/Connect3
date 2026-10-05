using System;
using System.Collections.Generic;
using System.IO;
using Cascade.Unity.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Cascade.Editor
{
    /// <summary>
    /// Renders review PNGs of the style without entering play mode (works in -batchmode with a GPU, no -nographics):
    ///   Unity -batchmode -quit -projectPath unity -logFile unity-art.log -executeMethod Cascade.Editor.StyleShowcase.RenderAll [-shots DIR]
    /// Writes board 1280x800 / 1080x1920, a shaped board with holes, a contact sheet and a 64 px/piece top-down silhouette check.
    /// </summary>
    public static class StyleShowcase
    {
        const int Supersample = 2;

        static string OutDir()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-shots") return args[i + 1];
            return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "shots"));
        }

        [MenuItem("Cascade/Render Style Showcase")]
        public static void RenderAll()
        {
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                EditorSettings.asyncShaderCompilation = false;
                PebbleMeshBuilder.BuildAll();
                LogShaders();
                var style = AssetDatabase.LoadAssetAtPath<PieceStyle>(PebbleMeshBuilder.StylePath);
                string dir = OutDir();
                Directory.CreateDirectory(dir);

                Board(style, 8, 8, null, 1280, 800, Path.Combine(dir, "board_1280x800.png"));
                Board(style, 8, 8, null, 1080, 1920, Path.Combine(dir, "board_1080x1920.png"));
                Board(style, 8, 8, Shaped, 1280, 800, Path.Combine(dir, "board_holes_1280x800.png"));
                ContactSheet(style, Path.Combine(dir, "contact_sheet.png"));
                Silhouettes(style, Path.Combine(dir, "silhouettes_64px.png"));
                Debug.Log("[Cascade] Showcase written to " + dir);
            }
            catch (Exception e)
            {
                Debug.LogError("[Cascade] Showcase failed: " + e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        /// <summary>Logs shader compile messages and SRP Batcher compatibility (reflection on the editor-internal check).</summary>
        static void LogShaders()
        {
            foreach (var name in new[] { "Clay", "Tray", "FxUnlit", "Background", "Grain" })
            {
                var sh = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Cascade/Art/Shaders/" + name + ".shader");
                if (sh == null) { Debug.LogWarning("[Cascade] shader missing: " + name); continue; }
                int msgs = ShaderUtil.GetShaderMessageCount(sh);
                Debug.Log("[Cascade] shader " + name + ": supported=" + sh.isSupported + " messages=" + msgs);
                for (int i = 0; i < msgs; i++) Debug.Log("[Cascade]   " + ShaderUtil.GetShaderMessages(sh)[i].message);
            }
        }

        // ---- scene pieces ----

        sealed class Stage : IDisposable
        {
            public readonly GameObject Root;
            public readonly Camera Cam;
            public readonly StyleApplier Applier;
            public readonly PieceStyle Style;

            public Stage(PieceStyle unused)
            {
                // NewScene unloads unreferenced assets, so the style is loaded only after it.
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var style = AssetDatabase.LoadAssetAtPath<PieceStyle>(PebbleMeshBuilder.StylePath);
                Style = style;
                Root = new GameObject("Showcase");
                var camGo = new GameObject("Camera") { tag = "MainCamera" };
                camGo.transform.SetParent(Root.transform, false);
                Cam = camGo.AddComponent<Camera>();
                Applier = new GameObject("Style").AddComponent<StyleApplier>();
                Applier.transform.SetParent(Root.transform, false);
                Applier.Apply(style, Cam);
            }

            public void Dispose() { UnityEngine.Object.DestroyImmediate(Root); style_release(); }
            void style_release() { Style.ReleaseRuntime(); }
        }

        static bool Shaped(int x, int y)
        {
            int c = 8;
            if (x + y < 2 || (c - 1 - x) + y < 2 || x + (c - 1 - y) < 2 || (c - 1 - x) + (c - 1 - y) < 2) return false; // cut corners
            if (x >= 3 && x <= 4 && y >= 3 && y <= 4) return false;                                                  // 2x2 hole
            if (x == 6 && y == 2) return false;                                                                       // single-cell hole
            return true;
        }

        static int[] Arrange(int cols, int rows, Func<int, int, bool> has, int seed, int colors)
        {
            var col = new int[cols * rows];
            var rng = new System.Random(seed);
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < cols; x++)
                {
                    int c;
                    for (int tries = 0; ; tries++)
                    {
                        c = rng.Next(colors);
                        if (tries > 20) break;
                        if (x >= 2 && col[y * cols + x - 1] == c && col[y * cols + x - 2] == c) continue;
                        if (y >= 2 && col[(y - 1) * cols + x] == c && col[(y - 2) * cols + x] == c) continue;
                        break;
                    }
                    col[y * cols + x] = c;
                }
            return col;
        }

        static PieceView Piece(Stage s, Transform parent, int cols, int rows, int x, int y, int color, PieceSpecial special, int id)
        {
            var v = PieceFactory.Create(s.Style, parent, color, special, PieceLook.FromId(id));
            v.BasePosition = BoardTray.CellToLocal(cols, rows, x, y);
            v.ApplyPose();
            return v;
        }

        static BoardTray MakeTray(Stage s, Transform parent, int cols, int rows, Func<int, int, bool> has)
        {
            var go = new GameObject("Tray");
            go.transform.SetParent(parent, false);
            var tray = go.AddComponent<BoardTray>();
            tray.Build(s.Style, cols, rows, has);
            return tray;
        }

        // ---- shots ----

        static void Board(PieceStyle style, int cols, int rows, Func<int, int, bool>? has, int w, int h, string path)
        {
            using var s = new Stage(style);
            Func<int, int, bool> present = has ?? ((x, y) => true);
            var board = new GameObject("Board").transform;
            board.SetParent(s.Root.transform, false);
            var tray = MakeTray(s, board, cols, rows, present);
            var colors = Arrange(cols, rows, present, 7, 7);
            var specials = new Dictionary<(int, int), (PieceSpecial, int)>
            {
                [(2, 2)] = (PieceSpecial.LineHorizontal, 2),
                [(5, 4)] = (PieceSpecial.LineVertical, 1),
                [(4, 6)] = (PieceSpecial.Bomb, 0),
                [(1, 5)] = (PieceSpecial.Color, 3),
            };
            int id = 1;
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < cols; x++)
                {
                    if (!present(x, y)) { id++; continue; }
                    int c = colors[y * cols + x];
                    var sp = PieceSpecial.None;
                    if (specials.TryGetValue((x, y), out var spec)) { sp = spec.Item1; c = spec.Item2; }
                    var v = Piece(s, board, cols, rows, x, y, c, sp, id++);
                    if (x == 3 && y == 3) { v.SelectT = 1f; v.ApplyPose(); }
                }
            s.Cam.aspect = w / (float)h;
            var b = tray.LocalBounds;
            s.Applier.FitCamera(b);
            Render(s.Cam, w, h, path);
        }

        static void ContactSheet(PieceStyle style, string path)
        {
            using var s = new Stage(style);
            const int cols = 7, rows = 2;
            var board = new GameObject("Board").transform;
            board.SetParent(s.Root.transform, false);
            var tray = MakeTray(s, board, cols, rows, (x, y) => true);
            for (int c = 0; c < 7; c++) Piece(s, board, cols, rows, c, 0, c, PieceSpecial.None, 3 + c);
            Piece(s, board, cols, rows, 0, 1, 2, PieceSpecial.LineHorizontal, 20);
            Piece(s, board, cols, rows, 1, 1, 1, PieceSpecial.LineVertical, 21);
            Piece(s, board, cols, rows, 2, 1, 0, PieceSpecial.Bomb, 22);
            Piece(s, board, cols, rows, 3, 1, 3, PieceSpecial.Color, 23);
            Piece(s, board, cols, rows, 4, 1, 4, PieceSpecial.None, 24).SelectT = 1f;
            var sink = Piece(s, board, cols, rows, 5, 1, 5, PieceSpecial.None, 25); sink.Sink = 0.5f; sink.ApplyPose();
            var roll = Piece(s, board, cols, rows, 6, 1, 6, PieceSpecial.None, 26); roll.Roll = 12f; roll.Squash = 0.85f; roll.ApplyPose();
            foreach (var v in board.GetComponentsInChildren<PieceView>()) v.ApplyPose();
            const int w = 1600, h = 520;
            s.Cam.aspect = w / (float)h;
            s.Applier.FitCamera(tray.LocalBounds, null, 0.02f);
            Render(s.Cam, w, h, path);
        }

        static void Silhouettes(PieceStyle style, string path)
        {
            using var s = new Stage(style);
            const int cols = 11;
            var board = new GameObject("Board").transform;
            board.SetParent(s.Root.transform, false);
            var shaded = new GameObject("Shaded").transform;
            shaded.SetParent(board, false);
            shaded.localPosition = new Vector3(0f, 0f, 1f);
            var tray = MakeTray(s, shaded, cols, 1, (x, y) => true);
            var plain = new GameObject("Plain").transform;
            plain.SetParent(board, false);
            plain.localPosition = new Vector3(0f, 0f, -1f);

            var black = new Material(s.Style.art.holeDiscMaterial) { hideFlags = HideFlags.HideAndDontSave };
            black.SetColor("_Color", Color.black);
            var specials = new[] { PieceSpecial.None, PieceSpecial.None, PieceSpecial.None, PieceSpecial.None, PieceSpecial.None, PieceSpecial.None, PieceSpecial.None,
                PieceSpecial.LineHorizontal, PieceSpecial.LineVertical, PieceSpecial.Bomb, PieceSpecial.Color };
            var colors = new[] { 0, 1, 2, 3, 4, 5, 6, 2, 2, 0, 3 };
            for (int i = 0; i < cols; i++)
            {
                Piece(s, shaded, cols, 1, i, 0, colors[i], specials[i], 40 + i);
                var v = Piece(s, plain, cols, 1, i, 0, colors[i], PieceSpecial.None, 40 + i);
                foreach (var r in v.GetComponentsInChildren<MeshRenderer>())
                {
                    if (r.name == "Body") r.sharedMaterial = black; else r.enabled = false;
                }
            }
            var cam = s.Cam;
            cam.orthographic = true;
            cam.orthographicSize = 2f;
            cam.transform.SetPositionAndRotation(new Vector3(0f, 20f, 0f), Quaternion.Euler(90f, 0f, 0f));
            Render(cam, 768, 256, path);
            UnityEngine.Object.DestroyImmediate(black);
        }

        // ---- render to PNG (supersampled, box-filtered) ----

        static void Render(Camera cam, int w, int h, string path)
        {
            int W = w * Supersample, H = h * Supersample;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            var prev = RenderTexture.active;
            cam.targetTexture = rt;
            cam.aspect = w / (float)h;
            cam.Render();
            RenderTexture.active = rt;
            var big = new Texture2D(W, H, TextureFormat.RGBA32, false);
            big.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            big.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);

            var src = big.GetPixels32();
            var dst = new Color32[w * h];
            int n = Supersample * Supersample;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int r = 0, g = 0, b = 0;
                    for (int sy = 0; sy < Supersample; sy++)
                        for (int sx = 0; sx < Supersample; sx++)
                        {
                            var p = src[(y * Supersample + sy) * W + x * Supersample + sx];
                            r += p.r; g += p.g; b += p.b;
                        }
                    dst[y * w + x] = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), 255);
                }
            var small = new Texture2D(w, h, TextureFormat.RGBA32, false);
            small.SetPixels32(dst);
            small.Apply();
            File.WriteAllBytes(path, small.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(big);
            UnityEngine.Object.DestroyImmediate(small);
            Debug.Log("[Cascade] wrote " + path);
        }
    }
}
