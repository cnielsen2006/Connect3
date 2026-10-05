using System;
using System.Collections.Generic;
using System.Globalization;
using Cascade.Core;

namespace Cascade.Presentation
{
    /// <summary>
    /// Turns the event list of one move into a <see cref="Timeline"/> (TRD 12.1, 12.3; the
    /// animation planner). Pure and deterministic: the same board, events, timing and layout give the same
    /// timeline. The view never changes logic state, and the last item of the timeline syncs the scene to
    /// the final board.
    ///
    /// Timing rules (all durations are divided by the animation speed; <c>d(x)</c> = x / speed):
    /// <list type="bullet">
    /// <item>A swap runs first and takes d(swap) (d(swap_reject) for a rejected one).</item>
    /// <item>Wave 0 of each cascade round starts when everything before it has landed; each later wave starts
    /// <c>d(special_fire) * 0.55 + d(wave_stagger)</c> after the previous wave started.</item>
    /// <item>Falls and spawns of a round start when its clears have finished. A piece falls at
    /// <c>fall_cells_per_sec * speed</c> and may not start moving before its previous move (and landing) ended.</item>
    /// <item>Spawns into the same cell within one round start one cell higher each so they never overlap.</item>
    /// <item>A special being created, or a shuffle, starts when everything before it has finished.</item>
    /// </list>
    /// </summary>
    public static class TimelinePlanner
    {
        /// <param name="before">The board before the first event (it is not modified).</param>
        /// <param name="events">What <c>Rules.Resolve</c> returned for one move (or any consistent event list).</param>
        public static Timeline Plan(Board before, IReadOnlyList<Event> events, TimingParams timing, ICellLayout layout)
            => new Planner(before, timing, layout).Run(events);

        sealed class Planner
        {
            readonly TimingParams _t;
            readonly ICellLayout _layout;
            readonly List<TimelineItem> _items = new List<TimelineItem>();
            readonly List<string> _warnings = new List<string>();

            // where each tile is "now" in the plan (lookups only; never iterated)
            readonly int[] _cellTile;
            readonly Dictionary<int, CellId> _tileCell = new Dictionary<int, CellId>();
            readonly Dictionary<int, Special?> _tileSpecial = new Dictionary<int, Special?>();
            readonly Dictionary<int, Float2> _clearedPos = new Dictionary<int, Float2>();
            readonly Dictionary<int, double> _busyUntil = new Dictionary<int, double>();
            readonly Dictionary<int, double> _spawnStart = new Dictionary<int, double>();
            readonly Dictionary<int, int> _spawnCount = new Dictionary<int, int>();

            readonly List<Float2> _pendingCenters = new List<Float2>(); // one per Matched since the round's first Cleared
            List<Float2> _roundCenters = new List<Float2>();            // the round being planned, in group order
            int _roundScored;                                           // Scored events of this round seen so far
            double _tAll, _tClearEnd, _wStart, _waveT;
            Float2 _matchCenter = Float2.Zero;
            int _fxId;
            int _round = -1; // cascade depth of the clears being planned (0 = the player's move)

            public Planner(Board before, TimingParams timing, ICellLayout layout)
            {
                _t = timing;
                _layout = layout;
                var topo = before.Topology;
                _cellTile = new int[topo.CellCapacity];
                for (int i = 0; i < _cellTile.Length; i++) _cellTile[i] = -1;
                foreach (var c in topo.Cells)
                {
                    var tile = before.TileAt(c);
                    if (tile == null) continue;
                    _cellTile[c.Value] = tile.Id;
                    _tileCell[tile.Id] = c;
                    _tileSpecial[tile.Id] = tile.Special;
                }
            }

            double D(double ms) => ms / _t.Speed;

