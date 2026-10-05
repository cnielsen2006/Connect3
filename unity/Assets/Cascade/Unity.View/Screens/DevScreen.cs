#if UNITY_EDITOR || DEVELOPMENT_BUILD || CASCADE_DEV
// Dev builds only (TRD 12.2, 13.8): this file is not compiled into a release player, so the page, its generator and the hotkeys cannot ship.
using System;
using System.Collections.Generic;
using Cascade.Config;
using Cascade.Presentation.Shell;
using UnityEngine;
using UnityEngine.UIElements;

namespace Cascade.Unity.View
{
    /// <summary>
    /// The dev settings page (PRD 5.2, TRD 12.2), generated from <see cref="DevViewModel"/>: one foldout per dial section, one row per dial
    /// with the right control (a slider plus a number field for a range, whole numbers for an integer dial), a changed flag, the default
    /// value and a per-dial reset; Save writes <c>dev_dials.toml</c>, Reset all restores the defaults; the level's seed, difficulty,
    /// target and measured win rate; "jump to level N". Edits go through the view-model, which sets the dial in the registry, so they
    /// apply live everywhere that listens to <c>DialRegistry.Changed</c>. The page lists nothing itself: a new dial appears when it is in the metadata.
    /// </summary>
    public sealed class DevScreen : IDisposable
    {
        sealed class FieldView
        {
            public DevField Field = null!;
            public VisualElement Root = null!;
            public Label Name = null!, Flag = null!, Default = null!;
            public Slider? DoubleSlider;
            public SliderInt? IntSlider;
            public TextField Number = null!;
            public Button Reset = null!;
        }

        sealed class SectionView
        {
            public DevSection Section = null!;
            public Foldout Foldout = null!;
            public readonly List<FieldView> Fields = new List<FieldView>();
        }

        readonly DevViewModel vm;
        readonly TouchSizing sizing;
        readonly VisualElement root, panel;
        readonly ScrollView scroll;
        readonly Label title, info, message, changed;
        readonly Button close, jump, save, resetAll;
        readonly IntegerField jumpField;
        readonly Toggle fpsToggle, autoPlayToggle, planToggle;
        readonly List<SectionView> sections = new List<SectionView>();
        readonly Dictionary<string, FieldView> byPath = new Dictionary<string, FieldView>(StringComparer.Ordinal);

        public DevScreen(VisualElement overlay, DevViewModel viewModel, TouchSizing touchSizing)
        {
            vm = viewModel;
            sizing = touchSizing;
            root = overlay;
            panel = Ui.Get<VisualElement>(root, "dev-panel");
            scroll = Ui.Get<ScrollView>(root, "dev-scroll");
            title = Ui.Get<Label>(root, "dev-title");
            info = Ui.Get<Label>(root, "dev-level-info");
            message = Ui.Get<Label>(root, "dev-message");
            changed = Ui.Get<Label>(root, "dev-changed");
            close = Ui.Get<Button>(root, "dev-close");
            jump = Ui.Get<Button>(root, "dev-jump");
            save = Ui.Get<Button>(root, "dev-save");
            resetAll = Ui.Get<Button>(root, "dev-reset-all");
            jumpField = Ui.Get<IntegerField>(root, "dev-jump-field");
            close.text = "Close";
            jump.text = "Go";
            save.text = "Save";
            save.AddToClassList("dev-button--accent");
            resetAll.text = "Reset all";

            close.clicked += () => vm.Close();
            jump.clicked += () => vm.JumpToLevel(jumpField.value);
            save.clicked += () => vm.Save();
            resetAll.clicked += () => vm.ResetAll();
            scroll.mouseWheelScrollSize = 120f;

            fpsToggle = new Toggle("Show FPS (frame-rate readout, TRD 13.9)") { name = "dev-fps" };
            fpsToggle.AddToClassList("dev-field");
            fpsToggle.RegisterValueChangedCallback(e => { if (FpsCounter.Instance != null) FpsCounter.Instance.Visible = e.newValue; });
            panel.Insert(panel.IndexOf(scroll), fpsToggle);

            autoPlayToggle = new Toggle("Auto-play (the app plays itself; F2 toggles)") { name = "dev-autoplay" };
            autoPlayToggle.AddToClassList("dev-field");
            autoPlayToggle.RegisterValueChangedCallback(e => vm.SetAutoPlay(e.newValue));
            panel.Insert(panel.IndexOf(scroll), autoPlayToggle);

            planToggle = new Toggle("Plan ahead (auto-play searches the best line and shows its research; F3 toggles)") { name = "dev-autoplay-plan" };
            planToggle.AddToClassList("dev-field");
            planToggle.RegisterValueChangedCallback(e => vm.SetAutoPlayPlans(e.newValue));
            panel.Insert(panel.IndexOf(scroll), planToggle);

            Build();
            vm.Changed += Refresh;
            Refresh();
        }

