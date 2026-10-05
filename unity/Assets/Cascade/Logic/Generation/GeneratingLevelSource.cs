using System;
using System.Collections.Generic;
using Cascade.Config;
using Cascade.Core;
using Cascade.Gameplay;
using Cascade.Platform.Abstractions;

namespace Cascade.Generation
{
    /// <summary>
    /// Where the game gets its levels (TRD §2, §11). The seed is always supplied by the caller (from
    /// <see cref="ISeedSource"/>): the same (number, seed) gives the same level.
    /// </summary>
    public interface ILevelSource
    {
        Level Get(int levelNumber, ulong seed);
    }

    /// <summary>What the last real generation did (TRD §7 API).</summary>
    public sealed record GenStats(double Ms, int Rounds, int BotRuns, int BotMoves, bool WithinTolerance, bool OverBudget);

    /// <summary>
    /// Generates levels just in time: candidate, validate, bot playtest, tune (TRD §7). The last
    /// <see cref="CacheSize"/> results are cached by (number, seed) so a retry returns the identical
    /// <see cref="Level"/> even if a wall-clock budget cut would differ. This is the only mutable
    /// object of the layer and it is <b>not thread-safe</b>: call it from one background task at a
    /// time (TRD §12.3). Dials are re-read on every <see cref="Get"/>; if they changed since the last
    /// call the cache is dropped.
    /// </summary>
    public sealed class GeneratingLevelSource : ILevelSource
    {
        /// <summary>How many levels are remembered.</summary>
        public const int CacheSize = 8;

        readonly DialRegistry _dials;
        readonly IStopwatch _stopwatch;
        readonly bool _hotReload;
        readonly List<(int Number, ulong Seed, Level Level)> _cache = new List<(int, ulong, Level)>(); // oldest first
        RuleSet? _cachedRules;
        GeneratorSettings? _cachedSettings;

        /// <param name="hotReload">Call <see cref="Profile.ReloadIfChanged"/> before each level (dev builds); a reload drops the cache.</param>
        public GeneratingLevelSource(DialRegistry dials, Profile profile, IStopwatch stopwatch, bool hotReload = false)
        {
            _dials = dials;
            Profile = profile;
            _stopwatch = stopwatch;
            _hotReload = hotReload;
        }

        public Profile Profile { get; }

        /// <summary>Describes the last real (not cached) generation; null before the first.</summary>
        public GenStats? LastStats { get; private set; }

        public Level Get(int levelNumber, ulong seed)
        {
            var rules = RuleSets.From(_dials);
            var settings = RuleSets.GeneratorFrom(_dials);
            bool dialsChanged = _cachedRules != null && (!_cachedRules.Equals(rules) || !_cachedSettings!.Equals(settings));
            _cachedRules = rules;
            _cachedSettings = settings;
            if ((_hotReload && Profile.ReloadIfChanged()) || dialsChanged) _cache.Clear();

            foreach (var entry in _cache)
                if (entry.Number == levelNumber && entry.Seed == seed) return entry.Level;

            double start = _stopwatch.ElapsedMilliseconds;
            var parameters = Profile.Evaluate(levelNumber);
            var candidate = LevelGenerator.MakeCandidate(parameters, seed, settings);
            var (level, stats) = Tuner.Tune(candidate, rules, settings, _stopwatch, start);
            var problems = Validator.Validate(level);
            if (problems.Count > 0) // tuning must not break a valid level
                throw new GenerationException(string.Join("; ", problems));
            LastStats = new GenStats(_stopwatch.ElapsedMilliseconds - start, stats.Rounds, stats.BotRuns, stats.BotMoves,
                stats.WithinTolerance, stats.OverBudget);
            if (_cache.Count >= CacheSize) _cache.RemoveAt(0);
            _cache.Add((levelNumber, seed, level));
            return level;
        }
    }
}
