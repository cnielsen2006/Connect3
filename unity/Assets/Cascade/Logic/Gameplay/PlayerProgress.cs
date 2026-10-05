using System;
using Cascade.Platform.Abstractions;

namespace Cascade.Gameplay
{
    /// <summary>
    /// The pure progress rules over <see cref="SaveData"/> (TRD §8, PRD §2.7): a retry keeps the
    /// seed until the level is won. Every function returns new data and never mutates its input.
    /// </summary>
    public static class ProgressRules
    {
        /// <summary>
        /// The seed to start level <paramref name="n"/> with, and the save to persist. The frontier
        /// level (<c>n == CurrentLevel</c>) reuses the saved seed (retries give the same level) and
        /// draws and stores one if there is none or it came from another generator version. Replaying
        /// an earlier level gets a fresh, unsaved seed.
        /// </summary>
        public static (SaveData Save, ulong Seed) StartLevel(SaveData save, int n, ISeedSource seeds)
        {
            if (n != save.CurrentLevel) return (save, seeds.NextSeed());
            if (save.CurrentSeed.HasValue && save.GeneratorVersion == GeneratorVersion.Current)
                return (save, save.CurrentSeed.Value);
            ulong seed = seeds.NextSeed();
            return (save with { CurrentSeed = seed, GeneratorVersion = GeneratorVersion.Current }, seed);
        }

        /// <summary>
        /// Stores the best stars; if this was the frontier level, advances to the next one and draws
        /// its seed.
        /// </summary>
        public static SaveData RecordWin(SaveData save, int n, int stars, ISeedSource seeds)
        {
            var best = save.Stars.With(n, Math.Max(save.Stars.Get(n), stars));
            if (n + 1 > save.CurrentLevel)
                return save with
                {
                    Stars = best,
                    CurrentLevel = n + 1,
                    CurrentSeed = seeds.NextSeed(),
                    GeneratorVersion = GeneratorVersion.Current,
                };
            return save with { Stars = best };
        }
    }

    /// <summary>
    /// The player's progress as the app holds it: the current <see cref="SaveData"/> plus the rules
    /// that change it. After a change that should survive, hand <see cref="Data"/> to
    /// <c>SaveService.WriteSave</c>; <see cref="Changed"/> fires so the composition root can do that.
    /// </summary>
    public sealed class PlayerProgress
    {
        readonly ISeedSource _seeds;

        public PlayerProgress(SaveData data, ISeedSource seeds)
        {
            Data = data;
            _seeds = seeds;
        }

        /// <summary>The current progress. Persist this after <see cref="Changed"/>.</summary>
        public SaveData Data { get; private set; }

        /// <summary>Raised after <see cref="Data"/> changed (a seed was drawn, a win recorded, the offset set).</summary>
        public event Action<SaveData>? Changed;

        /// <summary>The frontier level.</summary>
        public int CurrentLevel => Data.CurrentLevel;

        /// <summary>Best stars of a level (0 if never won).</summary>
        public int StarsFor(int level) => Data.Stars.Get(level);

        /// <summary>Sum of the best stars of all levels.</summary>
        public int TotalStars => Data.Stars.Total;

        /// <summary>True if the level may be played: any level up to the frontier.</summary>
        public bool IsUnlocked(int level) => level >= 1 && level <= Data.CurrentLevel;

        /// <summary>The adaptive difficulty offset (stored; unused until U8).</summary>
        public double AdaptiveOffset
        {
            get => Data.AdaptiveOffset;
            set
            {
                if (Data.AdaptiveOffset == value) return;
                Data = Data with { AdaptiveOffset = value };
                Changed?.Invoke(Data);
            }
        }

        /// <summary>
        /// The seed to start level <paramref name="n"/> with (see <see cref="ProgressRules.StartLevel"/>).
        /// Raises <see cref="Changed"/> only if a seed was stored.
        /// </summary>
        public ulong StartLevel(int n)
        {
            var (save, seed) = ProgressRules.StartLevel(Data, n, _seeds);
            if (!ReferenceEquals(save, Data)) Replace(save);
            return seed;
        }

        /// <summary>Records a win: best stars kept; the frontier advances and its next seed is drawn.</summary>
        public void RecordWin(int n, int stars) => Replace(ProgressRules.RecordWin(Data, n, stars, _seeds));

        /// <summary>Replaces the data wholesale (after loading, or to reset progress). Does not raise <see cref="Changed"/>.</summary>
        public void Load(SaveData data) => Data = data;

        void Replace(SaveData save)
        {
            Data = save;
            Changed?.Invoke(save);
        }
    }
}
