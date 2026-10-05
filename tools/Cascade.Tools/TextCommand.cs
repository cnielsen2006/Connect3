using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Cascade.Core;
using Cascade.Gameplay;
using Cascade.Generation;

namespace Cascade.Tools
{
    /// <summary>
    /// <c>text</c>: a small terminal view of the rules. Generates a level, prints the board and plays
    /// swaps, either from <c>--script</c> or interactively. Cells are written <c>x,y</c> with
    /// <c>0,0</c> at the top left. Commands: <c>x,y x,y</c> swap, <c>h</c> hint, <c>l</c> legal moves,
    /// <c>q</c> quit. Pieces: a digit for the color; after it <c>-</c> / <c>|</c> Line (row / column),
    /// <c>*</c> Bomb, <c>@</c> Color; <c>.</c> is a hole.
    /// </summary>
    public static class TextCommand
    {
        public static int Run(Args args)
        {
            int number = args.Int("level", 1);
            ulong seed = (ulong)args.Int("seed", 1);
            var env = Env.Load(args);
            var source = new GeneratingLevelSource(env.Dials, env.Profile, new SystemStopwatch());
            var level = source.Get(number, seed);
            var session = new Session(level, env.Rules, env.Settings.Weights);
            var o = Console.Out;
            o.WriteLine("Level " + level.Number + "  seed " + level.Seed + "  " + level.Width + "x" + level.Height + "  colors " + level.Colors +
                        (level.Intro != null ? "  (" + level.Intro + ")" : "") + "  stars at " + level.Star2 + " / " + level.Star3);
            Print(o, session);

            string? script = args.Get("script");
            if (script != null)
            {
                foreach (var part in script.Split(';'))
                {
                    if (string.IsNullOrWhiteSpace(part)) continue;
                    o.WriteLine("> " + part.Trim());
                    if (!Command(o, session, part.Trim())) break;
                }
                return 0;
            }
            string? line;
            o.Write("> ");
            while ((line = Console.In.ReadLine()) != null)
            {
                if (line.Trim() == "q") break;
                if (!Command(o, session, line.Trim())) break;
                o.Write("> ");
            }
            return 0;
        }

        static bool Command(System.IO.TextWriter o, Session s, string text)
        {
            if (text == "h")
            {
                var hint = s.Hint();
                o.WriteLine(hint == null ? "no hint" : "hint: " + Name(s.Board, hint.Value.A) + " " + Name(s.Board, hint.Value.B));
                return true;
            }
            if (text == "l")
            {
                o.WriteLine(string.Join("  ", s.LegalMoves.Select(m => Name(s.Board, m.A) + "-" + Name(s.Board, m.B))));
                return true;
            }
            var cells = Parse(s.Board, text);
            if (cells == null) { o.WriteLine("expected 'x,y x,y' (or h, l, q)"); return true; }
            var r = s.TrySwap(cells.Value.A, cells.Value.B);
            switch (r.Outcome)
            {
                case MoveOutcome.Rejected: o.WriteLine("rejected: that swap makes no match"); break;
                case MoveOutcome.Ignored: o.WriteLine("the level is over"); return false;
                default:
                    o.WriteLine("+" + r.ScoreDelta + " points: " + Summary(r.Events));
                    Print(o, s);
                    break;
            }
            if (s.Status != SessionStatus.Playing)
            {
                o.WriteLine(s.Status == SessionStatus.Won ? "WON with " + s.Stars + " star(s)" : "LOST");
                return false;
            }
            return true;
        }

        static string Summary(IEnumerable<Event> events)
        {
            int matches = 0, cleared = 0, fired = 0, created = 0, spawned = 0, shuffled = 0, depth = 0;
            foreach (var e in events)
            {
                switch (e)
                {
                    case Matched _: matches++; break;
                    case Cleared c: cleared += c.TileIds.Count; break;
                    case SpecialFired _: fired++; break;
                    case SpecialCreated _: created++; break;
                    case Spawned _: spawned++; break;
                    case Shuffled _: shuffled++; break;
                    case Scored sc: depth = Math.Max(depth, sc.Depth); break;
                }
            }
            return matches + " match(es), " + cleared + " cleared, cascade depth " + depth + ", " + fired + " special(s) fired, " +
                   created + " made, " + spawned + " spawned" + (shuffled > 0 ? ", board shuffled" : "");
        }

        static string Name(Board b, CellId c)
        {
            var p = b.Topology.Pos(c);
            return p.X.ToString(CultureInfo.InvariantCulture) + "," + p.Y.ToString(CultureInfo.InvariantCulture);
        }

        static (CellId A, CellId B)? Parse(Board board, string text)
        {
            var nums = new List<int>();
            foreach (var tok in text.Split(new[] { ' ', ',', '-' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!int.TryParse(tok, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) return null;
                nums.Add(n);
            }
            if (nums.Count != 4) return null;
            var a = board.Topology.CellAt(nums[0], nums[1]);
            var b = board.Topology.CellAt(nums[2], nums[3]);
            if (a == null || b == null) return (new CellId(-1), new CellId(-1)); // off the board: TrySwap rejects it
            return (a.Value, b.Value);
        }

        static void Print(System.IO.TextWriter o, Session s)
        {
            var board = s.Board;
            var topo = board.Topology;
            int w = s.Level.Width, h = s.Level.Height;
            var sb = new StringBuilder("    ");
            for (int x = 0; x < w; x++) sb.Append(x.ToString(CultureInfo.InvariantCulture).PadRight(3));
            sb.Append('\n');
            for (int y = 0; y < h; y++)
            {
                sb.Append(y.ToString(CultureInfo.InvariantCulture).PadLeft(2)).Append("  ");
                for (int x = 0; x < w; x++)
                {
                    var cell = topo.CellAt(x, y);
                    var tile = cell == null ? null : board.TileAt(cell.Value);
                    string text = cell == null ? "." : tile == null ? "_" : tile.Color.ToString(CultureInfo.InvariantCulture) + Mark(tile.Special);
                    sb.Append(text.PadRight(3));
                }
                sb.Append('\n');
            }
            o.Write(sb.ToString());
            var progress = s.Progress;
            var goals = s.GoalDescriptions();
            for (int i = 0; i < goals.Count; i++)
                o.WriteLine("goal: " + goals[i] + "  (" + progress[i] + "/" + s.Goals[i].Target + ")");
            o.WriteLine("moves left: " + s.MovesLeft + "   score: " + s.Score);
        }

        static string Mark(Special? special) =>
            special is Special.Line l ? (l.Axis == 0 ? "-" : "|") : special is Special.Bomb ? "*" : special is Special.Color ? "@" : "";
    }
}
