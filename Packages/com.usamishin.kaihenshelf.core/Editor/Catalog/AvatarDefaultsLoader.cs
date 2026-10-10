using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace KaihenShelf
{
    public static class AvatarDefaultsLoader
    {
        // The packaged dataset is local; loading it never accesses BLM or the network.
        public static bool Load(out string warning)
        {
            warning = "";
            try
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(AvatarDefaultsLoader).Assembly);
                string root = package != null ? package.resolvedPath : Path.Combine(Application.dataPath, "KaihenShelf");
                string file = Path.Combine(root, "Editor", "Data", "default-avatar-identifiers.json");
                string json = File.ReadAllText(file);
                var document = JsonUtility.FromJson<AvatarCatalogDocument>("{\"entries\":" + json + "}");
                if (document?.entries == null || document.entries.Length == 0) throw new InvalidDataException("識別タグの既定データが空です。");
                AvatarCatalog.SetDefaults(document.entries);
                return true;
            }
            catch (Exception ex)
            {
                AvatarCatalog.SetDefaults(null);
                warning = "既定の識別タグを読み込めないため、商品名から候補を作成します: " + ex.Message;
                return false;
            }
        }
    }
}
