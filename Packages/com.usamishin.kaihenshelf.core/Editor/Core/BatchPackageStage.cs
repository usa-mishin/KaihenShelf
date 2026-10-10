using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace KaihenShelf
{
    public static class BatchPackageStage
    {
        // Merge before planning: per-package plans cannot see collisions in other pending packages.
        public static DirectPackageStage Prepare(IEnumerable<string> sources, string cache, CancellationToken token = default)
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
                            if (byGuid.TryGetValue(asset.guid, out var existing))
                            {
                                if (existing.path != asset.path || existing.assetHash != asset.assetHash || existing.metaHash != asset.metaHash)
                                    throw new InvalidDataException("同じGUIDの異なるデータが含まれています。旧版や重複するパッケージの選択を解除してください: " + asset.path + "（" + Path.GetFileName(path) + "）");
                                continue;
                            }
                            if (byPath.ContainsKey(asset.path)) throw new InvalidDataException("同じ配置先に別GUIDのデータがあります: " + asset.path);
                            merged.expandedBytes = checked(merged.expandedBytes + asset.bytes);
                            if (merged.expandedBytes > PackageArchive.MaxExpandedBytes || merged.assets.Count >= PackageArchive.MaxEntries)
                                throw new InvalidDataException("一括インポートのデータ量が上限を超えています。選択を分けてください。");
                            File.Move(stage.Payload(asset, true), merged.Payload(asset, true));
                            if (!asset.folder) File.Move(stage.Payload(asset, false), merged.Payload(asset, false));
                            merged.assets.Add(asset); byGuid.Add(asset.guid, asset); byPath.Add(asset.path, asset);
                        }
                        merged.sources.Add(source);
                    }
                    finally { stage.Cleanup(); }
                }
                var files = new HashSet<string>(merged.assets.Where(a => !a.folder).Select(a => a.path), StringComparer.OrdinalIgnoreCase);
                foreach (var asset in merged.assets)
                    for (string parent = asset.path; parent.Contains('/'); parent = parent.Substring(0, parent.LastIndexOf('/')))
                        if (parent != asset.path && files.Contains(parent)) throw new InvalidDataException("パッケージ間でファイルとフォルダーが競合しています: " + parent);
                return merged;
            }
            catch { merged.Cleanup(); throw; }
        }
    }
}
