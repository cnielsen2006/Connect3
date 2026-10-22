using System;
using Cascade.Presentation.Shell;
using UnityEngine;

namespace Cascade.Unity.Platform
{
    /// <summary>
    /// <see cref="IJobRunner"/> on <c>Awaitable.BackgroundThreadAsync</c> (TRD 12.3): the work runs on a background thread, the
    /// callback is delivered on the main thread (<c>Awaitable.MainThreadAsync</c>). Level generation is the only user; it touches
    /// no Unity API. The generic method only wraps the delegates in non-generic ones, so the async state machine itself is not
    /// generic (IL2CPP-safe: no generic virtual or async instantiation over value types). On WebGL (no threads, and the thread hops
    /// never resume) the job runs inline on the main thread, one frame after it was requested.
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
#if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL has no threads, and the hop through BackgroundThreadAsync / MainThreadAsync never resumes there (verified in a
            // browser build: the loading screen stayed up forever). Let one frame render first so the loading screen is drawn, then
            // run the job inline on the main thread (it respects generator.budget_ms).
            await Awaitable.NextFrameAsync();
            deliver = job();
#else
            await Awaitable.BackgroundThreadAsync();
            try { deliver = job(); }
            finally { await Awaitable.MainThreadAsync(); }
#endif
            deliver();
        }
    }
}
