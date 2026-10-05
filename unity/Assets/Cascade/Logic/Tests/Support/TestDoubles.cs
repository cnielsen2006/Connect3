using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Cascade.Platform.Abstractions;

namespace Cascade.Tests.Support
{
    /// <summary>In-memory <see cref="IStorage"/>. <see cref="Files"/> is public so tests can inspect or seed it.</summary>
    public sealed class MemoryStorage : IStorage
    {
        public Dictionary<string, byte[]> Files { get; } = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        /// <summary>Number of WriteAtomic calls, to assert that something was (not) saved.</summary>
        public int Writes { get; private set; }

        public byte[]? TryRead(string name) => Files.TryGetValue(name, out var b) ? (byte[])b.Clone() : null;

        public void WriteAtomic(string name, byte[] bytes)
        {
            Files[name] = (byte[])bytes.Clone();
            Writes++;
        }

        public void Move(string name, string newName)
        {
            Files[newName] = Files[name];
            Files.Remove(name);
        }

        public void Delete(string name) => Files.Remove(name);

        /// <summary>Test helper: seed a text file (UTF-8).</summary>
        public void PutText(string name, string text) => Files[name] = System.Text.Encoding.UTF8.GetBytes(text);

        /// <summary>Test helper: read a file as UTF-8 text, or null.</summary>
        public string? GetText(string name) =>
            Files.TryGetValue(name, out var b) ? System.Text.Encoding.UTF8.GetString(b) : null;
    }

    /// <summary>
    /// An <see cref="IDataSource"/> over literal strings. Like the real DataCatalog, an unknown key
    /// throws <see cref="KeyNotFoundException"/>.
    /// </summary>
    public sealed class DictionaryDataSource : IDataSource
    {
        readonly Dictionary<string, string> texts = new Dictionary<string, string>(StringComparer.Ordinal);

        public DictionaryDataSource() { }

        public DictionaryDataSource(params (string Key, string Text)[] files)
        {
            foreach (var (key, text) in files) texts[key] = text;
        }

        public DictionaryDataSource With(string key, string text)
        {
            texts[key] = text;
            return this;
        }

        public string ReadText(string key) =>
            texts.TryGetValue(key, out var t) ? t : throw new KeyNotFoundException($"No shipped data '{key}'");
    }

    /// <summary>A deterministic <see cref="ISeedSource"/>: returns start, start + 1, start + 2, ...</summary>
    public sealed class FakeSeedSource : ISeedSource
    {
        ulong next;

        public FakeSeedSource(ulong start = 1) { next = start; }

        public ulong NextSeed() => next++;
    }

    /// <summary>A manually advanced <see cref="IStopwatch"/>. With <see cref="AutoAdvanceMs"/> > 0 every read advances it.</summary>
    public sealed class FakeStopwatch : IStopwatch
    {
        double now;

        public double AutoAdvanceMs { get; set; }

        public double ElapsedMilliseconds
        {
            get
            {
                var value = now;
                now += AutoAdvanceMs;
                return value;
            }
        }

        public void Advance(double ms) => now += ms;
    }

    /// <summary>An <see cref="ILog"/> that records lines as "Info: text", "Warn: text", "Error: text".</summary>
    public sealed class ListLog : ILog
    {
        public List<string> Lines { get; } = new List<string>();

        public void Info(string message) => Lines.Add("Info: " + message);
        public void Warn(string message) => Lines.Add("Warn: " + message);
        public void Error(string message) => Lines.Add("Error: " + message);
    }

    /// <summary>Locates files of the repository from a test (uses the compile-time path of this source file).</summary>
    public static class RepoFiles
    {
        /// <summary>Absolute path of unity/Assets/Cascade/Data/<paramref name="name"/>.</summary>
        public static string DataFile(string name, [CallerFilePath] string here = "")
        {
            // here = .../Assets/Cascade/Logic/Tests/Support/TestDoubles.cs
            var dir = System.IO.Path.GetDirectoryName(here)!;
            return System.IO.Path.GetFullPath(System.IO.Path.Combine(dir, "..", "..", "..", "Data", name));
        }

        public static string ReadData(string name) => System.IO.File.ReadAllText(DataFile(name));
    }
}
