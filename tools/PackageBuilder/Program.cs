using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KaihenShelf;

string root = Path.GetFullPath(args.Length > 0 ? args[0] : "../..");
string source = Path.Combine(root, "Packages", "com.usamishin.kaihenshelf.core");
string output = Path.Combine(root, "artifacts");
Directory.CreateDirectory(output);
using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(source, "package.json")));
string version = manifest.RootElement.GetProperty("version").GetString();
string packageId = manifest.RootElement.GetProperty("name").GetString();
string destination = "Assets/KaihenShelf";
string GuidFor(string relative) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(packageId + "/" + relative))).Substring(0, 32).ToLowerInvariant();
string Meta(string relative, bool folder)
{
    string importer = Path.GetExtension(relative) == ".cs" ? "MonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n" : Path.GetExtension(relative) == ".asmdef" ? "AssemblyDefinitionImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n" : "DefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n";
    return "fileFormatVersion: 2\nguid: " + GuidFor(relative) + "\n" + (folder ? "folderAsset: yes\n" : "") + importer;
}
var utf8 = new UTF8Encoding(false);
var sourceFiles = Directory.GetFiles(source, "*", SearchOption.AllDirectories).Where(x => !x.EndsWith(".meta", StringComparison.Ordinal)).OrderBy(x => x, StringComparer.Ordinal).ToArray();
var sourceDirs = Directory.GetDirectories(source, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal).ToArray();
foreach (string path in sourceFiles.Concat(sourceDirs))
{
    string relative = Path.GetRelativePath(source, path).Replace('\\', '/');
    if (!File.Exists(path + ".meta")) File.WriteAllText(path + ".meta", Meta(relative, Directory.Exists(path)), utf8);
}
string artifact = Path.Combine(output, "KaihenShelf-" + version + ".unitypackage");
using (var file = File.Create(artifact))
using (var gzip = new GZipStream(file, CompressionLevel.Optimal))
{
    void Add(string relative, string path, bool folder)
    {
        byte[] meta = relative.Length == 0 && !File.Exists(path + ".meta") ? utf8.GetBytes(Meta("", true)) : File.ReadAllBytes(path + ".meta");
        string guid = System.Text.RegularExpressions.Regex.Match(utf8.GetString(meta), @"(?m)^guid:\s*([0-9a-f]{32})").Groups[1].Value;
        if (guid.Length != 32) throw new InvalidDataException("Invalid GUID: " + path);
        PackageArchive.WriteEntry(gzip, guid + "/pathname", utf8.GetBytes(destination + (relative.Length == 0 ? "" : "/" + relative)));
        PackageArchive.WriteEntry(gzip, guid + "/asset.meta", meta);
        if (!folder) PackageArchive.WriteFile(gzip, guid + "/asset", path);
    }
    Add("", source, true);
    foreach (string path in sourceDirs) Add(Path.GetRelativePath(source, path).Replace('\\', '/'), path, true);
    foreach (string path in sourceFiles.Where(x => Path.GetFileName(x) != "package.json")) Add(Path.GetRelativePath(source, path).Replace('\\', '/'), path, false);
    gzip.Write(new byte[1024]);
}
var report = PackageArchive.Inspect(artifact);
string zipPath = Path.Combine(output, packageId + "-" + version + ".zip");
using (var zipFile = File.Create(zipPath))
using (var zip = new ZipArchive(zipFile, ZipArchiveMode.Create))
foreach (string path in Directory.GetFiles(source, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
{
    var entry = zip.CreateEntry(Path.GetRelativePath(source, path).Replace('\\', '/'), CompressionLevel.Optimal);
    entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
    using var input = File.OpenRead(path); using var destinationStream = entry.Open(); input.CopyTo(destinationStream);
}
var hashes = new[] { artifact, zipPath }.Select(path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant() + "  " + Path.GetFileName(path));
File.WriteAllLines(Path.Combine(output, "SHA256SUMS.txt"), hashes, utf8);
Console.WriteLine($"Built {artifact} ({report.assets} assets, validated)");
Console.WriteLine($"Built {zipPath}");
