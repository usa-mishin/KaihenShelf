using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KaihenShelf
{
    public static class FolderThumbnailRules
    {
        static bool Generic(string name) => new[] { "textures", "texture", "materials", "material", "prefabs", "prefab", "shaders", "shader", "scripts", "editor", "animations", "animation", "fbx", "models", "model" }.Contains(Key(name));
        public static string Key(string value) => new string((value ?? "").Normalize(NormalizationForm.FormKC).ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        public static bool Under(string file, string folder) => !string.IsNullOrEmpty(folder) && file.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase);
        public static string SingleChild(IEnumerable<string> files, string root)
        {
            var paths = files.Where(x => Under(x, root)).Select(x => x.Substring(root.Length + 1).Split('/')).ToArray();
            return paths.Length > 0 && paths.All(x => x.Length > 1) && paths.Select(x => x[0]).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1
                ? root + "/" + paths[0][0] : "";
        }
        public static string[] Children(IEnumerable<string> files, string root) => files
            .Where(x => !string.IsNullOrEmpty(x) && Under(x, root))
            .Select(x => x.Substring(root.Length + 1).Split('/')).Where(x => x.Length > 1)
            .Select(x => root + "/" + x[0]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        // Input contains files, not directories. Meta files must not influence ranking.
        public static string PreferredChild(IEnumerable<string> files, string root)
        {
            var paths = files.Where(x => !string.IsNullOrEmpty(x) && Under(x, root) && !x.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var ranked = Children(paths, root).Select(folder => new
            {
                folder,
                prefab = paths.Any(x => Under(x, folder) && x.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)),
                count = paths.Count(x => Under(x, folder))
            }).OrderByDescending(x => x.prefab).ThenByDescending(x => x.count).ThenBy(x => x.folder, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.folder, StringComparer.Ordinal).ToArray();
            if (ranked.Length == 0) return "";
            return ranked[0].folder;
        }
        public static string ImportRoot(string[] files, string target, string shop, string sourceName)
        {
            Suggest(files, shop, sourceName, out var shopFolder, out var productFolder);
            if (shopFolder.Length > 0) return shopFolder;
            string root = SingleChild(files, (target ?? "Assets").TrimEnd('/'));
            return root.Length > 0 ? root : productFolder;
        }
        public static string[] Candidates(IEnumerable<string> files)
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in files)
                for (string p = file; p.LastIndexOf('/') > 0;)
                {
                    p = p.Substring(0, p.LastIndexOf('/'));
                    if (p.StartsWith("Assets/", StringComparison.Ordinal)) paths.Add(p);
                }
            return paths.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        public static void Suggest(string[] files, string shop, string sourceName, out string shopFolder, out string productFolder)
        {
            var candidates = Candidates(files);
            string shopKey = Key(shop), productKey = Key(sourceName);
            var shops = candidates.Where(x => shopKey.Length > 0 && Key(x.Substring(x.LastIndexOf('/') + 1)) == shopKey).ToArray();
            shopFolder = shops.Length == 1 ? shops[0] : "";
            string chosenShop = shopFolder;
            var products = candidates.Where(x => productKey.Length > 0 && !Generic(sourceName) && Key(x.Substring(x.LastIndexOf('/') + 1)) == productKey && x != chosenShop
                && (chosenShop.Length == 0 || Under(x, chosenShop))).ToArray();
            productFolder = products.Length == 1 ? products[0] : "";
            if (productFolder.Length == 0 && chosenShop.Length > 0)
            {
                var shopFiles = files.Where(x => Under(x, chosenShop)).ToArray();
                var roots = shopFiles.Select(x => x.Substring(chosenShop.Length + 1).Split('/')).ToArray();
                if (roots.Length > 0 && roots.All(x => x.Length > 1) && !Generic(roots[0][0]) && roots.Select(x => x[0]).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1)
                    productFolder = chosenShop + "/" + roots[0][0];
            }
        }
    }
}
