using System;
using Cascade.Config;
using Cascade.Core;
using Cascade.Gameplay;
using Cascade.Generation;
using Cascade.Platform.Abstractions;
using GenProfile = Cascade.Generation.Profile;

namespace Cascade.Tests.Support
{
    /// <summary>
    /// Shared helpers of the Gameplay, Generation and Conformance tests: dials loaded from the
    /// shipped <c>dials.toml</c>, the shipped difficulty profile, and the test dial sets.
    /// No NUnit here, so <c>tools/Cascade.Tools</c> can compile this file too.
    /// </summary>
    public static class TestDials
    {
        public static readonly HostInfo Desktop = new HostInfo(PlatformKind.Desktop, true, true, false, true, 96f);

        /// <summary>A fresh registry from the shipped defaults; <paramref name="tweak"/> may set dials.</summary>
        public static DialRegistry Load(Action<DialRegistry>? tweak = null)
        {
            var data = new DictionaryDataSource().With("dials.toml", RepoFiles.ReadData("dials.toml"));
            var reg = DialRegistry.Load(data, Desktop, new MemoryStorage());
            tweak?.Invoke(reg);
            return reg;
        }

        /// <summary>The shipped defaults.</summary>
        public static DialRegistry Default() => Load();

        /// <summary>Fast and deterministic dials for tests: small K/R, no wall-clock cap (the <c>FAST</c> set).</summary>
        public static DialRegistry Fast() => Load(d =>
        {
            d.Set("generator", "budget_ms", 0);
            d.Set("generator", "bot_runs", 6);
            d.Set("generator", "tune_rounds", 1);
        });

        /// <summary>The defaults with <c>budget_ms = 0</c>: how the conformance fixtures were generated.</summary>
        public static DialRegistry NoBudget() => Load(d => d.Set("generator", "budget_ms", 0));

        /// <summary>The shipped difficulty profile.</summary>
        public static GenProfile Profile() => GenProfile.FromText(RepoFiles.ReadData("difficulty.toml"));

        /// <summary>The shipped difficulty profile text.</summary>
        public static string ProfileText() => RepoFiles.ReadData("difficulty.toml");
    }

    /// <summary>A trivial seed source for tests that need seeds from a fixed list.</summary>
    public sealed class ListSeedSource : ISeedSource
    {
        readonly ulong[] _seeds;
        int _next;

        public ListSeedSource(params ulong[] seeds) { _seeds = seeds; }

        /// <summary>Seeds handed out so far.</summary>
        public int Drawn => _next;

        public ulong NextSeed() => _next < _seeds.Length ? _seeds[_next++] : throw new InvalidOperationException("no more seeds");
    }
}
