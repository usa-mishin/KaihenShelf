using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace KaihenShelf
{
    [Serializable] public sealed class BatchAssetVariant
    {
        public string source;
        public StagedAsset asset;
    }
    [Serializable] public sealed class BatchAssetConflict
    {
        public string guid, path;
        public List<BatchAssetVariant> variants = new List<BatchAssetVariant>();
        public int selected = -1;
    }
    [Serializable] public sealed class BatchPackageSource
    {
        public string path;
        public List<string> guids = new List<string>();
    }
    [Serializable] public sealed class StagedAsset
    {
        public string guid, path, assetHash, metaHash;
        public string payloadKey;
        public bool folder;
        public long bytes;
    }
    [Serializable] public sealed class DirectPackageStage
    {
        public string directory;
        public bool containsScripts;
        public bool texturesOnly;
        public long expandedBytes;
        public bool preserveForRecovery;
        public List<StagedAsset> assets = new List<StagedAsset>();
        public List<BatchPackageSource> sources = new List<BatchPackageSource>();
        public List<BatchAssetConflict> batchConflicts = new List<BatchAssetConflict>();
        public string Payload(StagedAsset asset, bool meta) { return Path.Combine(directory, (string.IsNullOrEmpty(asset.payloadKey) ? asset.guid : asset.payloadKey) + (meta ? ".meta" : ".asset")); }
        public const long ReserveBytes = 2L * 1024 * 1024 * 1024;
        public static void RequireSpace(string path, long required)
        {
            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path)));
            if (drive.AvailableFreeSpace < checked(required + ReserveBytes)) throw new IOException("空き容量が不足しています。必要な作業容量に加え、2GiBの空きを確保してください。");
        }
        public static string Hash(string path)
        {
            using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        public static string MetaGuid(string path)
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("metaファイルがないか、大きすぎます: " + Path.GetFileName(path));
            string text = File.ReadAllText(path, new UTF8Encoding(false, true));
            var matches = Regex.Matches(text, @"(?m)^guid:\s*([0-9a-fA-F]{32})\s*$");
            if (matches.Count != 1) throw new InvalidDataException("metaのGUIDが不正です: " + Path.GetFileName(path));
            return matches[0].Groups[1].Value.ToLowerInvariant();
        }
        public static DirectPackageStage Prepare(string source, string stagingRoot, CancellationToken token = default)
        {
            var stage = new DirectPackageStage();
            try
            {
                using (var input = File.Open(source, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var report = PackageArchive.Transform(input, null, "", token);
                    RequireSpace(stagingRoot, checked(report.expandedBytes * 3));
                    Directory.CreateDirectory(stagingRoot);
                    if (SafePaths.IsReparse(stagingRoot)) throw new IOException("一時保存先にリンクは使用できません。");
                    stage.directory = Path.Combine(Path.GetFullPath(stagingRoot), Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(stage.directory);
                    File.WriteAllText(Path.Combine(stage.directory, ".owner"), "KaihenShelf.DirectImport.v1");
                    input.Position = 0;
                    PackageArchive.Transform(input, null, "", token, (name, length) =>
                    {
                        string[] parts = name.Split('/');
                        if (parts[1] != "asset" && parts[1] != "asset.meta") return null;
                        if (parts[1] == "asset.meta" && length > 4 * 1024 * 1024) throw new InvalidDataException("metaファイルが大きすぎます。");
                        string path = Path.Combine(stage.directory, parts[0].ToLowerInvariant() + (parts[1] == "asset.meta" ? ".meta" : ".asset"));
                        return new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    });
                    stage.containsScripts = report.containsScripts; stage.expandedBytes = report.expandedBytes;
                    foreach (var entry in report.manifest)
                    {
                        token.ThrowIfCancellationRequested();
                        var asset = new StagedAsset { guid = entry.guid, path = entry.path };
                        if (asset.path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("metaファイルをアセット本体として導入できません: " + asset.path);
                        string meta = stage.Payload(asset, true), payload = stage.Payload(asset, false);
                        if (MetaGuid(meta) != entry.guid) throw new InvalidDataException("パッケージとmetaのGUIDが一致しません: " + entry.path);
                        asset.folder = Regex.IsMatch(File.ReadAllText(meta), @"(?m)^folderAsset:\s*yes\s*$");
                        if (!asset.folder && !File.Exists(payload)) throw new InvalidDataException("アセット本体がありません: " + entry.path);
                        if (asset.folder && File.Exists(payload) && new FileInfo(payload).Length > 0) throw new InvalidDataException("フォルダーに不正な本体データがあります: " + entry.path);
                        asset.metaHash = Hash(meta); asset.assetHash = asset.folder ? "" : Hash(payload);
                        if (!asset.folder) TextureImportValidation.Check(payload, asset.path, token);
                        asset.bytes = new FileInfo(meta).Length + (asset.folder ? 0 : new FileInfo(payload).Length);
                        stage.assets.Add(asset);
                    }
                    // A file cannot also be an ancestor of another package item.
                    var files = new HashSet<string>(stage.assets.Where(x => !x.folder).Select(x => x.path), StringComparer.OrdinalIgnoreCase);
                    foreach (var asset in stage.assets)
                        for (string parent = asset.path; parent.Contains('/'); parent = parent.Substring(0, parent.LastIndexOf('/')))
                            if (parent != asset.path && files.Contains(parent)) throw new InvalidDataException("ファイルとフォルダーが競合しています: " + parent);
                }
                return stage;
            }
            catch { stage.Cleanup(); throw; }
        }
        public void Cleanup()
        {
            if (preserveForRecovery) return;
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return;
            string full = Path.GetFullPath(directory);
            if (!Guid.TryParseExact(Path.GetFileName(full), "N", out _) || SafePaths.IsReparse(full))
                throw new IOException("一時データの識別を確認できないため削除しません: " + full);
            string owner = File.ReadAllText(Path.Combine(full, ".owner"));
            if (owner != "KaihenShelf.DirectImport.v1" && owner != "VrcKaihenLibrary.DirectImport.v1")
                throw new IOException("一時データの識別を確認できないため削除しません: " + full);
            // Only flat, owned staging files. Never recurse into an unexpected directory or link.
            if (Directory.GetDirectories(full).Length != 0 || Directory.GetFiles(full).Any(SafePaths.IsReparse)) throw new IOException("一時保存先に予期しない項目があります: " + full);
            foreach (string file in Directory.GetFiles(full))
            {
                // Backups of read-only assets inherit that attribute; only clear it on our owned copy.
                File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
                File.Delete(file);
            }
            Directory.Delete(full);
        }
    }
}
