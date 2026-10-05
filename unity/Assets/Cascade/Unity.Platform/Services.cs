using System;
using System.Diagnostics;
using System.Security.Cryptography;
using Cascade.Platform.Abstractions;

namespace Cascade.Unity.Platform
{
    public sealed class CryptoSeedSource : ISeedSource
    {
        public ulong NextSeed()
        {
            var bytes = new byte[8];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return BitConverter.ToUInt64(bytes, 0);
        }
    }

    /// <summary>
    /// Hands out queued seeds first, then the inner source (<c>-seed S</c> and tests fix the seed of the next level this way).
    /// Main thread only.
    /// </summary>
    public sealed class QueuedSeedSource : ISeedSource
    {
        readonly ISeedSource inner;
        readonly System.Collections.Generic.Queue<ulong> queue = new System.Collections.Generic.Queue<ulong>();

        public QueuedSeedSource(ISeedSource inner) => this.inner = inner;

        public int Pending => queue.Count;

        public void Enqueue(ulong seed) => queue.Enqueue(seed);

        public ulong NextSeed() => queue.Count > 0 ? queue.Dequeue() : inner.NextSeed();
    }

    public sealed class SystemStopwatch : IStopwatch
    {
        readonly Stopwatch watch = Stopwatch.StartNew();
        public double ElapsedMilliseconds => watch.Elapsed.TotalMilliseconds;
    }

    public sealed class UnityLog : ILog
    {
        public void Info(string message) => UnityEngine.Debug.Log(message);
        public void Warn(string message) => UnityEngine.Debug.LogWarning(message);
        public void Error(string message) => UnityEngine.Debug.LogError(message);
    }
}
