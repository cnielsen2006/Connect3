using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Cascade.Core;
using Cascade.Tests.Conformance;
using Newtonsoft.Json.Linq;

namespace Cascade.Tools
{
    /// <summary>
    /// <c>verify conformance</c>: runs the §9.2 fixtures outside NUnit: PCG32/SplitMix/FNV/substream
    /// vectors, the golden sessions, and the profile, level and bot fixtures. Exit code 0 only if every
    /// fixture matches. Fixtures are never rewritten.
    /// </summary>
    public static class VerifyCommand
    {
        public static int Run(Args args)
        {
            if (args.Positional.Count == 0 || args.Positional[0] != "conformance")
                throw new UsageException("usage: verify conformance [--conformance <folder>]");
            var env = Env.Load(args);
            string folder = args.Get("conformance") ?? Path.Combine(env.RepoRoot, "conformance");
            if (!Directory.Exists(folder)) throw new UsageException("no conformance folder at " + folder);

            // the fixtures were exported with budget_ms = 0 and every other dial at its default
            var dials = Env.LoadDefaultsWithoutBudget(env);
            var verifier = GenerationConformance.ForFolder(folder, env.Profile, dials);
            var o = Console.Out;
            bool ok = true;

            ok &= Show(o, "rng vectors", RngVectors(folder));
            ok &= Show(o, "golden sessions", Golden(folder));
            ok &= Show(o, "profiles/evaluations.json", verifier.VerifyProfiles());
            ok &= Show(o, "levels/*.json", verifier.VerifyLevels());
            ok &= Show(o, "bot/runs.json", verifier.VerifyBot());
            o.WriteLine(ok ? "CONFORMING: every fixture matches" : "NOT CONFORMING");
            return ok ? 0 : 1;
        }

        static bool Show(TextWriter o, string name, ConformanceReport r)
        {
            o.WriteLine((r.Passed ? "PASS " : "FAIL ") + name.PadRight(28) + r.Checks + " checks" +
                        (r.Inexact.Count > 0 ? ", " + r.Inexact.Count + " double(s) equal only within 1e-12" : ""));
            foreach (var line in r.Inexact) o.WriteLine("       ~ " + line);
            foreach (var line in r.Failures.Take(10)) o.WriteLine("       ! " + line);
            if (r.Failures.Count > 10) o.WriteLine("       ... and " + (r.Failures.Count - 10) + " more");
            return r.Passed;
        }

        // -- core fixtures ---------------------------------------------------------------------

        static ulong U64(JToken t) => ulong.Parse((string)t!, CultureInfo.InvariantCulture);

        static ConformanceReport RngVectors(string folder)
        {
            var r = new ConformanceReport();
            var doc = JToken.Parse(File.ReadAllText(Path.Combine(folder, "rng", "vectors.json")));
            void Check<T>(string label, T expected, T actual)
            {
                r.Checks++;
                if (!EqualityComparer<T>.Default.Equals(expected, actual)) r.Failures.Add(label + ": expected " + expected + " but got " + actual);
            }
            foreach (var c in doc["pcg32"]!)
            {
                ulong init = U64(c["initstate"]!), inc = U64(c["inc"]!);
                var rng = new Pcg32(init, inc);
                var got = string.Join(",", Enumerable.Range(0, 10).Select(_ => rng.NextU32()));
                Check("pcg32 " + init + "/" + inc + " u32", string.Join(",", c["u32"]!.Select(x => (uint)x)), got);
                rng = new Pcg32(init, inc);
                var floats = string.Join(",", Enumerable.Range(0, 10).Select(_ => rng.Random().ToString("R", CultureInfo.InvariantCulture)));
                Check("pcg32 " + init + "/" + inc + " random", string.Join(",", c["random"]!.Select(x => ((double)x).ToString("R", CultureInfo.InvariantCulture))), floats);
            }
            foreach (var v in doc["splitmix64"]!) Check("splitmix64 " + (string)v["in"]!, U64(v["out"]!), Rng.SplitMix64(U64(v["in"]!)));
            foreach (var v in doc["fnv1a_64"]!) Check("fnv1a_64 '" + (string)v["utf8"]! + "'", U64(v["out"]!), Rng.Fnv1a64((string)v["utf8"]!));
            foreach (var v in doc["substream"]!)
            {
                var s = Rng.Substream(U64(v["seed"]!), (string)v["name"]!);
                Check("substream " + (string)v["seed"]! + "/" + (string)v["name"]!, string.Join(",", v["u32"]!.Select(x => (uint)x)),
                    string.Join(",", Enumerable.Range(0, 10).Select(_ => s.NextU32())));
            }
            return r;
        }

        static ConformanceReport Golden(string folder)
        {
            var r = new ConformanceReport();
            foreach (int seed in new[] { 1, 2, 3 })
            {
                var fixture = (JArray)JToken.Parse(File.ReadAllText(Path.Combine(folder, "golden", "session_" + seed + ".json")));
                var s = new Streams((ulong)seed);
                var topo = Topology.Grid(8, 8);
                var board = Moves.NewBoard(topo, Flow.GravityDown(topo), 5, s);
                int i = 0;
                foreach (var step in fixture)
                {
                    var mv = s.Bot.Choice(Moves.FindLegal(board));
                    var (next, events) = Rules.Resolve(board, mv, s, RuleSet.Default);
                    board = next;
                    r.Checks += 2;
                    if (ConformanceFiles.Canonical(new JArray(mv.A.Value, mv.B.Value)) != ConformanceFiles.Canonical(step["swap"]!))
                        r.Failures.Add("golden " + seed + " move " + i + ": swap differs");
                    if (ConformanceFiles.Canonical(JArray.Parse(Events.ToJson(events))) != ConformanceFiles.Canonical(step["events"]!))
                        r.Failures.Add("golden " + seed + " move " + i + ": events differ");
                    i++;
                }
            }
            return r;
        }
    }
}