            public Timeline Run(IReadOnlyList<Event> events)
            {
                foreach (var e in events)
                {
                    switch (e)
                    {
                        case Swapped s: OnSwapped(s); break;
                        case SwapRejected r: OnRejected(r); break;
                        case Matched m: OnMatched(m); break;
                        case Cleared c: OnCleared(c); break;
                        case SpecialFired f: OnSpecialFired(f); break;
                        case Scored sc: OnScored(sc); break;
                        case Spawned sp: OnSpawned(sp); break;
                        case Moved mv: OnMoved(mv); break;
                        case SpecialCreated sc2: OnSpecialCreated(sc2); break;
                        case Shuffled sh: OnShuffled(sh); break;
                    }
                }

                double total = _tAll, end = _tAll;
                foreach (var it in _items)
                {
                    end = Math.Max(end, it.EndMs);
                    if (it is ShowFx fx && fx.Kind == FxKind.ScorePopup) continue; // popups may outlive the settle
                    total = Math.Max(total, it.EndMs);
                }
                _items.Add(new SyncToFinalBoard(total));
                return new Timeline(_items, total, end, _warnings);
            }

            // ---- helpers -----------------------------------------------------------------------------

            bool TileAtCell(CellId cell, out int tile)
            {
                tile = cell.Value >= 0 && cell.Value < _cellTile.Length ? _cellTile[cell.Value] : -1;
                return tile >= 0;
            }

            void Place(int tile, CellId cell)
            {
                if (_tileCell.TryGetValue(tile, out var old) && old.Value < _cellTile.Length && _cellTile[old.Value] == tile)
                    _cellTile[old.Value] = -1;
                _cellTile[cell.Value] = tile;
                _tileCell[tile] = cell;
            }

            void Vacate(int tile)
            {
                if (_tileCell.TryGetValue(tile, out var old) && _cellTile[old.Value] == tile) _cellTile[old.Value] = -1;
            }

            void Warn(string message) => _warnings.Add(message);

            void Scalar(int tile, PoseProperty p, double from, double to, double start, double dur, Easing easing, double param = 0)
                => _items.Add(new Tween(tile, p, new Float2(from, 0), new Float2(to, 0), start, dur, easing, param));

            // ---- swaps -------------------------------------------------------------------------------

            void OnSwapped(Swapped e)
            {
                double d = D(_t.SwapMs);
                bool okA = TileAtCell(e.A, out int ta), okB = TileAtCell(e.B, out int tb);
                if (!okA || !okB) Warn("Swapped " + e.A + "," + e.B + ": a cell has no tile");
                var pa = _layout.CellPosition(e.A);
                var pb = _layout.CellPosition(e.B);
                _items.Add(new PlaySound(SoundCue.Swap, 0, _tAll));
                if (okA) Slide(ta, pa, pb, _tAll, d, +1);
                if (okB) Slide(tb, pb, pa, _tAll, d, -1);
                if (okA) { Vacate(ta); }
                if (okB) { Vacate(tb); }
                if (okB) Place(tb, e.A);
                if (okA) Place(ta, e.B);
                _tAll = _tClearEnd = _tAll + d;
            }

            void Slide(int tile, Float2 a, Float2 b, double start, double d, int rock)
            {
                _items.Add(new Tween(tile, PoseProperty.Position, a, b, start, d, Easing.SmoothStep));
                Scalar(tile, PoseProperty.Hover, 0, _t.SwapHover, start, d, Easing.Bump);
                Scalar(tile, PoseProperty.Roll, 0, rock * _t.SwapRockDeg, start, d, Easing.Bump);
            }

            void OnRejected(SwapRejected e)
            {
                double d = D(_t.SwapRejectMs);
                bool okA = TileAtCell(e.A, out int ta), okB = TileAtCell(e.B, out int tb);
                if (!okA || !okB) Warn("SwapRejected " + e.A + "," + e.B + ": a cell has no tile");
                var pa = _layout.CellPosition(e.A);
                var pb = _layout.CellPosition(e.B);
                _items.Add(new PlaySound(SoundCue.Reject, 0, _tAll));
                if (okA) Nudge(ta, pa, pb, _tAll, d);
                if (okB) Nudge(tb, pb, pa, _tAll, d);
                _tAll = _tClearEnd = _tAll + d;
            }

