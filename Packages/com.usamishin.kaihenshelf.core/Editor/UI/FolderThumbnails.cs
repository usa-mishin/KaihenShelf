using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace KaihenShelf
{
    // Project-only metadata and small images stay outside Assets and player builds.
    [InitializeOnLoad] public static class FolderThumbnails
    {
        [Serializable] public sealed class Entry
        {
            public string guid, path, owner, kind, local, remote, image;
            public string rootGuid;
            public string[] fileGuids = Array.Empty<string>();
            public string[] childGuids = Array.Empty<string>();
            public bool childrenOnly;
        }
        [Serializable] public sealed class Store
        {
            public bool enabled = true;
            public List<Entry> entries = new List<Entry>();
            // Provenance survives even when every touched folder is shared and has no image.
            public List<Entry> products = new List<Entry>();
        }
        static string Root => Path.Combine(LocalStore.ProjectDataRoot, "FolderThumbnails");
        static string StorePath => Path.Combine(Root, "folders.json");
        static Store store;
        static readonly Dictionary<string, Entry> byGuid = new Dictionary<string, Entry>();
        static readonly HashSet<string> diskImages = new HashSet<string>();
        static readonly HashSet<Entry> pendingImages = new HashSet<Entry>();
        static ThumbnailCache cache;
        static bool allowRemote;
        static double nextResolve;
        public static bool Enabled { get { Ensure(); return store.enabled; } }
        public static Entry Assignment(string folder)
        { Ensure(); string guid = AssetDatabase.AssetPathToGUID(folder); return byGuid.TryGetValue(guid, out var entry) ? entry : null; }
        static FolderThumbnails()
        {
            EditorApplication.projectWindowItemOnGUI += Draw;
            EditorApplication.update += Tick;
            EditorApplication.focusChanged += Focus;
        }
        static void Ensure()
        {
            if (store != null) return;
            try { store = LocalStore.Read<Store>(StorePath) ?? new Store(); }
            catch (Exception ex) { store = new Store(); Debug.LogWarning("フォルダ画像の設定を読み込めません: " + ex.Message); }
            bool migrated = store.entries != null && store.entries.Any(x => x != null && x.kind == "shop");
            store.entries = (store.entries ?? new List<Entry>()).Where(x => x != null && x.kind == "product" && ValidImage(x.image) && x.path != null && x.path.StartsWith("Assets/", StringComparison.Ordinal)).ToList();
            store.products = store.products ?? new List<Entry>();
            if (migrated) Save();
            foreach (var entry in store.entries)
            {
                if (!string.IsNullOrEmpty(entry.guid)) byGuid[entry.guid] = entry;
                if (ValidImage(entry.image) && File.Exists(Path.Combine(Root, entry.image))) diskImages.Add(entry.image);
                else if (ValidImage(entry.image)) pendingImages.Add(entry);
            }
            RefreshConsent();
        }
        static bool ValidImage(string name) => name != null && name.Length == 68 && name.EndsWith(".png", StringComparison.Ordinal) && name.Take(64).All(Uri.IsHexDigit);
        static void Focus(bool focused)
        {
            if (!focused || store == null) return;
            RefreshConsent(); cache?.RetryFailed();
            foreach (var entry in store.entries.Where(x => !diskImages.Contains(x.image))) pendingImages.Add(entry);
            EditorApplication.RepaintProjectWindow();
        }
        public static void RefreshConsent()
        {
            try { allowRemote = LocalStore.Load().consentVersion == BlmDatabaseReader.ConsentVersion; }
            catch { allowRemote = false; }
            cache?.SetRemoteEnabled(allowRemote);
        }
        static void Save() { LocalStore.Write(StorePath, store); }
        [MenuItem("KaihenShelf/KaihenShelf Image/Show")]
        static void Toggle()
        {
            Ensure(); store.enabled = !store.enabled; Save();
            if (!store.enabled) { cache?.Dispose(); cache = null; pendingImages.Clear(); }
            EditorApplication.RepaintProjectWindow();
        }
        [MenuItem("KaihenShelf/KaihenShelf Image/Show", true)]
        static bool ToggleCheck() { Menu.SetChecked("KaihenShelf/KaihenShelf Image/Show", Enabled); return true; }
        public static void Register(string folder, LibraryItem item, bool replace = false)
        {
            Ensure();
            if (item == null || string.IsNullOrEmpty(folder) || !folder.StartsWith("Assets/", StringComparison.Ordinal)) return;
            if (!Directory.Exists(DirectImporter.Full(folder))) throw new IOException("フォルダが見つかりません: " + folder);
            string local = item.thumbnailPath, remote = item.thumbnailUrl;
            if (string.IsNullOrEmpty(local) && string.IsNullOrEmpty(remote)) return;
            string kind = "product", owner = item.id;
            string guid = AssetDatabase.AssetPathToGUID(folder);
            var existing = store.entries.FirstOrDefault(x => (!string.IsNullOrEmpty(guid) && x.guid == guid) || (string.IsNullOrEmpty(x.guid) && x.path == folder));
            if (existing != null && !replace && (existing.kind != kind || existing.owner != owner))
                throw new InvalidOperationException("別の商品の画像を保持しました: " + folder);
            string image;
            using (var hash = SHA256.Create()) image = string.Concat(hash.ComputeHash(Encoding.UTF8.GetBytes((local ?? "") + "\n" + (remote ?? ""))).Select(x => x.ToString("x2"))) + ".png";
            var entry = new Entry { guid = guid, path = folder, owner = owner, kind = kind, local = local, remote = remote, image = image };
            if (existing != null) { store.entries.Remove(existing); pendingImages.Remove(existing); }
            store.entries.Add(entry); if (!string.IsNullOrEmpty(guid)) byGuid[guid] = entry;
            Save(); pendingImages.Add(entry); EditorApplication.RepaintProjectWindow();
        }
        public static void Remove(string folder)
        {
            Ensure(); string guid = AssetDatabase.AssetPathToGUID(folder);
            foreach (var entry in store.entries.Where(x => (!string.IsNullOrEmpty(guid) && x.guid == guid) || x.path == folder).ToArray()) { store.entries.Remove(entry); pendingImages.Remove(entry); }
            byGuid.Remove(guid); Save(); EditorApplication.RepaintProjectWindow();
        }
        public static void RegisterImport(ImportRecord record, DirectImportPlan plan)
        {
            var item = LocalStore.Load().items.FirstOrDefault(x => x.id == record.itemId);
            if (item == null) return;
            var files = plan.entries.Where(x => !x.asset.folder && x.selected && x.conflict.Length == 0).Select(x => x.destination).ToArray();
            string path = plan.thumbnailProductFolder;
            if (string.IsNullOrEmpty(path) || !files.Any(x => FolderThumbnailRules.Under(x, path)) || !Directory.Exists(DirectImporter.Full(path))) return;
            RegisterProductImport(path, item, files);
        }
        static string CurrentPath(Entry entry) => string.IsNullOrEmpty(entry.guid) ? entry.path : AssetDatabase.GUIDToAssetPath(entry.guid);
        static Entry Copy(Entry entry) => JsonUtility.FromJson<Entry>(JsonUtility.ToJson(entry));
        static string ImageKey(string local, string remote)
        {
            using (var hash = SHA256.Create()) return string.Concat(hash.ComputeHash(Encoding.UTF8.GetBytes((local ?? "") + "\n" + (remote ?? ""))).Select(x => x.ToString("x2"))) + ".png";
        }
        static string[] ChildGuids(IEnumerable<string> fileGuids, string root) => FolderThumbnailRules.Children(
            fileGuids.Select(AssetDatabase.GUIDToAssetPath), root).Where(AssetDatabase.IsValidFolder)
            .Select(AssetDatabase.AssetPathToGUID).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToArray();
        static string[] ProductChildren(Entry product, string root) => (product.childGuids ?? Array.Empty<string>())
            .Select(AssetDatabase.GUIDToAssetPath).Where(x => AssetDatabase.IsValidFolder(x) &&
                string.Equals(x.Substring(0, x.LastIndexOf('/')), root, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        public static void RegisterProductImport(string root, LibraryItem item, string[] files)
        {
            Ensure();
            if (item == null || string.IsNullOrEmpty(root) || !root.StartsWith("Assets/", StringComparison.Ordinal) || !AssetDatabase.IsValidFolder(root)) return;
            string rootGuid = AssetDatabase.AssetPathToGUID(root);
            var ownedFiles = files.Where(x => !string.IsNullOrEmpty(x) && FolderThumbnailRules.Under(x, root)).ToArray();
            if (ownedFiles.Length == 0) return;
            var related = store.entries.Where(x => x.rootGuid == rootGuid || CurrentPath(x) == root ||
                (string.IsNullOrEmpty(x.rootGuid) && FolderThumbnailRules.Under(CurrentPath(x), root))).ToArray();
            // Work on copies: no binding or provenance changes until the single save succeeds.
            var products = store.products.Select(Copy).ToList();
            foreach (var old in related)
            {
                var product = products.FirstOrDefault(x => x.rootGuid == rootGuid && x.owner == old.owner);
                if (product == null) { product = Copy(old); product.rootGuid = rootGuid; products.Add(product); }
                product.fileGuids = (product.fileGuids ?? Array.Empty<string>()).Concat(old.fileGuids ?? Array.Empty<string>()).Distinct().ToArray();
                product.childGuids = (product.childGuids ?? Array.Empty<string>()).Concat(ChildGuids(product.fileGuids, root)).Distinct().ToArray();
                if (CurrentPath(old) != root)
                {
                    product.childrenOnly = true;
                    // Preserve old assignments even if their recorded files were deleted.
                    product.childGuids = product.childGuids.Concat(new[] { old.guid }).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToArray();
                }
            }
            var current = products.FirstOrDefault(x => x.rootGuid == rootGuid && x.owner == item.id);
            if (current == null) { current = new Entry { rootGuid = rootGuid, owner = item.id, kind = "product" }; products.Add(current); }
            current.local = item.thumbnailPath; current.remote = item.thumbnailUrl; current.image = ImageKey(current.local, current.remote);
            current.fileGuids = (current.fileGuids ?? Array.Empty<string>()).Concat(ownedFiles.Select(AssetDatabase.AssetPathToGUID))
                .Where(x => !string.IsNullOrEmpty(x)).Distinct().ToArray();
            var group = products.Where(x => x.rootGuid == rootGuid).ToArray();
            bool split = group.Length > 1 || group.Any(x => x.childrenOnly);
            foreach (var product in group)
            {
                product.childrenOnly = split;
                product.childGuids = (product.childGuids ?? Array.Empty<string>()).Concat(ChildGuids(product.fileGuids, root)).Distinct().ToArray();
            }
            var children = group.ToDictionary(x => x.owner, x => ProductChildren(x, root));
            var entries = store.entries.Except(related).ToList();
            foreach (var product in group)
            {
                if (string.IsNullOrEmpty(product.local) && string.IsNullOrEmpty(product.remote)) continue;
                // Files need not overlap: two products touching the same child makes it shared.
                var destinations = split ? children[product.owner].Where(path => !group.Any(other =>
                    other.owner != product.owner && children[other.owner].Contains(path, StringComparer.OrdinalIgnoreCase))).ToArray() : new[] { root };
                foreach (string destination in destinations)
                {
                    // Assignments managed under a different root are not replaced automatically.
                    if (entries.Any(x => string.Equals(CurrentPath(x), destination, StringComparison.OrdinalIgnoreCase))) continue;
                    var entry = Copy(product);
                    entry.guid = AssetDatabase.AssetPathToGUID(destination); entry.path = destination;
                    entries.Add(entry);
                }
            }
            var previous = store.entries; var previousProducts = store.products;
            try { store.entries = entries; store.products = products; Save(); }
            catch { store.entries = previous; store.products = previousProducts; throw; }
            byGuid.Clear(); pendingImages.Clear();
            foreach (var entry in store.entries)
            {
                if (!string.IsNullOrEmpty(entry.guid)) byGuid[entry.guid] = entry;
                if (!diskImages.Contains(entry.image)) pendingImages.Add(entry);
            }
            EditorApplication.RepaintProjectWindow();
        }
        public static string PreferredProductChild(string[] ownedFiles, string root)
        {
            // Only folders touched by this product are candidates. Inspect their current
            // contents as well, so a prefab imported earlier through Unity is considered.
            var files = new List<string>();
            var pending = new Stack<string>(FolderThumbnailRules.Children(ownedFiles, root));
            int visited = 0;
            while (pending.Count > 0)
            {
                string folder = pending.Pop(), full = DirectImporter.Full(folder);
                if (!Directory.Exists(full)) continue;
                foreach (string entry in Directory.EnumerateFileSystemEntries(full))
                {
                    if (++visited > 100000) throw new IOException("サムネイル候補のファイル数が上限を超えました: " + root);
                    if (SafePaths.IsReparse(entry)) throw new IOException("リンクされたフォルダはサムネイルの自動判定に使用できません: " + folder);
                    string path = folder + "/" + Path.GetFileName(entry);
                    if (Directory.Exists(entry)) pending.Push(path);
                    else if (!path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) files.Add(path);
                }
            }
            return FolderThumbnailRules.PreferredChild(files, root);
        }
        static void CheckOwner(string path, string owner)
        {
            if (!AssetDatabase.IsValidFolder(path)) throw new IOException("フォルダが見つかりません: " + path);
            var entry = Assignment(path);
            if (entry != null && entry.owner != owner) throw new InvalidOperationException("別の商品の画像を保持しました: " + path);
        }
        static Texture2D Get(Entry entry) => Get(entry, out _);
        static Texture2D Get(Entry entry, out string status)
        {
            if (cache == null) cache = new ThumbnailCache(EditorApplication.RepaintProjectWindow);
            bool saved = diskImages.Contains(entry.image);
            var texture = cache.Get(saved ? Path.Combine(Root, entry.image) : entry.local, entry.remote, allowRemote, out status);
            if (!saved && status != ThumbnailCache.FailedStatus && status != ThumbnailCache.MissingStatus) pendingImages.Add(entry);
            return texture;
        }
        static void Draw(string guid, Rect rect)
        {
            if (Event.current.type != EventType.Repaint) return;
            Ensure(); if (!store.enabled || !byGuid.TryGetValue(guid, out var entry)) return;
            var texture = Get(entry); if (texture == null) return;
            bool grid = rect.height > 20;
            float size = grid ? Mathf.Min(rect.width, rect.height - 16) : 16;
            var imageRect = new Rect(grid ? rect.center.x - size / 2 : rect.x, rect.y, size, size);
            EditorGUI.DrawRect(imageRect, EditorGUIUtility.isProSkin ? new Color32(51, 51, 51, 255) : new Color32(200, 200, 200, 255));
            GUI.DrawTexture(imageRect, texture, ScaleMode.ScaleToFit);
        }
        static void Tick()
        {
            if (store == null || !store.enabled || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (EditorApplication.timeSinceStartup >= nextResolve)
            {
                nextResolve = EditorApplication.timeSinceStartup + 2; bool changed = false;
                foreach (var entry in store.entries.Where(x => string.IsNullOrEmpty(x.guid)))
                {
                    if (!AssetDatabase.IsValidFolder(entry.path)) continue;
                    entry.guid = AssetDatabase.AssetPathToGUID(entry.path);
                    if (!string.IsNullOrEmpty(entry.guid)) { byGuid[entry.guid] = entry; changed = true; }
                }
                if (changed) { Save(); EditorApplication.RepaintProjectWindow(); }
            }
            foreach (var entry in pendingImages.Take(32).ToArray())
            {
                if (diskImages.Contains(entry.image)) { pendingImages.Remove(entry); continue; }
                var texture = Get(entry, out string status);
                if (texture == null)
                {
                    if (status == ThumbnailCache.FailedStatus || status == ThumbnailCache.MissingStatus) pendingImages.Remove(entry);
                    continue;
                }
                try
                {
                    var previous = RenderTexture.active;
                    float scale = Mathf.Min(1, 128f / Mathf.Max(texture.width, texture.height));
                    int width = Mathf.Max(1, Mathf.RoundToInt(texture.width * scale)), height = Mathf.Max(1, Mathf.RoundToInt(texture.height * scale));
                    var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    Texture2D small = null;
                    try
                    {
                        Graphics.Blit(texture, target); RenderTexture.active = target;
                        small = new Texture2D(width, height, TextureFormat.RGBA32, false);
                        small.ReadPixels(new Rect(0, 0, width, height), 0, 0); small.Apply();
                        Directory.CreateDirectory(Root);
                        string destination = Path.Combine(Root, entry.image), temporary = destination + ".tmp";
                        try
                        {
                            File.WriteAllBytes(temporary, small.EncodeToPNG());
                            if (File.Exists(destination)) File.Replace(temporary, destination, null); else File.Move(temporary, destination);
                        }
                        finally { if (File.Exists(temporary)) File.Delete(temporary); }
                        diskImages.Add(entry.image);
                    }
                    finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); if (small != null) UnityEngine.Object.DestroyImmediate(small); }
                }
                catch (Exception ex) { Debug.LogWarning("フォルダ画像を保存できません: " + ex.Message); }
                pendingImages.Remove(entry); break; // At most one small PNG per frame.
            }
        }
    }
}
