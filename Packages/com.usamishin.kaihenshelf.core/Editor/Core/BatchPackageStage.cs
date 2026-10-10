using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Text;
using System.Text.RegularExpressions;

namespace KaihenShelf
{
    public static class BatchPackageStage
    {
        // Merge before planning: per-package plans cannot see collisions in other pending packages.
        public static DirectPackageStage Prepare(IEnumerable<string> sources, string cache, CancellationToken token = default, bool allowChoices = false)
        {
            var paths = sources.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (paths.Length == 0) throw new InvalidDataException("UnityPackageを選択してください。");
            var merged = new DirectPackageStage { directory = Path.Combine(Path.GetFullPath(cache), Guid.NewGuid().ToString("N")) };
            Directory.CreateDirectory(cache);
            if (SafePaths.IsReparse(cache)) throw new IOException("一時保存先にリンクは使用できません。");
            Directory.CreateDirectory(merged.directory);
            File.WriteAllText(Path.Combine(merged.directory, ".owner"), "KaihenShelf.DirectImport.v1");
            var byGuid = new Dictionary<string, StagedAsset>(StringComparer.OrdinalIgnoreCase);
            var byPath = new Dictionary<string, StagedAsset>(StringComparer.OrdinalIgnoreCase);
            var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var folderAliases = new Dictionary<string, StagedAsset>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (string path in paths)
                {
                    token.ThrowIfCancellationRequested();
                    var source = new BatchPackageSource { path = path };
                    var stage = DirectPackageStage.Prepare(path, cache, token);
                    try
                    {
                        merged.containsScripts |= stage.containsScripts;
                        foreach (var asset in stage.assets)
                        {
                            token.ThrowIfCancellationRequested();
                            source.guids.Add(asset.guid);
                            if (folderAliases.TryGetValue(asset.guid, out var alias))
                            {
                                if (asset.path != alias.path || !asset.folder || asset.metaHash != alias.metaHash)
                                    throw new InvalidDataException("共有フォルダーの設定が異なります: " + asset.path);
                                continue;
                            }
                            if (byGuid.TryGetValue(asset.guid, out var existing))
                            {
                                if (existing.path != asset.path || existing.folder != asset.folder)
                                    throw new InvalidDataException("同じGUIDの配置先またはファイル種別が異なります: " + existing.path + " / " + asset.path);
                                if (existing.assetHash != asset.assetHash || existing.metaHash != asset.metaHash)
                                {
                                    if (!allowChoices) throw new InvalidDataException("同じGUIDで内容が異なります。採用するパッケージの選択が必要です: " + asset.path + "（" + Path.GetFileName(owners[asset.guid]) + " / " + Path.GetFileName(path) + "）");
                                    var conflict = merged.batchConflicts.FirstOrDefault(x => x.guid == asset.guid);
                                    if (conflict == null)
                                    {
                                        conflict = new BatchAssetConflict { guid = asset.guid, path = asset.path };
                                        conflict.variants.Add(new BatchAssetVariant { source = owners[asset.guid], asset = existing });
                                        merged.batchConflicts.Add(conflict);
                                    }
                                    merged.expandedBytes = checked(merged.expandedBytes + asset.bytes);
                                    if (merged.expandedBytes > PackageArchive.MaxExpandedBytes) throw new InvalidDataException("一括インポートのデータ量が上限を超えています。選択を分けてください。");
                                    string meta = stage.Payload(asset, true), payload = stage.Payload(asset, false);
                                    asset.payloadKey = asset.guid + "-variant-" + conflict.variants.Count;
                                    File.Move(meta, merged.Payload(asset, true));
                                    if (!asset.folder) File.Move(payload, merged.Payload(asset, false));
                                    conflict.variants.Add(new BatchAssetVariant { source = path, asset = asset });
                                }
                                continue;
                            }
                            if (byPath.TryGetValue(asset.path, out var samePath))
                            {
                                if (samePath.folder && asset.folder && FolderSettings(merged.Payload(samePath, true)) == FolderSettings(stage.Payload(asset, true)))
                                { folderAliases.Add(asset.guid, asset); continue; }
                                throw new InvalidDataException("同じ配置先に別GUIDのデータがあります: " + asset.path);
                            }
                            merged.expandedBytes = checked(merged.expandedBytes + asset.bytes);
                            if (merged.expandedBytes > PackageArchive.MaxExpandedBytes || merged.assets.Count >= PackageArchive.MaxEntries)
                                throw new InvalidDataException("一括インポートのデータ量が上限を超えています。選択を分けてください。");
                            File.Move(stage.Payload(asset, true), merged.Payload(asset, true));
                            if (!asset.folder) File.Move(stage.Payload(asset, false), merged.Payload(asset, false));
                            merged.assets.Add(asset); byGuid.Add(asset.guid, asset); byPath.Add(asset.path, asset); owners.Add(asset.guid, path);
                        }
                        merged.sources.Add(source);
                    }
                    finally { stage.Cleanup(); }
                }
                var files = new HashSet<string>(merged.assets.Where(a => !a.folder).Select(a => a.path), StringComparer.OrdinalIgnoreCase);
                foreach (var asset in merged.assets)
                    for (string parent = asset.path; parent.Contains('/'); parent = parent.Substring(0, parent.LastIndexOf('/')))
                        if (parent != asset.path && files.Contains(parent)) throw new InvalidDataException("パッケージ間でファイルとフォルダーが競合しています: " + parent);
                if (folderAliases.Count > 0)
                {
                    var candidates = merged.assets.Concat(merged.batchConflicts.SelectMany(c => c.variants.Select(v => v.asset))).Distinct();
                    foreach (var asset in candidates.Where(a => !a.folder))
                        foreach (bool meta in new[] { false, true })
                            RejectFolderReferences(merged.Payload(asset, meta), folderAliases, token);
                }
                return merged;
            }
            catch { merged.Cleanup(); throw; }
        }
        static string FolderSettings(string meta)
        { return Regex.Replace(File.ReadAllText(meta).Replace("\r\n", "\n"), @"(?m)^guid:[^\n]*\n?", ""); }
        static void RejectFolderReferences(string payload, Dictionary<string, StagedAsset> aliases, CancellationToken token)
        {
            // Bounded streaming scan, including alternate payloads and importer metadata.
            // Overlap retains GUIDs that straddle a buffer boundary.
            using (var input = File.OpenRead(payload))
            {
                var bytes = new byte[65536]; string tail = ""; int read;
                while ((read = input.Read(bytes, 0, bytes.Length)) > 0)
                {
                    token.ThrowIfCancellationRequested();
                    string text = tail + Encoding.ASCII.GetString(bytes, 0, read);
                    foreach (Match match in Regex.Matches(text, "[0-9a-fA-F]{32}"))
                        if (aliases.ContainsKey(match.Value)) throw new InvalidDataException("別GUIDの共有フォルダーへの参照があります。個別にインポートしてください: " + aliases[match.Value].path);
                    tail = text.Substring(Math.Max(0, text.Length - 31));
                }
            }
        }
        public static void ResolveChoices(DirectPackageStage stage)
        {
            // Validate every choice before changing the merge used by the import planner.
            foreach (var conflict in stage.batchConflicts)
            {
                if (conflict.selected < 0 || conflict.selected >= conflict.variants.Count)
                    throw new InvalidDataException("採用するパッケージを選択してください: " + conflict.path);
                var asset = conflict.variants[conflict.selected].asset;
                if (asset.guid != conflict.guid || asset.path != conflict.path || !stage.assets.Any(x => x.guid == conflict.guid))
                    throw new InvalidDataException("競合データの識別が一致しません。");
            }
            foreach (var conflict in stage.batchConflicts)
                stage.assets[stage.assets.FindIndex(x => x.guid == conflict.guid)] = conflict.variants[conflict.selected].asset;
        }
    }
}
