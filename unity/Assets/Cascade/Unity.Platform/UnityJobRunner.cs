using System;
using Cascade.Presentation.Shell;
using UnityEngine;

namespace Cascade.Unity.Platform
{
    /// <summary>
    /// <see cref="IJobRunner"/> on <c>Awaitable.BackgroundThreadAsync</c> (TRD 12.3): the work runs on a background thread, the
    /// callback is delivered on the main thread (<c>Awaitable.MainThreadAsync</c>). Level generation is the only user; it touches
    /// no Unity API. The generic method only wraps the delegates in non-generic ones, so the async state machine itself is not
    /// generic (IL2CPP-safe: no generic virtual or async instantiation over value types).
    /// </summary>
    public sealed class UnityJobRunner : IJobRunner
    {
        public void Run<T>(Func<T> work, Action<T> onDone, Action<Exception> onError)
        {
            Func<Action> job = () =>
            {
                try
                {
                    T result = work();
                    return () => onDone(result);
                }
                catch (Exception e)
                {
                    return () => onError(e);
                }
            };
            _ = Execute(job);
        }

        static async Awaitable Execute(Func<Action> job)
        {
            Action deliver;
            await Awaitable.BackgroundThreadAsync();
            try { deliver = job(); }
            finally { await Awaitable.MainThreadAsync(); }
            deliver();
        }
    }
}
