using System;
using System.Collections.Generic;

namespace Cascade.Core
{
    /// <summary>
    /// Something that happened while resolving a move (TRD §3.6). Events reference tile ids and cell
    /// ids, never pixels; the view replays them. One sealed record per kind, so a consumer can use an
    /// exhaustive <c>switch</c> over the types. Records have value equality (lists compare by content).
    /// </summary>
    public abstract record Event
    {
        /// <summary>The type name used as the JSON <c>"type"</c> member, for example "Swapped".</summary>
        public abstract string TypeName { get; }

        /// <summary>Writes the fields (not the surrounding braces or the type member), snake_case, in declaration order.</summary>
        protected abstract void WriteFields(JsonWriter w);

        /// <summary>Writes this event as a JSON object: <c>type</c> first, then the fields.</summary>
        public void WriteJson(JsonWriter w)
        {
            w.BeginObject().Member("type", TypeName);
            WriteFields(w);
            w.EndObject();
        }

        /// <summary>This event as compact JSON (the golden-log format).</summary>
        public string ToJson()
        {
            var w = new JsonWriter();
            WriteJson(w);
            return w.ToString();
        }

        /// <summary>Reads one event from its JSON object form.</summary>
        public static Event FromJson(JsonValue v)
        {
            string type = v["type"].AsString();
            switch (type)
            {
                case "Swapped": return new Swapped(v["a"].AsInt(), v["b"].AsInt());
                case "SwapRejected": return new SwapRejected(v["a"].AsInt(), v["b"].AsInt());
                case "Matched":
                    return new Matched(JsonCodec.ReadCells(v["cells"]), JsonCodec.ReadQualifiers(v["qualifiers"]));
                case "Cleared": return new Cleared(JsonCodec.ReadInts(v["tile_ids"]), v["wave"].AsInt());
                case "SpecialFired": return new SpecialFired(v["tile_id"].AsInt(), JsonCodec.ReadCells(v["cells"]));
                case "SpecialCreated": return new SpecialCreated(v["tile_id"].AsInt(), JsonCodec.ReadSpecial(v["special"]));
                case "Moved": return new Moved(v["tile_id"].AsInt(), JsonCodec.ReadCells(v["path"]));
                case "Spawned":
                    return new Spawned(v["tile_id"].AsInt(), v["source_id"].AsInt(), v["cell"].AsInt(), v["color"].AsInt());
                case "Scored": return new Scored(v["points"].AsInt(), v["depth"].AsInt(), v.TryGet("multiplier")?.AsDouble() ?? 1.0);
                case "Shuffled": return new Shuffled(JsonCodec.ReadMapping(v["mapping"]));
                default: throw new FormatException("unknown event type \"" + type + "\"");
            }
        }
    }

    /// <summary>Two tiles were swapped (cells a and b).</summary>
    public sealed record Swapped(CellId A, CellId B) : Event
    {
        public override string TypeName => "Swapped";
        protected override void WriteFields(JsonWriter w) => w.Member("a", A.Value).Member("b", B.Value);
    }

    /// <summary>The swap makes no match and was undone; the board is unchanged and no move is used.</summary>
    public sealed record SwapRejected(CellId A, CellId B) : Event
    {
        public override string TypeName => "SwapRejected";
        protected override void WriteFields(JsonWriter w) => w.Member("a", A.Value).Member("b", B.Value);
    }

    /// <summary>A match group: the cells that will clear (ascending) and every qualifier it counts for.</summary>
    public sealed record Matched(ValueList<CellId> Cells, ValueList<Qualifier> Qualifiers) : Event
    {
        public override string TypeName => "Matched";
        protected override void WriteFields(JsonWriter w)
        {
            JsonCodec.WriteCells(w.Name("cells"), Cells);
            JsonCodec.WriteQualifiers(w.Name("qualifiers"), Qualifiers);
        }
    }

    /// <summary>These tiles were removed. <see cref="Wave"/> 0 is the match itself; later waves are cleared by specials that fired.</summary>
    public sealed record Cleared(ValueList<int> TileIds, int Wave) : Event
    {
        public override string TypeName => "Cleared";
        protected override void WriteFields(JsonWriter w)
        {
            JsonCodec.WriteInts(w.Name("tile_ids"), TileIds);
            w.Member("wave", Wave);
        }
    }

    /// <summary>A special tile fired and hit these cells (ascending, including its own).</summary>
    public sealed record SpecialFired(int TileId, ValueList<CellId> Cells) : Event
    {
        public override string TypeName => "SpecialFired";
        protected override void WriteFields(JsonWriter w)
        {
            w.Member("tile_id", TileId);
            JsonCodec.WriteCells(w.Name("cells"), Cells);
        }
    }

    /// <summary>An existing or just-spawned tile became a special.</summary>
    public sealed record SpecialCreated(int TileId, Special Special) : Event
    {
        public override string TypeName => "SpecialCreated";
        protected override void WriteFields(JsonWriter w)
        {
            w.Member("tile_id", TileId);
            JsonCodec.WriteSpecial(w.Name("special"), Special);
        }
    }

    /// <summary>A tile traveled along <see cref="Path"/> (first cell = where it was, last = where it rests; may pass through a funnel).</summary>
    public sealed record Moved(int TileId, ValueList<CellId> Path) : Event
    {
        public override string TypeName => "Moved";
        protected override void WriteFields(JsonWriter w)
        {
            w.Member("tile_id", TileId);
            JsonCodec.WriteCells(w.Name("path"), Path);
        }
    }

