using System;
using System.Collections.Generic;
using Cascade.Gameplay;

namespace Cascade.Presentation.Shell
{
    /// <summary>A star threshold on the HUD: the score that earns it and whether the shown score has reached it.</summary>
    public sealed record StarMarker(int Stars, int Score, bool Reached)
    {
        public string Text => Fmt.Int(Score);
    }

    /// <summary>
    /// The gameplay HUD (PRD 5.1): level title, moves left, score, goal progress rows and star thresholds.
    /// <para>
    /// The numbers follow the animation, as expected: when a move is played the <b>moves</b> change
    /// at once, but the <b>score and goal rows</b> keep their old values until the view moves them:
    /// <see cref="AddScore"/> for each score popup, and <see cref="Sync"/> when the board has settled (or
    /// at any time to jump to the session's real values). A new level syncs by itself.
    /// </para>
    /// </summary>
    public sealed class HudViewModel : ViewModelBase
    {
        readonly GameController _game;
        int _moves;
        int _shownScore;
        IReadOnlyList<GoalLine> _goals = new GoalLine[0];

        public HudViewModel(GameController game)
        {
            _game = game;
            Action ready = Sync;
            Action<MoveResult> played = r => { _moves = r.MovesLeft; RaiseChanged(); };
            game.LevelReady += ready;
            game.MovePlayed += played;
            OnDispose(() => { game.LevelReady -= ready; game.MovePlayed -= played; });
            Sync();
        }

        Session? S => _game.Session;

        public string LevelTitle => S == null ? "" : "Level " + Fmt.Plain(S.Level.Number);

        public string MovesLabel => "MOVES";
        public string ScoreLabel => "SCORE";
        public string HintLabel => "Hint";
        public string PauseLabel => "Pause";

        public int MovesLeft => _moves;
        public string MovesText => Fmt.Plain(_moves);

        /// <summary>The score on screen (animated by the view through <see cref="AddScore"/>).</summary>
        public int ShownScore => _shownScore;
        public string ScoreText => Fmt.Int(_shownScore);

        /// <summary>One row per goal: text, "5/12" and the fraction for a bar.</summary>
        public IReadOnlyList<GoalLine> Goals => _goals;

        /// <summary>The 2-star and 3-star scores of the level (one star is for winning).</summary>
        public IReadOnlyList<StarMarker> StarMarkers
        {
            get
            {
                var level = S?.Level;
                if (level == null) return new StarMarker[0];
                return new[]
                {
                    new StarMarker(2, level.Star2, _shownScore >= level.Star2),
                    new StarMarker(3, level.Star3, _shownScore >= level.Star3),
                };
            }
        }

        /// <summary>The shown score as a fraction of the 3-star score (0 to 1), for a progress bar.</summary>
        public double ScoreFraction => Fraction(_shownScore, S?.Level.Star3 ?? 0);

        /// <summary>Where the 2-star mark sits on that bar (0 to 1).</summary>
        public double Star2Fraction => Fraction(S?.Level.Star2 ?? 0, S?.Level.Star3 ?? 0);

        /// <summary>Stars the shown score would earn if the level were won now (1 to 3).</summary>
        public int ProvisionalStars
        {
            get
            {
                var level = S?.Level;
                if (level == null) return 0;
                return _shownScore >= level.Star3 ? 3 : _shownScore >= level.Star2 ? 2 : 1;
            }
        }

        double _combo = 1.0;
        int _comboTier;

        /// <summary>The combo multiplier of the move in progress (1 = none); it falls back to 1 when the move settles (<see cref="Sync"/>).</summary>
        public double Combo => _combo;
        public int ComboTier => _comboTier;
        public bool ComboVisible => _combo > 1.0 + 1e-9;
        public string ComboText => ComboVisible ? "\u00D7" + _combo.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) : "";

        /// <summary>The multiplier a match group just scored at (kept while it is the highest of this move).</summary>
        public void SetCombo(double multiplier, int tier)
        {
            if (multiplier <= _combo + 1e-9) return;
            _combo = multiplier;
            _comboTier = tier;
            RaiseChanged();
        }

        /// <summary>Adds points to the shown score (a score popup landed). It never passes the session's real score.</summary>
        public void AddScore(int points)
        {
            int cap = S?.Score ?? _shownScore + points;
            int next = Math.Min(cap, _shownScore + points);
            if (next == _shownScore) return;
            _shownScore = next;
            RaiseChanged();
        }

        /// <summary>Jumps moves, score and goals to the session's real values.</summary>
        public void Sync()
        {
            var s = S;
            _moves = s?.MovesLeft ?? 0;
            _shownScore = s?.Score ?? 0;
            _combo = 1.0;
            _comboTier = 0;
            _goals = s == null ? new GoalLine[0] : _game.Lines(s);
            RaiseChanged();
        }

