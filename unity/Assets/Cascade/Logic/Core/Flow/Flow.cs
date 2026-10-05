using System;
using System.Collections.Generic;

namespace Cascade.Core
{
    /// <summary>How a <see cref="Source"/> chooses colors. Only <see cref="RandomColors"/> exists in v1.</summary>
    public enum SourcePolicy
    {
        /// <summary>Uniform among colors that do not complete a run (TRD §3.4), from the source's own RNG stream.</summary>
        RandomColors,
    }

    /// <summary>A place new tiles enter the board: its id, the cells it feeds and its policy.</summary>
    public sealed record Source(SourceId Id, ValueList<CellId> Cells, SourcePolicy Policy = SourcePolicy.RandomColors);

    /// <summary>Where a cell pulls its next piece from: another cell, or a <see cref="Source"/>.</summary>
    public readonly struct CellOrSource : IEquatable<CellOrSource>
    {
        readonly int _value;
        CellOrSource(bool isSource, int value) { IsSource = isSource; _value = value; }

        public static CellOrSource FromCell(CellId cell) => new CellOrSource(false, cell.Value);
        public static CellOrSource FromSource(SourceId source) => new CellOrSource(true, source.Value);

        public bool IsSource { get; }
        public bool IsCell => !IsSource;
        /// <summary>The cell (valid when <see cref="IsCell"/>).</summary>
        public CellId Cell => new CellId(_value);
        /// <summary>The source (valid when <see cref="IsSource"/>).</summary>
        public SourceId Source => new SourceId(_value);

        public bool Equals(CellOrSource other) => IsSource == other.IsSource && _value == other._value;
        public override bool Equals(object? obj) => obj is CellOrSource o && Equals(o);
        public override int GetHashCode() => _value * 2 + (IsSource ? 1 : 0);
        public override string ToString() => IsSource ? "source " + _value : "cell " + _value;

        public static implicit operator CellOrSource(CellId cell) => FromCell(cell);
        public static implicit operator CellOrSource(SourceId source) => FromSource(source);
        public static implicit operator CellOrSource(int cell) => FromCell(new CellId(cell));
    }

    /// <summary>
    /// Flow: each cell's feed list (where it pulls its next piece from, in priority order) and the
    /// sources (TRD §3). Gravity is one preset (<see cref="GravityDown"/>); <see cref="Settle"/> is
    /// the generic algorithm that moves pieces along the feeds.
    /// </summary>
    public sealed class Flow
    {
        static readonly CellOrSource[] NoFeed = new CellOrSource[0];
        readonly CellOrSource[][] _feeds;

        /// <summary>Builds a flow from per-cell feed lists (a cell not listed pulls from nothing) and the sources.</summary>
        public Flow(IEnumerable<(CellId Cell, CellOrSource[] Feed)> feeds, IEnumerable<Source> sources)
        {
            var list = new List<(CellId Cell, CellOrSource[] Feed)>(feeds);
            int cap = 0;
            foreach (var f in list) cap = Math.Max(cap, f.Cell.Value + 1);
            _feeds = new CellOrSource[cap][];
            foreach (var (cell, feed) in list) _feeds[cell.Value] = (CellOrSource[])feed.Clone();
            Sources = new List<Source>(sources).ToArray();
        }

        /// <summary>The sources, in a fixed order.</summary>
        public IReadOnlyList<Source> Sources { get; }

        /// <summary>Where <paramref name="cell"/> pulls its next piece from, in priority order.</summary>
        public IReadOnlyList<CellOrSource> Feed(CellId cell)
            => cell.Value < _feeds.Length && _feeds[cell.Value] != null ? _feeds[cell.Value] : NoFeed;

