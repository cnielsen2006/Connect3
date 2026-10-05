using System.Collections.Generic;

namespace Cascade.Config
{
    /// <summary>
    /// The static part of each dial's metadata: type, allowed range, UI step and description. The
    /// default values live only in dials.toml. A dial is added by one row here, one line in dials.toml
    /// and one property in <c>SectionDials.cs</c>; a test keeps the three in step.
    /// </summary>
    internal static class DialCatalog
    {
        internal readonly struct Spec
        {
            public Spec(string section, string key, DialType type, double min, double max, double step, string description)
            {
                Section = section; Key = key; Type = type; Min = min; Max = max; Step = step; Description = description;
            }

            public string Section { get; }
            public string Key { get; }
            public DialType Type { get; }
            public double Min { get; }
            public double Max { get; }
            public double Step { get; }
            public string Description { get; }
        }

        static Spec I(string s, string k, double min, double max, double step, string d) =>
            new Spec(s, k, DialType.Int, min, max, step, d);

        static Spec D(string s, string k, double min, double max, double step, string d) =>
            new Spec(s, k, DialType.Double, min, max, step, d);

        /// <summary>Ordinal order by section then key, so the order never depends on a file or a hash.</summary>
        public static readonly IReadOnlyList<Spec> Specs = Build();

