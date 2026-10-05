using System;
using Cascade.Platform.Abstractions;

namespace Cascade.Presentation.Shell
{
    /// <summary>The full-screen pages of the game (PRD 5.1). Pause and Dev are overlays, see <see cref="ScreenFlow"/>.</summary>
    public enum ScreenId
    {
        Title,
        Map,
        /// <summary>"Preparing level N" while the level is generated (TRD 12.3).</summary>
        Loading,
        Intro,
        Play,
        Result,
        Settings,
    }

    /// <summary>The two layouts a screen can use, chosen by aspect ratio (TRD 12.2): the root element gets <c>layout--wide</c> or <c>layout--tall</c>.</summary>
    public enum LayoutClass { Wide, Tall }

    public enum LevelRequestKind
    {
        /// <summary>Picked on the map.</summary>
        Select,
        /// <summary>"Retry" on the lose screen.</summary>
        Retry,
        /// <summary>"Next level" on the win screen.</summary>
        Next,
        /// <summary>Dev page "jump to level N": fresh unsaved seed, any level number.</summary>
        DevJump,
    }

    /// <summary>The flow asks for a level to be prepared. <see cref="GameController"/> answers with <see cref="ScreenFlow.LevelReady"/> or <see cref="ScreenFlow.LoadFailed"/>.</summary>
    public sealed record LevelRequest(int Level, LevelRequestKind Kind);

    /// <summary>
    /// The navigation state machine (PRD 2.2, 5.1; TRD 12.2, 13.5). Pure: no timers, no I/O, no Unity.
    /// <para>
    /// Pages: Title, Map, Loading, Intro, Play, Result, Settings. <b>Pause</b> (during Play) and <b>Dev</b>
    /// (dev builds only) are overlays: <see cref="Paused"/> and <see cref="DevOpen"/>.
    /// </para>
    /// <code>
    /// Title --Play--> Map --SelectLevel(n)--> Loading --LevelReady--> Intro --StartLevel--> Play --LevelEnded--> Result
    ///   |  \--OpenSettings--> Settings                LoadFailed -> Map                    Result --Next/Retry--> Loading
    ///   \--Quit / Back on Android = LeaveAppRequested                       Result, Intro, Pause --ToMap--> Map
    /// </code>
    /// <para>
    /// Every command returns true if it was accepted in the current state, and does nothing (returns
    /// false) otherwise, so a stale button press can never corrupt the flow. State is complete before
    /// any event is raised, so a handler (for example the controller completing a synchronous job) may
    /// call back into the flow.
    /// </para>
    /// <para><b>BackPressed</b> (Android Back, Esc): closes Dev if open; Play: pause, or resume if already
    /// paused; Loading, Intro and Result go to Map, Map to Title, Settings to Title; Title:
    /// <see cref="LeaveAppRequested"/> on Android, nothing on desktop (desktop has a Quit button instead,
    /// <see cref="QuitAvailable"/>).</para>
    /// </summary>
    public sealed class ScreenFlow
    {
        readonly HostInfo _host;
        readonly Func<int, bool> _isUnlocked;

        /// <param name="isUnlocked">Which level numbers the map may start (progress rule); null allows all.</param>
        public ScreenFlow(HostInfo host, Func<int, bool>? isUnlocked = null)
        {
            _host = host;
            _isUnlocked = isUnlocked ?? (_ => true);
        }

        // -- state ----------------------------------------------------------------------------

        /// <summary>The page shown now. The app starts on <see cref="ScreenId.Title"/>.</summary>
        public ScreenId Current { get; private set; } = ScreenId.Title;

        /// <summary>The level of Loading, Intro, Play and Result.</summary>
        public int LevelNumber { get; private set; }

        /// <summary>On Result: true if the level was won.</summary>
        public bool Won { get; private set; }

        /// <summary>The pause overlay is shown (only during Play).</summary>
        public bool Paused { get; private set; }

        /// <summary>The dev overlay is shown (only in dev builds).</summary>
        public bool DevOpen { get; private set; }

