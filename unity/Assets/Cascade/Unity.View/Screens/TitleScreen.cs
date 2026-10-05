using System;
using Cascade.Presentation.Shell;
using UnityEngine.UIElements;

namespace Cascade.Unity.View
{
    /// <summary>Binder of <c>Title.uxml</c> to <see cref="TitleViewModel"/>: game name, Play / Continue, Settings, Quit (desktop only).</summary>
    public sealed class TitleScreen : IDisposable
    {
        readonly TitleViewModel vm;
        readonly Label name, tagline;
        readonly Button play, settings, quit;

        public TitleScreen(VisualElement root, TitleViewModel viewModel)
        {
            vm = viewModel;
            name = Ui.Get<Label>(root, "title-name");
            tagline = Ui.Get<Label>(root, "title-tagline");
            play = Ui.Get<Button>(root, "title-play");
            settings = Ui.Get<Button>(root, "title-settings");
            quit = Ui.Get<Button>(root, "title-quit");
            play.clicked += () => vm.Play();
            settings.clicked += () => vm.OpenSettings();
            quit.clicked += () => vm.Quit();
            vm.Changed += Refresh;
            Refresh();
        }

        public Button PlayButton => play;
        public Button SettingsButton => settings;
        public Button QuitButton => quit;
        public Label NameLabel => name;

        void Refresh()
        {
            Ui.SetText(name, vm.Title);
            Ui.SetText(tagline, vm.Tagline);
            Ui.SetText(play, vm.PlayLabel);
            Ui.SetText(settings, vm.SettingsLabel);
            Ui.SetText(quit, vm.QuitLabel);
            Ui.SetDisplay(quit, vm.QuitVisible);
        }

        public void Dispose() => vm.Changed -= Refresh;
    }
}