            void Nudge(int tile, Float2 a, Float2 b, double start, double d)
            {
                _items.Add(new Tween(tile, PoseProperty.Position, a, Float2.Lerp(a, b, _t.RejectNudge), start, d, Easing.BumpPow15));
                Scalar(tile, PoseProperty.Roll, 0, _t.RejectRockDeg, start, d, Easing.Wobble, 3);
            }

            // ---- matches, clears, specials -----------------------------------------------------------

            void OnMatched(Matched e)
            {
                if (e.Cells.Count == 0) return;
                double sx = 0, sz = 0;
                foreach (var c in e.Cells) { var p = _layout.CellPosition(c); sx += p.X; sz += p.Y; }
                _matchCenter = new Float2(sx / e.Cells.Count, sz / e.Cells.Count);
                _pendingCenters.Add(_matchCenter);
            }

            void OnCleared(Cleared e)
            {
                _spawnCount.Clear();
                double start = e.Wave == 0 ? _tAll : _waveT + D(_t.SpecialFireMs) * _t.WaveFireFraction;
                _waveT = _wStart = start;
                if (e.Wave == 0)
                {
                    _round++;
                    _roundCenters = new List<Float2>(_pendingCenters);
                    _pendingCenters.Clear();
                    _roundScored = 0;
                }
                _items.Add(new PlaySound(SoundCue.Clear, Math.Max(_round, 0), start));
                double d = D(_t.ClearMs);
                var positions = new List<(int Tile, Float2 Pos)>();
                foreach (int tile in e.TileIds)
                {
                    if (_tileCell.TryGetValue(tile, out var cell) && _cellTile[cell.Value] == tile)
                    {
                        var pos = _layout.CellPosition(cell);
                        _clearedPos[tile] = pos;
                        _cellTile[cell.Value] = -1;
                        positions.Add((tile, pos));
                    }
                    else Warn("Cleared tile " + tile + ": not on the board");
                }
                foreach (var (tile, pos) in positions)
                {
                    Scalar(tile, PoseProperty.Sink, 0, 1, start, d, Easing.QuadIn);
                    _items.Add(new ShowFx(_fxId++, FxKind.Ripple, pos, start, d));
                    _items.Add(new RemovePiece(tile, start + d));
                }
                _tClearEnd = Math.Max(_tClearEnd, start + d);
                _tAll = Math.Max(_tAll, start + d);
                _waveT = start + D(_t.WaveStaggerMs);
            }

            void OnSpecialFired(SpecialFired e)
            {
                double d = D(_t.SpecialFireMs);
                FireFx(e, _wStart, d);
                if (_tileSpecial.TryGetValue(e.TileId, out var fired))
                    _items.Add(new PlaySound(fired is Special.Line ? SoundCue.LineFire : fired is Special.Bomb ? SoundCue.BombFire : SoundCue.ColorFire, 0, _wStart));
                _tAll = Math.Max(_tAll, _wStart + d);
                _tClearEnd = Math.Max(_tClearEnd, _wStart + d * _t.FireClearFraction);
            }

