#if UNITY_EDITOR || DEVELOPMENT_BUILD || CASCADE_DEV
// Dev builds only (TRD 13.9): measures the frame rate that the U6/U7 performance checks are about.
using UnityEngine;
using UnityEngine.UIElements;

namespace Cascade.Unity.View
{
    /// <summary>
    /// A small frame-rate readout in the bottom-right corner, off unless asked for (<c>-showfps</c> on the command line, or the "Show FPS"
    /// switch on the dev page). It measures real frames (unscaled delta time), shows the average rate and the slowest frame of the last
    /// second, and writes the same numbers to the log every few seconds (<c>[fps]</c>, so <c>adb logcat -s Unity</c> shows them on a
    /// device with no screen capture). It draws nothing and changes nothing in the game.
    /// </summary>
    public sealed class FpsCounter : MonoBehaviour
    {
        [SerializeField, Tooltip("Seconds of frames behind each readout.")] float windowSeconds = 1f;
        [SerializeField, Tooltip("Seconds between [fps] log lines while visible.")] float logSeconds = 5f;

        Label? label;
        bool visible;
        float windowTime, windowWorst, logTime, logWorst;
        int windowFrames, logFrames;
        float logMin = float.MaxValue;

        /// <summary>The counter that exists in this run (null when none was created).</summary>
        public static FpsCounter? Instance { get; private set; }

        public bool Visible
        {
            get => visible;
            set
            {
                visible = value;
                if (label != null) label.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
                windowTime = windowWorst = logTime = logWorst = 0f;
                windowFrames = logFrames = 0;
                logMin = float.MaxValue;
            }
        }

        /// <summary>Last readout, e.g. "59.8 fps, worst 17.9 ms" (empty before the first window ends).</summary>
        public string Text { get; private set; } = "";

        public void Init(VisualElement root)
        {
            Instance = this;
            label = new Label { name = "fps-counter", pickingMode = PickingMode.Ignore };
            label.style.position = Position.Absolute;
            label.style.right = 14;
            label.style.bottom = 8;
            label.style.fontSize = 30;
            label.style.color = new Color(0.31f, 0.29f, 0.27f, 0.85f);
            label.style.unityTextAlign = TextAnchor.LowerRight;
            label.style.display = DisplayStyle.None;
            root.Add(label);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (!visible) return;
            float dt = Time.unscaledDeltaTime;
            windowTime += dt; windowFrames++; windowWorst = Mathf.Max(windowWorst, dt);
            logTime += dt; logFrames++; logWorst = Mathf.Max(logWorst, dt);
            if (windowTime >= windowSeconds)
            {
                float fps = windowFrames / windowTime;
                logMin = Mathf.Min(logMin, fps);
                Text = fps.ToString("0.0") + " fps, worst " + (windowWorst * 1000f).ToString("0.0") + " ms";
                if (label != null) label.text = Text;
                windowTime = 0f; windowFrames = 0; windowWorst = 0f;
            }
            if (logTime >= logSeconds)
            {
                Debug.Log("[fps] avg " + (logFrames / logTime).ToString("0.0") + " min " + logMin.ToString("0.0") + " worst_ms " + (logWorst * 1000f).ToString("0.0")
                    + " frames " + logFrames + " target " + Application.targetFrameRate + " vsync " + QualitySettings.vSyncCount + " " + Screen.width + "x" + Screen.height
                    + " api " + SystemInfo.graphicsDeviceType);
                logTime = 0f; logFrames = 0; logWorst = 0f; logMin = float.MaxValue;
            }
        }
    }
}
#endif
