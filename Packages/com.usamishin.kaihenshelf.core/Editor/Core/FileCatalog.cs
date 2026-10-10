using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace KaihenShelf
{
    public sealed class CatalogFile
    {
        public string path;
        public string relative;
        public long bytes;
        public DateTime modifiedUtc;
        public string kind;
    }
    public sealed class FileScan
    {
        public List<CatalogFile> files = new List<CatalogFile>();
        public List<string> warnings = new List<string>();
        public List<PackageFileCard> packages = new List<PackageFileCard>();
    }
    public static class FileCatalog
    {
        public static FileScan Scan(string root, CancellationToken token = default)
        {
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
            root = Path.GetFullPath(root);
            if (SafePaths.IsReparse(root)) throw new IOException("リンク先フォルダーは走査できません。");
            var result = new FileScan(); var queue = new Stack<string>(); queue.Push(root);
            int directories = 0;
            while (queue.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                if (++directories > 100000) throw new IOException("フォルダー数が多すぎます。商品単位で登録してください。");
                string dir = queue.Pop();
                try
                {
                    foreach (var entry in new DirectoryInfo(dir).EnumerateFileSystemInfos())
                    {
                        token.ThrowIfCancellationRequested();
                        if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                        if ((entry.Attributes & FileAttributes.Directory) != 0)
                        {
                            if (entry.Name == ".KaihenShelfImportCache" || entry.Name == ".VrcKaihenLibraryImportCache" || entry.Name == ".VrcKaihenManagerImportCache" || entry.Name == ".git") continue;
                            queue.Push(entry.FullName); continue;
                        }
                        if (result.files.Count >= 100000) throw new InvalidDataException("ファイル数が100,000を超えています。");
                        var file = (FileInfo)entry;
                        result.files.Add(new CatalogFile { path = file.FullName, relative = file.FullName.Substring(root.TrimEnd(Path.DirectorySeparatorChar).Length + 1), bytes = file.Length, modifiedUtc = file.LastWriteTimeUtc, kind = Kind(file.Extension) });
                    }
                }
                catch (UnauthorizedAccessException) { result.warnings.Add("アクセスできません: " + dir); }
                catch (DirectoryNotFoundException) { result.warnings.Add("見つかりません: " + dir); }
            }
            result.files = result.files.OrderBy(x => x.relative, StringComparer.OrdinalIgnoreCase).ToList();
            result.packages = PackageFileCards.Build(result.files);
            return result;
        }
        public static string Kind(string extension)
        {
            switch (extension.ToLowerInvariant())
            {
                case ".unitypackage": return "UnityPackage";
                case ".png": case ".jpg": case ".jpeg": case ".tga": case ".exr": return "テクスチャ";
                case ".psd": case ".psb": case ".clip": case ".sai": case ".sai2": return "画像編集データ";
                case ".fbx": case ".obj": case ".blend": case ".mat": return "3Dデータ";
                case ".txt": case ".pdf": case ".md": case ".html": return "ドキュメント";
                default: return "その他";
            }
        }
    }
}
