using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Cascade.Core;
using Cascade.Platform.Abstractions;
using Tomlyn;
using Tomlyn.Model;

namespace Cascade.Generation
{
    /// <summary>
    /// The difficulty profile (TRD §6): loads <c>difficulty.toml</c>, validates it, and evaluates it
    /// for a level number. An invalid file is rejected with a <see cref="ProfileException"/> naming
    /// the key. In dev builds <see cref="ReloadIfChanged"/> hot-reloads it; an invalid edit keeps the
    /// old profile and sets <see cref="LastError"/>. Not thread-safe (see <see cref="GeneratingLevelSource"/>).
    /// </summary>
    public sealed class Profile
    {
        /// <summary>The shipped profile's key in the <see cref="IDataSource"/>.</summary>
        public const string DataKey = "difficulty.toml";

        /// <summary>The dev override file in the user data folder (read through <see cref="IStorage"/>).</summary>
        public const string DevOverrideFile = "dev_difficulty.toml";

        // unlock key -> mechanic name shown by the intro tooltip (declaration order = display order)
        static readonly (string Key, string Name)[] UnlockNames =
        {
            ("goal_collect", "Collect goals"),
            ("special_line", "Line pieces"),
            ("special_bomb", "Bomb pieces"),
            ("holes", "Shaped boards"),
            ("special_color", "Color pieces"),
            ("combo_goals", "Combined goals"),
        };

        // unlock key -> special name enabled by it
        static readonly (string Key, string Special)[] SpecialUnlocks =
        {
            ("special_line", SpecialKinds.Line),
            ("special_bomb", SpecialKinds.Bomb),
            ("special_color", SpecialKinds.Color),
        };

        static readonly string[] RequiredParams =
        {
            "colors", "board_size", "move_limit", "goal_amount", "score_per_move", "collect_per_move",
        };

        sealed class State
        {
            public int Version;
            public Curve Trend = null!, Amplitude = null!;
            public int CycleLength;
            public double RampPower, ReleaseDepth, IntroDifficulty, EasiestWinRate, HardestWinRate;
            public Curve Colors = null!, BoardSize = null!, MoveLimit = null!, GoalAmount = null!,
                ScorePerMove = null!, CollectPerMove = null!;
            public int[] UnlockLevels = null!; // parallel to UnlockNames
            public int[] BandStarts = null!; // ascending
            public (string Kind, double Weight)[][] Bands = null!; // parallel to BandStarts, canonical kind order
        }

        readonly Func<string>? _reader;
        string _text;
        State _s = null!;

        Profile(string text, Func<string>? reader)
        {
            _text = text;
            _reader = reader;
            _s = Parse(text, "difficulty profile");
        }

        // -- loading ---------------------------------------------------------------------------

        /// <summary>A profile from TOML text. It has no source, so <see cref="ReloadIfChanged"/> does nothing.</summary>
        public static Profile FromText(string toml) => new Profile(toml, null);

        /// <summary>
        /// The shipped profile (<see cref="DataKey"/> from <paramref name="data"/>), or, when
        /// <paramref name="storage"/> holds a <see cref="DevOverrideFile"/>, that file (dev builds).
        /// <see cref="ReloadIfChanged"/> re-reads the same sources.
        /// </summary>
        public static Profile Load(IDataSource data, IStorage? storage = null)
        {
            string Read()
            {
                var dev = storage?.TryRead(DevOverrideFile);
                if (dev != null) return Encoding.UTF8.GetString(dev).TrimStart('﻿');
                return data.ReadText(DataKey);
            }
            return new Profile(Read(), Read);
        }

        /// <summary>The last reload error (an invalid edit that was ignored), or null.</summary>
        public string? LastError { get; private set; }

        /// <summary>
        /// Dev hot reload: re-reads the profile's sources and, if the text changed, parses it. Returns
        /// true if the profile changed. An invalid edit keeps the old profile and sets <see cref="LastError"/>.
        /// </summary>
        public bool ReloadIfChanged()
        {
            if (_reader == null) return false;
            string text;
            try { text = _reader(); }
            catch (Exception) { return false; } // source vanished: keep what we have
            if (string.Equals(text, _text, StringComparison.Ordinal)) return false;
            _text = text; // remember it either way, so a bad edit is reported once
            try { _s = Parse(text, "difficulty profile"); }
            catch (ProfileException e)
            {
                LastError = e.Message;
                return false;
            }
            LastError = null;
            return true;
        }

