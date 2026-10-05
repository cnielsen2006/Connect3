using System;
using System.Collections;
using System.Collections.Generic;
using Cascade.Core;
using Cascade.Gameplay;
using Cascade.Presentation;
using Cascade.Presentation.Shell;
using Cascade.Unity.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Cascade.Tests.Play
{
    /// <summary>
    /// U6 acceptance (STYLE-K-Pebbles.md): checks the numbers of the K doc against the style asset and the running pieces, and renders
    /// boards and deterministic frame sequences (the timeline clock is advanced by hand, see <see cref="TimelinePlayer.ManualClock"/>)
    /// into Builds/shots/art for review: a swap, a fall, a clear, a cascade, and one fire of each special.
    /// </summary>
    public class ArtAudit
    {
        PlayHarness h = null!;
        const int Seed = 20241004;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (h.App != null && h.App.Stage != null) h.App.Stage.Player.ManualClock = false;
            return h.TearDown();
        }

        static Color32 Hex(string s) =>
            new Color32(Convert.ToByte(s.Substring(0, 2), 16), Convert.ToByte(s.Substring(2, 2), 16), Convert.ToByte(s.Substring(4, 2), 16), 255);

        static void AssertColor(string what, Color32 actual, string hex)
        {
            var e = Hex(hex);
            Assert.That((actual.r, actual.g, actual.b), Is.EqualTo((e.r, e.g, e.b)), what + " is #" + hex);
        }

        [UnityTest]
        public IEnumerator The_style_asset_holds_the_values_of_the_K_doc()
        {
            h = new PlayHarness();
            yield return h.SetUpInLevel(1, Seed);
            var s = h.App.Stage.View.Style;
            var p = s.palette;
            Assert.That(p.pieces.Length, Is.EqualTo(7));
            string[] hex = { "cdb48d", "8fa08a", "b98468", "7b8794", "4f4a46", "ece4d6", "4e5e3a" };
            string[] names = { "Sand", "Sage", "Clay", "Slate", "Charcoal", "Shell", "Seaweed" };
            var shapes = new HashSet<PebbleShape>();
            for (int i = 0; i < 7; i++)
            {
                AssertColor(names[i], p.pieces[i].color, hex[i]);
                Assert.That(p.pieces[i].mesh, Is.Not.Null, names[i] + " has a mesh");
                shapes.Add(p.pieces[i].shape);
            }
            Assert.That(shapes.Count, Is.EqualTo(7), "seven distinct silhouettes");
            AssertColor("bgTop", p.bgTop, "efe8db");
            AssertColor("bgBottom", p.bgBottom, "dfd4c1");
            AssertColor("tray", p.tray, "d9c7a6");
            AssertColor("trayGrain", p.trayGrain, "c9b48f");
            AssertColor("text", p.text, "4f4a46");
            AssertColor("accent", p.accent, "b98468");
            AssertColor("pause", s.hud.buttonPause, "8fa08a");
            AssertColor("hint", s.hud.buttonHint, "cdb48d");
            Assert.That(s.material.heightRatio, Is.EqualTo(0.45f).Within(1e-4));
            Assert.That(s.material.selectLift, Is.EqualTo(0.15f).Within(1e-4));
            Assert.That(s.material.selectScale, Is.EqualTo(1.10f).Within(1e-4));
            Assert.That(s.material.grainAlpha, Is.EqualTo(0.16f).Within(1e-4));
            Assert.That(s.material.fleckCount, Is.InRange(2, 3));
            Assert.That(s.camera.pitchDeg, Is.EqualTo(25f).Within(1e-4));
            Assert.That(s.hud.titleFont, Is.Not.Null);
            Assert.That(s.hud.bodyFont, Is.Not.Null);
            Assert.That(s.hud.titleFont!.name, Does.Contain("DeliusSwashCaps"));
            Assert.That(s.hud.bodyFont!.name, Does.Contain("Delius"));
            Assert.That(s.art.grain!.GetFloat("_GrainAlpha"), Is.EqualTo(s.material.grainAlpha).Within(1e-3), "grain material follows the style");

            var cam = h.App.Rig.UnityCamera.transform;
            float pitch = Vector3.Angle(-cam.forward, Vector3.up);
            Debug.Log("[ArtAudit] camera pitch from vertical " + pitch + " fov " + h.App.Rig.UnityCamera.fieldOfView);
            Assert.That(pitch, Is.EqualTo(25f).Within(0.6f));
        }

        [UnityTest]
        public IEnumerator Per_instance_variation_follows_the_pieces_dials()
        {
            h = new PlayHarness();
            yield return h.SetUpInLevel(40, Seed);
            double yawMin = 99, yawMax = -99, sqMin = 9, sqMax = 0, brMin = 9, brMax = 0;
            var offs = new HashSet<(int, int)>();
            int n = 0;
            foreach (var c in h.Session.Board.Topology.Cells)
            {
                var t = h.Session.Board.TileAt(c);
                if (t == null) continue;
                var l = h.View.Get(t.Id)!.Look;
                yawMin = Math.Min(yawMin, l.YawDegrees); yawMax = Math.Max(yawMax, l.YawDegrees);
                sqMin = Math.Min(sqMin, l.Squash); sqMax = Math.Max(sqMax, l.Squash);
                brMin = Math.Min(brMin, l.Brightness); brMax = Math.Max(brMax, l.Brightness);
                offs.Add((Mathf.RoundToInt((float)l.FleckOffsetX * 10), Mathf.RoundToInt((float)l.FleckOffsetY * 10)));
                n++;
            }
            Debug.Log("[ArtAudit] variation over " + n + " pieces: yaw " + yawMin + ".." + yawMax + " squash " + sqMin + ".." + sqMax + " brightness " + brMin + ".." + brMax + " fleck positions " + offs.Count);
            Assert.That(yawMin, Is.GreaterThanOrEqualTo(-13.001)); Assert.That(yawMax, Is.LessThanOrEqualTo(13.001));
            Assert.That(yawMax - yawMin, Is.GreaterThan(15), "yaw actually varies");
            Assert.That(sqMin, Is.GreaterThanOrEqualTo(0.82)); Assert.That(sqMax, Is.LessThanOrEqualTo(1.0));
            Assert.That(sqMax - sqMin, Is.GreaterThan(0.08), "squash actually varies");
            Assert.That(brMin, Is.GreaterThanOrEqualTo(0.94 - 1e-6)); Assert.That(brMax, Is.LessThanOrEqualTo(1.06 + 1e-6));
            Assert.That(offs.Count, Is.GreaterThan(10), "fleck positions vary");

            h.App.Dials.Set("pieces", "variation_yaw_deg", 40);
            yield return h.StartLevel(41, Seed);
            double wide = 0;
            foreach (var c in h.Session.Board.Topology.Cells)
            {
                var t = h.Session.Board.TileAt(c);
                if (t != null) wide = Math.Max(wide, Math.Abs(h.View.Get(t.Id)!.Look.YawDegrees));
            }
            Debug.Log("[ArtAudit] with variation_yaw_deg 40 the largest yaw is " + wide);
            Assert.That(wide, Is.GreaterThan(14), "variation_yaw_deg drives the yaw");
            h.App.Dials.ResetAll();
        }

        [UnityTest]
        public IEnumerator Boards_with_five_six_and_seven_colors()
        {
            h = new PlayHarness();
            yield return h.SetUp(1280, 800);
            var seen = new SortedDictionary<int, int>();
            foreach (int lv in new[] { 5, 21, 51, 76, 90, 100 })
            {
                var l = h.App.Levels.Get(lv, Seed);
                Debug.Log("[ArtAudit] level " + lv + " colors " + l.Colors + " size " + l.Width + "x" + l.Height + " holes " + l.Holes.Count + " specials " + string.Join(",", l.Specials));
                if (!seen.ContainsKey(l.Colors)) seen[l.Colors] = lv;
            }
            foreach (var kv in seen)
            {
                yield return h.StartLevel(kv.Value, Seed);
                yield return PlayHarness.Seconds(0.3f);
                yield return h.Shot("art/board_" + kv.Key + "colors_L" + kv.Value + "_wide");
            }
            h.Resize(1080, 2400, new Cascade.Platform.Abstractions.SafeArea(0, 110, 0, 70));
            foreach (var kv in seen)
            {
                yield return h.StartLevel(kv.Value, Seed);
                yield return PlayHarness.Seconds(0.3f);
                yield return h.Shot("art/board_" + kv.Key + "colors_L" + kv.Value + "_phone");
            }
        }

        // ---- sequences ----

        void Manual() { h.App.Stage.Player.ManualClock = true; }

        IEnumerator Sequence(string tag, CellId a, CellId b, double stepMs, int maxFrames)
        {
            Manual();
            var player = h.App.Stage.Player;
            h.Controller.RequestSwap(a, b);
            int i = 0;
            yield return h.Shot("art/" + tag + "_" + i.ToString("D2"));
            while (h.Controller.IsAnimating && i < maxFrames)
            {
                player.Advance(stepMs);
                i++;
                yield return h.Shot("art/" + tag + "_" + i.ToString("D2"));
            }
            Debug.Log("[ArtAudit] sequence " + tag + ": " + (i + 1) + " frames of " + stepMs + " ms");
            int guard = 0;
            while (h.Controller.IsAnimating && guard++ < 1000) player.Advance(50);
            yield return null;
        }

        void FastPlay(CellId a, CellId b)
        {
            Manual();
            h.Controller.RequestSwap(a, b);
            int guard = 0;
            while (h.Controller.IsAnimating && guard++ < 1000) h.App.Stage.Player.Advance(50);
        }

        static int MaxDepth(MoveResult r)
        {
            int d = 0;
            foreach (var e in r.Events) if (e is Scored s) d = Math.Max(d, s.Depth);
            return d;
        }

        static string FiredKind(MoveResult r)
        {
            foreach (var e in r.Events)
            {
                if (e is SpecialFired f)
                {
                    var cell = r.Before.FindTile(f.TileId);
                    var t = cell.HasValue ? r.Before.TileAt(cell.Value) : null;
                    if (t?.Special is Special.Line ln) return "line" + ln.Axis;
                    if (t?.Special != null) return t.Special.Kind.ToLowerInvariant();
                    foreach (var e2 in r.Events)
                        if (e2 is SpecialCreated c && c.TileId == f.TileId) return c.Special is Special.Line l2 ? "line" + l2.Axis : c.Special.Kind.ToLowerInvariant();
                    return "unknown";
                }
            }
            return "";
        }

        /// <summary>Plays levels (best moves, fast) until a move satisfying the predicate is legal; reports it with the board in the state before it.</summary>
        IEnumerator Find(Func<MoveResult, bool> pred, int[] levels, Action<(CellId, CellId)?> done, int movesPerLevel = 25)
        {
            foreach (int lv in levels)
            {
                yield return h.StartLevel(lv, Seed);
                Manual();
                for (int m = 0; m < movesPerLevel && h.Session.IsPlaying; m++)
                {
                    foreach (var (a, b) in h.Session.LegalMoves)
                    {
                        var r = h.Session.Copy().TrySwap(a, b);
                        if (pred(r)) { done((a, b)); yield break; }
                    }
                    var hint = h.Session.Hint();
                    if (hint == null) break;
                    FastPlay(hint.Value.A, hint.Value.B);
                    if (h.Flow.Current != ScreenId.Play) break;
                }
            }
            done(null);
        }

        [UnityTest]
        public IEnumerator Swap_fall_clear_and_cascade_sequences()
        {
            h = new PlayHarness();
            yield return h.SetUp(1600, 1000);
            h.App.Live.Update(h.App.Live.Player with { AnimationSpeed = 1.0 });
            (CellId, CellId)? mv = null;
            yield return Find(r => r.Played && MaxDepth(r) == 0 && FiredKind(r) == "", new[] { 3 }, x => mv = x, 1);
            Assert.That(mv, Is.Not.Null);
            yield return Sequence("swap", mv!.Value.Item1, mv.Value.Item2, 45, 60);

            mv = null;
            yield return Find(r => r.Played && MaxDepth(r) >= 2, new[] { 4, 6, 8, 10, 12 }, x => mv = x, 30);
            Assert.That(mv, Is.Not.Null, "a cascading move exists");
            yield return Sequence("cascade", mv!.Value.Item1, mv.Value.Item2, 55, 80);
        }

        [UnityTest]
        public IEnumerator Special_fire_sequences()
        {
            h = new PlayHarness();
            yield return h.SetUp(1600, 1000);
            h.App.Live.Update(h.App.Live.Player with { AnimationSpeed = 1.0 });
            var levels = new[] { 40, 45, 50, 55, 60, 65, 70, 75, 80, 85, 90, 95, 100 };
            foreach (var kind in new[] { "line", "bomb", "color" })
            {
                (CellId, CellId)? mv = null;
                yield return Find(r => r.Played && FiredKind(r).StartsWith(kind), levels, x => mv = x, 40);
                if (mv == null && kind == "bomb")
                {
                    // bombs are rare in a hint-driven game: plant a few on a fresh board (colors kept) and look for a move that fires one
                    yield return h.StartLevel(60, Seed);
                    Manual();
                    var board = h.Session.Board;
                    foreach (var c in board.Topology.Cells)
                    {
                        var t = board.TileAt(c);
                        if (t != null && t.Special == null && (c.Value % 7) == 3) board.Put(c, new Tile(t.Id, t.Color, new Special.Bomb()));
                    }
                    h.View.Sync(board);
                    yield return PlayHarness.Frames(2);
                    foreach (var (a, b) in h.Session.LegalMoves)
                        if (FiredKind(h.Session.Copy().TrySwap(a, b)) == "bomb") { mv = (a, b); break; }
                }
                if (mv == null) { Debug.Log("[ArtAudit] no " + kind + " fire found"); continue; }
                Debug.Log("[ArtAudit] found a " + kind + " fire on level " + h.Session.Level.Number);
                yield return PlayHarness.Seconds(0.2f);
                yield return h.Shot("art/" + kind + "_before");
                yield return Sequence(kind, mv.Value.Item1, mv.Value.Item2, 45, 60);
            }
        }

        [UnityTest]
        public IEnumerator Selection_and_halo_close_up()
        {
            h = new PlayHarness();
            yield return h.SetUpInLevel(21, Seed, 1600, 1000);
            var (a, b) = h.FirstLegal();
            yield return h.Click(h.ScreenOf(a));
            yield return PlayHarness.Seconds(0.4f);
            yield return h.Shot("art/selected");
            Assert.That(h.View.Get(h.Session.Board.TileAt(a)!.Id)!.SelectT, Is.EqualTo(1f).Within(0.01f));
        }

        [UnityTest]
        public IEnumerator A_zoomed_board_is_clipped_to_its_slot_and_stays_off_the_hud()
        {
            h = new PlayHarness();
            yield return h.SetUpInLevel(21, Seed, 1280, 800);
            var slot = h.App.Rig.SlotPx;
            yield return h.Scroll(new Vector2((float)(slot.X + slot.Width * 0.5), 400f), 6);
            yield return PlayHarness.Seconds(0.3f);
            Assert.That(h.App.Rig.Board!.UserZoomed, Is.True);
            yield return h.Shot("art/zoom_wide");
            h.Resize(1080, 2400, new Cascade.Platform.Abstractions.SafeArea(0, 110, 0, 70));
            yield return PlayHarness.Seconds(0.3f);
            slot = h.App.Rig.SlotPx;
            yield return h.Scroll(new Vector2(540f, (float)(slot.Y + slot.Height * 0.5)), 6);
            yield return PlayHarness.Seconds(0.3f);
            yield return h.Shot("art/zoom_phone");
        }

        [UnityTest]
        public IEnumerator Shaped_trays_have_clean_rims()
        {
            h = new PlayHarness();
            yield return h.SetUp(1280, 800);
            // one level per distinct hole pattern touching the edge (corner cuts, notches), plus an inner hole
            var seenPatterns = new HashSet<string>();
            int shots = 0;
            for (int lv = 25; lv <= 100 && shots < 4; lv++)
            {
                var l = h.App.Levels.Get(lv, Seed);
                if (l.Holes.Count == 0) continue;
                string key = string.Join(";", l.Holes);
                bool edge = false;
                foreach (var (x, y) in l.Holes) if (x == 0 || y == 0 || x == l.Width - 1 || y == l.Height - 1) edge = true;
                if (!seenPatterns.Add(key)) continue;
                Debug.Log("[ArtAudit] shaped level " + lv + " holes " + key + " edge=" + edge);
                if (shots >= 1 && !edge) continue;
                yield return h.StartLevel(lv, Seed);
                yield return PlayHarness.Seconds(0.3f);
                yield return h.Shot("art/shaped_L" + lv);
                shots++;
            }
        }
    }
}
