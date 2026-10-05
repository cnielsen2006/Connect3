using System;
using System.Collections.Generic;
using Cascade.Config;
using Cascade.Platform.Abstractions;

namespace Cascade.Presentation.Shell
{
    /// <summary>One dial on the dev page. Everything is read live from the registry.</summary>
    public sealed class DevField
    {
        readonly DialRegistry _dials;
        readonly DialMeta _meta;

        internal DevField(DialRegistry dials, DialMeta meta)
        {
            _dials = dials;
            _meta = meta;
        }

        public string Section => _meta.Section;
        public string Key => _meta.Key;

        /// <summary>"section.key".</summary>
        public string Path => _meta.Path;

        public string Description => _meta.Description;
        public DialType Type => _meta.Type;
        public bool IsInt => _meta.Type == DialType.Int;
        public double Min => _meta.Min;
        public double Max => _meta.Max;
        public double Step => _meta.Step;

        /// <summary>The current value (a boxed int or double).</summary>
        public object Current => _dials.Get(_meta.Section, _meta.Key);

        /// <summary>The default value (dials.toml with the platform file applied).</summary>
        public object Default => _meta.Default;

        public string CurrentText => Text(Current);
        public string DefaultText => Text(Default);

        /// <summary>The current value differs from the default (the page marks it, and Save writes it).</summary>
        public bool IsChanged => _dials.IsChanged(_meta.Section, _meta.Key);

        /// <summary>For a hint under the field, for example "0 to 10,000, step 5".</summary>
        public string RangeText => Text(Min) + " to " + Text(Max) + ", step " + Text(Step);

