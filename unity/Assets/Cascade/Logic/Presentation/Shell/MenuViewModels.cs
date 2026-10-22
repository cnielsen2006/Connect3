using System;
using System.Collections.Generic;
using Cascade.Gameplay;

namespace Cascade.Presentation.Shell
{
    /// <summary>The title page (PRD 5.1): Play/Continue, Settings, Quit (desktop only).</summary>
    public sealed class TitleViewModel : ViewModelBase
    {
        readonly ScreenFlow _flow;
        readonly PlayerProgress _progress;

        public TitleViewModel(ScreenFlow flow, PlayerProgress progress)
        {
            _flow = flow;
            _progress = progress;
            Action<SaveData> onProgress = _ => RaiseChanged();
            progress.Changed += onProgress;
            OnDispose(() => progress.Changed -= onProgress);
        }

        public string Title => "Cascade";
        public string Tagline => "match, connect, relax";

        /// <summary>"Play" for a new game, "Continue (level N)" after that.</summary>
        public string PlayLabel => _progress.CurrentLevel <= 1 ? "Play" : "Continue (level " + Fmt.Plain(_progress.CurrentLevel) + ")";

        public string SettingsLabel => "Settings";
        public string QuitLabel => "Quit";

        /// <summary>False on Android (the system Back leaves the app) and on Web (a tab cannot quit itself): no Quit button.</summary>
        public bool QuitVisible => _flow.QuitAvailable;

        public bool Play() => _flow.Play();
        public bool OpenSettings() => _flow.OpenSettings();
        public bool Quit() => _flow.Quit();
    }

    /// <summary>The state of a level on the map.</summary>
    public enum MapNodeState
    {
        /// <summary>Not reachable yet.</summary>
        Locked,
        /// <summary>Played and won before (or skipped past); can be replayed.</summary>
        Completed,
        /// <summary>The frontier: the next level to win.</summary>
        Current,
    }

    /// <summary>One level on the map path.</summary>
    public sealed record MapNode(int Number, MapNodeState State, int Stars, double PathX)
    {
        /// <summary>The number as text.</summary>
        public string Label => Fmt.Plain(Number);

        /// <summary>The player may start it.</summary>
        public bool IsSelectable => State != MapNodeState.Locked;

        public bool IsCurrent => State == MapNodeState.Current;

        /// <summary>Stars as text for a label, for example "2/3"; empty if none.</summary>
        public string StarsText => Stars > 0 ? Fmt.Plain(Stars) + "/3" : "";
    }

    /// <summary>
    /// The level map (PRD 5.1): a scrolling path of levels. Only a window of levels is exposed, around
    /// <see cref="CenterLevel"/>, so a player at level 900 does not build 900 nodes. The window never
    /// reaches past <c>current + levelsAhead</c> (the unlocked frontier and a few locked levels as a teaser)
    /// or <c>maxLevel</c>. <see cref="Nodes"/> are in ascending order; the view draws level 1 at the bottom.
    /// Opening the map (any move to the Map page) scrolls back to the current level.
    /// </summary>
    public sealed class MapViewModel : ViewModelBase
    {
        readonly PlayerProgress _progress;
        readonly ScreenFlow _flow;
        readonly int _maxLevel;
        readonly int _behind;
        readonly int _ahead;
        readonly double _pathPhase;
        int _center;

        /// <param name="maxLevel">Highest level number the map shows.</param>
        /// <param name="levelsBehind">Levels shown below the center.</param>
        /// <param name="levelsAhead">Levels shown above the center, and how many locked levels show past the frontier.</param>
        /// <param name="pathPhase">Radians per level of the sideways wobble of the path (<see cref="MapNode.PathX"/> = sin(n * phase)).</param>
        public MapViewModel(PlayerProgress progress, ScreenFlow flow, int maxLevel = 9999, int levelsBehind = 12,
            int levelsAhead = 8, double pathPhase = 0.85)
        {
            _progress = progress;
            _flow = flow;
            _maxLevel = Math.Max(1, maxLevel);
            _behind = Math.Max(0, levelsBehind);
            _ahead = Math.Max(0, levelsAhead);
            _pathPhase = pathPhase;
            _center = progress.CurrentLevel;
            Clamp();

            Action<SaveData> onProgress = _ => { Clamp(); RaiseChanged(); };
            Action<ScreenId, ScreenId> onScreen = (_, to) =>
            {
                if (to != ScreenId.Map) return;
                _center = _progress.CurrentLevel;
                Clamp();
                RaiseChanged();
            };
            progress.Changed += onProgress;
            flow.ScreenChanged += onScreen;
            OnDispose(() => { progress.Changed -= onProgress; flow.ScreenChanged -= onScreen; });
        }

