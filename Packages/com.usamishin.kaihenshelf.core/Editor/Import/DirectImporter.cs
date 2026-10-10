using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace KaihenShelf
{
    [Serializable] public sealed class DirectImportEntry
    {
        public StagedAsset asset;
        public string destination, beforeAsset, beforeMeta, conflict = "";
        public bool selected = true, update, unchanged, mergeFolder;
    }
    [Serializable] public sealed class DirectImportPlan
    {
        public DirectPackageStage stage;
        public string thumbnailProductFolder = "";
        public List<DirectImportEntry> entries = new List<DirectImportEntry>();
    }
    public static class DirectImporter
    {
        static readonly StringComparison Comparison = StringComparison.OrdinalIgnoreCase;
        public static string Full(string path)
        {
            if (path != "Assets") SafePaths.AssetPath(path, "");
            string root = Path.GetFullPath(UnityEngine.Application.dataPath);
            string full = Path.GetFullPath(Path.Combine(LocalStore.ProjectRoot, path));
            // Unity's Windows file APIs can still fail beyond MAX_PATH even when
            // the managed runtime accepts long paths. Reserve room for '.meta'.
            if (Path.DirectorySeparatorChar == '\\' && full.Length + 5 >= 260)
                throw new IOException("配置先のパスが長すぎます。プロジェクトの場所、インポート先、またはフォルダ名を短くしてください: " + path);
            if (!full.Equals(root, Comparison) && !full.StartsWith(root + Path.DirectorySeparatorChar, Comparison)) throw new IOException("Assets外は変更できません。");
            for (string part = full; part.Length >= root.Length; part = Path.GetDirectoryName(part))
                if ((File.Exists(part) || Directory.Exists(part)) && SafePaths.IsReparse(part)) throw new IOException("リンクされた配置先は使用できません: " + path);
            return full;
        }
        static string Fingerprint(string path) { return File.Exists(path) ? DirectPackageStage.Hash(path) : ""; }
        static string Existing(string guid)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            return !string.IsNullOrEmpty(path) && (File.Exists(path) || Directory.Exists(path)) ? path : "";
        }
        public static DirectImportPlan Plan(DirectPackageStage stage, string target)
        {
            var plan = new DirectImportPlan { stage = stage };
            foreach (var asset in stage.assets)
            {
                var entry = new DirectImportEntry { asset = asset, destination = SafePaths.AssetPath(asset.path, target) };
                string existing = Existing(asset.guid);
                // Updates follow the GUID's current location, including shared dependencies.
                if (!asset.folder && existing.Length > 0) { entry.destination = existing; entry.update = true; }
                try
                {
                    string full = Full(entry.destination);
                    entry.beforeAsset = Fingerprint(full); entry.beforeMeta = Fingerprint(full + ".meta");
                    if (asset.folder)
                    {
                        if (File.Exists(full)) throw new IOException("同名ファイルあり");
                        if (!Directory.Exists(full) && File.Exists(full + ".meta")) throw new IOException("本体のないmetaあり");
                        entry.mergeFolder = Directory.Exists(full) || existing.Length > 0;
                    }
                    else
                    {
                        if (Directory.Exists(full)) throw new IOException("同名フォルダーあり");
                        if (File.Exists(full) || File.Exists(full + ".meta"))
                        {
                            if (!File.Exists(full) || DirectPackageStage.MetaGuid(full + ".meta") != asset.guid) throw new IOException("別GUIDの同名ファイルあり（上書きしません）");
                            entry.update = true;
                        }
                        entry.unchanged = entry.beforeAsset == asset.assetHash && entry.beforeMeta == asset.metaHash;
                    }
                    for (string parent = Path.GetDirectoryName(full); parent.Length > UnityEngine.Application.dataPath.Length; parent = Path.GetDirectoryName(parent))
                        if (File.Exists(parent) || (!Directory.Exists(parent) && File.Exists(parent + ".meta"))) throw new IOException("親フォルダーとファイルが競合しています");
                }
                catch (Exception ex) { entry.conflict = ex.Message; }
                plan.entries.Add(entry);
            }
            foreach (var group in plan.entries.GroupBy(x => x.destination, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1))
                foreach (var entry in group) entry.conflict = "複数のアセットが同じ配置先になります";
            return plan;
        }
        static bool NeededFolder(DirectImportPlan plan, DirectImportEntry folder)
        {
            return plan.entries.Any(x => !x.asset.folder && x.selected && !x.unchanged
                && x.destination.StartsWith(folder.destination.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase));
        }
        public static IEnumerable<DirectImportEntry> Chosen(DirectImportPlan plan)
        {
            return plan.entries.Where(x => x.asset.folder ? NeededFolder(plan, x) : x.selected && !x.unchanged);
        }
        public static void Validate(DirectImportPlan plan)
        {
            var chosen = Chosen(plan).ToArray();
            foreach (var entry in chosen)
            {
                if (entry.conflict.Length > 0) throw new IOException(entry.conflict + ": " + entry.destination);
                string full = Full(entry.destination);
                if (Fingerprint(full) != entry.beforeAsset || Fingerprint(full + ".meta") != entry.beforeMeta || (!entry.asset.folder && Directory.Exists(full)))
                    throw new IOException("確認後に配置先が変更されました。導入をやり直してください: " + entry.destination);
                if (DirectPackageStage.Hash(plan.stage.Payload(entry.asset, true)) != entry.asset.metaHash || (!entry.asset.folder && DirectPackageStage.Hash(plan.stage.Payload(entry.asset, false)) != entry.asset.assetHash))
                    throw new IOException("展開したデータが変更されています: " + entry.asset.path);
                string current = Existing(entry.asset.guid);
                if (!entry.asset.folder && current.Length > 0 && !current.Equals(entry.destination, Comparison)) throw new IOException("確認後にGUIDの配置が変わりました: " + entry.asset.path);
            }
            var selectedFiles = new HashSet<string>(chosen.Where(x => !x.asset.folder).Select(x => x.destination), StringComparer.OrdinalIgnoreCase);
            foreach (var entry in chosen)
                for (string parent = entry.destination; parent.Contains('/'); parent = parent.Substring(0, parent.LastIndexOf('/')))
                    if (parent != entry.destination && selectedFiles.Contains(parent)) throw new IOException("ファイルとフォルダーの配置が競合しています: " + parent);
        }
        sealed class Written
        {
            public string destination, backup, hash;
        }
        public static int Execute(DirectImportPlan plan)
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("コンパイル・更新・再生が終わるまでお待ちください。");
            Validate(plan);
            var chosen = Chosen(plan).OrderBy(x => x.asset.folder ? 0 : 1).ThenBy(x => x.destination.Length).ToArray();
            long backupBytes = chosen.Sum(x => { string p = Full(x.destination); return (File.Exists(p) ? new FileInfo(p).Length : 0) + (File.Exists(p + ".meta") ? new FileInfo(p + ".meta").Length : 0); });
            DirectPackageStage.RequireSpace(LocalStore.ProjectRoot, checked(backupBytes + plan.stage.expandedBytes * 2));
            var written = new List<Written>(); var created = new List<string>();
            string journal = Path.Combine(LocalStore.ProjectCacheRoot, "DirectImports", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".json");
            LocalStore.Write(journal, plan);
            EditorApplication.LockReloadAssemblies(); AssetDatabase.DisallowAutoRefresh();
            bool editing = false;
            try
            {
                AssetDatabase.StartAssetEditing(); editing = true;
                Action<string> ensure = null;
                ensure = path =>
                {
                    if (Directory.Exists(path)) return;
                    ensure(Path.GetDirectoryName(path)); Directory.CreateDirectory(path); created.Add(path);
                };
                Action<string, string, string> write = (source, destination, hash) =>
                {
                    ensure(Path.GetDirectoryName(destination));
                    string backup = File.Exists(destination) ? Path.Combine(plan.stage.directory, "backup-" + written.Count) : "";
                    if (backup.Length > 0) File.Copy(destination, backup, false);
                    var item = new Written { destination = destination, backup = backup, hash = hash };
                    using (var output = new FileStream(destination, backup.Length > 0 ? FileMode.Open : FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        written.Add(item); output.SetLength(0);
                        using (var input = File.OpenRead(source)) input.CopyTo(output);
                    }
                };
                foreach (var entry in chosen)
                {
                    string full = Full(entry.destination);
                    if (Fingerprint(full) != entry.beforeAsset || Fingerprint(full + ".meta") != entry.beforeMeta)
                        throw new IOException("配置直前にアセットが変更されました: " + entry.destination);
                    if (entry.asset.folder)
                    {
                        ensure(full);
                        if (!entry.mergeFolder) write(plan.stage.Payload(entry.asset, true), full + ".meta", entry.asset.metaHash);
                    }
                    else
                    {
                        write(plan.stage.Payload(entry.asset, false), full, entry.asset.assetHash);
                        write(plan.stage.Payload(entry.asset, true), full + ".meta", entry.asset.metaHash);
                    }
                }
                foreach (var item in written)
                    if (Fingerprint(item.destination) != item.hash) throw new IOException("配置後の内容が一致しません: " + item.destination);
                File.WriteAllText(journal + ".result", "Placed: " + chosen.Count(x => !x.asset.folder));
            }
            catch (Exception ex)
            {
                var errors = new List<string>();
                foreach (var item in written.AsEnumerable().Reverse())
                    try { if (item.backup.Length > 0) File.Copy(item.backup, item.destination, true); else if (File.Exists(item.destination)) File.Delete(item.destination); }
                    catch (Exception rollback) { errors.Add(rollback.Message); }
                foreach (string folder in created.AsEnumerable().Reverse())
                    try { if (!Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder); } catch (Exception rollback) { errors.Add(rollback.Message); }
                File.WriteAllText(journal + ".result", "Failed: " + ex.Message + "\nRollback: " + string.Join("\n", errors));
                if (errors.Count > 0) { plan.stage.preserveForRecovery = true; throw new IOException("復元できない項目があります。バックアップを保持しました: " + plan.stage.directory + "\n" + string.Join("\n", errors), ex); }
                throw;
            }
            finally
            {
                try { if (editing) AssetDatabase.StopAssetEditing(); }
                catch { plan.stage.preserveForRecovery = true; throw; }
                finally { AssetDatabase.AllowAutoRefresh(); EditorApplication.UnlockReloadAssemblies(); }
            }
            AssetDatabase.Refresh();
            return chosen.Count(x => !x.asset.folder);
        }
    }
}