    /// <summary>A new tile entered the board at <see cref="Cell"/> (where it enters; a <see cref="Moved"/> may follow).</summary>
    public sealed record Spawned(int TileId, SourceId SourceId, CellId Cell, int Color) : Event
    {
        public override string TypeName => "Spawned";
        protected override void WriteFields(JsonWriter w)
        {
            w.Member("tile_id", TileId).Member("source_id", SourceId.Value).Member("cell", Cell.Value).Member("color", Color);
        }
    }

    /// <summary>
    /// Points scored by one match group (or one bonus) at cascade <see cref="Depth"/> (0 = the player's move),
    /// with the combo <see cref="Multiplier"/> already applied (PRD 2.3, TRD 3.1).
    /// </summary>
    public sealed record Scored(int Points, int Depth, double Multiplier = 1.0) : Event
    {
        public override string TypeName => "Scored";
        protected override void WriteFields(JsonWriter w) => w.Member("points", Points).Member("depth", Depth).Name("multiplier").Value(Multiplier);
    }

    /// <summary>The board had no legal move and its colors were permuted: (tile id, new color) for every tile.</summary>
    public sealed record Shuffled(ValueList<(int TileId, int Color)> Mapping) : Event
    {
        public override string TypeName => "Shuffled";
        protected override void WriteFields(JsonWriter w) => JsonCodec.WriteMapping(w.Name("mapping"), Mapping);
    }

    /// <summary>Whole-list JSON helpers for event logs.</summary>
    public static class Events
    {
        /// <summary>An event list as a compact JSON array.</summary>
        public static string ToJson(IEnumerable<Event> events)
        {
            var w = new JsonWriter();
            WriteJson(w, events);
            return w.ToString();
        }

        public static void WriteJson(JsonWriter w, IEnumerable<Event> events)
        {
            w.BeginArray();
            foreach (var e in events) e.WriteJson(w);
            w.EndArray();
        }

        /// <summary>Parses a JSON array of events.</summary>
        public static List<Event> FromJson(string json)
        {
            var list = new List<Event>();
            foreach (var item in JsonValue.Parse(json).Items) list.Add(Event.FromJson(item));
            return list;
        }
    }

    /// <summary>Shared (de)serialisation of the small value shapes inside events and levels.</summary>
    static class JsonCodec
    {
        public static void WriteCells(JsonWriter w, IReadOnlyList<CellId> cells)
        {
            w.BeginArray();
            foreach (var c in cells) w.Value(c.Value);
            w.EndArray();
        }

        public static void WriteInts(JsonWriter w, IReadOnlyList<int> ints)
        {
            w.BeginArray();
            foreach (int i in ints) w.Value(i);
            w.EndArray();
        }

        public static void WriteQualifiers(JsonWriter w, IReadOnlyList<Qualifier> quals)
        {
            w.BeginArray();
            foreach (var q in quals) w.BeginObject().Member("kind", q.Kind.ToString()).Member("length", q.Length).EndObject();
            w.EndArray();
        }

        public static void WriteSpecial(JsonWriter w, Special s)
        {
            w.BeginObject().Member("kind", s.Kind);
            if (s is Special.Line line) w.Member("axis", line.Axis);
            w.EndObject();
        }

        public static void WriteMapping(JsonWriter w, IReadOnlyList<(int TileId, int Color)> mapping)
        {
            w.BeginArray();
            foreach (var (tile, color) in mapping) w.BeginArray().Value(tile).Value(color).EndArray();
            w.EndArray();
        }

        public static CellId[] ReadCells(JsonValue v)
        {
            var a = new CellId[v.Count];
            for (int i = 0; i < a.Length; i++) a[i] = new CellId(v[i].AsInt());
            return a;
        }

        public static int[] ReadInts(JsonValue v)
        {
            var a = new int[v.Count];
            for (int i = 0; i < a.Length; i++) a[i] = v[i].AsInt();
            return a;
        }

        public static Qualifier[] ReadQualifiers(JsonValue v)
        {
            var a = new Qualifier[v.Count];
            for (int i = 0; i < a.Length; i++)
            {
                string kind = v[i]["kind"].AsString();
                if (!Enum.TryParse(kind, out QualifierKind k) || !Enum.IsDefined(typeof(QualifierKind), k))
                    throw new FormatException("unknown qualifier kind \"" + kind + "\"");
                a[i] = new Qualifier(k, v[i]["length"].AsInt());
            }
            return a;
        }

        public static Special ReadSpecial(JsonValue v)
        {
            string kind = v["kind"].AsString();
            switch (kind)
            {
                case SpecialKinds.Line: return new Special.Line(v["axis"].AsInt());
                case SpecialKinds.Bomb: return new Special.Bomb();
                case SpecialKinds.Color: return new Special.Color();
                default: throw new FormatException("unknown special kind \"" + kind + "\"");
            }
        }

        public static (int TileId, int Color)[] ReadMapping(JsonValue v)
        {
            var a = new (int, int)[v.Count];
            for (int i = 0; i < a.Length; i++) a[i] = (v[i][0].AsInt(), v[i][1].AsInt());
            return a;
        }
    }
}
