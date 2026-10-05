using System.Collections.Generic;
using System.Linq;
using Cascade.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Cascade.Tests.CoreTests
{
    /// <summary>JSON contract of events (TRD §3.6) and the small JSON reader/writer in Core.</summary>
    public class EventsTests
    {
        static List<Event> AllKinds() => new List<Event>
        {
            new Swapped(1, 2),
            new SwapRejected(3, 4),
            new Matched(new CellId[] { 1, 2, 3 }, new[] { new Qualifier(QualifierKind.Run3, 3), new Qualifier(QualifierKind.Cross) }),
            new Cleared(new[] { 5, 6 }, 1),
            new SpecialFired(7, new CellId[] { 1, 2 }),
            new SpecialCreated(8, new Special.Line(1)),
            new SpecialCreated(9, new Special.Bomb()),
            new SpecialCreated(10, new Special.Color()),
            new Moved(11, new CellId[] { 1, 9, 17 }),
            new Spawned(12, 3, 4, 2),
            new Scored(270, 1, 1.5),
            new Shuffled(new[] { (1, 2), (3, 0) }),
        };

        [Test]
        public void Event_json_round_trip_all_types()
        {
            var events = AllKinds();
            Assert.That(events.Select(e => e.GetType()).Distinct().Count(), Is.EqualTo(10)); // every kind is covered
            Assert.That(Events.FromJson(Events.ToJson(events)), Is.EqualTo(events));
        }

        [Test]
        public void Each_event_matches_the_expected_json_text()
        {
            var expected = new[]
            {
                "{\"type\":\"Swapped\",\"a\":1,\"b\":2}",
                "{\"type\":\"SwapRejected\",\"a\":3,\"b\":4}",
                "{\"type\":\"Matched\",\"cells\":[1,2,3],\"qualifiers\":[{\"kind\":\"Run3\",\"length\":3},{\"kind\":\"Cross\",\"length\":0}]}",
                "{\"type\":\"Cleared\",\"tile_ids\":[5,6],\"wave\":1}",
                "{\"type\":\"SpecialFired\",\"tile_id\":7,\"cells\":[1,2]}",
                "{\"type\":\"SpecialCreated\",\"tile_id\":8,\"special\":{\"kind\":\"Line\",\"axis\":1}}",
                "{\"type\":\"SpecialCreated\",\"tile_id\":9,\"special\":{\"kind\":\"Bomb\"}}",
                "{\"type\":\"SpecialCreated\",\"tile_id\":10,\"special\":{\"kind\":\"Color\"}}",
                "{\"type\":\"Moved\",\"tile_id\":11,\"path\":[1,9,17]}",
                "{\"type\":\"Spawned\",\"tile_id\":12,\"source_id\":3,\"cell\":4,\"color\":2}",
                "{\"type\":\"Scored\",\"points\":270,\"depth\":1,\"multiplier\":1.5}",
                "{\"type\":\"Shuffled\",\"mapping\":[[1,2],[3,0]]}",
            };
            var events = AllKinds();
            for (int i = 0; i < events.Count; i++) Assert.That(events[i].ToJson(), Is.EqualTo(expected[i]), events[i].TypeName);
        }

        [Test]
        public void Written_json_parses_with_newtonsoft_and_type_member_is_first()
        {
            foreach (var e in AllKinds())
            {
                var obj = JObject.Parse(e.ToJson());
                Assert.That(obj.Properties().First().Name, Is.EqualTo("type"));
                Assert.That((string?)obj["type"], Is.EqualTo(e.TypeName));
            }
            var arr = JArray.Parse(Events.ToJson(AllKinds()));
            Assert.That(arr.Count, Is.EqualTo(12));
        }

        [Test]
        public void Newtonsoft_written_json_is_read_back_by_core()
        {
            // an independent writer (Json.NET, snake_case, other member order) must parse too
            var obj = new JObject
            {
                ["wave"] = 2,
                ["tile_ids"] = new JArray(4, 5),
                ["type"] = "Cleared",
            };
            Assert.That(Event.FromJson(JsonValue.Parse(obj.ToString())), Is.EqualTo(new Cleared(new[] { 4, 5 }, 2)));
        }

        [Test]
        public void Value_equality_of_events_and_lists()
        {
            Assert.That(new Moved(1, new CellId[] { 1, 2 }), Is.EqualTo(new Moved(1, new CellId[] { 1, 2 })));
            Assert.That(new Moved(1, new CellId[] { 1, 2 }), Is.Not.EqualTo(new Moved(1, new CellId[] { 1, 3 })));
            Assert.That(new Moved(1, new CellId[] { 1, 2 }).GetHashCode(), Is.EqualTo(new Moved(1, new CellId[] { 1, 2 }).GetHashCode()));
            Assert.That(new SpecialCreated(1, new Special.Line(0)), Is.Not.EqualTo(new SpecialCreated(1, new Special.Line(1))));
        }

        [Test]
        public void Unknown_event_type_is_rejected()
        {
            Assert.Throws<System.FormatException>(() => Events.FromJson("[{\"type\":\"Nope\"}]"));
        }

        [Test]
        public void JsonWriter_escapes_and_formats_doubles_in_round_trip_form()
        {
            var w = new JsonWriter();
            w.BeginObject().Member("s", "a\"b\\c\n\u0001é").Name("d").Value(1.0).Name("e").Value(0.4).Name("n").Null()
                .Name("u").ValueU64AsString(ulong.MaxValue).Name("l").Value(-5L).Name("b").Value(true).EndObject();
            Assert.That(w.ToString(), Is.EqualTo("{\"s\":\"a\\\"b\\\\c\\n\\u0001é\",\"d\":1.0,\"e\":0.4,\"n\":null,\"u\":\"18446744073709551615\",\"l\":-5,\"b\":true}"));
            var back = JsonValue.Parse(w.ToString());
            Assert.That(back["s"].AsString(), Is.EqualTo("a\"b\\c\n\u0001é"));
            Assert.That(back["u"].AsU64(), Is.EqualTo(ulong.MaxValue));
            Assert.That(back["d"].AsDouble(), Is.EqualTo(1.0));
            Assert.That(back["n"].IsNull, Is.True);
            Assert.That(JObject.Parse(w.ToString())["u"]!.Value<string>(), Is.EqualTo("18446744073709551615"));
        }

        [Test]
        public void JsonValue_parses_nested_documents_and_rejects_garbage()
        {
            var v = JsonValue.Parse(" {\"a\": [1, 2.5, \"x\", true, null, {\"k\": -3}], \"b\": {}} ");
            Assert.That(v["a"].Count, Is.EqualTo(6));
            Assert.That(v["a"][0].AsInt(), Is.EqualTo(1));
            Assert.That(v["a"][1].AsDouble(), Is.EqualTo(2.5));
            Assert.That(v["a"][3].AsBool(), Is.True);
            Assert.That(v["a"][5]["k"].AsInt(), Is.EqualTo(-3));
            Assert.That(v["b"].Count, Is.EqualTo(0));
            Assert.That(v.TryGet("zzz"), Is.Null);
            Assert.Throws<System.FormatException>(() => JsonValue.Parse("{\"a\":}"));
            Assert.Throws<System.FormatException>(() => JsonValue.Parse("[1,2] x"));
            Assert.Throws<System.FormatException>(() => JsonValue.Parse("[1,2"));
        }
    }
}
