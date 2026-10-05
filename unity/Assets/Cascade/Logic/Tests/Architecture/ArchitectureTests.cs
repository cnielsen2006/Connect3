using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using NUnit.Framework;

namespace Cascade.Tests.Architecture
{
    /// <summary>
    /// TRD §9.1: each logic assembly must not reference UnityEngine, System.IO, System.Random or
    /// System.DateTime (outside the allowed list), and may only reference the assemblies
    /// TRD §2.1 allows. Runs unchanged under `dotnet test` and Unity EditMode.
    /// </summary>
    public class ArchitectureTests
    {
        static readonly Dictionary<string, string[]> Allowed = new Dictionary<string, string[]>
        {
            ["Cascade.Core"] = new string[0],
            ["Cascade.Config"] = new[] { "Cascade.Platform.Abstractions" },
            ["Cascade.Platform.Abstractions"] = new string[0],
            ["Cascade.Gameplay"] = new[] { "Cascade.Core", "Cascade.Config", "Cascade.Platform.Abstractions" },
            ["Cascade.Generation"] = new[] { "Cascade.Core", "Cascade.Config", "Cascade.Gameplay", "Cascade.Platform.Abstractions" },
            ["Cascade.Persistence"] = new[] { "Cascade.Gameplay", "Cascade.Platform.Abstractions" },
            ["Cascade.Presentation"] = new[] { "Cascade.Core", "Cascade.Config", "Cascade.Gameplay", "Cascade.Generation", "Cascade.Platform.Abstractions" },
        };

        // Type-name prefixes a logic assembly must never reference. Exact names are matched for
        // single types; "ns." entries match a whole namespace.
        static readonly string[] ForbiddenTypes =
        {
            "System.Random", "System.DateTime", "System.DateTimeOffset", "System.Guid",
        };
        static readonly string[] ForbiddenNamespaces = { "System.IO", "UnityEngine", "UnityEditor" };

        // (assembly, type or namespace) pairs that are allowed on purpose. Empty for now.
        static readonly HashSet<(string, string)> AllowedExceptions = new HashSet<(string, string)>();

        public static IEnumerable<string> LogicAssemblies => Allowed.Keys;

        static string PathOf(string assemblyName) => Assembly.Load(assemblyName).Location;

        [TestCaseSource(nameof(LogicAssemblies))]
        public void References_only_allowed_assemblies(string name)
        {
            var refs = Assembly.Load(name).GetReferencedAssemblies()
                .Select(a => a.Name!).Where(n => n.StartsWith("Cascade.")).ToArray();
            var extra = refs.Except(Allowed[name]).ToArray();
            Assert.That(extra, Is.Empty, $"{name} references {string.Join(", ", extra)}");
        }

        [TestCaseSource(nameof(LogicAssemblies))]
        public void Uses_no_engine_io_clock_or_system_random(string name)
        {
            var offenders = new SortedSet<string>();
            using var stream = File.OpenRead(PathOf(name));
            using var pe = new PEReader(stream);
            var md = pe.GetMetadataReader();
            foreach (var handle in md.TypeReferences)
            {
                var tr = md.GetTypeReference(handle);
                var ns = md.GetString(tr.Namespace);
                var type = md.GetString(tr.Name);
                var full = ns.Length == 0 ? type : ns + "." + type;
                bool bad = ForbiddenTypes.Contains(full)
                    || ForbiddenNamespaces.Any(f => ns == f || ns.StartsWith(f + ".", StringComparison.Ordinal));
                if (bad && !AllowedExceptions.Contains((name, full)) && !AllowedExceptions.Contains((name, ns)))
                    offenders.Add(full);
            }
            Assert.That(offenders, Is.Empty, $"{name} references forbidden types: {string.Join(", ", offenders)}");
        }
    }
}