        public string Title => "Choose a level";
        public string BackLabel => "Back";

        /// <summary>The frontier level.</summary>
        public int CurrentLevel => _progress.CurrentLevel;

        /// <summary>The highest level shown (the frontier plus a few locked ones, capped at the maximum).</summary>
        public int HighestShown => Math.Min(_maxLevel, _progress.CurrentLevel + _ahead);

        /// <summary>The level the window is centered on.</summary>
        public int CenterLevel => _center;

        public int FirstLevel => Math.Max(1, _center - _behind);
        public int LastLevel => Math.Min(HighestShown, _center + _ahead);

        public bool CanScrollEarlier => FirstLevel > 1;
        public bool CanScrollLater => LastLevel < HighestShown;

        /// <summary>Total best stars, for example "Stars: 23".</summary>
        public string TotalStarsText => "Stars: " + Fmt.Plain(_progress.TotalStars);

        /// <summary>The levels of the window, ascending.</summary>
        public IReadOnlyList<MapNode> Nodes
        {
            get
            {
                var list = new List<MapNode>();
                for (int n = FirstLevel; n <= LastLevel; n++) list.Add(NodeFor(n));
                return list;
            }
        }

        /// <summary>One level's node (any number from 1 to the maximum).</summary>
        public MapNode NodeFor(int n)
        {
            int current = _progress.CurrentLevel;
            var state = n > current ? MapNodeState.Locked : n == current ? MapNodeState.Current : MapNodeState.Completed;
            return new MapNode(n, state, _progress.StarsFor(n), Math.Sin(n * _pathPhase));
        }

        /// <summary>Scrolls the window by whole levels (positive = toward higher levels).</summary>
        public void ScrollBy(int levels)
        {
            int before = _center;
            _center += levels;
            Clamp();
            if (_center != before) RaiseChanged();
        }

        /// <summary>Scrolls back to the current level.</summary>
        public void ScrollToCurrent()
        {
            int before = _center;
            _center = _progress.CurrentLevel;
            Clamp();
            if (_center != before) RaiseChanged();
        }

        /// <summary>A tap on a level. Locked levels do nothing.</summary>
        public bool Select(int n) => n >= 1 && n <= _maxLevel && _progress.IsUnlocked(n) && _flow.SelectLevel(n);

        public bool Back() => _flow.ToTitle();

        void Clamp() => _center = Math.Max(1, Math.Min(_center, HighestShown));
    }

    /// <summary>"Preparing level N" while the level is generated (TRD 12.3).</summary>
    public sealed class LoadingViewModel : ViewModelBase
    {
        readonly GameController _game;

        public LoadingViewModel(GameController game)
        {
            _game = game;
            Action changed = RaiseChanged;
            game.Changed += changed;
            OnDispose(() => game.Changed -= changed);
        }

        /// <summary>"Preparing level N" (empty when nothing is being prepared).</summary>
        public string Text => _game.PreparingText;

        public string CancelLabel => "Back";

        /// <summary>Gives up waiting and goes back to the map (the generation finishes in the background and is cached).</summary>
        public bool Cancel() => _game.Flow.ToMap();
    }

    /// <summary>Explanations of the mechanics a level can introduce (PRD 2.4, 3.3). Keyed by the generator's unlock names.</summary>
    public static class MechanicTexts
    {
        /// <summary>The tip for one unlock name as the generator spells it ("Line pieces"), or null if unknown.</summary>
        public static string? For(string name)
        {
            switch (name)
            {
                case "Collect goals": return "Clear pieces of the named colors before you run out of moves.";
                case "Line pieces": return "Match 4 in a row to earn a Line piece. It clears its whole row or column.";
                case "Bomb pieces": return "Match an L, T or + shape to earn a Bomb piece. It clears the 3x3 area around it.";
                case "Color pieces": return "Match 5 or more in a row to earn a Color piece. It clears every piece of one color.";
                case "Shaped boards": return "Some cells are missing. Pieces fall around the gaps.";
                case "Combined goals": return "This level has two goals. Reach both within the move limit.";
                default: return null;
            }
        }

