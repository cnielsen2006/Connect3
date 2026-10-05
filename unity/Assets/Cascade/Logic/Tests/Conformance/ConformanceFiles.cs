using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Cascade.Tests.Conformance
{
    /// <summary>
    /// Locates and reads the read-only fixtures in <c>conformance/</c> (TRD §9.2). The folder is
    /// found by walking up from the test assembly's directory and the current directory until a
    /// directory named <c>conformance</c> is found, so it works under <c>dotnet test</c> and Unity
    /// EditMode alike. Test code only; the logic assemblies never touch files.
    /// </summary>
    public static class ConformanceFiles
    {
        static string? _root;

        /// <summary>Absolute path of the <c>conformance</c> folder.</summary>
        public static string Root()
        {
            if (_root != null) return _root;
            foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            {
                for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
                {
                    string candidate = System.IO.Path.Combine(dir.FullName, "conformance");
                    if (Directory.Exists(candidate)) return _root = candidate;
                }
            }
            throw new DirectoryNotFoundException("no 'conformance' folder found above " + AppContext.BaseDirectory);
        }

        /// <summary>The full path of a fixture, e.g. <c>FilePath("rng", "vectors.json")</c>.</summary>
        public static string FilePath(params string[] parts)
        {
            var all = new string[parts.Length + 1];
            all[0] = Root();
            Array.Copy(parts, 0, all, 1, parts.Length);
            return System.IO.Path.Combine(all);
        }

        /// <summary>Reads a fixture as UTF-8 text.</summary>
        public static string ReadText(params string[] parts) => File.ReadAllText(FilePath(parts), System.Text.Encoding.UTF8);

        /// <summary>Reads and parses a JSON fixture. Parsed with <see cref="DateParseHandling.None"/> and decimal-exact doubles.</summary>
        public static JToken ReadJson(params string[] parts)
        {
            using var reader = new JsonTextReader(new StringReader(ReadText(parts))) { DateParseHandling = DateParseHandling.None };
            return JToken.Load(reader);
        }

        /// <summary>
        /// Canonical JSON text: object members sorted by name (ordinal), recursively, no whitespace.
        /// Two documents are "byte-for-byte equal after canonical JSON" when these strings are equal.
        /// </summary>
        public static string Canonical(JToken token)
        {
            return Sort(token).ToString(Formatting.None);
        }

        static JToken Sort(JToken token)
        {
            switch (token)
            {
                case JObject o:
                    var sorted = new JObject();
                    var names = new System.Collections.Generic.List<string>();
                    foreach (var p in o.Properties()) names.Add(p.Name);
                    names.Sort(StringComparer.Ordinal);
                    foreach (var n in names) sorted[n] = Sort(o[n]!);
                    return sorted;
                case JArray a:
                    var arr = new JArray();
                    foreach (var item in a) arr.Add(Sort(item));
                    return arr;
                default:
                    return token.DeepClone();
            }
        }
    }
}
