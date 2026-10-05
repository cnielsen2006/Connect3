using System;
using System.Collections.Generic;
using Cascade.Presentation.Shell;

namespace Cascade.Tests.Play
{
    /// <summary>
    /// Wraps the real job runner and, while <see cref="Held"/>, keeps level generation waiting, so a test can look at the Loading page
    /// and then let the level through. Everything else is the real thing: <see cref="Release"/> hands the jobs to the inner runner.
    /// </summary>
    public sealed class GatedJobRunner : IJobRunner
    {
        readonly List<Action> pending = new List<Action>();
        public IJobRunner? Inner;
        public bool Held;

        public int Waiting => pending.Count;

        public void Run<T>(Func<T> work, Action<T> onDone, Action<Exception> onError)
        {
            Action start = () => Inner!.Run(work, onDone, onError);
            if (Held) pending.Add(start); else start();
        }

        public void Hold() => Held = true;

        public void Release()
        {
            Held = false;
            var jobs = pending.ToArray();
            pending.Clear();
            foreach (var job in jobs) job();
        }
    }
}
