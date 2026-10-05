using Cascade.Gameplay;
using Cascade.Persistence;
using Cascade.Presentation.Shell;

namespace Cascade.App
{
    /// <summary>
    /// The shell's way to persist (<see cref="IProgressSink"/>): progress and settings go to <see cref="SaveService"/>, atomically, through
    /// <c>FileStorage</c>. A failed write is logged by the service and never throws into the game.
    /// </summary>
    public sealed class SaveSink : IProgressSink
    {
        readonly SaveService save;

        public SaveSink(SaveService saveService) => save = saveService;

        public int ProgressWrites { get; private set; }
        public int SettingsWrites { get; private set; }

        public void SaveProgress(SaveData data)
        {
            ProgressWrites++;
            save.TryWriteSave(data);
        }

        public void SaveSettings(PlayerSettings settings)
        {
            SettingsWrites++;
            save.TryWriteSettings(settings);
        }
    }
}
