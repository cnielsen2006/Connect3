using System;
using System.Collections.Generic;
using System.Globalization;

namespace Cascade.Core
{
    /// <summary>The names of the level RNG sub-streams (TRD §4.1).</summary>
    public static class StreamNames
    {
        public const string Layout = "layout";
        public const string Tiles = "tiles";
        public const string Goals = "goals";
        public const string Spawn = "spawn";
        public const string Specials = "specials";
        public const string Shuffle = "shuffle";
        public const string Bot = "bot";

        /// <summary>All stream names, in the order TRD §4.1 lists them.</summary>
        public static readonly string[] All = { Layout, Tiles, Goals, Spawn, Specials, Shuffle, Bot };

        /// <summary>The per-source spawn stream name, <c>spawn.&lt;source id&gt;</c>.</summary>
        public static string SpawnOf(SourceId id) => Spawn + "." + id.Value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The named sub-streams of one level. Mutable: state advances as the game is played. Streams
    /// are created lazily from the level seed; lookup is by ordinal name over a tiny list (never a
    /// dictionary iteration).
    /// </summary>
    public sealed class Streams
    {
        readonly List<KeyValuePair<string, Pcg32>> _streams = new List<KeyValuePair<string, Pcg32>>();

        public Streams(ulong levelSeed) { LevelSeed = levelSeed; }

        /// <summary>The seed every sub-stream is derived from.</summary>
        public ulong LevelSeed { get; }

        /// <summary>The sub-stream with this name, created on first use.</summary>
        public Pcg32 Get(string name)
        {
            for (int i = 0; i < _streams.Count; i++)
                if (string.Equals(_streams[i].Key, name, StringComparison.Ordinal))
                    return _streams[i].Value;
            var stream = Rng.Substream(LevelSeed, name);
            _streams.Add(new KeyValuePair<string, Pcg32>(name, stream));
            return stream;
        }

        public Pcg32 Layout => Get(StreamNames.Layout);
        public Pcg32 Tiles => Get(StreamNames.Tiles);
        public Pcg32 Goals => Get(StreamNames.Goals);
        public Pcg32 Specials => Get(StreamNames.Specials);
        public Pcg32 Shuffle => Get(StreamNames.Shuffle);
        public Pcg32 Bot => Get(StreamNames.Bot);

        /// <summary>The spawn stream of one source (<c>spawn.&lt;id&gt;</c>).</summary>
        public Pcg32 Spawn(SourceId source) => Get(StreamNames.SpawnOf(source));

        /// <summary>An independent copy: every stream created so far is copied in its current state.</summary>
        public Streams Copy()
        {
            var other = new Streams(LevelSeed);
            foreach (var kv in _streams)
                other._streams.Add(new KeyValuePair<string, Pcg32>(kv.Key, kv.Value.Copy()));
            return other;
        }
    }
}
