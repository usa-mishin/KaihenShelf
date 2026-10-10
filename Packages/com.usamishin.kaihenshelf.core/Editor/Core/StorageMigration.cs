using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace KaihenShelf
{
    public static class StorageMigration
    {
        const string Marker = ".kaihenshelf-migration-v1";
        public static string Ensure(string legacy, string destination, IDictionary<string, string> paths)
        {
            legacy = Path.GetFullPath(legacy); destination = Path.GetFullPath(destination);
            string key;
            using (var hash = SHA256.Create()) key = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(destination.ToUpperInvariant()))).Replace("-", "");
            using (var mutex = new Mutex(false, "KaihenShelf.StorageMigration." + key))
            {
                bool owned = false;
                try
                {
                    try { owned = mutex.WaitOne(TimeSpan.FromSeconds(30)); }
                    catch (AbandonedMutexException) { owned = true; }
                    if (!owned) throw new IOException("KaihenShelfのデータ移行が他のUnityで実行中です。");
                    RejectLinkedAncestors(legacy); RejectLinkedAncestors(destination);
                    if (File.Exists(Path.Combine(destination, Marker))) return destination;
                    if (Directory.Exists(legacy))
                    {
                        if (Directory.Exists(destination) || File.Exists(destination))
                            throw new IOException("旧保存先と新保存先が両方存在するため、自動で上書きしません。\n" + legacy + "\n" + destination);
                        // Validate links before moving any data. Rename on the same volume avoids duplicating large caches.
                        foreach (var file in Files(legacy)) { }
                        Directory.CreateDirectory(Path.GetDirectoryName(destination));
                        Directory.Move(legacy, destination);
                    }
                    else Directory.CreateDirectory(destination);
                    // Retry safely after an interrupted rename. Preserve the original JSON before rewriting owned paths.
                    foreach (string file in Files(destination))
                    {
                        if (!file.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && !file.EndsWith(".json.bak", StringComparison.OrdinalIgnoreCase)) continue;
                        string before = File.ReadAllText(file), after = RemapJson(before, paths);
                        if (before == after) continue;
                        string backup = file + ".before-kaihenshelf.bak";
                        if (!File.Exists(backup)) File.Copy(file, backup);
                        string temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
                        try
                        {
                            File.WriteAllText(temporary, after, new UTF8Encoding(false));
                            for (int attempt = 0; ; attempt++)
                            {
                                try { File.Replace(temporary, file, null); break; }
                                catch (IOException ex)
                                {
                                    // Unity's file watcher can briefly hold the renamed cache's JSON files.
                                    if (!File.Exists(temporary) && File.Exists(file) && File.ReadAllText(file) == after) break;
                                    if (attempt >= 4) throw new IOException("移行中のJSONを更新できません。元データのバックアップは保持されています: " + file, ex);
                                    Thread.Sleep(50);
                                }
                            }
                        }
                        finally { if (File.Exists(temporary)) File.Delete(temporary); }
                    }
                    File.WriteAllText(Path.Combine(destination, Marker), "KaihenShelf storage migration completed", new UTF8Encoding(false));
                    return destination;
                }
                finally { if (owned) mutex.ReleaseMutex(); }
            }
        }

        static void RejectLinkedAncestors(string path)
        {
            for (var current = new DirectoryInfo(path); current != null; current = current.Parent)
                if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("データ移行先にリンクは使用できません: " + current.FullName);
        }

        static IEnumerable<string> Files(string root)
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(root))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("データ移行中にリンクを検出しました: " + entry);
                if ((attributes & FileAttributes.Directory) == 0) yield return entry;
                // Upgrade snapshots are historical backups, never rewrite their contents.
                else if (Path.GetFileName(entry) != "UpgradeBackup") foreach (var file in Files(entry)) yield return file;
            }
        }

        public static string RemapJson(string json, IDictionary<string, string> paths)
        {
            return Regex.Replace(json, "\"(?:\\\\.|[^\"\\\\])*\"", match =>
            {
                string value = Decode(match.Value.Substring(1, match.Length - 2));
                string normalized = value.Replace('\\', '/');
                foreach (var pair in paths)
                {
                    string prefix = pair.Key.Replace('\\', '/');
                    if (!normalized.Equals(prefix, StringComparison.OrdinalIgnoreCase) && !normalized.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase)) continue;
                    string mapped = pair.Value.Replace('\\', '/') + normalized.Substring(prefix.Length);
                    if (value.Contains("\\")) mapped = mapped.Replace('/', '\\');
                    return "\"" + Encode(mapped) + "\"";
                }
                return match.Value;
            });
        }

        static string Decode(string value)
        {
            var result = new StringBuilder();
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] != '\\') { result.Append(value[i]); continue; }
                char code = value[++i];
                switch (code)
                {
                    case 'u': result.Append((char)Convert.ToInt32(value.Substring(i + 1, 4), 16)); i += 4; break;
                    case 'n': result.Append('\n'); break;
                    case 'r': result.Append('\r'); break;
                    case 't': result.Append('\t'); break;
                    case 'b': result.Append('\b'); break;
                    case 'f': result.Append('\f'); break;
                    default: result.Append(code); break;
                }
            }
            return result.ToString();
        }

        static string Encode(string value)
        {
            var result = new StringBuilder();
            foreach (char c in value)
                if (c == '\\') result.Append("\\\\");
                else if (c == '"') result.Append("\\\"");
                else if (c < 32) result.Append("\\u" + ((int)c).ToString("x4"));
                else result.Append(c);
            return result.ToString();
        }
    }
}

