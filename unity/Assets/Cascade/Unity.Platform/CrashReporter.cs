using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Cascade.Unity.Platform
{
    /// <summary>
    /// Captures every error and exception Unity logs (the player's own overlay is tiny and unreadable on a phone):
    /// it keeps them in <c>cascade-errors.txt</c> in the user data folder, and in development builds draws the latest ones
    /// as large text over everything (IMGUI, so it still works if UI Toolkit never came up). Tap the overlay to dismiss it.
    /// Pull the file with <c>adb shell run-as com.phoenix.cascade.dev cat files/cascade-errors.txt</c> (or from
    /// <c>Android/data/&lt;id&gt;/files</c> on the device).
    /// </summary>
    public sealed class CrashReporter : MonoBehaviour
    {
        const int MaxKept = 6;
        const int MaxChars = 1800;

        readonly object gate = new object();
        readonly string[] recent = new string[MaxKept];
        int count, total;
        string? path;
        bool dismissed;
        GUIStyle? style;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            var go = new GameObject("CrashReporter") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            go.AddComponent<CrashReporter>();
        }

        void Awake()
        {
            try { path = Path.Combine(Application.persistentDataPath, "cascade-errors.txt"); File.WriteAllText(path, "session " + DateTime.UtcNow.ToString("o") + "\n"); }
            catch (Exception) { path = null; }
            Application.logMessageReceivedThreaded += OnLog;
        }

        void OnDestroy() => Application.logMessageReceivedThreaded -= OnLog;

        void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error && type != LogType.Assert) return;
            var text = new StringBuilder().Append(type).Append(": ").Append(condition).Append('\n').Append(stackTrace).ToString();
            lock (gate)
            {
                // the same exception every frame is one entry, not a flood
                if (count > 0 && recent[(count - 1) % MaxKept] == text) { total++; return; }
                recent[count % MaxKept] = text;
                count++; total++;
                if (path != null)
                {
                    try { File.AppendAllText(path, "---- " + DateTime.UtcNow.ToString("o") + "\n" + text + "\n"); }
                    catch (Exception) { /* nothing sensible to do */ }
                }
            }
        }

        void OnGUI()
        {
            if (!Debug.isDebugBuild) return;
            string msg;
            int t;
            lock (gate)
            {
                t = total;
                if (count == 0) return;
                msg = recent[(count - 1) % MaxKept];
            }
            if (dismissed) return;
            if (msg.Length > MaxChars) msg = msg.Substring(0, MaxChars) + "...";

            float dpi = Screen.dpi > 0 ? Screen.dpi : 160f;
            int font = Mathf.RoundToInt(Mathf.Max(18f, dpi / 160f * 14f));
            style ??= new GUIStyle(GUI.skin.label) { wordWrap = true, alignment = TextAnchor.UpperLeft };
            style.fontSize = font;
            style.normal.textColor = Color.white;

            var area = new Rect(0, 0, Screen.width, Screen.height * 0.6f);
            var old = GUI.color;
            GUI.color = new Color(0.55f, 0.1f, 0.08f, 0.95f);
            GUI.DrawTexture(area, Texture2D.whiteTexture);
            GUI.color = old;
            GUI.Label(new Rect(font, font, area.width - 2 * font, area.height - 2 * font),
                $"ERROR x{t} (tap to dismiss)\n{msg}", style);
            var e = Event.current;
            if (e.type == EventType.MouseDown && area.Contains(e.mousePosition)) dismissed = true;
        }
    }
}
