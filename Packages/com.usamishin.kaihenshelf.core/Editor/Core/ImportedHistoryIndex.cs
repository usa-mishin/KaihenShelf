using System;
using System.Collections.Generic;
using System.IO;

namespace KaihenShelf
{
    public sealed class ImportedHistoryIndex
    {
        readonly Dictionary<string, HashSet<string>> sources = new Dictionary<string, HashSet<string>>();
        public void Add(string itemId, string source, string status)
        {
            if (string.IsNullOrEmpty(itemId) || status != "配置完了") return;
            if (!sources.TryGetValue(itemId, out var paths)) sources[itemId] = paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string path = Normalize(source);
            if (path.Length > 0) paths.Add(path);
        }
        public bool ContainsItem(string itemId) { return itemId != null && sources.ContainsKey(itemId); }
        public bool ContainsSource(string itemId, string source, bool folder)
        {
            if (itemId == null || !sources.TryGetValue(itemId, out var paths)) return false;
            string path = Normalize(source);
            if (path.Length == 0) return false;
            if (paths.Contains(path)) return true;
            // A texture-folder import includes its descendants, but not its parent or siblings.
            if (folder) foreach (string parent in paths)
                if (!parent.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase)
                    && path.StartsWith(parent + "/", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            try { return Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/'); }
            catch (Exception) { return ""; }
        }
    }
}