        /// <summary>
        /// Gravity preset: each cell pulls from the nearest cell above it (skipping holes); the
        /// topmost cell of each column pulls from that column's source (source id = column x).
        /// </summary>
        public static Flow GravityDown(Topology topology)
        {
            // columns: x -> list of (y, cell); built with explicit ordering, never by dictionary iteration
            var xs = new List<int>();
            var columns = new List<List<(int Y, CellId Cell)>>();
            foreach (var cell in topology.Cells)
            {
                var pos = topology.Pos(cell);
                int idx = xs.IndexOf(pos.X);
                if (idx < 0) { xs.Add(pos.X); columns.Add(new List<(int, CellId)>()); idx = xs.Count - 1; }
                columns[idx].Add((pos.Y, cell));
            }
            var order = new List<int>();
            for (int i = 0; i < xs.Count; i++) order.Add(i);
            order.Sort((p, q) => xs[p].CompareTo(xs[q])); // x values are unique

            var feeds = new List<(CellId, CellOrSource[])>();
            var sources = new List<Source>();
            foreach (int ci in order)
            {
                var col = columns[ci];
                col.Sort((p, q) => p.Y != q.Y ? p.Y.CompareTo(q.Y) : p.Cell.Value.CompareTo(q.Cell.Value));
                var sid = new SourceId(xs[ci]);
                sources.Add(new Source(sid, new[] { col[0].Cell }));
                feeds.Add((col[0].Cell, new[] { CellOrSource.FromSource(sid) }));
                for (int i = 1; i < col.Count; i++)
                    feeds.Add((col[i].Cell, new[] { CellOrSource.FromCell(col[i - 1].Cell) }));
            }
            return new Flow(feeds, sources);
        }

        /// <summary>
        /// Pulls pieces along <see cref="Feed"/> until no reachable empty cell can be filled, then
        /// chooses colors for the new pieces at their final cells (TRD §3.4). Deterministic: fixed
        /// cell order. The input board is untouched. Events: all <see cref="Spawned"/> first, then
        /// a <see cref="Moved"/> for every tile that traveled, in order of first movement.
        /// </summary>
        public static (Board Board, List<Event> Events) Settle(Board board, Streams streams)
        {
            board = board.Copy();
            var flow = board.Flow;
            var topo = board.Topology;
            var paths = new List<List<CellId>>();
            var pathTile = new List<int>();
            var pathOf = new Dictionary<int, int>(); // tile id -> index in paths (lookup only)
            var spawns = new List<(int Tile, SourceId Source, CellId Cell)>();

            int limit = topo.Cells.Count * topo.Cells.Count + 16;
            bool done = false;
            for (int iter = 0; iter < limit; iter++)
            {
                bool changed = false;
                foreach (var cell in topo.Cells)
                {
                    if (board.TileAt(cell) != null) continue;
                    foreach (var cand in flow.Feed(cell))
                    {
                        if (cand.IsSource)
                        {
                            int tid = board.NewTileId();
                            board.Put(cell, new Tile(tid, -1));
                            pathOf[tid] = paths.Count;
                            paths.Add(new List<CellId> { cell });
                            pathTile.Add(tid);
                            spawns.Add((tid, cand.Source, cell));
                            changed = true;
                            break;
                        }
                        var tile = board.TileAt(cand.Cell);
                        if (tile != null)
                        {
                            board.Put(cand.Cell, null);
                            board.Put(cell, tile);
                            if (!pathOf.TryGetValue(tile.Id, out int pi))
                            {
                                pi = paths.Count;
                                pathOf[tile.Id] = pi;
                                paths.Add(new List<CellId> { cand.Cell });
                                pathTile.Add(tile.Id);
                            }
                            paths[pi].Add(cell);
                            changed = true;
                            break;
                        }
                    }
                }
                if (!changed) { done = true; break; }
            }
            if (!done) throw new InvalidOperationException("settle did not terminate (cyclic flow?)");

            var spawnSource = new Dictionary<int, SourceId>(); // tile id -> source (lookup only)
            foreach (var s in spawns) spawnSource[s.Tile] = s.Source;
            var colors = new Dictionary<int, int>(); // tile id -> picked color (lookup only)
            foreach (var cell in topo.Cells) // final cells, fixed order
            {
                var tile = board.TileAt(cell);
                if (tile != null && tile.Color == -1)
                {
                    int color = Match.PickColor(board, cell, streams.Spawn(spawnSource[tile.Id]));
                    colors[tile.Id] = color;
                    board.Put(cell, new Tile(tile.Id, color));
                }
            }

            var events = new List<Event>();
            foreach (var s in spawns) events.Add(new Spawned(s.Tile, s.Source, s.Cell, colors[s.Tile]));
            for (int i = 0; i < paths.Count; i++)
                if (paths[i].Count > 1) events.Add(new Moved(pathTile[i], paths[i]));
            return (board, events);
        }
    }
}
