namespace Cascade.Config
{
    // One typed, read-only view per TOML section. Each property reads the registry's current value, so
    // live edits made through DialRegistry.Set are seen immediately. Property name = the TOML key in
    // PascalCase (run_extra_per_tile -> RunExtraPerTile); a test checks the mapping against the metadata.

    public sealed class ScoringDials
    {
        readonly Dial run3, run4, run5, runExtra, cross, special, step, groupStep, combo;
        internal ScoringDials(DialRegistry r)
        {
            run3 = r.Handle("scoring", "run3"); run4 = r.Handle("scoring", "run4"); run5 = r.Handle("scoring", "run5");
            runExtra = r.Handle("scoring", "run_extra_per_tile"); cross = r.Handle("scoring", "cross");
            special = r.Handle("scoring", "special_tile_cleared"); step = r.Handle("scoring", "cascade_step"); groupStep = r.Handle("scoring", "group_step");
            combo = r.Handle("scoring", "combo_color_color");
        }
        public int Run3 => run3.Int;
        public int Run4 => run4.Int;
        public int Run5 => run5.Int;
        public int RunExtraPerTile => runExtra.Int;
        public int Cross => cross.Int;
        public int SpecialTileCleared => special.Int;
        public double CascadeStep => step.Dbl;
        public double GroupStep => groupStep.Dbl;
        public int ComboColorColor => combo.Int;
    }

    public sealed class SpecialsDials
    {
        readonly Dial incoming, radius;
        internal SpecialsDials(DialRegistry r)
        {
            incoming = r.Handle("specials", "incoming_chance"); radius = r.Handle("specials", "bomb_radius");
        }
        public double IncomingChance => incoming.Dbl;
        public int BombRadius => radius.Int;
    }

    public sealed class StarsDials
    {
        readonly Dial two, three, minWins, fb2, fb3;
        internal StarsDials(DialRegistry r)
        {
            two = r.Handle("stars", "two_star_percentile"); three = r.Handle("stars", "three_star_percentile");
            minWins = r.Handle("stars", "min_wins"); fb2 = r.Handle("stars", "fallback_two");
            fb3 = r.Handle("stars", "fallback_three");
        }
        public int TwoStarPercentile => two.Int;
        public int ThreeStarPercentile => three.Int;
        public int MinWins => minWins.Int;
        public double FallbackTwo => fb2.Dbl;
        public double FallbackThree => fb3.Dbl;
    }

    public sealed class InputDials
    {
        readonly Dial drag, dbl, touch, tap, slop, pinch, target;
        internal InputDials(DialRegistry r)
        {
            drag = r.Handle("input", "drag_inner_fraction"); dbl = r.Handle("input", "double_click_ms");
            touch = r.Handle("input", "touch_inner_fraction"); tap = r.Handle("input", "double_tap_ms");
            slop = r.Handle("input", "tap_slop_mm"); pinch = r.Handle("input", "pinch_min_mm");
            target = r.Handle("input", "min_touch_target_mm");
        }
        public double DragInnerFraction => drag.Dbl;
        public int DoubleClickMs => dbl.Int;
        public double TouchInnerFraction => touch.Dbl;
        public int DoubleTapMs => tap.Int;
        public double TapSlopMm => slop.Dbl;
        public double PinchMinMm => pinch.Dbl;
        public double MinTouchTargetMm => target.Dbl;
    }

    public sealed class RenderDials
    {
        readonly Dial msaa, fps;
        internal RenderDials(DialRegistry r)
        {
            msaa = r.Handle("render", "multisamples"); fps = r.Handle("render", "target_fps");
        }
        public int Multisamples => msaa.Int;
        public int TargetFps => fps.Int;
    }

