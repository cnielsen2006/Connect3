using System;
using System.Collections.Generic;
using Cascade.Presentation.Shell;
using UnityEngine.UIElements;

namespace Cascade.Unity.View
{
    /// <summary>
    /// A thin binder over <c>Hud.uxml</c> (TRD 12.2): the play screen of style K (moves and score as large numbers, star marks, the goals
    /// between two driftwood lines, pebble buttons) and its pause overlay. Every number and text comes from <see cref="HudViewModel"/> and
    /// <see cref="PauseViewModel"/>; the layout is data (<c>Hud.uss</c>, per <c>layout--wide</c> / <c>layout--tall</c>). The class only
    /// forwards button presses: Pause and the overlay's Resume and Quit go to the view-models, Hint is raised as an event because the hint
    /// is drawn by the board (<see cref="BoardController.ShowHint"/>).
    /// </summary>
    public sealed class HudScreen : IDisposable
    {
        sealed class GoalRow
        {
            public VisualElement Root = null!;
            public Label Text = null!, Progress = null!;
        }

        sealed class MarkerView
        {
            public VisualElement Root = null!;
            public StarElement[] Stars = null!;
            public Label Text = null!;
        }

        readonly HudViewModel hud;
        readonly PauseViewModel pause;
        readonly Label title, level, movesValue, movesCaption, scoreValue, scoreCaption, pauseTitle, comboValue;
        readonly VisualElement goals, markers;
        readonly Button pauseButton, hintButton, resumeButton, quitButton;
        readonly List<GoalRow> goalRows = new List<GoalRow>();
        readonly List<MarkerView> markerViews = new List<MarkerView>();

        public HudScreen(VisualElement root, HudViewModel hudViewModel, PauseViewModel pauseViewModel, string titleText)
        {
            hud = hudViewModel;
            pause = pauseViewModel;
            BoardSlot = Ui.Get<VisualElement>(root, "BoardSlot");
            PopupLayer = Ui.Get<VisualElement>(root, "popups");
            PauseOverlay = Ui.Get<VisualElement>(root, "pause");
            PauseScrim = Ui.Get<VisualElement>(root, "pause-scrim");
            title = Ui.Get<Label>(root, "title");
            level = Ui.Get<Label>(root, "level-label");
            movesValue = Ui.Get<Label>(root, "moves-value");
            movesCaption = Ui.Get<Label>(root, "moves-caption");
            scoreValue = Ui.Get<Label>(root, "score-value");
            scoreCaption = Ui.Get<Label>(root, "score-caption");
            comboValue = Ui.Get<Label>(root, "combo-value");
            goals = Ui.Get<VisualElement>(root, "goals");
            markers = Ui.Get<VisualElement>(root, "star-markers");
            pauseButton = Ui.Get<Button>(root, "button-pause");
            hintButton = Ui.Get<Button>(root, "button-hint");
            pauseTitle = Ui.Get<Label>(root, "pause-title");
            resumeButton = Ui.Get<Button>(root, "pause-resume");
            quitButton = Ui.Get<Button>(root, "pause-quit");
            title.text = titleText;

            pauseButton.clicked += () => pause.Pause();
            hintButton.clicked += () => HintPressed?.Invoke();
            resumeButton.clicked += () => pause.Resume();
            quitButton.clicked += () => pause.QuitToMap();

            hud.Changed += Refresh;
            pause.Changed += RefreshPause;
            Refresh();
            RefreshPause();
        }

        /// <summary>The empty element the board camera fits the board into.</summary>
        public VisualElement BoardSlot { get; }

        /// <summary>A full-screen element score popups are added to.</summary>
        public VisualElement PopupLayer { get; }

        /// <summary>The pause overlay (<see cref="UiRoot"/> fades it in and out with the view-model's <see cref="PauseViewModel.IsOpen"/>).</summary>
        public VisualElement PauseOverlay { get; }

        /// <summary>The full-bleed backdrop of the overlay (extended over the unsafe margins by <see cref="UiRoot"/>).</summary>
        public VisualElement PauseScrim { get; }

