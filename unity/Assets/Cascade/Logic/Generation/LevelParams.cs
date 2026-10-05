using System;
using Cascade.Core;

namespace Cascade.Generation
{
    /// <summary>The difficulty TOML is invalid (TRD §6). The message names the offending key.</summary>
    public sealed class ProfileException : Exception
    {
        public ProfileException(string message) : base(message) { }
    }

    /// <summary>The goal kinds a profile's goal pool can name (a combo is one score and one collect goal).</summary>
    public static class GoalKinds
    {
        public const string Score = "score";
        public const string Collect = "collect";
        public const string Combo = "combo";

        // All kinds, in the canonical order the weighted pool is built in.
        public static readonly string[] All = { Score, Collect, Combo };
    }

    /// <summary>
    /// Everything the generator needs to know about level <see cref="Number"/>, evaluated from the
    /// profile (<see cref="Profile.Evaluate"/>). Immutable with value equality.
    /// </summary>
    public sealed record LevelParams(
        int Number,
        double Difficulty,
        double TargetWinRate,
        bool IsIntro,
        bool IsPinch,
        bool IsRelease,
        string? Intro,
        int Colors,
        int BoardSize,
        // The move limit starting guess; the tuner adjusts it.
        int MoveLimit,
        double GoalAmount,
        double ScorePerMove,
        double CollectPerMove,
        // (kind, weight) pairs of the unlocked goal kinds, in canonical kind order.
        ValueList<(string Kind, double Weight)> GoalPool,
        // Intro levels of a goal type use that goal type ("collect" or "combo").
        string? ForceGoal,
        // Holes are unlocked.
        bool Shaped,
        // The holes intro level is always shaped.
        bool ForceHoles,
        ValueList<string> Specials,
        // The unlock keys reached at this level, ascending (ordinal).
        ValueList<string> Unlocked);
}
