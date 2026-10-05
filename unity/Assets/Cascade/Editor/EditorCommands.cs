using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Cascade.Editor
{
    /// <summary>
    /// Lets a tool outside the editor (the VS Code launch configs, <c>.vscode/unity.ps1</c>) drive a running editor: write one word
    /// (<c>play</c>, <c>stop</c>, <c>pause</c>) to <c>unity/Library/cascade-command.txt</c> and the editor does it. Unity has no external event
    /// API, so the editor polls the file. It lives in Library (not Temp) so a command written before the editor starts survives the launch
    /// and runs once the editor has finished loading. The file is deleted when read.
    /// </summary>
    [InitializeOnLoad]
    public static class EditorCommands
    {
        const string BootScene = "Assets/Cascade/Scenes/Boot.unity";
        static readonly string CommandPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library", "cascade-command.txt"));
        static double nextPoll;

        static EditorCommands() => EditorApplication.update += Poll;

        static void Poll()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now < nextPoll) return;
            nextPoll = now + 0.5;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode != EditorApplication.isPlaying) return;
            if (!File.Exists(CommandPath)) return;

            string command;
            try { command = File.ReadAllText(CommandPath).Trim().ToLowerInvariant(); File.Delete(CommandPath); }
            catch (IOException) { return; } // being written: try again at the next poll
            try { Run(command); }
            catch (System.Exception e) { Log("command \"" + command + "\" failed: " + e); }
        }

        /// <summary>Writes to the Console and to <c>Library/cascade-command.log</c> (so a tool outside the editor can see what happened).</summary>
        static void Log(string message)
        {
            Debug.Log("[Cascade] " + message);
            try { File.AppendAllText(Path.ChangeExtension(CommandPath, ".log"), System.DateTime.Now.ToString("HH:mm:ss ") + message + "\n"); }
            catch (IOException) { }
        }

        /// <summary>A build cannot start while in (or leaving) play mode, so wait for edit mode first.</summary>
        static void BuildWhenEdit()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            EditorApplication.update -= BuildWhenEdit;
            BuildScript.BuildAndroidDevForTool();
        }

        static void Run(string command)
        {
            switch (command)
            {
                case "play":
                    if (EditorApplication.isPlaying) break;
                    if (EditorSceneManager.GetActiveScene().path != BootScene && !EditorSceneManager.GetActiveScene().isDirty)
                        EditorSceneManager.OpenScene(BootScene, OpenSceneMode.Single);
                    Log("entering play mode in " + EditorSceneManager.GetActiveScene().path);
                    EditorApplication.delayCall += EditorApplication.EnterPlaymode;
                    return;
                case "build-android":
                    Log("building the Android dev APK");
                    if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
                    EditorApplication.update += BuildWhenEdit;
                    return;
                case "build-skins":
                    if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
                    Log("building the K, I and J art styles");
                    EditorApplication.delayCall += () =>
                    {
                        try { PebbleMeshBuilder.BuildAll(); Log("skins built"); }
                        catch (System.Exception e) { Log("skin build failed: " + e); }
                    };
                    return;
                case "stop":
                    EditorApplication.ExitPlaymode();
                    break;
                case "pause":
                    EditorApplication.isPaused = !EditorApplication.isPaused;
                    break;
                default:
                    Log("unknown editor command \"" + command + "\"");
                    return;
            }
            Log("editor command: " + command);
        }
    }
}
