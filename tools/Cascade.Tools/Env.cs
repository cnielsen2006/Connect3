using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Cascade.Config;
using Cascade.Gameplay;
using Cascade.Generation;
using Cascade.Platform.Abstractions;

namespace Cascade.Tools
{
    /// <summary>Wall-clock <see cref="IStopwatch"/> (the one place the tools read a clock).</summary>
    public sealed class SystemStopwatch : IStopwatch
    {
        readonly Stopwatch _watch = Stopwatch.StartNew();
        public double ElapsedMilliseconds => _watch.Elapsed.TotalMilliseconds;
    }

    /// <summary>Shipped data read from a folder.</summary>
    public sealed class FolderDataSource : IDataSource
    {
        readonly string _folder;
        public FolderDataSource(string folder) { _folder = folder; }

        public string ReadText(string key)
        {
            string path = Path.Combine(_folder, key);
            if (!File.Exists(path)) throw new KeyNotFoundException("No shipped data '" + key + "' in " + _folder);
            return File.ReadAllText(path);
        }
    }

    /// <summary>What every command needs: the dials, the profile and the snapshots built from them.</summary>
    public sealed class Env
    {
        public static readonly HostInfo Desktop = new HostInfo(PlatformKind.Desktop, true, true, false, true, 96f);

        Env(DialRegistry dials, Profile profile, string dataFolder, string repoRoot)
        {
            Dials = dials;
            Profile = profile;
            DataFolder = dataFolder;
            RepoRoot = repoRoot;
        }

        public DialRegistry Dials { get; }
        public Profile Profile { get; }
        public string DataFolder { get; }
        public string RepoRoot { get; }
        public Core.RuleSet Rules => RuleSets.From(Dials);
        public GeneratorSettings Settings => RuleSets.GeneratorFrom(Dials);

        /// <summary>The repository root: the folder above <c>unity/Assets/Cascade/Data</c>, found from the working directory or the executable.</summary>
        public static string FindRepoRoot()
        {
            foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
                for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
                    if (Directory.Exists(Path.Combine(dir.FullName, "unity", "Assets", "Cascade", "Data"))) return dir.FullName;
            throw new UsageException("cannot find the repository (unity/Assets/Cascade/Data); run from inside it or pass --data");
        }

        /// <summary>Fresh dials from the same data folder with every dial at its default except <c>budget_ms = 0</c> (how the fixtures were exported).</summary>
        public static DialRegistry LoadDefaultsWithoutBudget(Env env)
        {
            var dials = DialRegistry.Load(new FolderDataSource(env.DataFolder), Desktop);
            dials.Set("generator", "budget_ms", 0);
            return dials;
        }

        /// <summary>Loads dials and profile from <c>--data</c> (or the repository), then applies the dial options.</summary>
        public static Env Load(Args args)
        {
            string? data = args.Get("data");
            string root = data != null ? Path.GetFullPath(data) : FindRepoRoot();
            string folder = data != null ? root : Path.Combine(root, "unity", "Assets", "Cascade", "Data");
            var source = new FolderDataSource(folder);
            var dials = DialRegistry.Load(source, Desktop);
            var profile = Profile.Load(source);

            if (args.Has("no-budget")) dials.Set("generator", "budget_ms", 0);
            else if (args.Get("budget-ms") != null) dials.Set("generator", "budget_ms", args.Int("budget-ms", 0));
            foreach (var setting in args.GetAll("set"))
            {
                int eq = setting.IndexOf('=');
                int dot = setting.IndexOf('.');
                if (eq < 0 || dot < 0 || dot > eq) throw new UsageException("--set expects section.key=value, got '" + setting + "'");
                string section = setting.Substring(0, dot), key = setting.Substring(dot + 1, eq - dot - 1), raw = setting.Substring(eq + 1);
                try
                {
                    var meta = dials.GetMeta(section, key);
                    if (meta.Type == DialType.Int) dials.Set(section, key, int.Parse(raw, CultureInfo.InvariantCulture));
                    else dials.Set(section, key, double.Parse(raw, CultureInfo.InvariantCulture));
                }
                catch (Exception e) when (e is DialException || e is FormatException || e is OverflowException)
                {
                    throw new UsageException("--set " + setting + ": " + e.Message);
                }
            }
            return new Env(dials, profile, folder, args.Get("data") != null ? root : root);
        }
    }
}
