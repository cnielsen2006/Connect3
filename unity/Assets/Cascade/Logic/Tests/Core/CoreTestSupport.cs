using System;
using System.Collections.Generic;
using System.Linq;
using Cascade.Core;

namespace Cascade.Tests.CoreTests
{
    /// <summary>
    /// Hand-built boards for tests (shared test helpers). Rows are strings of
    /// space-separated tokens: a digit is a color, a suffix marks a special (h/v = Line axis 0/1,
    /// b = Bomb, c = Color), and <c>.</c> is a hole.
    /// </summary>
    public static class T
    {
        public static readonly RuleSet DefaultRules = RuleSet.Default;

        public static Board BoardFrom(string[] rows, int colors = 5)
        {
            var grid = rows.Select(r => r.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)).ToArray();
            int h = grid.Length, w = grid[0].Length;
            var holes = new List<(int X, int Y)>();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (grid[y][x] == ".") holes.Add((x, y));
            var topo = Topology.Grid(w, h, holes);
            var board = Board.Empty(topo, Flow.GravityDown(topo), colors);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    string tok = grid[y][x];
                    if (tok == ".") continue;
                    Special? special = tok.Substring(1) switch
                    {
                        "h" => new Special.Line(0),
                        "v" => new Special.Line(1),
                        "b" => new Special.Bomb(),
                        "c" => new Special.Color(),
                        _ => null,
                    };
                    board.Put(y * w + x, new Tile(board.NewTileId(), tok[0] - '0', special));
                }
            return board;
        }

        public static CellId Cell(Board board, int x, int y)
        {
            var c = board.Topology.CellAt(x, y);
            if (c == null) throw new KeyNotFoundException($"({x}, {y})");
            return c.Value;
        }

        /// <summary>Colors by row; -1 for an empty cell, -2 for a hole.</summary>
        public static int[][] ColorsOf(Board board)
        {
            int xs = board.Topology.Cells.Max(c => board.Topology.Pos(c).X) + 1;
            int ys = board.Topology.Cells.Max(c => board.Topology.Pos(c).Y) + 1;
            var rows = new int[ys][];
            for (int y = 0; y < ys; y++)
            {
                rows[y] = new int[xs];
                for (int x = 0; x < xs; x++)
                {
                    var c = board.Topology.CellAt(x, y);
                    rows[y][x] = c == null ? -2 : board.TileAt(c.Value)?.Color ?? -1;
                }
            }
            return rows;
        }

        public static string ColorsText(Board board) => string.Join("/", ColorsOf(board).Select(r => string.Join(",", r)));

        /// <summary>A snapshot of every cell's tile, for "was not mutated" checks.</summary>
        public static Tile?[] Snapshot(Board board) => board.Topology.Cells.Select(c => board.TileAt(c)).ToArray();

        public static List<TEvent> Of<TEvent>(IEnumerable<Event> events) where TEvent : Event => events.OfType<TEvent>().ToList();

        public static (Board Board, List<Event> Events) Play(Board board, (int, int) a, (int, int) b, ulong seed = 1, RuleSet? rules = null)
            => Core.Rules.Resolve(board, (Cell(board, a.Item1, a.Item2), Cell(board, b.Item1, b.Item2)), new Streams(seed), rules ?? DefaultRules);

        public static CellId[] Ids(params int[] ids) => ids.Select(i => new CellId(i)).ToArray();
    }
}
