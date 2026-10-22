using System.IO;
using Cascade.Platform.Abstractions;

namespace Cascade.Unity.Platform
{
    /// <summary>User data folder (Application.persistentDataPath); writes go to a temp file then replace.</summary>
    public sealed class FileStorage : IStorage
    {
        readonly string root;

        public FileStorage(string root)
        {
            this.root = root;
            Directory.CreateDirectory(root);
        }

        string PathOf(string name) => Path.Combine(root, name);

        public byte[]? TryRead(string name)
        {
            var p = PathOf(name);
            return File.Exists(p) ? File.ReadAllBytes(p) : null;
        }

        public void WriteAtomic(string name, byte[] bytes)
        {
            var p = PathOf(name);
            var tmp = p + ".tmp";
            File.WriteAllBytes(tmp, bytes);
#if UNITY_WEBGL && !UNITY_EDITOR
            // File.Replace is not available on the WebGL file system (in-memory, mirrored to IndexedDB by the page template's
            // autoSyncPersistentDataPath); a plain delete + move is as atomic as that file system gets.
            if (File.Exists(p)) File.Delete(p);
            File.Move(tmp, p);
#else
            if (File.Exists(p)) File.Replace(tmp, p, null);
            else File.Move(tmp, p);
#endif
        }

        public void Move(string name, string newName)
        {
            var dst = PathOf(newName);
            if (File.Exists(dst)) File.Delete(dst);
            File.Move(PathOf(name), dst);
        }

        public void Delete(string name)
        {
            var p = PathOf(name);
            if (File.Exists(p)) File.Delete(p);
        }
    }
}
