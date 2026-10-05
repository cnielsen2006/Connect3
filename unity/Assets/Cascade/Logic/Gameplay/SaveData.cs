using System;
using System.Collections.Generic;

namespace Cascade.Gameplay
{
    /// <summary>
    /// An immutable map from level number to best stars (0-3), kept sorted by level so it has a
    /// deterministic order and value equality. Never iterated through a hash container.
    /// </summary>
    public sealed class StarTable : IEquatable<StarTable>
    {
        /// <summary>No stars yet.</summary>
        public static readonly StarTable Empty = new StarTable(new int[0], new int[0]);

        readonly int[] _levels; // ascending
        readonly int[] _stars;

        StarTable(int[] levels, int[] stars) { _levels = levels; _stars = stars; }

        /// <summary>Builds a table from (level, stars) pairs; a repeated level keeps the last pair.</summary>
        public static StarTable From(IEnumerable<KeyValuePair<int, int>> pairs)
        {
            var t = Empty;
            foreach (var kv in pairs) t = t.With(kv.Key, kv.Value);
            return t;
        }

        public int Count => _levels.Length;

        /// <summary>The levels that have an entry, ascending.</summary>
        public IReadOnlyList<int> Levels => _levels;

        /// <summary>Sum of the best stars of all levels.</summary>
        public int Total
        {
            get
            {
                int sum = 0;
                foreach (int s in _stars) sum += s;
                return sum;
            }
        }

        /// <summary>The best stars of a level; 0 if it has none.</summary>
        public int Get(int level)
        {
            int i = Array.BinarySearch(_levels, level);
            return i >= 0 ? _stars[i] : 0;
        }

        /// <summary>True if the level has an entry (even with 0 stars).</summary>
        public bool Contains(int level) => Array.BinarySearch(_levels, level) >= 0;

        /// <summary>A copy with this level set to <paramref name="stars"/> (replacing any entry).</summary>
        public StarTable With(int level, int stars)
        {
            int i = Array.BinarySearch(_levels, level);
            if (i >= 0)
            {
                var s = (int[])_stars.Clone();
                s[i] = stars;
                return new StarTable(_levels, s);
            }
            int at = ~i;
            var lv = new int[_levels.Length + 1];
            var st = new int[_levels.Length + 1];
            Array.Copy(_levels, 0, lv, 0, at);
            Array.Copy(_stars, 0, st, 0, at);
            lv[at] = level;
            st[at] = stars;
            Array.Copy(_levels, at, lv, at + 1, _levels.Length - at);
            Array.Copy(_stars, at, st, at + 1, _levels.Length - at);
            return new StarTable(lv, st);
        }

        public bool Equals(StarTable? other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other is null || other._levels.Length != _levels.Length) return false;
            for (int i = 0; i < _levels.Length; i++)
                if (_levels[i] != other._levels[i] || _stars[i] != other._stars[i]) return false;
            return true;
        }

        public override bool Equals(object? obj) => obj is StarTable t && Equals(t);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = 17;
                for (int i = 0; i < _levels.Length; i++) h = (h * 31 + _levels[i]) * 31 + _stars[i];
                return h;
            }
        }

        public static bool operator ==(StarTable? a, StarTable? b) => a is null ? b is null : a.Equals(b);
        public static bool operator !=(StarTable? a, StarTable? b) => !(a == b);

        public override string ToString()
        {
            var parts = new List<string>();
            for (int i = 0; i < _levels.Length; i++) parts.Add(_levels[i] + ":" + _stars[i]);
            return "{" + string.Join(", ", parts) + "}";
        }
    }

    /// <summary>
    /// The saved player progress (TRD §8): what is needed to resume and to regenerate the current
    /// level, never the level itself. Immutable; <see cref="PlayerProgress"/> produces new values.
    /// The JSON form is written by <c>Cascade.Persistence.SaveService</c>.
    /// </summary>
    public sealed record SaveData
    {
        /// <summary>The save file format version this build writes and reads.</summary>
        public const int CurrentSaveVersion = 1;

        public int SaveVersion { get; init; } = CurrentSaveVersion;

        /// <summary>The generator version <see cref="CurrentSeed"/> belongs to.</summary>
        public int GeneratorVersion { get; init; } = Gameplay.GeneratorVersion.Current;

        /// <summary>The frontier level: the first one not yet won.</summary>
        public int CurrentLevel { get; init; } = 1;

        /// <summary>The seed of <see cref="CurrentLevel"/>, kept for retries until the level is won; null if none yet.</summary>
        public ulong? CurrentSeed { get; init; }

        /// <summary>Best stars per level number.</summary>
        public StarTable Stars { get; init; } = StarTable.Empty;

        /// <summary>The adaptive difficulty offset (U8; stored, unused until then).</summary>
        public double AdaptiveOffset { get; init; }
    }
}