        // -- parsing ---------------------------------------------------------------------------

        static double Num(object? value, string where)
        {
            switch (value)
            {
                case long l: return l;
                case int i: return i;
                case double d: return d;
                default:
                    throw new ProfileException(where + ": expected a number, got " + Describe(value));
            }
        }

        static bool IsInteger(object? value, out long l)
        {
            switch (value)
            {
                case long a: l = a; return true;
                case int b: l = b; return true;
                default: l = 0; return false;
            }
        }

        static string Describe(object? value) =>
            value == null ? "null" : Convert.ToString(value is TomlTable ? "a table" : value is TomlArray ? "a list" : value, CultureInfo.InvariantCulture) ?? "?";

        static Curve ParseCurve(object? spec, string where)
        {
            if (!(spec is TomlTable t) || !t.ContainsKey("curve") || !t.ContainsKey("points"))
                throw new ProfileException(where + ": expected { curve = ..., points = [[n, v], ...] }");
            var pointsRaw = t["points"];
            if (!(pointsRaw is TomlArray arr) || arr.Count == 0)
                throw new ProfileException(where + ".points: expected a non-empty list of [n, value] pairs");
            var pts = new List<(double, double)>();
            for (int i = 0; i < arr.Count; i++)
            {
                string at = where + ".points[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                if (!(arr[i] is TomlArray p) || p.Count != 2) throw new ProfileException(at + ": expected [n, value]");
                pts.Add((Num(p[0], at), Num(p[1], at)));
            }
            string name = t["curve"] is string s ? s : Describe(t["curve"]);
            if (!Curve.TryParseShape(name, out var shape))
                throw new ProfileException(where + ": unknown curve shape '" + name + "' (expected one of linear, ease_out, step)");
            try { return new Curve(shape, pts); }
            catch (ArgumentException e) { throw new ProfileException(where + ": " + e.Message); }
        }

        static TomlTable Section(TomlTable data, string key)
        {
            if (data.TryGetValue(key, out var v) && v is TomlTable t) return t;
            throw new ProfileException("missing section [" + key + "]");
        }

        static object? Get(TomlTable t, string key) => t.TryGetValue(key, out var v) ? v : null;

        static List<string> SortedKeys(TomlTable t)
        {
            var keys = new List<string>(t.Keys);
            keys.Sort(StringComparer.Ordinal);
            return keys;
        }

