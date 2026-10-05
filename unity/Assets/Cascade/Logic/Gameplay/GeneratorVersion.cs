namespace Cascade.Gameplay
{
    /// <summary>
    /// The level generator's version (TRD §4.1). Bump it whenever generation output changes for the
    /// same (level number, seed); saves store it so a stale seed is not reused. Version 2: the combo multiplier (PRD 2.3) changed scores, so star thresholds moved. It lives in Gameplay (not
    /// Generation) because <see cref="PlayerProgress"/> and the save defaults need it and Gameplay
    /// may not reference Generation (TRD §2.1).
    /// </summary>
    public static class GeneratorVersion
    {
        /// <summary>The current generator version.</summary>
        public const int Current = 2;
    }
}
