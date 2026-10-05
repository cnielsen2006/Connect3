using System.IO;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace Cascade.Editor
{
    /// <summary>Imports every .toml as a TextAsset so DataCatalog can ship it (TRD §13.4).</summary>
    [ScriptedImporter(1, "toml")]
    public sealed class TomlImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            var asset = new TextAsset(File.ReadAllText(ctx.assetPath));
            ctx.AddObjectToAsset("text", asset);
            ctx.SetMainObject(asset);
        }
    }
}
