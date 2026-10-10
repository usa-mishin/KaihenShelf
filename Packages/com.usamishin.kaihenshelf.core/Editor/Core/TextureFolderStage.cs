using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace KaihenShelf
{
    public static class TextureFolderStage
    {
        public static string NewTextureMeta(string guid)
        {
            if (!Guid.TryParseExact(guid, "N", out _)) throw new InvalidDataException("Invalid texture GUID");
            return "fileFormatVersion: 2\nguid: " + guid + "\nTextureImporter:\n  serializedVersion: 12\n  mipmaps:\n    enableMipMap: 1\n    sRGBTexture: 1\n  isReadable: 0\n  textureFormat: 1\n  maxTextureSize: 2048\n  textureSettings:\n    serializedVersion: 2\n    filterMode: 1\n    aniso: 1\n    mipBias: 0\n    wrapU: 0\n    wrapV: 0\n    wrapW: 0\n  nPOTScale: 1\n  compressionQuality: 50\n  alphaUsage: 1\n  textureType: 0\n  textureShape: 1\n  swizzle: 50462976\n  platformSettings:\n  - serializedVersion: 3\n    buildTarget: DefaultTexturePlatform\n    maxTextureSize: 2048\n    textureFormat: -1\n    textureCompression: 1\n    compressionQuality: 50\n    overridden: 0\n";
        }
        public static string FolderName(string name)
        {
            string result = new string((name ?? "").Select(c => c < 32 || "/\\:*?\"<>|".Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
            if (result.Length == 0) result = "ショップ名なし";
            try { SafePaths.Relative(result); } catch (InvalidDataException) { result = "_" + result; }
            return SafePaths.Relative(result);
        }
        public static void CheckLinks(string path)
        {
            for (string part = Path.GetFullPath(path); !string.IsNullOrEmpty(part); part = Path.GetDirectoryName(part))
                if ((File.Exists(part) || Directory.Exists(part)) && SafePaths.IsReparse(part)) throw new IOException("リンクされたパスは使用できません: " + path);
        }
        public static DirectPackageStage Prepare(string source, string assetsRoot, string cache, string target, string shop, CancellationToken token = default)
        {
            source = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar);
            assetsRoot = Path.GetFullPath(assetsRoot).TrimEnd(Path.DirectorySeparatorChar);
            CheckLinks(source); CheckLinks(assetsRoot); CheckLinks(cache);
            if (source.Equals(assetsRoot, StringComparison.OrdinalIgnoreCase) || source.StartsWith(assetsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || assetsRoot.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new IOException("UnityのAssets内や、その親フォルダはコピー元に指定できません。");
            if (!string.IsNullOrEmpty(target)) SafePaths.Relative(target);
            string destination = (string.IsNullOrEmpty(target) ? "" : target + "/") + FolderName(shop) + "/" + SafePaths.Relative(Path.GetFileName(source));
            var scan = FileCatalog.Scan(source, token);
            if (scan.warnings.Count > 0) throw new IOException(string.Join("\n", scan.warnings));
            var files = scan.files.Where(x => x.kind == "テクスチャ").ToArray();
            if (files.Length == 0) throw new IOException("このフォルダにはテクスチャがありません。");
            long bytes = files.Sum(x => x.bytes);
            if (bytes > PackageArchive.MaxExpandedBytes) throw new IOException("テクスチャの合計が20GiBを超えています。小さいフォルダ単位で取り込んでください。");
            DirectPackageStage.RequireSpace(cache, checked(bytes * 3));
            var stage = new DirectPackageStage { directory = Path.Combine(cache, Guid.NewGuid().ToString("N")), texturesOnly = true, expandedBytes = bytes };
            Directory.CreateDirectory(stage.directory);
            File.WriteAllText(Path.Combine(stage.directory, ".owner"), "KaihenShelf.DirectImport.v1");
            try
            {
                foreach (var file in files)
                {
                    token.ThrowIfCancellationRequested(); CheckLinks(file.path);
                    string relative = destination + "/" + SafePaths.Relative(file.relative.Replace('\\', '/'));
                    string full = Path.Combine(assetsRoot, relative.Replace('/', Path.DirectorySeparatorChar));
                    CheckLinks(full); CheckLinks(full + ".meta");
                    if (Directory.Exists(full) || (!File.Exists(full) && File.Exists(full + ".meta"))) throw new IOException("配置先と競合しています: " + relative);
                    if (File.Exists(full) && !File.Exists(full + ".meta")) throw new IOException("配置先のUnity読み込みが完了していません: " + relative);
                    var asset = new StagedAsset { path = "Assets/" + relative, bytes = file.bytes, guid = File.Exists(full + ".meta") ? DirectPackageStage.MetaGuid(full + ".meta") : Guid.NewGuid().ToString("N") };
                    string payload = stage.Payload(asset, false), meta = stage.Payload(asset, true);
                    using (var input = new FileStream(file.path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var output = new FileStream(payload, FileMode.CreateNew, FileAccess.Write))
                    {
                        if (input.Length != file.bytes) throw new IOException("コピー元が変更されました。再度実行してください。");
                        byte[] buffer = new byte[81920]; int read;
                        while ((read = input.Read(buffer, 0, buffer.Length)) > 0) { token.ThrowIfCancellationRequested(); output.Write(buffer, 0, read); }
                    }
                    TextureImportValidation.Check(payload, file.relative, token);
                    // Never copy product .meta files. Existing project settings/GUIDs remain intact.
                    if (File.Exists(full + ".meta")) File.Copy(full + ".meta", meta, false);
                    else File.WriteAllText(meta, NewTextureMeta(asset.guid), new UTF8Encoding(false));
                    asset.assetHash = DirectPackageStage.Hash(payload); asset.metaHash = DirectPackageStage.Hash(meta);
                    stage.assets.Add(asset);
                }
                return stage;
            }
            catch { stage.Cleanup(); throw; }
        }
    }
}