            void FireFx(SpecialFired e, double start, double d)
            {
                if (!_tileSpecial.TryGetValue(e.TileId, out var special))
                {
                    Warn("SpecialFired tile " + e.TileId + ": unknown tile");
                    return;
                }
                bool haveOrigin = _clearedPos.TryGetValue(e.TileId, out var origin);
                var pts = new List<Float2>();
                foreach (var c in e.Cells) pts.Add(_layout.CellPosition(c));
                if (pts.Count == 0) return;

                if (special is Special.Line line)
                {
                    double cx = 0, cz = 0, minX = double.MaxValue, maxX = double.MinValue, minZ = double.MaxValue, maxZ = double.MinValue;
                    foreach (var p in pts)
                    {
                        cx += p.X; cz += p.Y;
                        minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
                        minZ = Math.Min(minZ, p.Y); maxZ = Math.Max(maxZ, p.Y);
                    }
                    bool horizontal = line.Axis == 0;
                    double length = (horizontal ? maxX - minX : maxZ - minZ) + 1.0;
                    _items.Add(new ShowFx(_fxId++, FxKind.LineWash, new Float2(cx / pts.Count, cz / pts.Count), start, d)
                    { Axis = line.Axis, Length = length });
                }
                else if (!haveOrigin)
                {
                    Warn("SpecialFired tile " + e.TileId + ": it was not cleared first");
                }
                else if (special is Special.Bomb)
                {
                    double far = 0;
                    foreach (var p in pts) far = Math.Max(far, p.Distance(origin));
                    _items.Add(new ShowFx(_fxId++, FxKind.BombWash, origin, start, d) { Radius = far + 0.5 });
                }
                else
                {
                    var order = new List<(double Dist, int Index, Float2 Pos)>();
                    for (int i = 0; i < pts.Count; i++) order.Add((pts[i].Distance(origin), i, pts[i]));
                    order.Sort((a, b) => { int c = a.Dist.CompareTo(b.Dist); return c != 0 ? c : a.Index.CompareTo(b.Index); });
                    var cells = new Float2[order.Count];
                    for (int i = 0; i < cells.Length; i++) cells[i] = order[i].Pos;
                    _items.Add(new ShowFx(_fxId++, FxKind.ColorWash, origin, start, d) { Cells = cells });
                }
            }

            void OnScored(Scored e)
            {
                // the k-th Scored of a round belongs to the k-th match group; the bonus after the groups uses the last center
                int k = _roundScored++;
                var center = k < _roundCenters.Count ? _roundCenters[k] : (_roundCenters.Count > 0 ? _roundCenters[_roundCenters.Count - 1] : _matchCenter);
                var c = _t.Celebration;
                double start = _wStart + D(_t.ClearMs) * _t.PopupDelayFraction + D(c.GroupStaggerMs) * Math.Min(k, _roundCenters.Count);
                _items.Add(new ScoreApplied(e.Points, e.Depth, start, e.Multiplier));
                if (e.Points <= 0) return;
                _items.Add(new ShowFx(_fxId++, FxKind.ScorePopup, center, start, D(_t.PopupMs))
                { Text = "+" + e.Points.ToString(CultureInfo.InvariantCulture), Depth = e.Depth, Multiplier = e.Multiplier });
                if (e.Multiplier <= 1.0 + 1e-9) return;

                int tier = c.TierOf(e.Multiplier);
                _items.Add(new ShowFx(_fxId++, FxKind.ComboBadge, center, start, D(c.BadgeMs))
                { Text = "x" + e.Multiplier.ToString("0.##", CultureInfo.InvariantCulture), Multiplier = e.Multiplier, Tier = tier, Size = c.BadgeScale(tier) });
                if (tier >= c.StingerFromTier) _items.Add(new PlaySound(SoundCue.Stinger, tier, start));
                if (tier >= 1 && !c.ReduceEffects)
                {
                    if (c.ParticlesPerTier > 0)
                        _items.Add(new ShowFx(_fxId++, FxKind.Burst, center, start, D(c.BadgeMs))
                        { Count = c.ParticlesPerTier * tier, Tier = tier, Radius = 1.2 + 0.4 * tier });
                    if (c.CameraPunchPerTier > 0)
                        _items.Add(new CameraPunch(c.CameraPunchPerTier * tier, D(c.BadgeMs) * 0.5, start));
                }
            }

            // ---- spawns, falls ----------------------------------------------------------------------

