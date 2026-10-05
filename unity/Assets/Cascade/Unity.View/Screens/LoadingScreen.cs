using System;
using System.Collections.Generic;
using Cascade.Presentation.Shell;
using UnityEngine.UIElements;

namespace Cascade.Unity.View
{
    /// <summary>
    /// Binder of <c>Loading.uxml</c> to <see cref="LoadingViewModel"/>: "Preparing level N" and a calm wave of three pebbles. While
    /// the page is shown, one pebble after another gets the class <c>is-up</c>; the pace is the pebbles' USS
    /// <c>transition-duration</c>, so the animation is edited in Loading.uss, not here.
    /// </summary>
    public sealed class LoadingScreen : IScreenView, IDisposable
    {
        readonly LoadingViewModel vm;
        readonly Label text;
        readonly Button back;
        readonly List<VisualElement> pebbles = new List<VisualElement>();
        IVisualElementScheduledItem? wave;
        int step;

        public LoadingScreen(VisualElement root, LoadingViewModel viewModel)
        {
            vm = viewModel;
            text = Ui.Get<Label>(root, "loading-text");
            back = Ui.Get<Button>(root, "loading-back");
            for (int i = 0; i < 3; i++) pebbles.Add(Ui.Get<VisualElement>(root, "loading-pebble-" + i));
            back.clicked += () => vm.Cancel();
            vm.Changed += Refresh;
            Refresh();
        }

        public Button BackButton => back;
        public Label TextLabel => text;

        void Refresh()
        {
            // keep the last text while the page fades out, so it does not flash empty
            var t = vm.Text;
            if (t.Length > 0) Ui.SetText(text, t);
            Ui.SetText(back, vm.CancelLabel);
        }

        public void OnShown()
        {
            if (wave != null) return;
            long interval = 700;
            var d = pebbles[0].resolvedStyle.transitionDuration;
            foreach (var t in d) { interval = (long)(t.value * (t.unit == TimeUnit.Second ? 1000f : 1f)); break; }
            if (interval < 100) interval = 100;
            step = 0;
            wave = pebbles[0].schedule.Execute(Tick).Every(interval);
            Tick();
        }

        public void OnHidden()
        {
            wave?.Pause();
            wave = null;
            foreach (var p in pebbles) p.RemoveFromClassList("is-up");
        }

        void Tick()
        {
            // the pebble of this step rises, the others settle: 0, 1, 2, then a beat of rest
            for (int i = 0; i < pebbles.Count; i++) pebbles[i].EnableInClassList("is-up", i == step % (pebbles.Count + 1));
            step++;
        }

        public void Dispose()
        {
            vm.Changed -= Refresh;
            OnHidden();
        }
    }
}
