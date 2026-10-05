using System;
using System.Collections.Generic;

namespace Cascade.Core
{
    /// <summary>
    /// The plain-data description of one generated level (TRD §2, §11): everything needed to play
    /// it. The board itself is rebuilt deterministically from <see cref="Seed"/> with
    /// <see cref="BuildBoard"/>. Immutable. The JSON form (<see cref="ToJson"/>) is the one in
    /// <c>conformance/levels/*.json</c>, with the seed as decimal text.
    /// </summary>
    public sealed record Level(
        int Number,
        ulong Seed,
        int Width,
        int Height,
        ValueList<(int X, int Y)> Holes,
        int Colors,
        ValueList<Goal> Goals,
        int MoveLimit,
        int Star2,
        int Star3,
        ValueList<string> Specials,
        string? Intro,
        double Difficulty,
        double TargetWinRate,
        double? MeasuredWinRate,
        int GeneratorVersion)
    {
        /// <summary>
        /// The starting board: grid with holes, gravity-down flow, no matches, at least one legal
        /// move, with <see cref="Specials"/> enabled. Draws from the <c>tiles</c> stream of <paramref name="streams"/>.
        /// </summary>
        public Board BuildBoard(Streams streams)
        {
            var topo = Topology.Grid(Width, Height, Holes);
            var board = Moves.NewBoard(topo, Flow.GravityDown(topo), Colors, streams);
            board.SpecialsEnabled = Specials;
            return board;
        }

        /// <summary>Writes the level as a JSON object (reference member order; seed as a decimal string).</summary>
        public void WriteJson(JsonWriter w)
        {
            w.BeginObject();
            w.Member("number", Number);
            w.Name("seed").ValueU64AsString(Seed);
            w.Member("width", Width).Member("height", Height);
            w.Name("holes").BeginArray();
            foreach (var (x, y) in Holes) w.BeginArray().Value(x).Value(y).EndArray();
            w.EndArray();
            w.Member("colors", Colors);
            w.Name("goals").BeginArray();
            foreach (var g in Goals) g.WriteJson(w);
            w.EndArray();
            w.Member("move_limit", MoveLimit).Member("star2", Star2).Member("star3", Star3);
            w.Name("specials").BeginArray();
            foreach (var s in Specials) w.Value(s);
            w.EndArray();
            w.Name("intro").Value(Intro);
            w.Name("difficulty").Value(Difficulty);
            w.Name("target_win_rate").Value(TargetWinRate);
            w.Name("measured_win_rate");
            if (MeasuredWinRate.HasValue) w.Value(MeasuredWinRate.Value); else w.Null();
            w.Member("generator_version", GeneratorVersion);
            w.EndObject();
        }

        /// <summary>The level as compact JSON.</summary>
        public string ToJson()
        {
            var w = new JsonWriter();
            WriteJson(w);
            return w.ToString();
        }

        /// <summary>Reads a level from its JSON object form. The seed may be a decimal string or a number.</summary>
        public static Level FromJson(JsonValue d)
        {
            var holes = new List<(int, int)>();
            foreach (var h in d["holes"].Items) holes.Add((h[0].AsInt(), h[1].AsInt()));
            var goals = new List<Goal>();
            foreach (var g in d["goals"].Items) goals.Add(Goal.FromJson(g));
            var specials = new List<string>();
            foreach (var s in d["specials"].Items) specials.Add(s.AsString());
            var intro = d.TryGet("intro");
            var measured = d.TryGet("measured_win_rate");
            return new Level(
                d["number"].AsInt(), d["seed"].AsU64(), d["width"].AsInt(), d["height"].AsInt(), holes,
                d["colors"].AsInt(), goals, d["move_limit"].AsInt(), d["star2"].AsInt(), d["star3"].AsInt(),
                specials, intro == null || intro.IsNull ? null : intro.AsString(),
                d["difficulty"].AsDouble(), d["target_win_rate"].AsDouble(),
                measured == null || measured.IsNull ? (double?)null : measured.AsDouble(),
                d["generator_version"].AsInt());
        }

        /// <summary>Parses a level from JSON text.</summary>
        public static Level FromJson(string json) => FromJson(JsonValue.Parse(json));
    }
}