            void OnSpawned(Spawned e)
            {
                var entry = e.Cell;
                _spawnCount.TryGetValue(entry.Value, out int k); // later spawns into the same cell start higher
                _spawnCount[entry.Value] = k + 1;
                double height = _t.SpawnHeight + k * _t.SpawnStackStep;
                var rest = _layout.CellPosition(entry);
                var above = _layout.AbovePosition(entry, height);
                double start = _tClearEnd;
                double d = D(_t.SpawnDropMs) * height / _t.SpawnHeight;
                _items.Add(new SpawnPiece(e.TileId, e.Color, above, start));
                _items.Add(new Tween(e.TileId, PoseProperty.Position, above, rest, start, d, Easing.FallAccel));
                _busyUntil[e.TileId] = start + d;
                _spawnStart[e.TileId] = start;
                _tileSpecial[e.TileId] = null;
                Place(e.TileId, entry);
            }

            void OnMoved(Moved e)
            {
                if (e.Path.Count < 2) { Warn("Moved tile " + e.TileId + ": path shorter than two cells"); return; }
                var pts = new Float2[e.Path.Count];
                for (int i = 0; i < pts.Length; i++) pts[i] = _layout.CellPosition(e.Path[i]);
                _busyUntil.TryGetValue(e.TileId, out double busy);
                double start = Math.Max(_tClearEnd, busy);
                int n = pts.Length - 1;
                double d = n / Math.Max(_t.FallCellsPerSec * _t.Speed, 0.1) * 1000.0;
                _items.Add(new Tween(e.TileId, PoseProperty.Position, pts[0], pts[pts.Length - 1], start, d, Easing.FallAccel, 0, pts));
                if (_t.LandSquash > 0)
                {
                    double w = _t.SettleWobbles;
                    double ld = D(_t.LandBaseMs + _t.LandPerWobbleMs * w);
                    Scalar(e.TileId, PoseProperty.Squash, 1, 1 - _t.LandSquash, start + d, ld, Easing.Wobble, 1 + w);
                    Scalar(e.TileId, PoseProperty.Roll, 0, _t.LandRollDeg, start + d, ld, Easing.Wobble, 2 * (1 + w));
                }
                _items.Add(new PlaySound(SoundCue.Land, 0, start + d));
                double free = start + d + D(_t.ClearMs) * _t.LandBusyFraction;
                _busyUntil[e.TileId] = free;
                _tAll = Math.Max(_tAll, free);
                Place(e.TileId, e.Path[e.Path.Count - 1]);
            }

            // ---- specials created, shuffles ------------------------------------------------------------

            void OnSpecialCreated(SpecialCreated e)
            {
                if (!_tileCell.ContainsKey(e.TileId)) { Warn("SpecialCreated tile " + e.TileId + ": unknown tile"); return; }
                double start = _tAll;
                if (_spawnStart.TryGetValue(e.TileId, out double s)) start = Math.Max(start, s); // never before the piece exists
                double d = D(_t.SpecialCreateMs);
                _items.Add(new SetSpecial(e.TileId, e.Special, start));
                _items.Add(new PlaySound(SoundCue.SpecialCreate, 0, start));
                Scalar(e.TileId, PoseProperty.Squash, 1, 1 + _t.SpecialPopSquash, start, d, Easing.Bump);
                _tileSpecial[e.TileId] = e.Special;
                _tAll = start + d;
            }

            void OnShuffled(Shuffled e)
            {
                double start = _tAll;
                double d = D(_t.ShuffleMs);
                _items.Add(new PlaySound(SoundCue.Shuffle, 0, start));
                foreach (var (tile, color) in e.Mapping)
                {
                    if (!_tileCell.ContainsKey(tile)) { Warn("Shuffled tile " + tile + ": unknown tile"); continue; }
                    Scalar(tile, PoseProperty.Squash, 1, 1 - _t.ShuffleSquash, start, d, Easing.Bump);
                    _items.Add(new SetColor(tile, color, start + d / 2));
                }
                _tAll = start + d;
            }
        }
    }
}
