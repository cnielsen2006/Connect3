using System;
using System.Collections.Generic;
using System.Globalization;

namespace Cascade.Tools
{
    /// <summary>Headless tools of the Cascade logic (TRD §1): report, calibrate, verify, deadlock-check, text, serve.</summary>
    public static class Program
    {
        const string Usage = @"Cascade.Tools - headless tools that run the same logic Unity runs

  report <from> <to> [--seed 1] [--out build/difficulty.csv]
        Generate levels from..to, playtest them with the bot, write a CSV of target vs
        measured win rate and print the generation times (mean/median/p95/max).
  calibrate [--step 5] [--max 150] [--seeds 3] [--runs 10]
        Suggest [params.score_per_move] / [params.collect_per_move] values.
  verify conformance [--conformance <folder>]
        Run the conformance fixtures (rng, golden, profiles, levels, bot) outside NUnit.
  export conformance [--conformance <folder>]
        Rewrite golden/, levels/ and bot/ from the C# logic (only after a deliberate rule change).
  deadlock-check [--levels 10000] [--moves 30] [--full]
        MRD section 5: generate levels and play random moves; no board may ever deadlock.
  serve [folder] [--port 28420] [--bind 0.0.0.0]
        Static web server for the WebGL build (default folder Builds/WebGL/dev) on every network interface, with the gzip
        Content-Encoding Unity needs; prints the LAN URLs to open on a phone.
  text [--level 1] [--seed 1] [--script ""x,y x,y; ...""]
        A small terminal view of the rules: print the board, play swaps (interactive without --script).

Options for every command:
  --data <folder>        folder holding dials.toml and difficulty.toml (default: unity/Assets/Cascade/Data)
  --budget-ms <n>        override generator.budget_ms
  --no-budget            generator.budget_ms = 0 (fully deterministic)
  --set section.key=v    set any dial (repeatable)
";

        public static int Main(string[] argv)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            if (argv.Length == 0 || argv[0] == "-h" || argv[0] == "--help" || argv[0] == "help")
            {
                Console.Out.Write(Usage);
                return argv.Length == 0 ? 2 : 0;
            }
            try
            {
                var args = new Args(argv, 1);
                switch (argv[0])
                {
                    case "report": return ReportCommand.Run(args);
                    case "calibrate": return CalibrateCommand.Run(args);
                    case "verify": return VerifyCommand.Run(args);
                    case "export": return ExportCommand.Run(args);
                    case "deadlock-check": return DeadlockCommand.Run(args);
                    case "text": return TextCommand.Run(args);
                    case "serve": return ServeCommand.Run(args);
                    default:
                        Console.Error.WriteLine("unknown command '" + argv[0] + "'");
                        Console.Error.Write(Usage);
                        return 2;
                }
            }
            catch (UsageException e)
            {
                Console.Error.WriteLine(e.Message);
                return 2;
            }
        }
    }

    /// <summary>A command line problem the user can fix.</summary>
    public sealed class UsageException : Exception
    {
        public UsageException(string message) : base(message) { }
    }

    /// <summary>Positional words and <c>--name value</c> / <c>--flag</c> options, in the order given.</summary>
    public sealed class Args
    {
        static readonly string[] Flags = { "no-budget", "full", "help" };
        readonly List<string> _positional = new List<string>();
        readonly List<KeyValuePair<string, string?>> _options = new List<KeyValuePair<string, string?>>();

        public Args(string[] argv, int start)
        {
            for (int i = start; i < argv.Length; i++)
            {
                string a = argv[i];
                if (!a.StartsWith("--", StringComparison.Ordinal)) { _positional.Add(a); continue; }
                string name = a.Substring(2);
                if (Array.IndexOf(Flags, name) >= 0) { _options.Add(new KeyValuePair<string, string?>(name, null)); continue; }
                if (i + 1 >= argv.Length) throw new UsageException("option --" + name + " needs a value");
                _options.Add(new KeyValuePair<string, string?>(name, argv[++i]));
            }
        }

        public IReadOnlyList<string> Positional => _positional;

        public bool Has(string name)
        {
            foreach (var o in _options) if (o.Key == name) return true;
            return false;
        }

        /// <summary>The last value given for an option, or null.</summary>
        public string? Get(string name)
        {
            string? value = null;
            foreach (var o in _options) if (o.Key == name) value = o.Value;
            return value;
        }

        public List<string> GetAll(string name)
        {
            var list = new List<string>();
            foreach (var o in _options) if (o.Key == name && o.Value != null) list.Add(o.Value);
            return list;
        }

        public int Int(string name, int fallback)
        {
            var v = Get(name);
            if (v == null) return fallback;
            if (!int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
                throw new UsageException("--" + name + " expects an integer, got '" + v + "'");
            return n;
        }

        public int PositionalInt(int index, string what)
        {
            if (index >= _positional.Count) throw new UsageException("missing <" + what + ">");
            if (!int.TryParse(_positional[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
                throw new UsageException("<" + what + "> expects an integer, got '" + _positional[index] + "'");
            return n;
        }
    }
}
