using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Cascade.Core;
using Cascade.Generation;
using Cascade.Gameplay;
using Cascade.Platform.Abstractions;
using Cascade.Tests.Conformance;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Cascade.Tools
{
    /// <summary>
    /// <c>export conformance</c>: rewrites the rule-dependent fixtures (<c>golden/</c>, <c>levels/</c>, <c>bot/</c>) from
    /// the C# logic. Used only when the TRD deliberately changes a rule (the original reference implementation is gone,
    /// so C# is the reference now). Keeps the same sessions, level numbers, seeds and bot runs as the existing files.
    /// Profiles, RNG vectors and <c>data/</c> are untouched (edit <c>data/dials.toml</c> by hand when a rule dial is added).
    /// </summary>
    public static class ExportCommand
    {
        sealed class NoClock : IStopwatch { public double ElapsedMilliseconds => 0; }

        public static int Run(Args args)
        {
            if (args.Positional.Count == 0 || args.Positional[0] != "conformance")
                throw new UsageException("usage: export conformance [--conformance <folder>]");
            var env = Env.Load(args);
            string folder = args.Get("conformance") ?? Path.Combine(env.RepoRoot, "conformance");
            var dials = Env.LoadDefaultsWithoutBudget(env);
            var rules = RuleSets.From(dials);
            var settings = RuleSets.GeneratorFrom(dials);
            var source = new GeneratingLevelSource(dials, env.Profile, new NoClock());

            // golden sessions: 12 moves per seed, exactly as the tests replay them
            foreach (int seed in new[] { 1, 2, 3 })
            {
                var s = new Streams((ulong)seed);
                var topo = Topology.Grid(8, 8);
                var board = Moves.NewBoard(topo, Flow.GravityDown(topo), 5, s);
                var steps = new JArray();
                for (int i = 0; i < 12; i++)
                {
                    var mv = s.Bot.Choice(Moves.FindLegal(board));
                    var (next, events) = Rules.Resolve(board, mv, s, rules);
                    board = next;
                    steps.Add(new JObject { ["swap"] = new JArray(mv.A.Value, mv.B.Value), ["events"] = JArray.Parse(Events.ToJson(events)) });
                }
                Write(Path.Combine(folder, "golden", "session_" + seed + ".json"), steps);
            }

            // levels: same (n, seed) pairs as the existing files
            int levels = 0;
            foreach (var file in Directory.GetFiles(Path.Combine(folder, "levels"), "level_*.json"))
            {
                var doc = JObject.Parse(File.ReadAllText(file));
                int n = (int)doc["n"]!;
                ulong seed = ulong.Parse((string)doc["seed"]!, CultureInfo.InvariantCulture);
                var level = source.Get(n, seed);
                doc["level"] = JObject.Parse(level.ToJson());
                Write(file, doc);
                levels++;
            }

            // bot runs: same (n, seed, run) triples
            string botFile = Path.Combine(folder, "bot", "runs.json");
            var runs = (JArray)JToken.Parse(File.ReadAllText(botFile));
            var boards = new Dictionary<(int, ulong), (Level, Board)>();
            foreach (JObject r in runs)
            {
                int n = (int)r["n"]!;
                ulong seed = ulong.Parse((string)r["seed"]!, CultureInfo.InvariantCulture);
                if (!boards.TryGetValue((n, seed), out var lb))
                {
                    var level = source.Get(n, seed);
                    lb = (level, Bot.StartBoard(level));
                    boards[(n, seed)] = lb;
                }
                var res = Bot.Play(lb.Item1, rules, settings, (int)r["run"]!, lb.Item2);
                r["won"] = res.Won; r["score"] = res.Score; r["moves"] = res.Moves;
            }
            Write(botFile, runs);

            Console.Out.WriteLine("exported 3 golden sessions, " + levels + " levels, " + runs.Count + " bot runs; run 'verify conformance' next");
            return 0;
        }

        /// <summary>Sorted members, one-space indent, LF: the layout the existing fixtures use.</summary>
        static void Write(string path, JToken token)
        {
            var sorted = JToken.Parse(ConformanceFiles.Canonical(token));
            using var sw = new StringWriter { NewLine = "\n" };
            using (var w = new JsonTextWriter(sw) { Formatting = Formatting.Indented, Indentation = 1, IndentChar = ' ' })
                sorted.WriteTo(w);
            File.WriteAllText(path, sw.ToString() + "\n", new System.Text.UTF8Encoding(false));
        }
    }
}
