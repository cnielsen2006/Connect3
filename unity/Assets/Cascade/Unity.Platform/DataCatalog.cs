using System;
using System.Collections.Generic;
using Cascade.Platform.Abstractions;
using UnityEngine;

namespace Cascade.Unity.Platform
{
    /// <summary>
    /// Shipped data files (TOML as TextAssets) by file name. Not StreamingAssets: on Android that is
    /// inside the APK (TRD §13.4). Filled by the Editor (Cascade.Editor.DataCatalogBuilder).
    /// </summary>
    [CreateAssetMenu(menuName = "Cascade/Data Catalog")]
    public sealed class DataCatalog : ScriptableObject, IDataSource
    {
        [Serializable]
        public struct Entry
        {
            public string key;
            public TextAsset asset;
        }

        [SerializeField] Entry[] entries = Array.Empty<Entry>();

        public Entry[] Entries { get => entries; set => entries = value; }

        public bool Has(string key)
        {
            foreach (var e in entries)
                if (e.key == key && e.asset != null) return true;
            return false;
        }

        public string ReadText(string key)
        {
            foreach (var e in entries)
                if (e.key == key && e.asset != null) return e.asset.text;
            throw new KeyNotFoundException($"No shipped data '{key}'");
        }
    }
}
