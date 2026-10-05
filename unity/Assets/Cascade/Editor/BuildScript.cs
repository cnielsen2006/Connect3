using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using Debug = UnityEngine.Debug;

namespace Cascade.Editor
{
    /// <summary>
    /// The one way to build (TRD §13.1):
    /// Unity -batchmode -nographics -quit -projectPath unity -buildTarget Android
    ///   -executeMethod Cascade.Editor.BuildScript.BuildAndroid [-dev] [-install] -logFile Builds/build.log
    /// Sets every setting itself, so a fresh checkout builds the same on any machine.
    /// </summary>
    public static class BuildScript
    {
        const string ProductName = "Cascade";
        const string Company = "phoenix";
        const string ApplicationId = "com.phoenix.cascade";
        const string Version = "0.1.0";

        // Google Play's current target API requirement; update here when it changes.
        const int TargetSdk = 36;
        const int MinSdk = 29;

        static string RepoRoot => Directory.GetParent(Directory.GetCurrentDirectory())!.FullName;
        static string BuildsDir => Path.Combine(RepoRoot, "Builds");

        static bool HasFlag(string flag) => Environment.GetCommandLineArgs().Contains(flag);

        [MenuItem("Cascade/Build Android (dev)")]
        public static void BuildAndroidDevMenu() => BuildAndroidCore(dev: true, install: false);

        /// <summary>
        /// Builds the dev APK inside the running editor (for <c>.vscode/android.ps1</c> via <see cref="EditorCommands"/>) and records the
        /// outcome in <c>Library/cascade-build.txt</c> ("ok" or "failed") for the calling script to read.
        /// </summary>
        internal static void BuildAndroidDevForTool()
        {
            var ok = false;
            try { ok = BuildAndroidCore(dev: true, install: false); }
            catch (Exception e) { Debug.LogException(e); }
            File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "Library", "cascade-build.txt"), ok ? "ok" : "failed");
        }

        public static void BuildAndroid() => Exit(BuildAndroidCore(HasFlag("-dev"), HasFlag("-install")));

        public static void BuildWindows() =>
            Exit(BuildDesktop(BuildTarget.StandaloneWindows64, "Windows", "cascade.exe", HasFlag("-dev")));

        static void Exit(bool ok)
        {
            if (UnityEngine.Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        static void ApplyCommonSettings(bool dev)
        {
            ProjectSetup.Apply();
            PlayerSettings.productName = ProductName;
            PlayerSettings.companyName = Company;
            PlayerSettings.bundleVersion = Version;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            EditorUserBuildSettings.development = dev;
        }

        static bool BuildAndroidCore(bool dev, bool install)
        {
            ApplyCommonSettings(dev);
            var target = NamedBuildTarget.Android;
            PlayerSettings.SetApplicationIdentifier(target, dev ? ApplicationId + ".dev" : ApplicationId);
            PlayerSettings.SetScriptingBackend(target, ScriptingImplementation.IL2CPP);
            // Unity 6000.6's Android player has no x86_64 libraries; the emulator runs arm64 through
            // the system image's ARM translation (API 30+).
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            EditorUserBuildSettings.buildAppBundle = false;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,
                new[] { UnityEngine.Rendering.GraphicsDeviceType.Vulkan, UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.Android.optimizedFramePacing = true;
            PlayerSettings.SetManagedStrippingLevel(target, ManagedStrippingLevel.Medium);
            PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)MinSdk;
            PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)TargetSdk;

            bool release = ApplyKeystoreFromEnvironment();
            try
            {
                var apk = Path.Combine(BuildsDir, "Android",
                    $"cascade-{Version}-{GitShortHash()}{(dev ? "-dev" : "")}.apk");
                Directory.CreateDirectory(Path.GetDirectoryName(apk)!);
                var ok = Build(BuildTarget.Android, apk, dev);
                if (ok) Debug.Log($"[Cascade] APK: {apk} ({(release ? "keystore from environment" : "Unity debug keystore")})");
                if (ok && install) ok = AdbInstall(apk);
                return ok;
            }
            finally
            {
                // Credentials live in memory only (TRD §13.1): never leave them in ProjectSettings.
                PlayerSettings.Android.useCustomKeystore = false;
                PlayerSettings.Android.keystoreName = "";
                PlayerSettings.Android.keystorePass = "";
                PlayerSettings.Android.keyaliasName = "";
                PlayerSettings.Android.keyaliasPass = "";
            }
        }

        static bool BuildDesktop(BuildTarget buildTarget, string folder, string exeName, bool dev)
        {
            ApplyCommonSettings(dev);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.IL2CPP);
            var exe = Path.Combine(BuildsDir, folder, exeName);
            Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
            return Build(buildTarget, exe, dev);
        }

        static bool Build(BuildTarget buildTarget, string location, bool dev)
        {
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ProjectSetup.BootScenePath },
                locationPathName = location,
                target = buildTarget,
                options = dev ? BuildOptions.Development : BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"[Cascade] Build {report.summary.result}: {report.summary.totalSize / 1024 / 1024} MB, {report.summary.totalTime}");
            return report.summary.result == BuildResult.Succeeded;
        }

        static bool ApplyKeystoreFromEnvironment()
        {
            var path = Environment.GetEnvironmentVariable("CASCADE_KEYSTORE");
            if (string.IsNullOrEmpty(path)) return false;
            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = path;
            PlayerSettings.Android.keystorePass = Environment.GetEnvironmentVariable("CASCADE_KEYSTORE_PASS") ?? "";
            PlayerSettings.Android.keyaliasName = Environment.GetEnvironmentVariable("CASCADE_KEY_ALIAS") ?? "";
            PlayerSettings.Android.keyaliasPass = Environment.GetEnvironmentVariable("CASCADE_KEY_PASS") ?? "";
            return true;
        }

        static string GitShortHash()
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo("git", "rev-parse --short HEAD")
                {
                    WorkingDirectory = RepoRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                });
                var hash = p!.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit();
                return p.ExitCode == 0 && hash.Length > 0 ? hash : "nogit";
            }
            catch (Exception)
            {
                return "nogit";
            }
        }

        static bool AdbInstall(string apk)
        {
            var sdk = Environment.GetEnvironmentVariable("ANDROID_HOME");
            var adb = !string.IsNullOrEmpty(sdk)
                ? Path.Combine(sdk, "platform-tools", "adb.exe")
                : Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines", "AndroidPlayer", "SDK", "platform-tools", "adb.exe");
            var p = Process.Start(new ProcessStartInfo(adb, $"install -r \"{apk}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            Debug.Log("[Cascade] " + p!.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd());
            p.WaitForExit();
            return p.ExitCode == 0;
        }
    }
}
