using System;
using Cascade.Gameplay;

namespace Cascade.Presentation.Shell
{
    /// <summary>
    /// Runs slow work (level generation, TRD 12.3) off the main thread without Presentation knowing how.
    /// <para>Contract: <paramref name="work"/> may run on any thread; <paramref name="onDone"/> or
    /// <paramref name="onError"/> (exactly one of them, exactly once) is called later on the thread that
    /// owns the game (the main thread). Work started by one controller is never overlapped by it: the
    /// <see cref="GameController"/> queues its jobs and keeps only one in flight, because
    /// <c>GeneratingLevelSource</c> is not thread-safe. Unity implements this with
    /// <c>Awaitable.BackgroundThreadAsync</c> and a hop back to the main thread; tests use
    /// <see cref="ImmediateJobRunner"/> or a manual runner.</para>
    /// </summary>
    public interface IJobRunner
    {
        void Run<T>(Func<T> work, Action<T> onDone, Action<Exception> onError);
    }

    /// <summary>Runs the work right now on the calling thread and calls back before returning (tests, headless tools).</summary>
    public sealed class ImmediateJobRunner : IJobRunner
    {
        public void Run<T>(Func<T> work, Action<T> onDone, Action<Exception> onError)
        {
            T result;
            try { result = work(); }
            catch (Exception e) { onError(e); return; }
            onDone(result);
        }
    }

    /// <summary>
    /// Where Presentation hands data that must survive: the composition root forwards these to
    /// <c>Persistence.SaveService</c> (Presentation may not reference Persistence). Called on the main
    /// thread; an implementation should not throw (use <c>SaveService.TryWriteSave</c>).
    /// </summary>
    public interface IProgressSink
    {
        void SaveProgress(SaveData data);
        void SaveSettings(PlayerSettings settings);
    }

    /// <summary>A sink that drops everything.</summary>
    public sealed class NullProgressSink : IProgressSink
    {
        public static readonly NullProgressSink Instance = new NullProgressSink();
        public void SaveProgress(SaveData data) { }
        public void SaveSettings(PlayerSettings settings) { }
    }
}