    public sealed class AnimationDials
    {
        readonly Dial lift, swap, reject, clear, fire, fall, drop, squash, wobbles, stagger;
        internal AnimationDials(DialRegistry r)
        {
            lift = r.Handle("animation", "select_lift"); swap = r.Handle("animation", "swap");
            reject = r.Handle("animation", "swap_reject"); clear = r.Handle("animation", "clear");
            fire = r.Handle("animation", "special_fire"); fall = r.Handle("animation", "fall_cells_per_sec");
            drop = r.Handle("animation", "spawn_drop"); squash = r.Handle("animation", "land_squash");
            wobbles = r.Handle("animation", "settle_wobbles"); stagger = r.Handle("animation", "wave_stagger");
        }
        public int SelectLift => lift.Int;
        public int Swap => swap.Int;
        public int SwapReject => reject.Int;
        public int Clear => clear.Int;
        public int SpecialFire => fire.Int;
        public int FallCellsPerSec => fall.Int;
        public int SpawnDrop => drop.Int;
        public double LandSquash => squash.Dbl;
        public int SettleWobbles => wobbles.Int;
        public int WaveStagger => stagger.Int;
    }

    public sealed class CelebrationDials
    {
        readonly Dial t1, t2, t3, t4, scaleBase, scaleStep, badgeMs, stagger, particles, punch, stinger, reduce;
        internal CelebrationDials(DialRegistry r)
        {
            t1 = r.Handle("celebration", "tier_1"); t2 = r.Handle("celebration", "tier_2");
            t3 = r.Handle("celebration", "tier_3"); t4 = r.Handle("celebration", "tier_4");
            scaleBase = r.Handle("celebration", "badge_scale_base"); scaleStep = r.Handle("celebration", "badge_scale_step");
            badgeMs = r.Handle("celebration", "badge_ms"); stagger = r.Handle("celebration", "group_stagger_ms");
            particles = r.Handle("celebration", "particles_per_tier"); punch = r.Handle("celebration", "camera_punch_per_tier");
            stinger = r.Handle("celebration", "stinger_from_tier"); reduce = r.Handle("celebration", "reduce_effects");
        }
        public double Tier1 => t1.Dbl;
        public double Tier2 => t2.Dbl;
        public double Tier3 => t3.Dbl;
        public double Tier4 => t4.Dbl;
        public double BadgeScaleBase => scaleBase.Dbl;
        public double BadgeScaleStep => scaleStep.Dbl;
        public int BadgeMs => badgeMs.Int;
        public int GroupStaggerMs => stagger.Int;
        public int ParticlesPerTier => particles.Int;
        public double CameraPunchPerTier => punch.Dbl;
        public int StingerFromTier => stinger.Int;
        public int ReduceEffects => reduce.Int;
    }

    public sealed class AudioDials
    {
        readonly Dial volume, pitch, cooldown;
        internal AudioDials(DialRegistry r)
        {
            volume = r.Handle("audio", "master_volume"); pitch = r.Handle("audio", "cascade_pitch_semitones");
            cooldown = r.Handle("audio", "cue_cooldown_ms");
        }
        public double MasterVolume => volume.Dbl;
        public double CascadePitchSemitones => pitch.Dbl;
        public int CueCooldownMs => cooldown.Int;
    }

    public sealed class CameraDials
    {
        readonly Dial margin, zmin, zmax, zstep, pitch;
        internal CameraDials(DialRegistry r)
        {
            margin = r.Handle("camera", "fit_margin"); zmin = r.Handle("camera", "zoom_min");
            zmax = r.Handle("camera", "zoom_max"); zstep = r.Handle("camera", "zoom_step");
            pitch = r.Handle("camera", "pitch_deg");
        }
        public double FitMargin => margin.Dbl;
        public double ZoomMin => zmin.Dbl;
        public double ZoomMax => zmax.Dbl;
        public double ZoomStep => zstep.Dbl;
        public int PitchDeg => pitch.Int;
    }