        static double Fraction(int value, int whole) =>
            whole <= 0 ? 0.0 : Math.Min(1.0, Math.Max(0.0, (double)value / whole));
    }

    /// <summary>The result page (PRD 2.7, 5.1): win with stars and Next, or lose with the goals and Retry; Map either way.</summary>
    public sealed class ResultViewModel : ViewModelBase
    {
        readonly GameController _game;

        public ResultViewModel(GameController game)
        {
            _game = game;
            Action changed = RaiseChanged;
            game.Changed += changed;
            OnDispose(() => game.Changed -= changed);
        }

        LevelResult? R => _game.LastResult;

        public bool HasResult => R != null;
        public bool Won => R?.Won ?? false;
        public int LevelNumber => R?.Number ?? 0;

        /// <summary>"Level complete!" or "Out of moves".</summary>
        public string Headline => Won ? "Level complete!" : "Out of moves";

        /// <summary>Stars earned (0 on a loss).</summary>
        public int Stars => Won ? R!.Stars : 0;

        /// <summary>Always three entries: true for a star earned.</summary>
        public IReadOnlyList<bool> StarSlots => new[] { Stars >= 1, Stars >= 2, Stars >= 3 };

        public bool ShowStars => Won;

        /// <summary>"New best!" when the win beat the earlier best for this level, otherwise null.</summary>
        public string? NewBestText => R != null && R.NewBest ? "New best!" : null;

        /// <summary>"Score 1,234".</summary>
        public string ScoreText => "Score " + Fmt.Int(R?.Score ?? 0);

        /// <summary>The goals with progress, shown on a loss ("Score 1,000 points  (250/1,000)"); empty after a win.</summary>
        public IReadOnlyList<string> GoalLines
        {
            get
            {
                var lines = new List<string>();
                if (R == null || R.Won) return lines;
                foreach (var g in R.Goals) lines.Add(g.Text + "  (" + g.ProgressText + ")");
                return lines;
            }
        }

        public bool NextAvailable => Won;
        public bool RetryAvailable => HasResult && !Won;
        public bool MapAvailable => true;

        public string NextLabel => "Next level";
        public string RetryLabel => "Retry";
        public string MapLabel => "Map";

        /// <summary>The level "Next" would start.</summary>
        public int NextLevelNumber => LevelNumber + 1;

        /// <summary>The next level is already generated, so Next starts at once; otherwise it shows "Preparing level N".</summary>
        public bool NextLevelReady => Won && _game.NextLevelReady;

        public bool Next() => _game.Flow.Next();
        public bool Retry() => _game.Flow.Retry();
        public bool ToMap() => _game.Flow.ToMap();
    }

    /// <summary>
    /// The settings page (PRD 5.1): sound, animation speed (0.5 to 3.0) and adaptive difficulty. Every
    /// change applies to <see cref="LiveSettings"/> at once (the board animation follows live) and is
    /// handed to the <see cref="IProgressSink"/> to be saved.
    /// </summary>
    public sealed class SettingsViewModel : ViewModelBase
    {
        readonly LiveSettings _settings;
        readonly IProgressSink _sink;
        readonly ScreenFlow _flow;

        public SettingsViewModel(LiveSettings settings, IProgressSink sink, ScreenFlow flow)
        {
            _settings = settings;
            _sink = sink;
            _flow = flow;
            Action<PlayerSettings> changed = _ => RaiseChanged();
            settings.PlayerChanged += changed;
            OnDispose(() => settings.PlayerChanged -= changed);
        }

        public string Title => "Settings";
        public string BackLabel => "Back";

        public double SpeedMin => PlayerSettings.MinAnimationSpeed;
        public double SpeedMax => PlayerSettings.MaxAnimationSpeed;

        /// <summary>The slider step.</summary>
        public double SpeedStep => 0.05;

        public bool Sound => _settings.Player.Sound;
        public double AnimationSpeed => _settings.Player.AnimationSpeed;
        public bool Adaptive => _settings.Player.Adaptive;
        public int TargetFps => _settings.Player.TargetFps;
        public string Skin => _settings.Player.Skin;

        static string SkinName(string id) => id == "I" ? "Claymation" : id == "J" ? "Studio Set" : "Pebbles";

        /// <summary>"Style: Claymation": the art style applies at once.</summary>
        public string SkinText => "Style: " + SkinName(Skin);

        /// <summary>"Animation speed x1.50".</summary>
        public string SpeedText => "Animation speed x" + Fmt.Fixed(AnimationSpeed, 2);

        /// <summary>"Sound: on".</summary>
        public string SoundText => "Sound: " + (Sound ? "on" : "off");

        /// <summary>"Adaptive difficulty: on".</summary>
        public string AdaptiveText => "Adaptive difficulty: " + (Adaptive ? "on" : "off");

        /// <summary>"Smooth 60 fps: on" (off = 30 fps, easier on the battery).</summary>
        public string FpsText => "Smooth 60 fps: " + (TargetFps == PlayerSettings.HighFps ? "on" : "off");

        /// <summary>Sets the speed, clamped to the allowed range and rounded to 2 decimals.</summary>
        public void SetAnimationSpeed(double speed)
        {
            if (double.IsNaN(speed)) return;
            speed = Math.Round(Math.Min(SpeedMax, Math.Max(SpeedMin, speed)), 2);
            Apply(_settings.Player with { AnimationSpeed = speed });
        }

        public void SetSound(bool on) => Apply(_settings.Player with { Sound = on });
        public void ToggleSound() => SetSound(!Sound);
        public void SetAdaptive(bool on) => Apply(_settings.Player with { Adaptive = on });
        public void ToggleAdaptive() => SetAdaptive(!Adaptive);
        /// <summary>Switches to the next art style (K, I, J, K...).</summary>
        public void CycleSkin()
        {
            int i = Array.IndexOf(PlayerSettings.Skins, Skin);
            Apply(_settings.Player with { Skin = PlayerSettings.Skins[(i + 1) % PlayerSettings.Skins.Length] });
        }

        public void SetHighFps(bool on) => Apply(_settings.Player with { TargetFps = on ? PlayerSettings.HighFps : PlayerSettings.LowFps });

        public bool Back() => _flow.ToTitle();

        void Apply(PlayerSettings next)
        {
            if (next == _settings.Player) return;
            _settings.Update(next);
            _sink.SaveSettings(_settings.Player);
        }
    }
}