        /// <summary>The tips for a <c>Level.Intro</c> text, which joins several names with " and ".</summary>
        public static IReadOnlyList<string> ForIntro(string? intro)
        {
            var tips = new List<string>();
            if (string.IsNullOrEmpty(intro)) return tips;
            foreach (var name in intro!.Split(new[] { " and " }, StringSplitOptions.RemoveEmptyEntries))
            {
                var tip = For(name.Trim());
                if (tip != null) tips.Add(tip);
            }
            return tips;
        }
    }

    /// <summary>The level intro (PRD 5.1): goals, move limit and, on an intro level, what is new.</summary>
    public sealed class IntroViewModel : ViewModelBase
    {
        readonly GameController _game;
        readonly bool _dev;

        /// <param name="dev">Show the seed, difficulty and target win rate (dev builds).</param>
        public IntroViewModel(GameController game, bool dev)
        {
            _game = game;
            _dev = dev;
            Action changed = RaiseChanged;
            game.Changed += changed;
            OnDispose(() => game.Changed -= changed);
        }

        bool Has => _game.Level != null;

        public bool HasLevel => Has;

        /// <summary>"Level 12".</summary>
        public string LevelTitle => Has ? "Level " + Fmt.Plain(_game.Level!.Number) : "";

        public string GoalsHeader => "Goals";

        /// <summary>One line per goal, for example "Score 1,000 points" or "Clear 12 Red / Blue colored pieces".</summary>
        public IReadOnlyList<string> GoalLines
        {
            get
            {
                var lines = new List<string>();
                if (_game.Level == null) return lines;
                foreach (var g in _game.Level.Goals) lines.Add(g.Describe(_game.ColorNames));
                return lines;
            }
        }

        public int MoveLimit => _game.Level?.MoveLimit ?? 0;

        /// <summary>"25 moves".</summary>
        public string MovesText => MovesLabel(MoveLimit);

        /// <summary>"New: Line pieces and Collect goals" on an intro level, otherwise null.</summary>
        public string? MechanicHeadline => string.IsNullOrEmpty(_game.Level?.Intro) ? null : "New: " + _game.Level!.Intro;

        /// <summary>The tooltip texts that explain the new mechanics (empty if none).</summary>
        public IReadOnlyList<string> MechanicTips => MechanicTexts.ForIntro(_game.Level?.Intro);

        /// <summary>Dev builds only: "seed 123  difficulty 0.45  target win 0.80", otherwise null.</summary>
        public string? DevInfo
        {
            get
            {
                var level = _game.Level;
                if (!_dev || level == null) return null;
                return "seed " + level.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + "  difficulty " + Fmt.Fixed(level.Difficulty, 2)
                    + "  target win " + Fmt.Fixed(level.TargetWinRate, 2);
            }
        }

        public string PlayLabel => "Play";
        public string BackLabel => "Back";

        public bool Play() => _game.Flow.StartLevel();
        public bool Back() => _game.Flow.ToMap();

        internal static string MovesLabel(int moves) => Fmt.Plain(moves) + (moves == 1 ? " move" : " moves");
    }

    /// <summary>The pause overlay (PRD 2.2): Resume, Quit to map. Shown while <see cref="IsOpen"/>.</summary>
    public sealed class PauseViewModel : ViewModelBase
    {
        readonly ScreenFlow _flow;

        public PauseViewModel(ScreenFlow flow)
        {
            _flow = flow;
            Action changed = RaiseChanged;
            flow.Changed += changed;
            OnDispose(() => flow.Changed -= changed);
        }

        public bool IsOpen => _flow.Current == ScreenId.Play && _flow.Paused;

        public string Title => "Paused";
        public string ResumeLabel => "Resume";
        public string QuitLabel => "Quit to map";

        /// <summary>The HUD's pause button.</summary>
        public string PauseButtonLabel => "Pause";

        public bool Pause() => _flow.Pause();
        public bool Resume() => _flow.Resume();
        public bool QuitToMap() => _flow.ToMap();
    }
}