    public sealed class PiecesDials
    {
        readonly Dial yaw, squash, bright;
        internal PiecesDials(DialRegistry r)
        {
            yaw = r.Handle("pieces", "variation_yaw_deg"); squash = r.Handle("pieces", "variation_squash_min");
            bright = r.Handle("pieces", "variation_brightness");
        }
        public int VariationYawDeg => yaw.Int;
        public double VariationSquashMin => squash.Dbl;
        public double VariationBrightness => bright.Dbl;
    }

    public sealed class PlannerDials
    {
        readonly Dial depth, width, nodes, win, left, progress, score, probes, probeMs, thinkMs, showMs;
        internal PlannerDials(DialRegistry r)
        {
            depth = r.Handle("planner", "depth"); width = r.Handle("planner", "width");
            nodes = r.Handle("planner", "max_nodes"); win = r.Handle("planner", "win_bonus");
            left = r.Handle("planner", "moves_left_weight"); progress = r.Handle("planner", "progress_weight");
            score = r.Handle("planner", "score_weight"); probes = r.Handle("planner", "max_probes");
            probeMs = r.Handle("planner", "probe_ms"); thinkMs = r.Handle("planner", "think_ms");
            showMs = r.Handle("planner", "show_ms");
        }
        public int Depth => depth.Int;
        public int Width => width.Int;
        public int MaxNodes => nodes.Int;
        public double WinBonus => win.Dbl;
        public double MovesLeftWeight => left.Dbl;
        public double ProgressWeight => progress.Dbl;
        public double ScoreWeight => score.Dbl;
        public int MaxProbes => probes.Int;
        public int ProbeMs => probeMs.Int;
        public int ThinkMs => thinkMs.Int;
        public int ShowMs => showMs.Int;
    }

    public sealed class GeneratorDials
    {
        readonly Dial runs, rounds, tol, budget, minRuns, rnd, wGoal, wSpecial, wPoints, mlMin, mlMax,
            goalStep, round, collectMin, twoColor, combo, holes, minCells;
        internal GeneratorDials(DialRegistry r)
        {
            runs = r.Handle("generator", "bot_runs"); rounds = r.Handle("generator", "tune_rounds");
            tol = r.Handle("generator", "win_rate_tolerance"); budget = r.Handle("generator", "budget_ms");
            minRuns = r.Handle("generator", "min_bot_runs"); rnd = r.Handle("generator", "bot_random_chance");
            wGoal = r.Handle("generator", "bot_weight_goal"); wSpecial = r.Handle("generator", "bot_weight_special");
            wPoints = r.Handle("generator", "bot_weight_points"); mlMin = r.Handle("generator", "move_limit_min");
            mlMax = r.Handle("generator", "move_limit_max"); goalStep = r.Handle("generator", "goal_step");
            round = r.Handle("generator", "goal_score_round"); collectMin = r.Handle("generator", "collect_min");
            twoColor = r.Handle("generator", "collect_two_color_chance"); combo = r.Handle("generator", "combo_scale");
            holes = r.Handle("generator", "holes_chance"); minCells = r.Handle("generator", "min_cells");
        }
        public int BotRuns => runs.Int;
        public int TuneRounds => rounds.Int;
        public double WinRateTolerance => tol.Dbl;
        public int BudgetMs => budget.Int;
        public int MinBotRuns => minRuns.Int;
        public double BotRandomChance => rnd.Dbl;
        public double BotWeightGoal => wGoal.Dbl;
        public double BotWeightSpecial => wSpecial.Dbl;
        public double BotWeightPoints => wPoints.Dbl;
        public int MoveLimitMin => mlMin.Int;
        public int MoveLimitMax => mlMax.Int;
        public double GoalStep => goalStep.Dbl;
        public int GoalScoreRound => round.Int;
        public int CollectMin => collectMin.Int;
        public double CollectTwoColorChance => twoColor.Dbl;
        public double ComboScale => combo.Dbl;
        public double HolesChance => holes.Dbl;
        public int MinCells => minCells.Int;
    }
}
