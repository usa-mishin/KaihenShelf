using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace KaihenShelf
{
    [InitializeOnLoad]
    public static class ImportCoordinator
    {
        static Task<Prepared> preparation;
        static CancellationTokenSource cancellation;
        static Prepared waiting;
        public static string Status { get; private set; } = "";
        public static bool Busy { get { return preparation != null || waiting != null || BatchConflictWindow.Pending || DirectImportWindow.Pending || BatchPreviewRefresh.Pending; } }
        internal static void BatchPreviewFinished(bool success)
        { Status = Status.Replace("プレビューを更新しています。", success ? "プレビュー更新完了。" : "プレビュー更新に失敗しました。Consoleを確認してください。"); }
        public static bool CanCancelPreparation { get { return preparation != null || waiting != null; } }
        sealed class Prepared { public ImportRecord record; public DirectPackageStage stage; }
        static ImportCoordinator()
        {
            // Discard the obsolete post-import workflow marker without touching imported assets.
            SessionState.EraseString("KaihenShelf.ActiveImport");
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload += StopPreparation;
            EditorApplication.quitting += StopPreparation;
        }
        public static void Begin(string source, string itemId, string target, string productName = "")
        {
            if (Busy) throw new InvalidOperationException("現在のインポートが終了するまでお待ちください。");
            string cache = Path.Combine(LocalStore.ProjectCacheRoot, "Staging");
            cancellation = new CancellationTokenSource(); var token = cancellation.Token;
            Status = "パッケージを安全検査しています…";
            preparation = Task.Run(() =>
            {
                if (!string.IsNullOrEmpty(target)) SafePaths.Relative(target);
                var prepared = DirectPackageStage.Prepare(source, cache, token);
                if (token.IsCancellationRequested) { prepared.Cleanup(); token.ThrowIfCancellationRequested(); }
                return new Prepared { stage = prepared, record = new ImportRecord { id = Guid.NewGuid().ToString("N"), itemId = itemId, productName = productName, source = source, cache = prepared.directory, target = "Assets/" + target, utc = DateTime.UtcNow.ToString("o"), status = "確認待ち" } };
            }, token);
        }
        public static void BeginBatch(string[] sources, string itemId, string target, string productName = "")
        {
            if (Busy) throw new InvalidOperationException("現在のインポートが終了するまでお待ちください。");
            var paths = sources.ToArray();
            string cache = Path.Combine(LocalStore.ProjectCacheRoot, "Staging");
            cancellation = new CancellationTokenSource(); var token = cancellation.Token;
            Status = paths.Length + "個のUnityPackageをまとめて検査しています…";
            preparation = Task.Run(() =>
            {
                if (!string.IsNullOrEmpty(target)) SafePaths.Relative(target);
                var stage = BatchPackageStage.Prepare(paths, cache, token, allowChoices: true);
                if (token.IsCancellationRequested) { stage.Cleanup(); token.ThrowIfCancellationRequested(); }
                return new Prepared { stage = stage, record = new ImportRecord { itemId = itemId, productName = productName, target = "Assets/" + target } };
            }, token);
        }
        public static void BeginTextures(string source, string itemId, string target, string shop, string productName = "")
        {
            if (Busy) throw new InvalidOperationException("現在のインポートが終了するまでお待ちください。");
            string cache = Path.Combine(LocalStore.ProjectCacheRoot, "Staging");
            string assets = Application.dataPath;
            cancellation = new CancellationTokenSource(); var token = cancellation.Token;
            Status = "フォルダ内のテクスチャを準備しています…";
            preparation = Task.Run(() =>
            {
                var stage = TextureFolderStage.Prepare(source, assets, cache, target, shop, token);
                if (token.IsCancellationRequested) { stage.Cleanup(); token.ThrowIfCancellationRequested(); }
                return new Prepared { stage = stage, record = new ImportRecord { id = Guid.NewGuid().ToString("N"), itemId = itemId, productName = productName, source = source, cache = stage.directory, target = "Assets/", utc = DateTime.UtcNow.ToString("o"), status = "確認待ち" } };
            }, token);
        }
        static void StopPreparation()
        {
            cancellation?.Cancel(); waiting?.stage.Cleanup(); waiting = null;
            var task = preparation;
            if (task != null) task.ContinueWith(done => { if (done.Status == TaskStatus.RanToCompletion) done.Result.stage.Cleanup(); }, TaskScheduler.Default);
        }
        public static void CancelPreparation() { if (!CanCancelPreparation) return; cancellation?.Cancel(); waiting?.stage.Cleanup(); waiting = null; Status = "準備を取り消しました。"; }
        public static void DirectConfirmationClosed() { Status = "インポートを取り消しました。"; }
        internal static void ConflictsResolved() { Status = "インポート内容と配置先を確認してください。"; }
        public static void DirectNothingToImport() { Status = "すべてのアセットが導入済みです。"; }
        static void Update()
        {
            if (preparation != null && preparation.IsCompleted)
            {
                var finished = preparation; preparation = null;
                if (finished.IsCanceled || cancellation != null && cancellation.IsCancellationRequested)
                {
                    if (finished.Status == TaskStatus.RanToCompletion) finished.Result.stage.Cleanup();
                    Status = "準備を取り消しました。";
                }
                else if (finished.IsFaulted) { Status = "エラー: " + finished.Exception.GetBaseException().Message; Debug.LogWarning("KaihenShelf: " + Status); }
                else waiting = finished.Result;
                cancellation?.Dispose(); cancellation = null;
            }
            if (waiting == null || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            var ready = waiting; waiting = null;
            try
            {
                if (ready.stage.batchConflicts.Count > 0)
                {
                    BatchConflictWindow.Open(ready.stage, ready.record);
                    Status = "共通ファイルの内容が異なります。採用するパッケージを選択してください。";
                }
                else
                {
                    DirectImportWindow.Open(ready.stage, ready.record);
                    Status = "インポート内容と配置先を確認してください。";
                }
            }
            catch (Exception ex) { ready.stage.Cleanup(); Status = "エラー: " + ex.Message; }
        }
        public static string CommitDirect(ImportRecord record, DirectImportPlan plan)
        {
            if (plan.stage.sources.Count > 0)
            { Status = BatchImporter.Commit(plan, record); return Status; }
            record.utc = DateTime.UtcNow.ToString("o");
            record.destinationFolder = ImportHistoryUtility.CommonFolder(DirectImporter.Chosen(plan).Where(x => !x.asset.folder).Select(x => x.destination));
            record.status = "配置中"; Record(record);
            try
            {
                int count = DirectImporter.Execute(plan);
                record.importedCount = count; record.hasImportedCount = true;
                record.destinationFolderGuid = AssetDatabase.AssetPathToGUID(record.destinationFolder);
                try { FolderThumbnails.RegisterImport(record, plan); }
                catch (Exception ex) { Debug.LogWarning("インポートは完了しましたが、フォルダ画像の設定を一部スキップしました: " + ex.Message); }
                record.status = "配置完了";
                record.message = count + "件を指定先に配置しました。Unityの読み込み・コンパイル結果はConsoleで確認できます。";
                Record(record); plan.stage.Cleanup(); Status = record.message; return record.message;
            }
            catch (Exception ex)
            {
                record.status = "要確認"; record.message = ex.Message; Record(record); throw;
            }
        }
        static void Record(ImportRecord record)
        {
            var history = LocalStore.Read<ImportHistory>(LocalStore.HistoryPath) ?? new ImportHistory();
            history.records.RemoveAll(x => x.id == record.id); history.records.Insert(0, record);
            LocalStore.Write(LocalStore.HistoryPath, history);
        }
    }
}
