using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;

namespace KaihenShelf
{
    public static class BatchImporter
    {
        public static void ValidateDependencies(DirectImportPlan plan)
        {
            var incoming = new HashSet<string>(plan.entries.Where(e => e.selected).Select(e => e.asset.guid));
            // Text-serialized material/shader references; texture omissions can be optional in saved shader properties.
            var reference = new Regex(@"fileID:\s*(?:2100000|4800000)\s*,\s*guid:\s*([0-9a-f]{32})");
            foreach (var entry in plan.entries.Where(e => e.selected && !e.asset.folder && (e.asset.path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) || e.asset.path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase) || e.asset.path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))))
            {
                bool meta = entry.asset.path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase);
                string text = File.ReadAllText(plan.stage.Payload(entry.asset, meta));
                foreach (Match match in reference.Matches(text))
                {
                    string guid = match.Groups[1].Value;
                    if (!guid.StartsWith("0000000000000000") && !incoming.Contains(guid) && string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(guid)))
                        throw new IOException("必要なマテリアルまたはシェーダーが見つかりません。関連パッケージを選択するか、必要なシェーダーを先に導入してください。\n" + entry.asset.path + "\nGUID: " + guid);
                }
            }
        }
        public static string Commit(DirectPackageStage stage, ImportRecord request)
        { return Commit(DirectImporter.Plan(stage, request.target.Substring(7)), request); }
        public static string Commit(DirectImportPlan plan, ImportRecord request)
        {
            // Use the exact plan reviewed in the confirmation window, including deselections.
            var stage = plan.stage;
            DirectImporter.Validate(plan); ValidateDependencies(plan);
            var changed = new HashSet<string>(DirectImporter.Chosen(plan).Where(e => !e.asset.folder).Select(e => e.asset.guid));
            int count = DirectImporter.Execute(plan); // One write/rollback scope and one import barrier for every selected package.
            // Retain the journal payload/backups if post-placement bookkeeping fails.
            stage.preserveForRecovery = true;
            BatchPreviewRefresh.Enqueue(plan.entries.Where(e => e.selected && !e.asset.folder).Select(e => e.destination));
            var history = LocalStore.Read<ImportHistory>(LocalStore.HistoryPath) ?? new ImportHistory();
            int sourceCount = 0;
            foreach (var source in stage.sources)
            {
                var guids = new HashSet<string>(source.guids);
                var entries = plan.entries.Where(e => e.selected && guids.Contains(e.asset.guid)).ToList();
                if (!entries.Any(e => !e.asset.folder)) continue;
                sourceCount++;
                string folder = ImportHistoryUtility.CommonFolder(entries.Where(e => !e.asset.folder).Select(e => e.destination));
                var record = new ImportRecord {
                    id = Guid.NewGuid().ToString("N"), itemId = request.itemId, productName = request.productName,
                    source = source.path, cache = stage.directory, target = request.target, utc = DateTime.UtcNow.ToString("o"),
                    status = "配置完了", importedCount = entries.Count(e => !e.asset.folder && changed.Contains(e.asset.guid)), hasImportedCount = true,
                    destinationFolder = folder, destinationFolderGuid = AssetDatabase.AssetPathToGUID(folder), message = "一括インポートで配置しました。"
                };
                history.records.Insert(0, record);
                var subplan = new DirectImportPlan { stage = stage, entries = entries, thumbnailProductFolder = plan.thumbnailProductFolder };
                try { FolderThumbnails.RegisterImport(record, subplan); }
                catch (Exception ex) { UnityEngine.Debug.LogWarning("商品サムネイルの設定をスキップしました: " + ex.Message); }
            }
            LocalStore.Write(LocalStore.HistoryPath, history);
            stage.preserveForRecovery = false;
            stage.Cleanup();
            return sourceCount + "個のUnityPackageを一括インポートしました（" + count + "ファイル）。プレビューを更新しています。";
        }
    }
    [InitializeOnLoad] public static class BatchPreviewRefresh
    {
        [Serializable] public sealed class Job { public List<string> paths = new List<string>(); }
        static string JobPath => Path.Combine(LocalStore.ProjectCacheRoot, "BatchPreviewRefresh.json");
        public static bool Pending => File.Exists(JobPath);
        static bool running;
        static double nextCheck;
        static BatchPreviewRefresh() { EditorApplication.update += Update; }
        public static void Enqueue(IEnumerable<string> paths)
        {
            var job = LocalStore.Read<Job>(JobPath) ?? new Job();
            job.paths = job.paths.Concat(paths).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            LocalStore.Write(JobPath, job); nextCheck = EditorApplication.timeSinceStartup + 1;
        }
        static void Update()
        {
            if (running || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.timeSinceStartup < nextCheck) return;
            nextCheck = EditorApplication.timeSinceStartup + 2;
            if (!File.Exists(JobPath)) return;
            running = true;
            try
            {
                var job = LocalStore.Read<Job>(JobPath);
                // Save only imported materials, never unrelated dirty scenes/assets.
                foreach (string path in job.paths.Where(p => p.EndsWith(".mat", StringComparison.OrdinalIgnoreCase)))
                {
                    var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(path);
                    if (asset != null) AssetDatabase.SaveAssetIfDirty(asset);
                }
                // Models were already imported after the entire batch was placed. Reimporting
                // FBX again is expensive and can trigger importer consistency warnings.
                foreach (string path in job.paths.Where(p => p.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)))
                    if (File.Exists(path)) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                File.Delete(JobPath);
                EditorApplication.RepaintProjectWindow();
                ImportCoordinator.BatchPreviewFinished(true);
            }
            catch (Exception ex)
            {
                if (File.Exists(JobPath)) File.Move(JobPath, JobPath + ".failed-" + Guid.NewGuid().ToString("N"));
                ImportCoordinator.BatchPreviewFinished(false);
                UnityEngine.Debug.LogWarning("一括インポート後のプレビュー更新: " + ex.Message);
            }
            finally { running = false; }
        }
    }
}
