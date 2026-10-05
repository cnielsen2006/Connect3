using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Cascade.Editor
{
    /// <summary>
    /// Batch check of the style's shaders (TRD 12.1, U6): they compile for the current platform, are supported, and are SRP Batcher
    /// compatible (every material property in the UnityPerMaterial CBUFFER of every pass). The compatibility query is editor-internal,
    /// so it is reached by reflection; the check also verifies the CBUFFER text against the Properties block as a fallback.
    ///   Unity -batchmode -nographics -quit -projectPath unity -executeMethod Cascade.Editor.ShaderChecks.Run -logFile Builds/shaders.log
    /// Exits 1 on any problem.
    /// </summary>
    public static class ShaderChecks
    {
        static readonly string[] Names = { "Clay", "Tray", "FxUnlit", "Background", "Grain" };

        [MenuItem("Cascade/Check Shaders")]
        public static void Run()
        {
            bool ok = true;
            foreach (var n in Names)
            {
                string path = "Assets/Cascade/Art/Shaders/" + n + ".shader";
                var sh = AssetDatabase.LoadAssetAtPath<Shader>(path);
                if (sh == null) { Debug.LogError("[ShaderChecks] missing " + path); ok = false; continue; }
                int msgs = ShaderUtil.GetShaderMessageCount(sh);
                bool errors = false;
                foreach (var m in ShaderUtil.GetShaderMessages(sh))
                    if (m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error) { errors = true; Debug.LogError("[ShaderChecks] " + n + ": " + m.message + " " + m.messageDetails); }
                string batcher = BatcherStatus(sh);
                string cbuf = CbufferStatus(sh, path, out bool cbufOk);
                Debug.Log("[ShaderChecks] " + n + ": supported=" + sh.isSupported + " messages=" + msgs + " srpBatcher=" + batcher + " cbuffer=" + cbuf);
                if (!sh.isSupported || errors || !cbufOk || batcher.StartsWith("INCOMPATIBLE")) ok = false;
            }
            Debug.Log("[ShaderChecks] " + (ok ? "OK" : "FAILED"));
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        static string BatcherStatus(Shader sh)
        {
            try
            {
                var m = typeof(ShaderUtil).GetMethod("GetSRPBatcherCompatibilityCode", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (m == null) return "unknown (no internal API)";
                var pars = m.GetParameters();
                var args = new object[pars.Length];
                args[0] = sh;
                for (int i = 1; i < args.Length; i++) args[i] = 0;
                var code = m.Invoke(null, args);
                int c = Convert.ToInt32(code);
                var rm = typeof(ShaderUtil).GetMethod("GetSRPBatcherCompatibilityIssueReason", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                string reason = rm != null ? Convert.ToString(rm.Invoke(null, new object[] { sh, 0, c })) ?? "" : "";
                // In batch mode the Editor has not initialised its batcher query ("Not initialized"): then only the CBUFFER check below counts.
                if (reason.Contains("Not initialized")) return "unknown (editor query not initialised in batch mode)";
                return c == 1 ? "compatible (" + reason + ")" : "INCOMPATIBLE (code " + c + ": " + reason + ")";
            }
            catch (Exception e) { return "unknown (" + e.GetType().Name + ")"; }
        }

        /// <summary>Every property of the Properties block (except textures) must also be declared inside each CBUFFER_START(UnityPerMaterial) of the file.</summary>
        static string CbufferStatus(Shader sh, string path, out bool ok)
        {
            ok = true;
            string text = System.IO.File.ReadAllText(path);
            int count = sh.GetPropertyCount();
            var missingAll = new System.Text.StringBuilder();
            int pos = 0, buffers = 0;
            while (true)
            {
                int a = text.IndexOf("CBUFFER_START(UnityPerMaterial)", pos, StringComparison.Ordinal);
                if (a < 0) break;
                int b = text.IndexOf("CBUFFER_END", a, StringComparison.Ordinal);
                string body = text.Substring(a, b - a);
                buffers++;
                // the DepthOnly passes repeat the buffer; every pass must carry the same declarations
                for (int i = 0; i < count; i++)
                {
                    if (sh.GetPropertyType(i) == UnityEngine.Rendering.ShaderPropertyType.Texture) continue;
                    string name = sh.GetPropertyName(i);
                    if (!System.Text.RegularExpressions.Regex.IsMatch(body, @"\b" + name + @"\b")) { missingAll.Append(name).Append(' '); ok = false; }
                }
                pos = b;
            }
            if (buffers == 0) { ok = false; return "no UnityPerMaterial buffer"; }
            return ok ? "all " + count + " properties declared in " + buffers + " buffer(s)" : "MISSING in buffer: " + missingAll;
        }
    }
}