        /// <summary>Validates everything first and only then returns, so a bad reload leaves the old state.</summary>
        static State Parse(string text, string source)
        {
            TomlTable data;
            try { data = Toml.ToModel(text); }
            catch (TomlException e) { throw new ProfileException(source + ": invalid TOML: " + e.Message); }

            var s = new State();
            var meta = Section(data, "meta");
            if (!IsInteger(Get(meta, "version"), out long version)) throw new ProfileException("[meta].version: expected an integer");
            s.Version = (int)version;

            var d = Section(data, "difficulty");
            s.Trend = ParseCurve(Get(d, "trend"), "[difficulty].trend");
            s.Amplitude = ParseCurve(Get(d, "amplitude"), "[difficulty].amplitude");
            s.CycleLength = (int)Math.Truncate(Num(Get(d, "cycle_length"), "[difficulty].cycle_length"));
            if (s.CycleLength < 3) throw new ProfileException("[difficulty].cycle_length: must be >= 3");
            s.RampPower = Num(Get(d, "ramp_power"), "[difficulty].ramp_power");
            s.ReleaseDepth = Num(Get(d, "release_depth"), "[difficulty].release_depth");
            if (s.RampPower <= 0) throw new ProfileException("[difficulty].ramp_power: must be > 0");
            double Rate(string name)
            {
                double v = Num(Get(d, name), "[difficulty]." + name);
                if (!(v >= 0 && v <= 1)) throw new ProfileException("[difficulty]." + name + ": must be within 0..1");
                return v;
            }
            s.IntroDifficulty = Rate("intro_level_difficulty");
            s.EasiestWinRate = Rate("easiest_win_rate");
            s.HardestWinRate = Rate("hardest_win_rate");

            var paramsRaw = Section(data, "params");
            var curves = new Curve[RequiredParams.Length];
            for (int i = 0; i < RequiredParams.Length; i++)
            {
                string name = RequiredParams[i];
                if (!paramsRaw.ContainsKey(name)) throw new ProfileException("missing section [params." + name + "]");
                curves[i] = ParseCurve(paramsRaw[name], "[params." + name + "]");
            }
            s.Colors = curves[0]; s.BoardSize = curves[1]; s.MoveLimit = curves[2];
            s.GoalAmount = curves[3]; s.ScorePerMove = curves[4]; s.CollectPerMove = curves[5];

            var unlocksRaw = Section(data, "unlocks");
            s.UnlockLevels = new int[UnlockNames.Length];
            for (int i = 0; i < UnlockNames.Length; i++)
            {
                string name = UnlockNames[i].Key;
                if (!IsInteger(Get(unlocksRaw, name), out long v) || v < 1 || v > int.MaxValue)
                    throw new ProfileException("[unlocks]." + name + ": expected a level number >= 1");
                s.UnlockLevels[i] = (int)v;
            }
            foreach (string name in SortedKeys(unlocksRaw))
            {
                bool known = false;
                foreach (var u in UnlockNames) if (u.Key == name) known = true;
                if (!known) throw new ProfileException("[unlocks]." + name + ": unknown unlock");
            }

            var weightsRaw = Section(data, "goal_weights");
            var starts = new List<int>();
            var bands = new List<(string, double)[]>();
            var seenStarts = new SortedDictionary<int, (string, double)[]>();
            foreach (string key in SortedKeys(weightsRaw))
            {
                if (!int.TryParse(key.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int start))
                    throw new ProfileException("[goal_weights]." + key + ": key must be a level number");
                if (!(weightsRaw[key] is TomlTable w) || w.Count == 0)
                    throw new ProfileException("[goal_weights]." + key + ": expected { score = 1.0, ... }");
                var weights = new double[GoalKinds.All.Length];
                double total = 0;
                foreach (string kind in SortedKeys(w))
                {
                    int ki = Array.IndexOf(GoalKinds.All, kind);
                    if (ki < 0) throw new ProfileException("[goal_weights]." + key + "." + kind + ": unknown goal kind");
                    double weight = Num(w[kind], "[goal_weights]." + key + "." + kind);
                    if (weight < 0) throw new ProfileException("[goal_weights]." + key + "." + kind + ": must be >= 0");
                    weights[ki] = weight;
                }
                var band = new List<(string, double)>();
                for (int ki = 0; ki < weights.Length; ki++)
                {
                    if (!w.ContainsKey(GoalKinds.All[ki])) continue;
                    band.Add((GoalKinds.All[ki], weights[ki]));
                    total += weights[ki];
                }
                if (total <= 0) throw new ProfileException("[goal_weights]." + key + ": weights must not all be zero");
                seenStarts[start] = band.ToArray();
            }
            if (seenStarts.Count == 0) throw new ProfileException("[goal_weights]: at least one band is required");
            foreach (var kv in seenStarts) { starts.Add(kv.Key); bands.Add(kv.Value); }
            s.BandStarts = starts.ToArray();
            s.Bands = bands.ToArray();
            return s;
        }

        // -- queries ---------------------------------------------------------------------------

        public int Version => _s.Version;
        public int CycleLength => _s.CycleLength;
        public double RampPower => _s.RampPower;
        public double ReleaseDepth => _s.ReleaseDepth;
        public double IntroDifficulty => _s.IntroDifficulty;
        public double EasiestWinRate => _s.EasiestWinRate;
        public double HardestWinRate => _s.HardestWinRate;

        /// <summary>The level at which the unlock named <paramref name="key"/> (for example "holes") is reached.</summary>
        public int UnlockLevel(string key)
        {
            for (int i = 0; i < UnlockNames.Length; i++)
                if (UnlockNames[i].Key == key) return _s.UnlockLevels[i];
            throw new ArgumentException("unknown unlock '" + key + "'");
        }

