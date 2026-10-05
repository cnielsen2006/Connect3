using System;
using System.Collections.Generic;

namespace Cascade.Core
{
    /// <summary>Legal-move finder, initial board fill and deadlock shuffle (TRD §3.5).</summary>
    public static class Moves
    {
        /// <summary>Shuffle attempts before the colors are regenerated instead.</summary>
        public const int ShuffleTries = 100;
        /// <summary>Regeneration attempts after the shuffle attempts.</summary>
        public const int RegenerateTries = 1000;

        const int Empty = int.MinValue;

        static bool MakesRun(Topology topo, int[] col, CellId cell)
        {
            int color = col[cell.Value];
            foreach (var slot in topo.LinesThrough(cell))
            {
                var cs = slot.Line.Cells;
                int n = 1;
                int i = slot.Index - 1;
                while (i >= 0 && col[cs[i].Value] == color) { n++; i--; }
                i = slot.Index + 1;
                int end = cs.Count;
                while (i < end && col[cs[i].Value] == color) { n++; i++; }
                if (n >= Match.MinRun) return true;
            }
            return false;
        }

        /// <summary>
        /// Every neighbor swap that makes a match, plus every special-with-special swap, ordered by
        /// <see cref="Topology.SwapPairs"/>. Works on a plain color map so it never touches the board.
        /// </summary>
        public static List<(CellId A, CellId B)> FindLegal(Board board)
        {
            var topo = board.Topology;
            var col = new int[topo.CellCapacity];
            foreach (var c in topo.Cells)
            {
                var t = board.TileAt(c);
                col[c.Value] = t == null ? Empty : t.Color;
            }
            var legal = new List<(CellId A, CellId B)>();
            foreach (var (a, b) in topo.SwapPairs)
            {
                var ta = board.TileAt(a);
                var tb = board.TileAt(b);
                if (ta == null || tb == null) continue;
                if (ta.Special != null && tb.Special != null)
                {
                    legal.Add((a, b));
                    continue;
                }
                int ca = col[a.Value], cb = col[b.Value];
                col[a.Value] = cb;
                col[b.Value] = ca;
                bool ok = MakesRun(topo, col, a) || MakesRun(topo, col, b);
                col[a.Value] = ca;
                col[b.Value] = cb;
                if (ok) legal.Add((a, b));
            }
            return legal;
        }

        static void FillNoMatches(Board board, Pcg32 rng)
        {
            foreach (var cell in board.Topology.Cells) board.Put(cell, null);
            foreach (var cell in board.Topology.Cells)
                board.Put(cell, new Tile(board.NewTileId(), Match.PickColor(board, cell, rng)));
        }

        /// <summary>A starting board: no matches, at least one legal move (draws from the <c>tiles</c> stream).</summary>
        public static Board NewBoard(Topology topology, Flow flow, int colors, Streams streams)
        {
            var board = Board.Empty(topology, flow, colors);
            var rng = streams.Tiles;
            for (int i = 0; i < RegenerateTries; i++)
            {
                FillNoMatches(board, rng);
                if (Match.FindRuns(board).Count == 0 && FindLegal(board).Count > 0) return board;
            }
            throw new InvalidOperationException("could not build a playable starting board");
        }

        static bool Valid(Board board) => Match.FindRuns(board).Count == 0 && FindLegal(board).Count > 0;

        /// <summary>
        /// Permutes tile colors among tiles (specials stay on their tiles) until there are no matches
        /// and at least one legal move, drawing from the <c>shuffle</c> stream. After
        /// <see cref="ShuffleTries"/> failures it regenerates the colors instead. The input board is untouched.
        /// </summary>
        public static (Board Board, Shuffled Event) Shuffle(Board board, Streams streams)
        {
            board = board.Copy();
            var rng = streams.Shuffle;
            var cells = new List<CellId>();
            foreach (var c in board.Topology.Cells) if (board.TileAt(c) != null) cells.Add(c);
            var tiles = new List<Tile>();
            foreach (var c in cells) tiles.Add(board.TileAt(c)!);

            for (int attempt = 0; attempt < ShuffleTries + RegenerateTries; attempt++)
            {
                if (attempt < ShuffleTries)
                {
                    var colors = new List<int>();
                    foreach (var t in tiles) colors.Add(t.Color);
                    rng.Shuffle(colors);
                    for (int i = 0; i < cells.Count; i++)
                        board.Put(cells[i], new Tile(tiles[i].Id, colors[i], tiles[i].Special));
                }
                else
                {
                    for (int i = 0; i < cells.Count; i++)
                        board.Put(cells[i], new Tile(tiles[i].Id, -1, tiles[i].Special));
                    for (int i = 0; i < cells.Count; i++)
                    {
                        int color = Match.PickColor(board, cells[i], rng);
                        board.Put(cells[i], new Tile(tiles[i].Id, color, tiles[i].Special));
                    }
                }
                if (Valid(board))
                {
                    var mapping = new (int TileId, int Color)[cells.Count];
                    for (int i = 0; i < cells.Count; i++)
                    {
                        var t = board.TileAt(cells[i])!;
                        mapping[i] = (t.Id, t.Color);
                    }
                    return (board, new Shuffled(mapping));
                }
            }
            throw new InvalidOperationException("shuffle could not produce a playable board");
        }
    }
}
