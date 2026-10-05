using System;
using Cascade.Presentation.Shell;
using UnityEngine.UIElements;

namespace Cascade.Unity.View
{
    /// <summary>Binder of <c>Intro.uxml</c> to <see cref="IntroViewModel"/>: level title, goal lines, move limit, the new-mechanic tooltip on an intro level, Play and Back.</summary>
    public sealed class IntroScreen : IDisposable
    {
        readonly IntroViewModel vm;
        readonly Label title, goalsHeader, moves, mechanicHeadline, dev;
        readonly VisualElement goals, mechanic, tips;
        readonly Button play, back;
        readonly TouchSizing sizing;

        public IntroScreen(VisualElement root, IntroViewModel viewModel, TouchSizing touchSizing)
        {
            vm = viewModel;
            sizing = touchSizing;
            title = Ui.Get<Label>(root, "intro-title");
            goalsHeader = Ui.Get<Label>(root, "intro-goals-header");
            goals = Ui.Get<VisualElement>(root, "intro-goals");
            moves = Ui.Get<Label>(root, "intro-moves");
            mechanic = Ui.Get<VisualElement>(root, "intro-mechanic");
            mechanicHeadline = Ui.Get<Label>(root, "intro-mechanic-headline");
            tips = Ui.Get<VisualElement>(root, "intro-mechanic-tips");
            dev = Ui.Get<Label>(root, "intro-dev");
            play = Ui.Get<Button>(root, "intro-play");
            back = Ui.Get<Button>(root, "intro-back");
            play.clicked += () => vm.Play();
            back.clicked += () => vm.Back();
            vm.Changed += Refresh;
            Refresh();
        }

        public Button PlayButton => play;
        public Button BackButton => back;
        public VisualElement MechanicBox => mechanic;
        public Label TitleLabel => title;

        void Refresh()
        {
            if (!vm.HasLevel) return; // keep the last level while the page fades out
            Ui.SetText(title, vm.LevelTitle);
            Ui.SetText(goalsHeader, vm.GoalsHeader);
            Ui.SetText(moves, vm.MovesText);
            Ui.SetText(play, vm.PlayLabel);
            Ui.SetText(back, vm.BackLabel);

            goals.Clear();
            foreach (var line in vm.GoalLines)
            {
                var l = new Label(line) { pickingMode = PickingMode.Ignore };
                l.AddToClassList("intro__goal");
                goals.Add(l);
            }

            var headline = vm.MechanicHeadline;
            Ui.SetDisplay(mechanic, headline != null);
            Ui.SetText(mechanicHeadline, headline ?? "");
            tips.Clear();
            foreach (var tip in vm.MechanicTips)
            {
                var l = new Label(tip) { pickingMode = PickingMode.Ignore };
                l.AddToClassList("intro__tip");
                tips.Add(l);
            }

            var info = vm.DevInfo;
            Ui.SetDisplay(dev, info != null);
            Ui.SetText(dev, info ?? "");
        }

        public void Dispose() => vm.Changed -= Refresh;
    }
}