        static IReadOnlyList<Spec> Build()
        {
            var list = new List<Spec>
            {
                I("scoring", "run3", 0, 10000, 5, "Points for a run of 3."),
                I("scoring", "run4", 0, 10000, 5, "Points for a run of 4."),
                I("scoring", "run5", 0, 10000, 5, "Points for a run of 5."),
                I("scoring", "run_extra_per_tile", 0, 1000, 5, "Extra points for each tile beyond 5 in a run."),
                I("scoring", "cross", 0, 5000, 5, "Points for each Cross qualifier."),
                I("scoring", "special_tile_cleared", 0, 500, 1, "Points per tile cleared by a special."),
                D("scoring", "cascade_step", 0, 5, 0.05, "Combo multiplier = 1 + cascade_step * round + group_step * groups already scored this round."),
                D("scoring", "group_step", 0, 5, 0.05, "Multiplier added for each further match group scored in the same round."),
                I("scoring", "combo_color_color", 0, 100000, 100, "Points for a color + color special combo."),

                D("specials", "incoming_chance", 0, 1, 0.05, "Chance that a spawned tile is a special."),
                I("specials", "bomb_radius", 1, 5, 1, "Bomb blast radius in cells (1 = 3x3)."),

                I("stars", "two_star_percentile", 0, 100, 1, "Bot score percentile used for the two-star score."),
                I("stars", "three_star_percentile", 0, 100, 1, "Bot score percentile used for the three-star score."),
                I("stars", "min_wins", 0, 100, 1, "Fewer bot wins than this and the star fallback is used."),
                D("stars", "fallback_two", 0.1, 5, 0.05, "Fallback two-star score as a multiple of the median bot score."),
                D("stars", "fallback_three", 0.1, 10, 0.05, "Fallback three-star score as a multiple of the median bot score."),

                D("input", "drag_inner_fraction", 0.05, 1, 0.05, "Central fraction of the neighbor cell that counts as well inside for a drag swap."),
                I("input", "double_click_ms", 50, 1000, 10, "Longest gap between two clicks that counts as a double click (ms)."),
                D("input", "touch_inner_fraction", 0.05, 1, 0.05, "Touch version of drag_inner_fraction (fingers are less precise)."),
                I("input", "double_tap_ms", 50, 1000, 10, "Longest gap between two taps that counts as a double tap (ms)."),
                D("input", "tap_slop_mm", 0.5, 10, 0.5, "Finger movement still counted as a tap, not a drag (mm)."),
                D("input", "pinch_min_mm", 1, 20, 0.5, "Change in finger spread before a pinch starts zooming (mm)."),
                D("input", "min_touch_target_mm", 4, 20, 0.5, "Smallest button on touch devices (mm)."),

                I("render", "multisamples", 0, 8, 2, "MSAA samples (0, 2, 4, 8), applied to the URP asset at startup."),
                I("render", "target_fps", 0, 240, 1, "Target frame rate (0 = the display's refresh rate)."),

                D("celebration", "tier_1", 1, 20, 0.1, "Combo multiplier at which celebration tier 1 starts."),
                D("celebration", "tier_2", 1, 20, 0.1, "Combo multiplier at which celebration tier 2 starts."),
                D("celebration", "tier_3", 1, 20, 0.1, "Combo multiplier at which celebration tier 3 starts."),
                D("celebration", "tier_4", 1, 20, 0.1, "Combo multiplier at which celebration tier 4 starts."),
                D("celebration", "badge_scale_base", 0.2, 4, 0.05, "Size of the multiplier badge at tier 0."),
                D("celebration", "badge_scale_step", 0, 2, 0.05, "Extra badge size per celebration tier."),
                I("celebration", "badge_ms", 100, 3000, 10, "How long the multiplier badge lives (ms)."),
                I("celebration", "group_stagger_ms", 0, 1000, 10, "Delay between the badges of groups scored in the same round (ms)."),
                I("celebration", "particles_per_tier", 0, 100, 1, "Pebble-dust particles per tier in a celebration burst."),
                D("celebration", "camera_punch_per_tier", 0, 0.2, 0.002, "Camera zoom-in per tier on a celebration (fraction of the view)."),
                I("celebration", "stinger_from_tier", 1, 5, 1, "First tier that plays the stinger sound."),
                I("celebration", "reduce_effects", 0, 1, 1, "1 = no particles or camera punch (the badge stays)."),

                I("animation", "select_lift", 1, 2000, 5, "Select lift duration (ms)."),
                I("animation", "swap", 1, 2000, 5, "Swap duration (ms)."),
                I("animation", "swap_reject", 1, 2000, 5, "Rejected swap duration (ms)."),
                I("animation", "clear", 1, 2000, 5, "Clear duration (ms)."),
                I("animation", "special_fire", 1, 2000, 5, "Special firing duration (ms)."),
                I("animation", "fall_cells_per_sec", 1, 100, 1, "Fall speed in cells per second."),
                I("animation", "spawn_drop", 1, 2000, 5, "Spawn drop duration (ms)."),
                D("animation", "land_squash", 0, 0.8, 0.01, "Squash amount when a piece lands (fraction)."),
                I("animation", "settle_wobbles", 0, 5, 1, "Number of settle wobbles after landing."),
                I("animation", "wave_stagger", 0, 500, 5, "Delay between consecutive cells of a wave (ms)."),

                D("audio", "master_volume", 0, 1, 0.05, "Volume of every sound cue (0-1)."),
                D("audio", "cascade_pitch_semitones", 0, 4, 0.1, "Pitch rise of a clear per cascade depth (semitones)."),
                I("audio", "cue_cooldown_ms", 0, 500, 5, "The same cue is not restarted within this time (ms)."),

                D("camera", "fit_margin", 0, 0.4, 0.005, "Fraction of the board area kept as margin when auto-fitting."),
                D("camera", "zoom_min", 0.1, 1, 0.05, "Smallest zoom, relative to auto-fit."),
                D("camera", "zoom_max", 1, 10, 0.1, "Largest zoom, relative to auto-fit."),
                D("camera", "zoom_step", 1.01, 2, 0.01, "Zoom factor per mouse wheel notch."),
                I("camera", "pitch_deg", 0, 80, 1, "Camera pitch in degrees (0 = straight down)."),

                I("pieces", "variation_yaw_deg", 0, 45, 1, "Random yaw per piece (degrees, plus or minus)."),
                D("pieces", "variation_squash_min", 0.5, 1, 0.01, "Smallest random squash factor per piece."),
                D("pieces", "variation_brightness", 0, 0.5, 0.01, "Random brightness variation per piece."),

                I("planner", "depth", 1, 6, 1, "Plan mode: moves looked ahead."),
                I("planner", "width", 1, 20, 1, "Plan mode: candidate moves played at each step of the search."),
                I("planner", "max_nodes", 10, 20000, 10, "Plan mode: budget of played moves per plan."),
                D("planner", "win_bonus", 0, 1000, 5, "Plan mode: value of a won level."),
                D("planner", "moves_left_weight", 0, 50, 0.5, "Plan mode: value per move left when the level is won."),
                D("planner", "progress_weight", 0, 500, 1, "Plan mode: value of goal progress (mean fraction of goals met)."),
                D("planner", "score_weight", 0, 1, 0.0005, "Plan mode: value per point."),
                I("planner", "max_probes", 0, 2000, 10, "Ghost replay: most explored lines kept."),
                I("planner", "probe_ms", 10, 1000, 10, "Ghost replay: time each explored line is shown (ms)."),
                I("planner", "think_ms", 0, 10000, 100, "Ghost replay: longest time spent showing the research (ms)."),
                I("planner", "show_ms", 0, 5000, 100, "Ghost replay: time the chosen line is shown before the swap (ms)."),

                I("generator", "bot_runs", 1, 500, 1, "Bot runs per measurement (K)."),
                I("generator", "tune_rounds", 0, 20, 1, "Tuning rounds per level (R)."),
                D("generator", "win_rate_tolerance", 0.005, 0.5, 0.005, "Accepted distance between measured and target win rate."),
                I("generator", "budget_ms", 0, 60000, 50, "Wall-clock cap per level in ms (0 = no cap)."),
                I("generator", "min_bot_runs", 1, 100, 1, "The budget never cuts a measurement below this many runs."),
                D("generator", "bot_random_chance", 0, 1, 0.05, "Chance the bot plays a random legal move."),
                D("generator", "bot_weight_goal", 0, 100, 0.5, "Bot move value weight: goal progress."),
                D("generator", "bot_weight_special", 0, 100, 0.5, "Bot move value weight: specials earned."),
                D("generator", "bot_weight_points", 0, 1, 0.001, "Bot move value weight: points."),
                I("generator", "move_limit_min", 1, 100, 1, "Tuner lower bound for move_limit."),
                I("generator", "move_limit_max", 1, 200, 1, "Tuner upper bound for move_limit."),
                D("generator", "goal_step", 0.01, 1, 0.01, "Tuner goal size change per step once move_limit is at a bound."),
                I("generator", "goal_score_round", 1, 1000, 5, "Score goal targets round to a multiple of this."),
                I("generator", "collect_min", 1, 50, 1, "Smallest collect goal."),
                D("generator", "collect_two_color_chance", 0, 1, 0.05, "Chance a collect goal names two colors (needs 5+ colors)."),
                D("generator", "combo_scale", 0.1, 1, 0.05, "Each half of a combo goal is this fraction of a single goal."),
                D("generator", "holes_chance", 0, 1, 0.05, "Chance a level past the holes unlock is shaped."),
                I("generator", "min_cells", 4, 200, 1, "Shaped boards must keep at least this many cells."),
            };
            list.Sort((a, b) =>
            {
                int c = string.CompareOrdinal(a.Section, b.Section);
                return c != 0 ? c : string.CompareOrdinal(a.Key, b.Key);
            });
            return list;
        }
    }
}