        public VisualElement Panel => panel;
        public Button SaveButton => save;
        public Button ResetAllButton => resetAll;
        public Button JumpButton => jump;
        public IntegerField JumpField => jumpField;
        public Toggle AutoPlayToggle => autoPlayToggle;
        public Toggle PlanToggle => planToggle;
        public Label MessageLabel => message;
        public Label LevelInfoLabel => info;
        public ScrollView Scroll => scroll;
        public IReadOnlyList<Foldout> Foldouts
        {
            get
            {
                var list = new List<Foldout>();
                foreach (var s in sections) list.Add(s.Foldout);
                return list;
            }
        }

        public int FieldCount => byPath.Count;

        /// <summary>The control of one dial (tests drive these like a person would), or null.</summary>
        public Slider? SliderOf(string path) => byPath.TryGetValue(path, out var f) ? f.DoubleSlider : null;
        public SliderInt? IntSliderOf(string path) => byPath.TryGetValue(path, out var f) ? f.IntSlider : null;
        public TextField? NumberOf(string path) => byPath.TryGetValue(path, out var f) ? f.Number : null;
        public Button? ResetOf(string path) => byPath.TryGetValue(path, out var f) ? f.Reset : null;
        public VisualElement? RowOf(string path) => byPath.TryGetValue(path, out var f) ? f.Root : null;

        // ---- generation ----

        void Build()
        {
            scroll.Clear();
            sections.Clear();
            byPath.Clear();
            foreach (var section in vm.Sections)
            {
                var sv = new SectionView { Section = section };
                sv.Foldout = new Foldout { text = section.Name, value = section.Name == vm.SelectedSection };
                sv.Foldout.AddToClassList("dev-foldout");
                foreach (var field in section.Fields)
                {
                    var fv = MakeField(field);
                    sv.Fields.Add(fv);
                    byPath[field.Path] = fv;
                    sv.Foldout.Add(fv.Root);
                }
                sections.Add(sv);
                scroll.Add(sv.Foldout);
            }
            sizing.Apply(root);
        }

