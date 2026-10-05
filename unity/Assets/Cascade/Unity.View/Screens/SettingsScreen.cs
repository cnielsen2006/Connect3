using System;
using Cascade.Presentation.Shell;
using UnityEngine.UIElements;

namespace Cascade.Unity.View
{
    /// <summary>
    /// Binder of <c>Settings.uxml</c> to <see cref="SettingsViewModel"/>: sound toggle, animation speed slider (0.5 to 3.0) and adaptive
    /// difficulty toggle. Every change goes to the view-model at once (it applies live and hands the new settings to the save).
    /// </summary>
    public sealed class SettingsScreen : IDisposable
    {
        readonly SettingsViewModel vm;
        readonly Label title, speedText, speedMin, speedMax;
        readonly Toggle sound, adaptive;
        readonly Slider speed;
        readonly Button back;

        public SettingsScreen(VisualElement root, SettingsViewModel viewModel)
        {
            vm = viewModel;
            title = Ui.Get<Label>(root, "settings-title");
            speedText = Ui.Get<Label>(root, "settings-speed-text");
            speedMin = Ui.Get<Label>(root, "settings-speed-min");
            speedMax = Ui.Get<Label>(root, "settings-speed-max");
            sound = Ui.Get<Toggle>(root, "settings-sound");
            adaptive = Ui.Get<Toggle>(root, "settings-adaptive");
            speed = Ui.Get<Slider>(root, "settings-speed");
            back = Ui.Get<Button>(root, "settings-back");

            speed.lowValue = (float)vm.SpeedMin;
            speed.highValue = (float)vm.SpeedMax;
            speed.showInputField = false;
            speedMin.text = Fmt.Fixed(vm.SpeedMin, 1);
            speedMax.text = Fmt.Fixed(vm.SpeedMax, 1);

            AddKnob(sound);
            AddKnob(adaptive);
            sound.RegisterValueChangedCallback(e => vm.SetSound(e.newValue));
            adaptive.RegisterValueChangedCallback(e => vm.SetAdaptive(e.newValue));
            speed.RegisterValueChangedCallback(e => vm.SetAnimationSpeed(e.newValue));
            back.clicked += () => vm.Back();
            vm.Changed += Refresh;
            Refresh();
        }

        static void AddKnob(Toggle t)
        {
            var mark = t.Q(className: "unity-toggle__checkmark");
            if (mark == null) return;
            var knob = new VisualElement { pickingMode = PickingMode.Ignore };
            knob.AddToClassList("setting__knob");
            mark.Add(knob);
        }

        public Toggle SoundToggle => sound;
        public Toggle AdaptiveToggle => adaptive;
        public Slider SpeedSlider => speed;
        public Button BackButton => back;
        public Label SpeedLabel => speedText;

        void Refresh()
        {
            Ui.SetText(title, vm.Title);
            Ui.SetText(back, vm.BackLabel);
            Ui.SetText(speedText, vm.SpeedText);
            if (sound.label != vm.SoundText) sound.label = vm.SoundText;
            if (adaptive.label != vm.AdaptiveText) adaptive.label = vm.AdaptiveText;
            sound.SetValueWithoutNotify(vm.Sound);
            adaptive.SetValueWithoutNotify(vm.Adaptive);
            speed.SetValueWithoutNotify((float)vm.AnimationSpeed);
        }

        public void Dispose() => vm.Changed -= Refresh;
    }
}