        public Button PauseButton => pauseButton;
        public Button HintButton => hintButton;
        public Button ResumeButton => resumeButton;
        public Button QuitButton => quitButton;
        public Label MovesLabel => movesValue;
        public Label ScoreLabel => scoreValue;
        public Label ComboLabel => comboValue;
        public Label LevelLabel => level;
        public int GoalRowCount => goalRows.Count;

        /// <summary>The hint button was pressed.</summary>
        public event Action? HintPressed;

        void Refresh()
        {
            Ui.SetText(level, hud.LevelTitle);
            Ui.SetText(movesValue, hud.MovesText);
            Ui.SetText(movesCaption, hud.MovesLabel);
            Ui.SetText(scoreValue, hud.ScoreText);
            Ui.SetText(scoreCaption, hud.ScoreLabel);
            Ui.SetText(comboValue, hud.ComboText);
            comboValue.EnableInClassList("hud__combo--on", hud.ComboVisible);
            for (int t = 0; t <= 4; t++) comboValue.EnableInClassList("combo-badge--tier" + t, hud.ComboVisible && t == hud.ComboTier);
            Ui.SetText(hintButton, hud.HintLabel);
            Ui.SetText(pauseButton, hud.PauseLabel);
            RefreshGoals();
            RefreshMarkers();
        }

        void RefreshGoals()
        {
            var lines = hud.Goals;
            if (goalRows.Count != lines.Count)
            {
                goals.Clear();
                goalRows.Clear();
                for (int i = 0; i < lines.Count; i++)
                {
                    var row = new GoalRow { Root = new VisualElement { pickingMode = PickingMode.Ignore } };
                    row.Root.AddToClassList("goal");
                    var line = new VisualElement { pickingMode = PickingMode.Ignore };
                    line.AddToClassList("goal__line");
                    row.Text = new Label { pickingMode = PickingMode.Ignore };
                    row.Text.AddToClassList("goal__text");
                    row.Progress = new Label { pickingMode = PickingMode.Ignore };
                    row.Progress.AddToClassList("goal__progress");
                    line.Add(row.Text);
                    line.Add(row.Progress);
                    row.Root.Add(line);
                    goals.Add(row.Root);
                    goalRows.Add(row);
                }
            }
            for (int i = 0; i < lines.Count; i++)
            {
                var g = lines[i];
                var row = goalRows[i];
                Ui.SetText(row.Text, g.Text);
                Ui.SetText(row.Progress, g.ProgressText);
                row.Root.EnableInClassList("goal--done", g.Done);
            }
        }

        void RefreshMarkers()
        {
            var list = hud.StarMarkers;
            if (markerViews.Count != list.Count)
            {
                markers.Clear();
                markerViews.Clear();
                foreach (var m in list)
                {
                    var view = new MarkerView { Root = new VisualElement { pickingMode = PickingMode.Ignore }, Stars = new StarElement[m.Stars] };
                    view.Root.AddToClassList("marker");
                    for (int i = 0; i < m.Stars; i++)
                    {
                        var s = new StarElement();
                        s.AddToClassList("star--small");
                        view.Stars[i] = s;
                        view.Root.Add(s);
                    }
                    view.Text = new Label { pickingMode = PickingMode.Ignore };
                    view.Text.AddToClassList("marker__text");
                    view.Root.Add(view.Text);
                    markers.Add(view.Root);
                    markerViews.Add(view);
                }
            }
            for (int i = 0; i < list.Count; i++)
            {
                var m = list[i];
                var view = markerViews[i];
                Ui.SetText(view.Text, m.Text);
                view.Root.EnableInClassList("marker--reached", m.Reached);
                foreach (var s in view.Stars) if (s.Earned != m.Reached) s.Earned = m.Reached;
            }
        }

        void RefreshPause()
        {
            Ui.SetText(pauseTitle, pause.Title);
            Ui.SetText(resumeButton, pause.ResumeLabel);
            Ui.SetText(quitButton, pause.QuitLabel);
        }

        public void Dispose()
        {
            hud.Changed -= Refresh;
            pause.Changed -= RefreshPause;
        }
    }
}
