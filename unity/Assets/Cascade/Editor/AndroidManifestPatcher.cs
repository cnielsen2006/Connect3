using System.IO;
using System.Xml;
using UnityEditor.Android;

namespace Cascade.Editor
{
    /// <summary>TRD §13.1: allowBackup false and no permissions, enforced on every Android build.</summary>
    public sealed class AndroidManifestPatcher : IPostGenerateGradleAndroidProject
    {
        const string AndroidNs = "http://schemas.android.com/apk/res/android";

        public int callbackOrder => 100;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            foreach (var manifest in new[]
            {
                Path.Combine(path, "src", "main", "AndroidManifest.xml"),
                Path.Combine(path, "..", "launcher", "src", "main", "AndroidManifest.xml"),
            })
            {
                if (!File.Exists(manifest)) continue;
                var doc = new XmlDocument();
                doc.Load(manifest);
                var app = doc.SelectSingleNode("/manifest/application") as XmlElement;
                if (app == null) continue;
                app.SetAttribute("allowBackup", AndroidNs, "false");
                doc.Save(manifest);
            }
        }
    }
}
