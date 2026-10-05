using System;
using System.IO;
using Cascade.Platform.Abstractions;
using UnityEngine;

namespace Cascade.App
{
    /// <summary>
    /// How this run starts: the command line plus a test seam. A player never sets any of it.
    /// <list type="bullet">
    /// <item><c>-level N</c>: skip the menus and play level N (the map's unlock rule is waived for it). <c>-seed S</c>: the seed of that level.
    /// <c>-autoplay K</c>: play K suggested moves, check the view against the session after each, log the result and quit (a self-test of a build).</item>
    /// <item><c>-screen title|map|settings|dev</c>: open on that page (screenshots; <c>dev</c> only in a dev build).</item>
    /// <item><c>-datadir PATH</c>: keep saves and dev overrides there instead of the user data folder. Runs that use <c>-level</c> or
    /// <c>-autoplay</c> without it use a scratch folder that starts empty, so a self-test never touches a real save.</item>
    /// <item><c>-shot PATH</c> (and <c>-shotdelay SECONDS</c>, default 3): write a screenshot of the real window and quit.</item>
    /// </list>
    /// </summary>
    public sealed class BootOptions
    {
        public string? DataDir;
        public int? Level;
        public ulong? Seed;
        public int AutoplayMoves;
        public string? Screen;
        public string? ShotPath;
        public float ShotDelaySeconds = 3f;

        /// <summary>Changes what the host reports (tests: pretend to be an Android phone with touch).</summary>
        public Func<HostInfo, HostInfo>? HostOverride;

        /// <summary>Wraps the job runner that generates levels (tests hold a job to see the Loading page).</summary>
        public Func<Cascade.Presentation.Shell.IJobRunner, Cascade.Presentation.Shell.IJobRunner>? JobRunnerWrap;

        /// <summary>True when the run goes straight into a level.</summary>
        public bool SkipMenus => Level.HasValue;

        /// <summary><c>-showfps</c>: the frame-rate readout is on from the start (dev builds only).</summary>
        public bool ShowFps { get; set; }

        /// <summary>True when a run is a self-test or screenshot run that must not touch the player's own save.</summary>
        public bool UsesScratchData => DataDir == null && (Level.HasValue || AutoplayMoves > 0);

        /// <summary>
        /// Options for the next <see cref="AppBootstrap"/> that starts (it takes and clears them), so a test can choose the data folder
        /// before the Boot scene loads. Test seam only; the app never sets it.
        /// </summary>
        public static BootOptions? Pending { get; set; }

        public static BootOptions Take()
        {
            var options = Pending ?? FromArgs(Environment.GetCommandLineArgs());
            Pending = null;
            return options;
        }

        public static BootOptions FromArgs(string[] args)
        {
            var o = new BootOptions();
            for (int i = 0; i < args.Length - 1; i++)
            {
                string next = args[i + 1];
                switch (args[i])
                {
                    case "-level": if (int.TryParse(next, out int n) && n > 0) o.Level = n; break;
                    case "-seed": if (ulong.TryParse(next, out ulong s)) o.Seed = s; break;
                    case "-autoplay": if (int.TryParse(next, out int k) && k > 0) o.AutoplayMoves = k; break;
                    case "-screen": o.Screen = next.ToLowerInvariant(); break;
                    case "-datadir": o.DataDir = next; break;
                    case "-shot": o.ShotPath = next; break;
                    case "-shotdelay": if (float.TryParse(next, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float d)) o.ShotDelaySeconds = d; break;
                }
            }
            o.ShowFps = Array.IndexOf(args, "-showfps") >= 0;
            if (o.AutoplayMoves > 0 && !o.Level.HasValue) o.Level = 1;
            return o;
        }

        /// <summary>The folder for saves: <c>-datadir</c>, a fresh scratch folder for self-test runs, or the user data folder.</summary>
        public string ResolveDataDir()
        {
            if (DataDir != null) return DataDir;
            if (!UsesScratchData) return Application.persistentDataPath;
            string scratch = Path.Combine(Application.temporaryCachePath, "cascade-selftest");
            try { if (Directory.Exists(scratch)) Directory.Delete(scratch, true); }
            catch (IOException) { /* in use: keep what is there */ }
            return scratch;
        }
    }
}
