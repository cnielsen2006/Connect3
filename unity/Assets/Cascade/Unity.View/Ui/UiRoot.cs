using System;
using System.Collections.Generic;
using Cascade.Platform.Abstractions;
using Cascade.Presentation.Shell;
using UnityEngine.UIElements;

namespace Cascade.Unity.View
{
    /// <summary>Every view-model the screens bind to, built by the composition root.</summary>
    public sealed class ShellViewModels
    {
        public TitleViewModel Title = null!;
        public MapViewModel Map = null!;
        public LoadingViewModel Loading = null!;
        public IntroViewModel Intro = null!;
        public HudViewModel Hud = null!;
        public PauseViewModel Pause = null!;
        public ResultViewModel Result = null!;
        public SettingsViewModel Settings = null!;
        public DevViewModel Dev = null!;
    }

    /// <summary>
    /// The screen router of the game (TRD 12.2): it owns the one UI document, builds the binder of every page and shows exactly the page
    /// <see cref="ScreenFlow"/> says (plus the pause overlay and the dev overlay), with a short fade. It also puts the layout class
    /// (<c>layout--wide</c> or <c>layout--tall</c>, from the flow) on the root and insets the <c>safe</c> element by the platform's safe
    /// area, converted from screen pixels to panel units. It decides nothing about the game: every state it reads is the flow's.
    /// </summary>
    public sealed class UiRoot : IDisposable
    {
        const string ShownClass = "screen--shown";
        const long FadeFallbackMs = 220;
        const long FrameMs = 30;

        readonly VisualElement documentRoot, shell, safe;
        readonly PanelSettings? panelSettings;
        readonly ScreenFlow flow;
        readonly ShellViewModels models;
        readonly Dictionary<ScreenId, VisualElement> pages = new Dictionary<ScreenId, VisualElement>();
        readonly Dictionary<ScreenId, IScreenView?> views = new Dictionary<ScreenId, IScreenView?>();
        readonly Dictionary<VisualElement, bool> wanted = new Dictionary<VisualElement, bool>();
        readonly Dictionary<VisualElement, IScreenView?> overlayViews = new Dictionary<VisualElement, IScreenView?>();
        ScreenInfo screen;
        bool haveScreen;
        float insetLeft, insetTop, insetRight, insetBottom;

        public TitleScreen Title { get; }
        public MapScreen Map { get; }
        public LoadingScreen Loading { get; }
        public IntroScreen Intro { get; }
        public HudScreen Hud { get; }
        public ResultScreen Result { get; }
        public SettingsScreen Settings { get; }
        public TouchSizing Sizing { get; }
#if UNITY_EDITOR || DEVELOPMENT_BUILD || CASCADE_DEV
        public DevScreen? Dev { get; }
#endif

        public UiRoot(VisualElement documentRootElement, ScreenFlow screenFlow, ShellViewModels viewModels, string gameTitle, TouchSizing touchSizing, PanelSettings? panel = null)
        {
            panelSettings = panel;
            documentRoot = documentRootElement;
            flow = screenFlow;
            models = viewModels;
            Sizing = touchSizing;
            shell = Ui.Get<VisualElement>(documentRoot, "shell");
            safe = Ui.Get<VisualElement>(documentRoot, "safe");

            Title = new TitleScreen(Page("screen-title", ScreenId.Title), models.Title);
            Map = new MapScreen(Page("screen-map", ScreenId.Map), models.Map, touchSizing);
            Loading = new LoadingScreen(Page("screen-loading", ScreenId.Loading), models.Loading);
            Intro = new IntroScreen(Page("screen-intro", ScreenId.Intro), models.Intro, touchSizing);
            Hud = new HudScreen(Page("screen-play", ScreenId.Play), models.Hud, models.Pause, gameTitle);
            Result = new ResultScreen(Page("screen-result", ScreenId.Result), models.Result);
            Settings = new SettingsScreen(Page("screen-settings", ScreenId.Settings), models.Settings);
            views[ScreenId.Map] = Map;
            views[ScreenId.Loading] = Loading;
            views[ScreenId.Result] = Result;
            foreach (var id in pages.Keys) if (!views.ContainsKey(id)) views[id] = null;

            var devOverlay = Ui.Get<VisualElement>(documentRoot, "overlay-dev");
#if UNITY_EDITOR || DEVELOPMENT_BUILD || CASCADE_DEV
            Dev = models.Dev.Enabled ? new DevScreen(devOverlay, models.Dev, touchSizing) : null;
            DevOverlay = Dev != null ? devOverlay : null;
            if (Dev == null) devOverlay.RemoveFromHierarchy();
#else
            devOverlay.RemoveFromHierarchy(); // a release build has no dev page at all
            DevOverlay = null;
#endif
            overlayViews[Hud.PauseOverlay] = null;

            flow.Changed += Sync;
            documentRoot.RegisterCallback<GeometryChangedEvent>(_ => ApplyInsets());
            Sync();
        }

        VisualElement Page(string name, ScreenId id)
        {
            var page = Ui.Get<VisualElement>(documentRoot, name);
            pages[id] = page;
            return page;
        }

        /// <summary>Re-colours the HUD for a skin: adds <c>skin-&lt;id&gt;</c> to the shell (no class for K; see Theme.uss).</summary>
        public void SetSkin(string skinId)
        {
            foreach (var id in new[] { "i", "j" }) shell.EnableInClassList("skin-" + id, id == skinId.ToLowerInvariant());
        }

