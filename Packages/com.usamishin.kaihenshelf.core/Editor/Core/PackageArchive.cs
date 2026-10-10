using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace KaihenShelf
{
    [Serializable] public sealed class PackageAsset
    {
        public string guid, path;
    }
    public sealed class PackageReport
    {
        public int entries;
        public long expandedBytes;
        public int assets;
        public bool containsScripts;
        public List<string> paths = new List<string>();
        public List<PackageAsset> manifest = new List<PackageAsset>();
    }

    public static class SafePaths
    {
        static readonly HashSet<string> Reserved = new HashSet<string>(new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "COM¹", "COM²", "COM³", "LPT¹", "LPT²", "LPT³" }, StringComparer.OrdinalIgnoreCase);
        public static string Relative(string path)
        {
            if (string.IsNullOrEmpty(path) || path.StartsWith("/", StringComparison.Ordinal) || path.Contains("\\")) throw new InvalidDataException("相対パスが不正です: " + path);
            foreach (string part in path.Split('/'))
            {
                if (part.Length == 0 || part == "." || part == ".." || part.EndsWith(".", StringComparison.Ordinal) || part.EndsWith(" ", StringComparison.Ordinal) || part.Any(c => c < 32 || ":*?\"<>|".Contains(c)) || Reserved.Contains(part.Split('.')[0]))
                    throw new InvalidDataException("危険なパスを拒否しました: " + path);
            }
            return path;
        }
        public static string AssetPath(string path, string target)
        {
            Relative(path);
            if (path.StartsWith("Packages/", StringComparison.Ordinal)) throw new InvalidDataException("Packages配下のUPM形式はこのインポート方式では扱えません。配布元の導入手順を利用してください: " + path);
            if (!path.StartsWith("Assets/", StringComparison.Ordinal)) throw new InvalidDataException("Assets 外の配置先を拒否しました: " + path);
            if (!string.IsNullOrEmpty(target)) { Relative(target); path = "Assets/" + target + "/" + path.Substring(7); }
            foreach (string part in path.Split('/'))
                if (part.StartsWith(".", StringComparison.Ordinal) || part.EndsWith("~", StringComparison.Ordinal) || part.Equals("cvs", StringComparison.OrdinalIgnoreCase) || part.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Unityが読み込まない名前が含まれています。名前を変更してください: " + path);
            return path;
        }
        public static bool IsReparse(string path) { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
    }

    // A deliberately small USTAR reader. Optional sinks receive only validated entry names.
    public static class PackageArchive
    {
        public const int MaxEntries = 100000;
        public const long MaxExpandedBytes = 20L * 1024 * 1024 * 1024;
        public const long MinimumExpansionAllowance = 512L * 1024 * 1024;
        public const int MaxExpansionRatio = 200;
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        static readonly HashSet<string> Allowed = new HashSet<string> { "asset", "asset.meta", "pathname", "preview.png" };

        public static PackageReport Inspect(string file, CancellationToken token = default)
        {
            using (var input = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.Read)) return Transform(input, null, "", token);
        }

        public static PackageReport Transform(Stream input, Stream output, string target, CancellationToken token = default, Func<string, long, Stream> entrySink = null)
        {
            if (!string.IsNullOrEmpty(target)) SafePaths.Relative(target);
            long expandedLimit = input.CanSeek
                ? Math.Min(MaxExpandedBytes, Math.Max(MinimumExpansionAllowance, Math.Min(input.Length, MaxExpandedBytes / MaxExpansionRatio) * MaxExpansionRatio))
                : MaxExpandedBytes;
            var report = new PackageReport();
            var entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pathGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var gzip = new GZipStream(input, CompressionMode.Decompress, true))
            using (var writer = output == null ? null : new GZipStream(output, CompressionLevel.Optimal, true))
            {
                byte[] header = new byte[512];
                byte[] buffer = new byte[65536];
                bool end = false;
                while (ReadBlock(gzip, header))
                {
                    token.ThrowIfCancellationRequested();
                    report.expandedBytes = checked(report.expandedBytes + 512);
                    if (report.expandedBytes > expandedLimit) throw new InvalidDataException("パッケージの展開容量上限を超えています。");
                    if (header.All(b => b == 0))
                    {
                        if (!ReadBlock(gzip, header) || header.Any(b => b != 0)) throw new InvalidDataException("TAR終端が不正です。");
                        report.expandedBytes += 512;
                        if (report.expandedBytes > expandedLimit) throw new InvalidDataException("パッケージの展開容量上限を超えています。");
                        // Consume padding to force GZip CRC verification and reject hidden trailing data.
                        int n;
                        while ((n = gzip.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            token.ThrowIfCancellationRequested(); report.expandedBytes += n;
                            if (report.expandedBytes > expandedLimit || buffer.Take(n).Any(b => b != 0)) throw new InvalidDataException("TAR終端後に不正なデータがあります。");
                        }
                        end = true; break;
                    }
                    long expected = Octal(header, 148, 8);
                    long checksum = header.Select((b, i) => i >= 148 && i < 156 ? 32L : b).Sum();
                    if (expected != checksum) throw new InvalidDataException("TARヘッダーのチェックサムが不正です。");
                    string name = Text(header, 0, 100);
                    string prefix = Text(header, 345, 155);
                    if (prefix.Length > 0) name = prefix + "/" + name;
                    int type = header[156];
                    long length = Octal(header, 124, 12);
                    if (++report.entries > MaxEntries) throw new InvalidDataException("TAR項目数が100,000を超えています。");
                    if (length < 0 || length > expandedLimit - report.expandedBytes) throw new InvalidDataException("パッケージの展開容量上限を超えています。");
                    if (type != 0 && type != '0' && type != '5') throw new InvalidDataException("リンク・特殊TAR項目は使用できません。");
                    if (Text(header, 157, 100).Length > 0) throw new InvalidDataException("リンクを拒否しました。");
                    bool directory = type == '5';
                    // Standard tar tools may emit './' and './<guid>/...'. Normalize
                    // only that prefix, then apply the same traversal and duplicate checks.
                    if (directory && (name == "./" || name == ".") && length == 0) continue;
                    if (name.StartsWith("./", StringComparison.Ordinal)) name = name.Substring(2);
                    name = directory ? name.TrimEnd('/') : name;
                    SafePaths.Relative(name);
                    string[] parts = name.Split('/');
                    if (parts[0].Length != 32 || parts[0].Any(c => !Uri.IsHexDigit(c)) || (directory ? parts.Length != 1 || length != 0 : parts.Length != 2 || !Allowed.Contains(parts[1])))
                        throw new InvalidDataException("UnityPackageの項目形式が不正です: " + name);
                    if (!entries.Add(name)) throw new InvalidDataException("重複TAR項目: " + name);
                    groups.Add(parts[0]);
                    report.expandedBytes += length;
                    if (!directory && parts[1] == "pathname")
                    {
                        if (length > 32768) throw new InvalidDataException("pathnameが長すぎます。");
                        byte[] bytes = new byte[(int)length]; ReadExact(gzip, bytes, bytes.Length);
                        string original = Utf8.GetString(bytes).TrimEnd('\0', '\r', '\n');
                        string mapped = SafePaths.AssetPath(original, target);
                        if (!paths.Add(mapped)) throw new InvalidDataException("重複配置先: " + mapped);
                        pathGroups.Add(parts[0]); report.assets++;
                        report.manifest.Add(new PackageAsset { guid = parts[0].ToLowerInvariant(), path = mapped });
                        if (report.paths.Count < 200) report.paths.Add(mapped);
                        string extension = Path.GetExtension(mapped).ToLowerInvariant();
                        if (extension == ".cs" || extension == ".dll" || extension == ".asmdef" || extension == ".rsp") report.containsScripts = true;
                        if (writer != null) WriteEntry(writer, name, Utf8.GetBytes(mapped));
                    }
                    else
                    {
                        if (writer != null) WriteHeader(writer, directory ? name + "/" : name, length, directory);
                        using (var sink = directory ? null : entrySink?.Invoke(name, length))
                        {
                            long left = length;
                            while (left > 0)
                            {
                                token.ThrowIfCancellationRequested();
                                int n = gzip.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
                                if (n == 0) throw new EndOfStreamException("TAR項目が途中で切れています。");
                                writer?.Write(buffer, 0, n); sink?.Write(buffer, 0, n); left -= n;
                            }
                        }
                        if (writer != null) Pad(writer, length);
                    }
                    int padding = (int)((512 - length % 512) % 512);
                    ReadExact(gzip, buffer, padding);
                    report.expandedBytes += padding;
                }
                if (!end || pathGroups.Count == 0 || !groups.SetEquals(pathGroups)) throw new InvalidDataException("pathname またはTAR終端がありません。");
                if (writer != null) writer.Write(new byte[1024], 0, 1024);
            }
            return report;
        }

        // Validated byte-for-byte copy for archive tooling; direct imports use DirectPackageStage.
        public static string PrepareOriginal(string source, string cacheDirectory, CancellationToken token, out PackageReport report)
        {
            Directory.CreateDirectory(cacheDirectory);
            using (var input = File.Open(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                report = Transform(input, null, "", token);
                input.Position = 0;
                string temp = Path.Combine(cacheDirectory, "original-" + Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    string digest;
                    using (var hash = SHA256.Create())
                    using (var output = File.Create(temp))
                    {
                        var bytes = new byte[65536]; int n;
                        while ((n = input.Read(bytes, 0, bytes.Length)) > 0)
                        {
                            token.ThrowIfCancellationRequested(); output.Write(bytes, 0, n); hash.TransformBlock(bytes, 0, n, bytes, 0);
                        }
                        hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                        digest = BitConverter.ToString(hash.Hash).Replace("-", "").ToLowerInvariant();
                    }
                    string path = Path.Combine(cacheDirectory, "original-" + digest + ".unitypackage");
                    token.ThrowIfCancellationRequested();
                    // A prior cache is not trusted: use this freshly validated byte-for-byte copy.
                    if (File.Exists(path)) path = Path.Combine(cacheDirectory, "original-" + digest + "-" + Guid.NewGuid().ToString("N") + ".unitypackage");
                    File.Move(temp, path); return path;
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
        }

        public static string Prepare(string source, string cacheDirectory, string target, CancellationToken token, out PackageReport report)
        {
            Directory.CreateDirectory(cacheDirectory);
            using (var input = File.Open(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                string digest;
                using (var hash = SHA256.Create())
                {
                    var buffer = new byte[65536]; int n;
                    while ((n = input.Read(buffer, 0, buffer.Length)) > 0) { token.ThrowIfCancellationRequested(); hash.TransformBlock(buffer, 0, n, buffer, 0); }
                    byte[] suffix = Utf8.GetBytes("\nvrckl-cache-v1\n" + target);
                    hash.TransformFinalBlock(suffix, 0, suffix.Length);
                    digest = BitConverter.ToString(hash.Hash).Replace("-", "").ToLowerInvariant();
                }
                string path = Path.Combine(cacheDirectory, "package-" + digest + ".unitypackage");
                if (File.Exists(path)) { report = Inspect(path, token); return path; }
                input.Position = 0;
                string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    using (var output = File.Create(temp)) report = Transform(input, output, target, token);
                    token.ThrowIfCancellationRequested();
                    File.Move(temp, path);
                    return path;
                }
                finally { if (File.Exists(temp)) File.Delete(temp); } // Only our incomplete output.
            }
        }

        static string Text(byte[] data, int offset, int length) { int n = 0; while (n < length && data[offset + n] != 0) n++; return Utf8.GetString(data, offset, n); }
        static long Octal(byte[] data, int offset, int length)
        {
            string value = Encoding.ASCII.GetString(data, offset, length).Trim('\0', ' ');
            if (value.Length == 0) return 0;
            long result = 0;
            foreach (char c in value) { if (c < '0' || c > '7') throw new InvalidDataException("不正なTARサイズです。"); result = checked(result * 8 + c - '0'); }
            return result;
        }
        static bool ReadBlock(Stream stream, byte[] block) { int n = stream.Read(block, 0, block.Length); if (n == 0) return false; while (n < block.Length) { int read = stream.Read(block, n, block.Length - n); if (read == 0) throw new EndOfStreamException(); n += read; } return true; }
        static void ReadExact(Stream stream, byte[] data, int size) { int n = 0; while (n < size) { int read = stream.Read(data, n, size - n); if (read == 0) throw new EndOfStreamException(); n += read; } }
        public static void WriteEntry(Stream tar, string name, byte[] data) { WriteHeader(tar, name, data.LongLength, false); tar.Write(data, 0, data.Length); Pad(tar, data.Length); }
        public static void WriteFile(Stream tar, string name, string file)
        { using (var source = File.OpenRead(file)) { WriteHeader(tar, name, source.Length, false); source.CopyTo(tar); Pad(tar, source.Length); } }
        public static void WriteHeader(Stream stream, string name, long length, bool directory)
        {
            byte[] header = new byte[512];
            byte[] nameBytes = Utf8.GetBytes(name);
            if (nameBytes.Length > 100) throw new InvalidDataException("TAR名が長すぎます。");
            Array.Copy(nameBytes, header, nameBytes.Length);
            Put(header, 100, "0000755\0"); Put(header, 108, "0000000\0"); Put(header, 116, "0000000\0");
            Put(header, 124, Convert.ToString(length, 8).PadLeft(11, '0') + "\0");
            Put(header, 136, "00000000000\0"); Put(header, 148, "        ");
            header[156] = (byte)(directory ? '5' : '0'); Put(header, 257, "ustar\0"); Put(header, 263, "00");
            Put(header, 148, Convert.ToString(header.Sum(b => (int)b), 8).PadLeft(6, '0') + "\0 ");
            stream.Write(header, 0, header.Length);
        }
        static void Put(byte[] header, int offset, string value) { byte[] bytes = Encoding.ASCII.GetBytes(value); Array.Copy(bytes, 0, header, offset, bytes.Length); }
        static void Pad(Stream stream, long size) { int padding = (int)((512 - size % 512) % 512); stream.Write(new byte[padding], 0, padding); }
    }
}
