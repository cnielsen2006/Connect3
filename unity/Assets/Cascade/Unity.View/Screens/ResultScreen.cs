using System;
using System.Collections.Generic;
using Cascade.Presentation.Shell;
using UnityEngine.UIElements;

namespace Cascade.Unity.View
{
    /// <summary>
    /// Binder of <c>Result.uxml</c> to <see cref="ResultViewModel"/>: a win shows 1 to 3 stars (earned ones in the accent color, they pop in
    /// one after the other: the timing is the <c>transition-delay</c> in Result.uss), the score and Next / Map; a loss shows the goal
    /// progress and Retry / Map.
    /// </summary>
    public sealed class ResultScreen : IScreenView, IDisposable
    {
        readonly ResultViewModel vm;
        readonly Label headline, best, score;
        readonly VisualElement starsRow, goals;
        readonly Button next, retry, map;
        readonly List<StarElement> stars = new List<StarElement>();
        IVisualElementScheduledItem? pop;

        public ResultScreen(VisualElement root, ResultViewModel viewModel)
        {
            vm = viewModel;
            headline = Ui.Get<Label>(root, "result-headline");
            best = Ui.Get<Label>(root, "result-best");
            score = Ui.Get<Label>(root, "result-score");
            starsRow = Ui.Get<VisualElement>(root, "result-stars");
            goals = Ui.Get<VisualElement>(root, "result-goals");
            next = Ui.Get<Button>(root, "result-next");
            retry = Ui.Get<Button>(root, "result-retry");
            map = Ui.Get<Button>(root, "result-map");
            for (int i = 1; i <= 3; i++)
            {
                var s = new StarElement();
                s.AddToClassList("star--slot" + i);
                starsRow.Add(s);
                stars.Add(s);
            }
            next.clicked += () => vm.Next();
            retry.clicked += () => vm.Retry();
            map.clicked += () => vm.ToMap();
            vm.Changed += Refresh;
            Refresh();
        }

        public Button NextButton => next;
        public Button RetryButton => retry;
        public Button MapButton => map;
        public IReadOnlyList<StarElement> Stars => stars;
        public Label HeadlineLabel => headline;
        public Label ScoreLabel => score;

        void Refresh()
        {
            if (!vm.HasResult) return; // keep the last result while the page fades out
            Ui.SetText(headline, vm.Headline);
            Ui.SetText(score, vm.ScoreText);
            Ui.SetText(best, vm.NewBestText ?? "");
            Ui.SetDisplay(starsRow, vm.ShowStars);
            var slots = vm.StarSlots;
            for (int i = 0; i < stars.Count; i++) stars[i].Earned = i < slots.Count && slots[i];

            goals.Clear();
            foreach (var line in vm.GoalLines)
            {
                var l = new Label(line) { pickingMode = PickingMode.Ignore };
                l.AddToClassList("result__goal");
                goals.Add(l);
            }
            Ui.SetDisplay(goals, goals.childCount > 0);

            Ui.SetText(next, vm.NextLabel);
            Ui.SetText(retry, vm.RetryLabel);
            Ui.SetText(map, vm.MapLabel);
            Ui.SetDisplay(next, vm.NextAvailable);
            Ui.SetDisplay(retry, vm.RetryAvailable);
            Ui.SetDisplay(map, vm.MapAvailable);
        }

        public void OnShown()
        {
            foreach (var s in stars) s.RemoveFromClassList("is-in");
            pop?.Pause();
            // next frame, so the transition runs from the hidden state
            pop = starsRow.schedule.Execute(() => { foreach (var s in stars) s.AddToClassList("is-in"); });
            pop.ExecuteLater(30);
        }

        public void OnHidden()
        {
            pop?.Pause();
            foreach (var s in stars) s.RemoveFromClassList("is-in");
        }

        public void Dispose() => vm.Changed -= Refresh;
    }
}