        /// <summary>True when board input should be accepted: Play, not paused, no dev overlay.</summary>
        public bool InputEnabled => Current == ScreenId.Play && !Paused && !DevOpen;

        /// <summary>Desktop shows a Quit button on the title; Android does not (the system Back leaves the app).</summary>
        public bool QuitAvailable => _host.Kind != PlatformKind.Android;

        /// <summary>Dev builds only (<see cref="HostInfo.Dev"/>).</summary>
        public bool DevAvailable => _host.Dev;

        // -- layout ---------------------------------------------------------------------------

        public const string WideCssClass = "layout--wide";
        public const string TallCssClass = "layout--tall";

        /// <summary>The layout for the last <see cref="SetScreen(int,int)"/>. Wide until a size is known.</summary>
        public LayoutClass Layout { get; private set; } = LayoutClass.Wide;

        /// <summary>The USS class for the current layout.</summary>
        public string LayoutCssClass => Layout == LayoutClass.Wide ? WideCssClass : TallCssClass;

        /// <summary>
        /// The layout closest to the screen's shape. The wide layout is drawn for landscape and the tall one
        /// for portrait, so "closest to 1:1 wins" reduces to: wider than tall is Wide; taller than wide is Tall;
        /// an exactly square screen (or an unknown size) is a tie and picks Tall.
        /// </summary>
        public static LayoutClass LayoutFor(int width, int height) =>
            width > 0 && height > 0 && width > height ? LayoutClass.Wide : LayoutClass.Tall;

        /// <summary>Call on every screen size change (rotation, resize, split-screen).</summary>
        public void SetScreen(int width, int height)
        {
            var layout = LayoutFor(width, height);
            if (layout == Layout) return;
            Layout = layout;
            LayoutChanged?.Invoke(layout);
            Changed?.Invoke();
        }

        public void SetScreen(ScreenInfo info) => SetScreen(info.Width, info.Height);

        // -- events ---------------------------------------------------------------------------

        /// <summary>The page changed: (from, to). Not raised for overlays.</summary>
        public event Action<ScreenId, ScreenId>? ScreenChanged;

        /// <summary>The layout class changed.</summary>
        public event Action<LayoutClass>? LayoutChanged;

        /// <summary>Anything changed: page, pause, dev overlay or layout. Raised after the specific event.</summary>
        public event Action? Changed;

        /// <summary>The player wants to leave the app. The composition root quits (Android: finish the activity).</summary>
        public event Action? LeaveAppRequested;

        /// <summary>A level must be prepared. <see cref="GameController"/> subscribes.</summary>
        public event Action<LevelRequest>? LevelRequested;

        // -- commands -------------------------------------------------------------------------

        /// <summary>Title: "Play" / "Continue" opens the map.</summary>
        public bool Play() => Current == ScreenId.Title && Go(ScreenId.Map);

        /// <summary>Title: opens the settings.</summary>
        public bool OpenSettings() => Current == ScreenId.Title && Go(ScreenId.Settings);

        /// <summary>Title, desktop only: leave the app.</summary>
        public bool Quit()
        {
            if (Current != ScreenId.Title || !QuitAvailable) return false;
            LeaveAppRequested?.Invoke();
            return true;
        }

        /// <summary>The system Back or Esc (see the class remarks). Returns true if it did something.</summary>
        public bool BackPressed()
        {
            if (DevOpen) return CloseDev();
            switch (Current)
            {
                case ScreenId.Title:
                    if (_host.Kind != PlatformKind.Android) return false;
                    LeaveAppRequested?.Invoke();
                    return true;
                case ScreenId.Map: return Go(ScreenId.Title);
                case ScreenId.Loading:
                case ScreenId.Intro:
                case ScreenId.Result: return Go(ScreenId.Map);
                case ScreenId.Settings: return Go(ScreenId.Title);
                case ScreenId.Play: return SetPaused(!Paused);
                default: return false;
            }
        }

