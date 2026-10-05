using System.Collections.Generic;

namespace Cascade.Core
{
    /// <summary>Score from qualifiers and cascade depth. All numbers come from <see cref="ScoringRules"/> (TRD §5).</summary>
    public static class Scoring
    {
        /// <summary>Sum of the qualifier values (before the cascade multiplier).</summary>
        public static int Base(IEnumerable<Qualifier> qualifiers, ScoringRules s)
        {
            int total = 0;
            foreach (var q in qualifiers)
            {
                switch (q.Kind)
                {
                    case QualifierKind.Run3: total += s.Run3; break;
                    case QualifierKind.Run4: total += s.Run4; break;
                    case QualifierKind.Run5: total += s.Run5 + s.RunExtraPerTile * (q.Length - 5); break;
                    case QualifierKind.Cross: total += s.Cross; break;
                }
            }
            return total;
        }

        /// <summary>Bonus for tiles cleared by specials.</summary>
        public static int SpecialCleared(int count, ScoringRules s) => count * s.SpecialTileCleared;

        /// <summary><c>1 + CascadeStep * level + GroupStep * groupIndex</c> (PRD 2.3).</summary>
        public static double Multiplier(int level, int groupIndex, ScoringRules s)
            => 1 + s.CascadeStep * level + s.GroupStep * groupIndex;

        /// <summary>Multiplier of the first group at a cascade level.</summary>
        public static double Multiplier(int level, ScoringRules s) => Multiplier(level, 0, s);

        /// <summary><c>round(basePoints * multiplier)</c>, half to even.</summary>
        public static int Points(int basePoints, double multiplier) => MathX.RoundHalfEven(basePoints * multiplier);

        /// <summary><c>round(basePoints * multiplier(level))</c>, half to even.</summary>
        public static int Points(int basePoints, int level, ScoringRules s) => Points(basePoints, Multiplier(level, s));

        /// <summary>Points of one group's qualifiers at a multiplier.</summary>
        public static int Score(IEnumerable<Qualifier> qualifiers, double multiplier, ScoringRules s)
            => Points(Base(qualifiers, s), multiplier);
    }
}
