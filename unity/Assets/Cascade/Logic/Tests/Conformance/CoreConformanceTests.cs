using System.Collections.Generic;
using System.Linq;
using Cascade.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Cascade.Tests.Conformance
{
    /// <summary>
    /// Core conformance (TRD §9.2): <c>rng/vectors.json</c> and <c>golden/session_*.json</c> must
    /// match the reference exactly. Fixtures are an oracle and are never regenerated from C#.
    /// </summary>
    public class CoreConformanceTests
    {
        static ulong U64(JToken t) => ulong.Parse((string)t!, System.Globalization.CultureInfo.InvariantCulture);
        static uint[] U32s(JToken t) => t.Select(x => (uint)x).ToArray();

        static JToken Vectors() => ConformanceFiles.ReadJson("rng", "vectors.json");

        // -- rng -------------------------------------------------------------------------------

        [Test]
        public void Pcg32_vectors_next_u32_randbelow_random_and_shuffle()
        {
            var cases = Vectors()["pcg32"]!.ToArray();
            Assert.That(cases.Length, Is.GreaterThan(0));
            foreach (var c in cases)
            {
                ulong initstate = U64(c["initstate"]!), inc = U64(c["inc"]!);
                string label = $"initstate={initstate} inc={inc}";

                var r = new Pcg32(initstate, inc);
                Assert.That(Enumerable.Range(0, 10).Select(_ => r.NextU32()).ToArray(), Is.EqualTo(U32s(c["u32"]!)), label);

                // The exporter drew all bounds from ONE generator, in this order (not reset per bound).
                r = new Pcg32(initstate, inc);
                foreach (long n in new long[] { 1, 2, 3, 6, 10, 100, 4294967296L })
                {
                    var got = Enumerable.Range(0, 10).Select(_ => r.RandBelow(n)).ToArray();
                    Assert.That(got, Is.EqualTo(c["randbelow"]![n.ToString()]!.Select(x => (long)x).ToArray()), label + " randbelow " + n);
                }

                r = new Pcg32(initstate, inc);
                var floats = Enumerable.Range(0, 10).Select(_ => r.Random()).ToArray();
                Assert.That(floats, Is.EqualTo(c["random"]!.Select(x => (double)x).ToArray()), label + " random"); // exact ==

                r = new Pcg32(initstate, inc);
                var items = Enumerable.Range(0, 10).ToList();
                r.Shuffle(items);
                Assert.That(items, Is.EqualTo(c["shuffle_0_9"]!.Select(x => (int)x).ToList()), label + " shuffle");
            }
        }

        [Test]
        public void SplitMix64_vectors()
        {
            foreach (var v in Vectors()["splitmix64"]!)
                Assert.That(Rng.SplitMix64(U64(v["in"]!)), Is.EqualTo(U64(v["out"]!)), (string)v["in"]!);
        }

        [Test]
        public void Fnv1a64_vectors_utf8()
        {
            foreach (var v in Vectors()["fnv1a_64"]!)
                Assert.That(Rng.Fnv1a64(System.Text.Encoding.UTF8.GetBytes((string)v["utf8"]!)), Is.EqualTo(U64(v["out"]!)), (string)v["utf8"]!);
        }

        [Test]
        public void Substream_vectors()
        {
            var subs = Vectors()["substream"]!.ToArray();
            Assert.That(subs.Length, Is.EqualTo(20));
            foreach (var v in subs)
            {
                var s = Rng.Substream(U64(v["seed"]!), (string)v["name"]!);
                Assert.That(Enumerable.Range(0, 10).Select(_ => s.NextU32()).ToArray(), Is.EqualTo(U32s(v["u32"]!)),
                    $"seed {(string)v["seed"]!} name {(string)v["name"]!}");
            }
        }

        // -- golden sessions -----------------------------------------------------------------------

        public static IEnumerable<int> GoldenSeeds() => new[] { 1, 2, 3 };

        /// <summary>Replays a session exactly like <c>export_conformance.golden_session</c>: seed -> Streams, an 8x8 five-color board, the bot stream picks among the legal moves.</summary>
        static List<(JObject Expected, JObject Actual)> Replay(int seed, out int moves)
        {
            var fixture = (JArray)ConformanceFiles.ReadJson("golden", $"session_{seed}.json");
            var s = new Streams((ulong)seed);
            var topo = Topology.Grid(8, 8);
            var board = Moves.NewBoard(topo, Flow.GravityDown(topo), 5, s);
            var result = new List<(JObject, JObject)>();
            foreach (var step in fixture)
            {
                var mv = s.Bot.Choice(Moves.FindLegal(board));
                var (next, events) = Rules.Resolve(board, mv, s, RuleSet.Default);
                board = next;
                var actual = new JObject
                {
                    ["swap"] = new JArray(mv.A.Value, mv.B.Value),
                    ["events"] = JArray.Parse(Events.ToJson(events)),
                };
                result.Add(((JObject)step, actual));
            }
            moves = fixture.Count;
            return result;
        }

        [TestCaseSource(nameof(GoldenSeeds))]
        public void Golden_session_matches_the_reference_event_log(int seed)
        {
            var steps = Replay(seed, out int moves);
            Assert.That(moves, Is.EqualTo(12));
            for (int i = 0; i < steps.Count; i++)
            {
                var (expected, actual) = steps[i];
                Assert.That(ConformanceFiles.Canonical(actual["swap"]!), Is.EqualTo(ConformanceFiles.Canonical(expected["swap"]!)), $"session {seed} move {i}: swap");
                Assert.That(ConformanceFiles.Canonical(actual["events"]!), Is.EqualTo(ConformanceFiles.Canonical(expected["events"]!)), $"session {seed} move {i}: events");
            }
        }

        [TestCaseSource(nameof(GoldenSeeds))]
        public void Golden_events_parse_back_and_reserialize_identically(int seed)
        {
            var fixture = (JArray)ConformanceFiles.ReadJson("golden", $"session_{seed}.json");
            foreach (var step in fixture)
            {
                var original = (JArray)step["events"]!;
                var events = Events.FromJson(original.ToString());
                var again = JArray.Parse(Events.ToJson(events));
                Assert.That(ConformanceFiles.Canonical(again), Is.EqualTo(ConformanceFiles.Canonical(original)));
            }
        }

        [Test]
        public void Golden_sessions_cover_every_event_type_that_a_plain_session_can_produce()
        {
            var seen = new HashSet<string>();
            foreach (int seed in GoldenSeeds())
                foreach (var step in (JArray)ConformanceFiles.ReadJson("golden", $"session_{seed}.json"))
                    foreach (var e in (JArray)step["events"]!) seen.Add((string)e["type"]!);
            foreach (var type in new[] { "Swapped", "Matched", "Cleared", "Scored", "Spawned", "Moved" })
                Assert.That(seen, Does.Contain(type));
        }

        // -- level JSON form -------------------------------------------------------------------------

        [Test]
        public void Level_json_form_round_trips_every_level_fixture()
        {
            var files = System.IO.Directory.GetFiles(ConformanceFiles.FilePath("levels"), "level_*.json");
            Assert.That(files.Length, Is.EqualTo(74));
            foreach (var file in files.OrderBy(f => f, System.StringComparer.Ordinal))
            {
                var doc = (JObject)ConformanceFiles.ReadJson("levels", System.IO.Path.GetFileName(file));
                var levelJson = (JObject)doc["level"]!;
                var level = Level.FromJson(levelJson.ToString());
                Assert.That(level.Seed, Is.EqualTo(U64(doc["seed"]!)), file);
                Assert.That(ConformanceFiles.Canonical(JObject.Parse(level.ToJson())), Is.EqualTo(ConformanceFiles.Canonical(levelJson)), file);
                var board = level.BuildBoard(new Streams(level.Seed)); // the starting board is valid (Core only; generation is tested elsewhere)
                Assert.That(Match.FindRuns(board), Is.Empty, file);
                Assert.That(Moves.FindLegal(board), Is.Not.Empty, file);
            }
        }
    }
}