        FieldView MakeField(DevField field)
        {
            var fv = new FieldView { Field = field };
            string path = field.Path;
            fv.Root = new VisualElement { name = "dev-field-" + path };
            fv.Root.AddToClassList("dev-field");

            var top = new VisualElement();
            top.AddToClassList("dev-field__top");
            fv.Name = new Label(field.Key) { pickingMode = PickingMode.Ignore };
            fv.Name.AddToClassList("dev-field__name");
            fv.Flag = new Label("changed") { pickingMode = PickingMode.Ignore };
            fv.Flag.AddToClassList("dev-field__flag");
            fv.Reset = new Button(() => vm.ResetField(path)) { text = "reset" };
            fv.Reset.AddToClassList("dev-button");
            fv.Reset.AddToClassList("dev-button--small");
            var left = new VisualElement { pickingMode = PickingMode.Ignore };
            left.style.flexDirection = FlexDirection.Row;
            left.Add(fv.Name);
            left.Add(fv.Flag);
            top.Add(left);
            top.Add(fv.Reset);
            fv.Root.Add(top);

            var desc = new Label(field.Description) { pickingMode = PickingMode.Ignore };
            desc.AddToClassList("dev-field__desc");
            fv.Root.Add(desc);

            var control = new VisualElement();
            control.AddToClassList("dev-field__control");
            if (field.IsInt)
            {
                var s = new SliderInt((int)Math.Round(field.Min), (int)Math.Round(field.Max)) { showInputField = false };
                s.AddToClassList("dev-field__slider");
                s.RegisterValueChangedCallback(e => vm.Set(path, Fmt.Plain(e.newValue)));
                fv.IntSlider = s;
                control.Add(s);
            }
            else
            {
                var s = new Slider((float)field.Min, (float)field.Max) { showInputField = false };
                s.AddToClassList("dev-field__slider");
                double step = field.Step;
                s.RegisterValueChangedCallback(e =>
                {
                    double v = e.newValue;
                    if (step > 0.0) v = Math.Round(v / step) * step;
                    vm.Set(path, Fmt.Round(Math.Round(v, 6)));
                });
                fv.DoubleSlider = s;
                control.Add(s);
            }
            fv.Number = new TextField { isDelayed = true };
            fv.Number.AddToClassList("dev-field__number");
            fv.Number.RegisterValueChangedCallback(e => vm.Set(path, e.newValue));
            control.Add(fv.Number);
            fv.Default = new Label { pickingMode = PickingMode.Ignore };
            fv.Default.AddToClassList("dev-field__default");
            control.Add(fv.Default);
            fv.Root.Add(control);

            var range = new Label("range " + field.RangeText) { pickingMode = PickingMode.Ignore };
            range.AddToClassList("dev-field__desc");
            fv.Root.Add(range);
            return fv;
        }

        // ---- live values ----

        void Refresh()
        {
            Ui.SetText(title, vm.Title);
            Ui.SetText(info, vm.LevelInfoText);
            Ui.SetText(message, vm.Message);
            Ui.SetText(changed, vm.ChangedCount + " changed");
            autoPlayToggle.SetValueWithoutNotify(vm.AutoPlay);
            planToggle.SetValueWithoutNotify(vm.AutoPlayPlans);
            foreach (var sv in sections)
            {
                int n = sv.Section.ChangedCount;
                string text = n > 0 ? sv.Section.Name + " (" + n + " changed)" : sv.Section.Name;
                if (sv.Foldout.text != text) sv.Foldout.text = text;
                sv.Foldout.EnableInClassList("dev-foldout--changed", n > 0);
                foreach (var fv in sv.Fields) RefreshField(fv);
            }
        }

        static void RefreshField(FieldView fv)
        {
            var f = fv.Field;
            bool isChanged = f.IsChanged;
            if (fv.IntSlider != null) fv.IntSlider.SetValueWithoutNotify(Convert.ToInt32(f.Current, System.Globalization.CultureInfo.InvariantCulture));
            if (fv.DoubleSlider != null) fv.DoubleSlider.SetValueWithoutNotify((float)Convert.ToDouble(f.Current, System.Globalization.CultureInfo.InvariantCulture));
            fv.Number.SetValueWithoutNotify(f.CurrentText);
            Ui.SetText(fv.Default, "default " + f.DefaultText);
            fv.Flag.style.display = isChanged ? DisplayStyle.Flex : DisplayStyle.None;
            fv.Reset.SetEnabled(isChanged);
            fv.Root.EnableInClassList("dev-field--changed", isChanged);
        }

        /// <summary>Called when the page opens: the jump field starts at the level on screen.</summary>
        public void OnShown()
        {
            if (vm.LevelNumber > 0) jumpField.SetValueWithoutNotify(vm.LevelNumber);
            if (FpsCounter.Instance != null) fpsToggle.SetValueWithoutNotify(FpsCounter.Instance.Visible);
        }

        public void Dispose() => vm.Changed -= Refresh;
    }
}
#endif