        /// <summary>Map: start preparing level <paramref name="n"/> if the player may play it.</summary>
        public bool SelectLevel(int n)
        {
            if (Current != ScreenId.Map || n < 1 || !_isUnlocked(n)) return false;
            return BeginLoading(n, LevelRequestKind.Select);
        }

        /// <summary>Loading: the level is ready; show its intro. Called by the controller.</summary>
        public bool LevelReady() => Current == ScreenId.Loading && Go(ScreenId.Intro);

        /// <summary>Loading: generation failed; back to the map. Called by the controller.</summary>
        public bool LoadFailed() => Current == ScreenId.Loading && Go(ScreenId.Map);

        /// <summary>Intro: "Play" starts the level.</summary>
        public bool StartLevel() => Current == ScreenId.Intro && Go(ScreenId.Play);

        /// <summary>Play: show the pause overlay.</summary>
        public bool Pause() => SetPaused(true);

        /// <summary>Play: hide the pause overlay.</summary>
        public bool Resume() => SetPaused(false);

        /// <summary>Play: pause if running, resume if paused (the pause button and Esc).</summary>
        public bool TogglePause() => SetPaused(!Paused);

        /// <summary>Play: the level is over; show the result. Called by the controller once the board has settled.</summary>
        public bool LevelEnded(bool won)
        {
            if (Current != ScreenId.Play) return false;
            Won = won;
            return Go(ScreenId.Result);
        }

        /// <summary>Result after a loss: play the same level again.</summary>
        public bool Retry() => Current == ScreenId.Result && !Won && BeginLoading(LevelNumber, LevelRequestKind.Retry);

        /// <summary>Result after a win: the next level.</summary>
        public bool Next() => Current == ScreenId.Result && Won && BeginLoading(LevelNumber + 1, LevelRequestKind.Next);

        /// <summary>Loading (cancel), Intro, Result, or the pause overlay ("Quit to map"): back to the map.</summary>
        public bool ToMap()
        {
            bool ok = Current == ScreenId.Intro || Current == ScreenId.Result || Current == ScreenId.Loading
                || (Current == ScreenId.Play && Paused);
            return ok && Go(ScreenId.Map);
        }

        /// <summary>Map or Settings: back to the title.</summary>
        public bool ToTitle() => (Current == ScreenId.Map || Current == ScreenId.Settings) && Go(ScreenId.Title);

        /// <summary>Opens the dev overlay (dev builds only).</summary>
        public bool OpenDev()
        {
            if (!_host.Dev || DevOpen) return false;
            DevOpen = true;
            Changed?.Invoke();
            return true;
        }

        public bool CloseDev()
        {
            if (!DevOpen) return false;
            DevOpen = false;
            Changed?.Invoke();
            return true;
        }

        /// <summary>F1 / three-finger tap.</summary>
        public bool ToggleDev() => DevOpen ? CloseDev() : OpenDev();

        /// <summary>
        /// Dev page "jump to level N" (dev builds only): closes the overlay, leaves whatever page is up and
        /// prepares level <paramref name="n"/> (any number from 1, no unlock check) with a fresh unsaved seed.
        /// </summary>
        public bool DevJump(int n)
        {
            if (!_host.Dev || n < 1) return false;
            DevOpen = false;
            return BeginLoading(n, LevelRequestKind.DevJump);
        }

        // -- internals ------------------------------------------------------------------------

        bool SetPaused(bool paused)
        {
            if (Current != ScreenId.Play || Paused == paused) return false;
            Paused = paused;
            Changed?.Invoke();
            return true;
        }

        bool BeginLoading(int n, LevelRequestKind kind)
        {
            LevelNumber = n;
            Go(ScreenId.Loading);
            LevelRequested?.Invoke(new LevelRequest(n, kind));
            return true;
        }

        bool Go(ScreenId to)
        {
            var from = Current;
            Current = to;
            Paused = false;
            ScreenChanged?.Invoke(from, to);
            Changed?.Invoke();
            return true;
        }
    }
}
