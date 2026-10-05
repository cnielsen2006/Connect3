namespace Cascade.Core
{
    /// <summary>
    /// Scoring numbers (the <c>[scoring]</c> dials, TRD §5). Immutable; the defaults equal the
    /// shipped <c>dials.toml</c>.
    /// </summary>
    public sealed record ScoringRules
    {
        /// <summary>A run of 3.</summary>
        public int Run3 { get; init; } = 60;
        /// <summary>A run of 4.</summary>
        public int Run4 { get; init; } = 120;
        /// <summary>A run of 5.</summary>
        public int Run5 { get; init; } = 200;
        /// <summary>Added per tile beyond 5 in a run.</summary>
        public int RunExtraPerTile { get; init; } = 40;
        /// <summary>Each Cross qualifier.</summary>
        public int Cross { get; init; } = 150;
        /// <summary>Per tile cleared by a special (waves after the first).</summary>
        public int SpecialTileCleared { get; init; } = 20;
        /// <summary>Multiplier = 1 + CascadeStep * level + GroupStep * groupIndex.</summary>
        public double CascadeStep { get; init; } = 0.5;
        /// <summary>Added to the multiplier for each group already scored in the same round.</summary>
        public double GroupStep { get; init; } = 0.25;
        /// <summary>Flat bonus for Color + Color (no multiplier).</summary>
        public int ComboColorColor { get; init; } = 5000;
    }

    /// <summary>Special-piece numbers (the <c>[specials]</c> dials). Immutable; defaults equal <c>dials.toml</c>.</summary>
    public sealed record SpecialRules
    {
        /// <summary>Chance that an earned special lands on a tile that spawns in this step (else on an existing normal tile).</summary>
        public double IncomingChance { get; init; } = 0.4;
        /// <summary>Bomb reach in neighbor hops (1 = 3x3 on a grid).</summary>
        public int BombRadius { get; init; } = 1;
    }

    /// <summary>
    /// The immutable rule parameters <see cref="Rules.Resolve"/> takes (TRD §3). Core never reads
    /// dials; <c>Gameplay.RuleSets</c> builds a snapshot from them. <c>new RuleSet()</c> (or
    /// <see cref="Default"/>) holds the <c>dials.toml</c> defaults.
    /// </summary>
    public sealed record RuleSet
    {
        public ScoringRules Scoring { get; init; } = new ScoringRules();
        public SpecialRules Specials { get; init; } = new SpecialRules();

        /// <summary>The defaults (an immutable shared instance).</summary>
        public static readonly RuleSet Default = new RuleSet();
    }
}
