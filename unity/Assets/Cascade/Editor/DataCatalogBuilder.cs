using System.Linq;
using Cascade.Unity.Platform;
using UnityEditor;
using UnityEngine;

namespace Cascade.Editor
{
    /// <summary>Keeps Data/DataCatalog.asset listing every .toml under Assets/Cascade/Data.</summary>
    public static class DataCatalogBuilder
    {
        public const string DataFolder = "Assets/Cascade/Data";
        public const string CatalogPath = DataFolder + "/DataCatalog.asset";

        [MenuItem("Cascade/Refresh Data Catalog")]
        public static DataCatalog Refresh()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<DataCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<DataCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            catalog.Entries = AssetDatabase.FindAssets("t:TextAsset", new[] { DataFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".toml"))
                .OrderBy(p => p, System.StringComparer.Ordinal)
                .Select(p => new DataCatalog.Entry
                {
                    key = System.IO.Path.GetFileName(p),
                    asset = AssetDatabase.LoadAssetAtPath<TextAsset>(p),
                })
                .ToArray();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            return catalog;
        }
    }
}
