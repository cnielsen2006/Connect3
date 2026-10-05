using System;
using Cascade.Config;
using Cascade.Gameplay;

namespace Cascade.Presentation.Shell
{
    /// <summary>
    /// The player's settings as the running game sees them, plus the animation timing derived from
    /// the <c>[animation]</c> dials and the player's animation speed (TRD 12.3). Changes apply live:
    /// subscribe to <see cref="TimingChanged"/> and rebuild nothing yourself, read <see cref="Timing"/>.
    /// Persistence is not done here: <see cref="SettingsViewModel"/> hands the new value to the
    /// <see cref="IProgressSink"/>.
    /// </summary>
    public sealed class LiveSettings : IDisposable
    {
        readonly DialRegistry _dials;
        TimingParams _timing;

        public LiveSettings(PlayerSettings initial, DialRegistry dials)
        {
            _dials = dials;
            Player = Sanitize(initial);
            _timing = TimingParams.From(dials, Animation);
            dials.Changed += OnDialChanged;
        }

        /// <summary>The current settings (immutable; replaced on every change).</summary>
        public PlayerSettings Player { get; private set; }

        /// <summary>The animation setting derived from <see cref="Player"/>.</summary>
        public AnimationSettings Animation => new AnimationSettings(Player.AnimationSpeed);

        /// <summary>Timings for the board animation: the dials with the player's speed applied.</summary>
        public TimingParams Timing => _timing;

        /// <summary>The settings changed (any field).</summary>
        public event Action<PlayerSettings>? PlayerChanged;

        /// <summary>The animation timings changed (a dial or the player's speed).</summary>
        public event Action<TimingParams>? TimingChanged;

        /// <summary>Replaces the settings. An animation speed outside 0.5-3.0 (or NaN) is clamped.</summary>
        public void Update(PlayerSettings settings)
        {
            settings = Sanitize(settings);
            if (settings == Player) return;
            Player = settings;
            PlayerChanged?.Invoke(Player);
            RebuildTiming();
        }

        void OnDialChanged(DialChange change)
        {
            if (change.Section == "animation") RebuildTiming();
        }

        void RebuildTiming()
        {
            var timing = TimingParams.From(_dials, Animation);
            if (timing == _timing) return;
            _timing = timing;
            TimingChanged?.Invoke(timing);
        }

        static PlayerSettings Sanitize(PlayerSettings s) =>
            PlayerSettings.IsValidSpeed(s.AnimationSpeed) ? s : s with { AnimationSpeed = new AnimationSettings(s.AnimationSpeed).ClampedSpeed };

        public void Dispose() => _dials.Changed -= OnDialChanged;
    }
}