        /// <summary>The root element that carries the layout class.</summary>
        public VisualElement Shell => shell;

        /// <summary>The element that is inset by the safe area; every page lives inside it.</summary>
        public VisualElement Safe => safe;

        /// <summary>The dev overlay element, or null in a release build.</summary>
        public VisualElement? DevOverlay { get; }

        /// <summary>The page element of a screen (tests read its state).</summary>
        public VisualElement PageOf(ScreenId id) => pages[id];

        /// <summary>The page the router is showing (the flow's page).</summary>
        public ScreenId VisibleScreen => flow.Current;

        public bool IsPageShown(ScreenId id) => wanted.TryGetValue(pages[id], out var w) && w;
        public bool IsPauseShown => wanted.TryGetValue(Hud.PauseOverlay, out var w) && w;
        public bool IsDevShown => DevOverlay != null && wanted.TryGetValue(DevOverlay, out var w) && w;

        /// <summary>The safe-area insets in panel units as last applied (left, top, right, bottom).</summary>
        public (float Left, float Top, float Right, float Bottom) Insets => (insetLeft, insetTop, insetRight, insetBottom);

        // ---- showing ----

        void Sync()
        {
            foreach (var kv in pages) SetShown(kv.Value, kv.Key == flow.Current, views[kv.Key]);
            SetShown(Hud.PauseOverlay, flow.Current == ScreenId.Play && flow.Paused, null);
            if (DevOverlay != null) SetShown(DevOverlay, flow.DevOpen, null);
            bool wide = flow.Layout == LayoutClass.Wide;
            shell.EnableInClassList(ScreenFlow.WideCssClass, wide);
            shell.EnableInClassList(ScreenFlow.TallCssClass, !wide);
            // wide layouts are designed 1080 units tall, tall ones 1080 units wide (the panel scales by height or by width)
            if (panelSettings != null) panelSettings.match = wide ? 1f : 0f;
        }

        void SetShown(VisualElement el, bool shown, IScreenView? view)
        {
            bool known = wanted.TryGetValue(el, out var was);
            if (known && was == shown) return;
            wanted[el] = shown;
            if (!known && !shown) return; // hidden from the start (display: none in USS)
            if (shown)
            {
                el.style.display = DisplayStyle.Flex;
                el.pickingMode = PickingMode.Position;
                view?.OnShown();
                if (DevOverlay != null && el == DevOverlay) OnDevShown();
                // one frame later, so the fade runs from the hidden state
                el.schedule.Execute(() => { if (wanted[el]) el.AddToClassList(el.ClassListContains("overlay") ? "overlay--shown" : ShownClass); }).ExecuteLater(FrameMs);
            }
            else
            {
                el.RemoveFromClassList(ShownClass);
                el.RemoveFromClassList("overlay--shown");
                el.pickingMode = PickingMode.Ignore;
                long ms = FadeFallbackMs;
                foreach (var t in el.resolvedStyle.transitionDuration) { ms = (long)(t.value * (t.unit == TimeUnit.Second ? 1000f : 1f)); break; }
                el.schedule.Execute(() =>
                {
                    if (wanted[el]) return;
                    el.style.display = DisplayStyle.None;
                    view?.OnHidden();
                }).ExecuteLater(ms + FrameMs);
            }
        }

        void OnDevShown()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || CASCADE_DEV
            Dev?.OnShown();
#endif
        }

        // ---- screen: layout class is the flow's; the safe area and the touch size are applied here ----

        /// <summary>Applies a new screen size and safe area (call from <see cref="IAppLifecycle.ScreenChanged"/>, after <see cref="ScreenFlow.SetScreen(ScreenInfo)"/>).</summary>
        public void ApplyScreen(ScreenInfo info)
        {
            screen = info;
            haveScreen = true;
            Sync();
            ApplyInsets();
        }

        void ApplyInsets()
        {
            if (!haveScreen || screen.Width <= 0) return;
            float panelWidth = documentRoot.layout.width;
            if (!(panelWidth > 0f)) return; // not laid out yet: the geometry callback calls again
            float k = panelWidth / screen.Width; // panel units per screen pixel
            insetLeft = screen.SafeArea.Left * k;
            insetTop = screen.SafeArea.Top * k;
            insetRight = screen.SafeArea.Right * k;
            insetBottom = screen.SafeArea.Bottom * k;
            safe.style.left = insetLeft;
            safe.style.top = insetTop;
            safe.style.right = insetRight;
            safe.style.bottom = insetBottom;
            // full-bleed backdrops reach over the unsafe margins
            Hud.PauseScrim.style.left = -insetLeft;
            Hud.PauseScrim.style.top = -insetTop;
            Hud.PauseScrim.style.right = -insetRight;
            Hud.PauseScrim.style.bottom = -insetBottom;
            Sizing.Update(shell, panelWidth, screen.Width);
        }

        public void Dispose()
        {
            flow.Changed -= Sync;
            Title.Dispose();
            Map.Dispose();
            Loading.Dispose();
            Intro.Dispose();
            Hud.Dispose();
            Result.Dispose();
            Settings.Dispose();
#if UNITY_EDITOR || DEVELOPMENT_BUILD || CASCADE_DEV
            Dev?.Dispose();
#endif
        }
    }
}
