using System;
using System.Collections.Generic;
using Cascade.Platform.Abstractions;
using Newtonsoft.Json;
using Tomlyn;
using Tomlyn.Model;

namespace Cascade.App
{
    public readonly struct CheckResult
    {
        public CheckResult(string name, bool ok, string detail)
        {
            Name = name; Ok = ok; Detail = detail;
        }

        public string Name { get; }
        public bool Ok { get; }
        public string Detail { get; }
    }

    /// <summary>
    /// TRD §13.2 questions 2: a shipped TOML parses with Tomlyn and a Newtonsoft round trip works,
    /// including in a stripped IL2CPP player. Results go to the log at start-up.
    /// </summary>
    public static class StackChecks
    {
        public static List<CheckResult> Run(IDataSource data)
        {
            var results = new List<CheckResult>();
            results.Add(Check("Tomlyn dials.toml", () =>
            {
                var model = Toml.ToModel(data.ReadText("dials.toml"));
                return $"{model.Count} top-level keys";
            }));
            results.Add(Check("Tomlyn difficulty.toml", () =>
            {
                var model = Toml.ToModel(data.ReadText("difficulty.toml"));
                var d = (TomlTable)model["difficulty"];
                return $"cycle_length = {d["cycle_length"]}";
            }));
            results.Add(Check("Newtonsoft round trip", () =>
            {
                var original = new Dictionary<string, object>
                {
                    ["seed"] = "18446744073709551615",
                    ["moves"] = new List<int> { 1, 2, 3 },
                };
                var json = JsonConvert.SerializeObject(original);
                var back = JsonConvert.DeserializeObject<Dictionary<string, object>>(json)!;
                if ((string)back["seed"] != "18446744073709551615") throw new Exception("seed changed");
                return json;
            }));
            return results;
        }

        static CheckResult Check(string name, Func<string> body)
        {
            try { return new CheckResult(name, true, body()); }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"[Cascade] {name}: {e}");
                return new CheckResult(name, false, e.GetType().Name + ": " + e.Message);
            }
        }
    }
}