        string Text(object value) => value is int i ? Fmt.Plain(i) : Fmt.Round(Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>A group of dials, one tab or foldout on the dev page.</summary>
    public sealed class DevSection
    {
        internal DevSection(string name, IReadOnlyList<DevField> fields)
        {
            Name = name;
            Fields = fields;
        }

        public string Name { get; }
        public IReadOnlyList<DevField> Fields { get; }

        /// <summary>How many dials differ from their defaults.</summary>
        public int ChangedCount
        {
            get
            {
                int n = 0;
                foreach (var f in Fields) if (f.IsChanged) n++;
                return n;
            }
        }
    }

    /// <summary>
    /// The dev settings page (PRD 5.2, TRD 12.2), generated from <see cref="DialRegistry.Metadata"/>: one
    /// section per dial section, one field per dial with its range, step, type, current and default value
    /// and a changed flag. Edits apply live. It also shows the running level's seed, difficulty, target and
    /// measured win rate, and jumps to a level. Everything is inert (and the sections empty) unless
    /// <see cref="HostInfo.Dev"/> is true: <see cref="Enabled"/>.
    /// <para>All feedback goes through <see cref="Message"/>.</para>
    /// </summary>
    public sealed class DevViewModel : ViewModelBase
    {
        // The order of the dev page; a dial section not listed is appended in ordinal order.
        static readonly string[] PreferredOrder =
            { "scoring", "specials", "stars", "input", "render", "animation", "celebration", "audio", "camera", "pieces", "generator", "planner" };

        readonly DialRegistry _dials;
        readonly GameController _game;
        readonly bool _enabled;
        readonly List<DevSection> _sections = new List<DevSection>();
        readonly Dictionary<string, DevField> _byPath = new Dictionary<string, DevField>(StringComparer.Ordinal);
        string _selected = "";

        public DevViewModel(DialRegistry dials, GameController game, HostInfo host)
        {
            _dials = dials;
            _game = game;
            _enabled = host.Dev;
            if (_enabled) Build();

            Action<DialChange> onDial = _ => RaiseChanged();
            Action onGame = RaiseChanged;
            Action onFlow = RaiseChanged;
            dials.Changed += onDial;
            game.Changed += onGame;
            game.Flow.Changed += onFlow;
            OnDispose(() => { dials.Changed -= onDial; game.Changed -= onGame; game.Flow.Changed -= onFlow; });
        }

        void Build()
        {
            var groups = new Dictionary<string, List<DevField>>(StringComparer.Ordinal);
            var names = new List<string>();
            foreach (var meta in _dials.Metadata)
            {
                if (!groups.TryGetValue(meta.Section, out var list))
                {
                    list = new List<DevField>();
                    groups[meta.Section] = list;
                    names.Add(meta.Section);
                }
                var field = new DevField(_dials, meta);
                list.Add(field);
                _byPath[meta.Path] = field;
            }
            foreach (var name in PreferredOrder)
                if (groups.ContainsKey(name)) _sections.Add(new DevSection(name, groups[name]));
            var rest = new List<string>();
            foreach (var name in names) if (Array.IndexOf(PreferredOrder, name) < 0) rest.Add(name);
            rest.Sort(StringComparer.Ordinal);
            foreach (var name in rest) _sections.Add(new DevSection(name, groups[name]));
            if (_sections.Count > 0) _selected = _sections[0].Name;
        }

        /// <summary>False in a release build: nothing here does anything.</summary>
        public bool Enabled => _enabled;

        /// <summary>The overlay is shown (<see cref="ScreenFlow.DevOpen"/>).</summary>
        public bool IsOpen => _enabled && _game.Flow.DevOpen;

        public string Title => "Dev settings";

        /// <summary>The last feedback line ("scoring.run3 = 80", "rejected: ...", "saved ...").</summary>
        public string Message { get; private set; } = "";

        // -- dials ----------------------------------------------------------------------------

        public IReadOnlyList<DevSection> Sections => _sections;

        /// <summary>The section whose fields the page shows (a tab), initially the first.</summary>
        public string SelectedSection => _selected;

        public DevSection? Selected
        {
            get
            {
                foreach (var s in _sections) if (s.Name == _selected) return s;
                return null;
            }
        }

        /// <summary>Total number of dials that differ from their defaults.</summary>
        public int ChangedCount
        {
            get
            {
                int n = 0;
                foreach (var s in _sections) n += s.ChangedCount;
                return n;
            }
        }

        public bool SelectSection(string name)
        {
            if (!_enabled || name == _selected) return false;
            foreach (var s in _sections)
            {
                if (s.Name != name) continue;
                _selected = name;
                Message = "";
                RaiseChanged();
                return true;
            }
            return false;
        }

        public DevField? Field(string path) => _byPath.TryGetValue(path, out var f) ? f : null;

        /// <summary>
        /// Sets a dial from text typed in a field (invariant culture). A whole number is required for an int
        /// dial; a value outside the range is rejected. Returns true if the dial now has that value.
        /// </summary>
        public bool Set(string path, string text)
        {
            if (!TryField(path, out var f)) return false;
            if (f.IsInt)
            {
                if (!Fmt.TryParseInt(text, out int i)) return Reject(path + ": enter a whole number");
                return Apply(f, i);
            }
            if (!Fmt.TryParseDouble(text, out double d)) return Reject(path + ": enter a number");
            return Apply(f, d);
        }

        /// <summary>Moves a dial one step up (+1) or down (-1), clamped to its range.</summary>
        public bool Nudge(string path, int direction)
        {
            if (!TryField(path, out var f)) return false;
            double current = Convert.ToDouble(f.Current, System.Globalization.CultureInfo.InvariantCulture);
            double next = Math.Min(f.Max, Math.Max(f.Min, current + Math.Sign(direction) * f.Step));
            if (f.IsInt) return Apply(f, (int)Math.Round(next));
            // keep decimals tidy: 0.1 + 0.05 must not show as 0.15000000000000002
            return Apply(f, Math.Round(next, 6));
        }

        /// <summary>Puts one dial back to its default.</summary>
        public bool ResetField(string path)
        {
            if (!TryField(path, out var f)) return false;
            _dials.Reset(f.Section, f.Key);
            Message = f.Path + " reset to " + f.DefaultText;
            RaiseChanged();
            return true;
        }

        /// <summary>Puts every dial back to its default (not saved until <see cref="Save"/>).</summary>
        public bool ResetAll()
        {
            if (!_enabled) return false;
            _dials.ResetAll();
            Message = "reset to defaults (not saved)";
            RaiseChanged();
            return true;
        }

        /// <summary>Writes the changed dials to <c>dev_dials.toml</c> (<see cref="DialRegistry.SaveDevOverrides"/>).</summary>
        public bool Save()
        {
            if (!_enabled) return false;
            try
            {
                _dials.SaveDevOverrides();
                Message = "saved " + Fmt.Plain(ChangedCount) + " changed dials to " + DialRegistry.DevOverridesFile;
                RaiseChanged();
                return true;
            }
            catch (Exception e)
            {
                return Reject("save failed: " + e.Message);
            }
        }

        bool TryField(string path, out DevField field)
        {
            field = null!;
            if (!_enabled) return false;
            if (_byPath.TryGetValue(path, out field!)) return true;
            Reject("unknown dial " + path);
            return false;
        }

        bool Apply(DevField f, object value)
        {
            try
            {
                _dials.Set(f.Section, f.Key, value);
            }
            catch (DialException e)
            {
                return Reject("rejected: " + e.Message);
            }
            Message = f.Path + " = " + f.CurrentText;
            RaiseChanged();
            return true;
        }

        bool Reject(string message)
        {
            Message = message;
            RaiseChanged();
            return false;
        }

        // -- level info -----------------------------------------------------------------------

        /// <summary>A level is loaded (running or about to).</summary>
        public bool HasLevel => _enabled && _game.Level != null;

        public int LevelNumber => _game.Level?.Number ?? 0;

        /// <summary>The seed as decimal text (it does not fit a JSON number).</summary>
        public string SeedText => _game.Level == null ? "-" : _game.Level.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>The level's difficulty(n) as it was generated (the intro override included).</summary>
        public string DifficultyText => _game.Level == null ? "-" : Fmt.Fixed(_game.Level.Difficulty, 2);

        /// <summary>The bot win rate the level aimed for.</summary>
        public string TargetWinRateText => _game.Level == null ? "-" : Fmt.Fixed(_game.Level.TargetWinRate, 2);

        /// <summary>The bot win rate the generator measured, or "-" if none.</summary>
        public string MeasuredWinRateText =>
            _game.Level?.MeasuredWinRate is double m ? Fmt.Fixed(m, 2) : "-";

        /// <summary>One line: "level 12  seed 123  difficulty 0.45  target 0.80  measured 0.78".</summary>
        public string LevelInfoText => HasLevel
            ? "level " + Fmt.Plain(LevelNumber) + "  seed " + SeedText + "  difficulty " + DifficultyText
                + "  target " + TargetWinRateText + "  measured " + MeasuredWinRateText
            : "no level running";

        // -- auto-play ------------------------------------------------------------------------

        /// <summary>
        /// Dev aid: the app plays by itself (starts levels, makes the suggested move, goes on to the next level or retries).
        /// Always false in a release build. The driver that acts on it lives in the app layer.
        /// </summary>
        public bool AutoPlay { get; private set; }

        public bool SetAutoPlay(bool on)
        {
            if (!_enabled || AutoPlay == on) return false;
            AutoPlay = on;
            Message = on ? "auto-play on" : "auto-play off";
            RaiseChanged();
            return true;
        }

        public bool ToggleAutoPlay() => SetAutoPlay(!AutoPlay);

        /// <summary>
        /// Auto-play mode (TRD 7.1): false = <c>Next</c>, play the suggested move; true = <c>Plan</c>, look several moves ahead,
        /// show the research and play the first move of the best line. Always false in a release build.
        /// </summary>
        public bool AutoPlayPlans { get; private set; }

        public bool SetAutoPlayPlans(bool on)
        {
            if (!_enabled || AutoPlayPlans == on) return false;
            AutoPlayPlans = on;
            Message = on ? "auto-play: plan ahead" : "auto-play: next move";
            RaiseChanged();
            return true;
        }

        public bool ToggleAutoPlayPlans() => SetAutoPlayPlans(!AutoPlayPlans);

        // -- commands -------------------------------------------------------------------------

        /// <summary>Jump to level <paramref name="n"/> (at least 1): closes the page and prepares it with a fresh seed.</summary>
        public bool JumpToLevel(int n)
        {
            if (!_enabled) return false;
            Message = "";
            return _game.Flow.DevJump(Math.Max(1, n));
        }

        /// <summary>Jump to the level typed in the field; anything else says "enter a level number".</summary>
        public bool JumpToLevel(string text)
        {
            if (!_enabled) return false;
            if (!Fmt.TryParseInt(text, out int n)) return Reject("enter a level number");
            return JumpToLevel(n);
        }

        public bool Close() => _enabled && _game.Flow.CloseDev();
        public bool Toggle() => _enabled && _game.Flow.ToggleDev();
    }
}