        /// <summary>The unlock keys whose level is exactly <paramref name="n"/> (this level introduces them).</summary>
        public List<string> IntroUnlocks(int n)
        {
            var list = new List<string>();
            for (int i = 0; i < UnlockNames.Length; i++) if (_s.UnlockLevels[i] == n) list.Add(UnlockNames[i].Key);
            return list;
        }

        /// <summary><c>difficulty(n)</c> from the pressure/release curve (ignores the intro override).</summary>
        public double DifficultyAt(int n) =>
            Difficulty.At(n, _s.Trend, _s.Amplitude, _s.CycleLength, _s.RampPower, _s.ReleaseDepth);

        /// <summary>The bot win rate a level of this difficulty aims for.</summary>
        public double TargetWinRateFor(double difficulty) =>
            _s.EasiestWinRate + (_s.HardestWinRate - _s.EasiestWinRate) * difficulty;

        /// <summary>Evaluates the profile for level <paramref name="n"/> (n &gt;= 1).</summary>
        public LevelParams Evaluate(int n)
        {
            if (n < 1) throw new ArgumentOutOfRangeException(nameof(n), "level numbers start at 1");
            var s = _s;
            var introKeys = IntroUnlocks(n);
            bool isIntro = introKeys.Count > 0;
            double diff = isIntro ? s.IntroDifficulty : DifficultyAt(n);

            var unlocked = new List<string>();
            var isUnlocked = new bool[UnlockNames.Length];
            for (int i = 0; i < UnlockNames.Length; i++)
                if (n >= s.UnlockLevels[i]) { isUnlocked[i] = true; unlocked.Add(UnlockNames[i].Key); }
            unlocked.Sort(StringComparer.Ordinal);
            bool Has(string key) => isUnlocked[IndexOfUnlock(key)];

            int band = 0; // the band with the largest start <= n, else the first
            for (int i = 0; i < s.BandStarts.Length; i++) if (s.BandStarts[i] <= n) band = i;
            var pool = new List<(string, double)>();
            foreach (var (kind, weight) in s.Bands[band])
            {
                string? needs = kind == GoalKinds.Collect ? "goal_collect" : kind == GoalKinds.Combo ? "combo_goals" : null;
                if (weight > 0 && (needs == null || Has(needs))) pool.Add((kind, weight));
            }
            if (pool.Count == 0) pool.Add((GoalKinds.Score, 1.0));

            string? forceGoal = introKeys.Contains("goal_collect") ? GoalKinds.Collect
                : introKeys.Contains("combo_goals") ? GoalKinds.Combo : null;

            string? intro = null;
            if (isIntro)
            {
                var names = new List<string>();
                foreach (var key in introKeys) names.Add(UnlockNames[IndexOfUnlock(key)].Name);
                intro = string.Join(" and ", names);
            }

            var specials = new List<string>();
            foreach (var (key, special) in SpecialUnlocks) if (Has(key)) specials.Add(special);

            return new LevelParams(
                Number: n,
                Difficulty: diff,
                TargetWinRate: TargetWinRateFor(diff),
                IsIntro: isIntro,
                IsPinch: Cascade.Generation.Difficulty.IsPinch(n, s.CycleLength),
                IsRelease: Cascade.Generation.Difficulty.IsRelease(n, s.CycleLength),
                Intro: intro,
                Colors: MathX.RoundHalfEven(s.Colors.At(n)),
                BoardSize: MathX.RoundHalfEven(s.BoardSize.At(n)),
                MoveLimit: MathX.RoundHalfEven(s.MoveLimit.At(n)),
                GoalAmount: s.GoalAmount.At(n),
                ScorePerMove: s.ScorePerMove.At(n),
                CollectPerMove: s.CollectPerMove.At(n),
                GoalPool: pool,
                ForceGoal: forceGoal,
                Shaped: Has("holes"),
                ForceHoles: introKeys.Contains("holes"),
                Specials: specials,
                Unlocked: unlocked);
        }

        static int IndexOfUnlock(string key)
        {
            for (int i = 0; i < UnlockNames.Length; i++) if (UnlockNames[i].Key == key) return i;
            throw new ArgumentException("unknown unlock '" + key + "'");
        }
    }
}
