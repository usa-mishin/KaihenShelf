using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;

namespace KaihenShelf
{
    public static class ImportHistoryUtility
    {
        public static string LocalTime(string utc)
        {
            return DateTimeOffset.TryParse(utc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time)
                ? time.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss") : "日時不明";
        }

        public static string CountLabel(ImportRecord record)
        {
            if (record.hasImportedCount) return "インポート数：" + record.importedCount + "件";
            // Older histories saved the count only in the completion message.
            var match = Regex.Match(record.message ?? "", @"^(\d+)件を");
            return match.Success ? "インポート数：" + match.Groups[1].Value + "件" : "インポート数：不明";
        }

        public static string ProductName(ImportRecord record, LibraryItem item)
        {
            if (!string.IsNullOrEmpty(record.productName)) return record.productName;
            return item != null && !string.IsNullOrEmpty(item.title) ? item.title : "商品情報なし";
        }

        public static string CommonFolder(IEnumerable<string> files)
        {
            string common = null;
            foreach (string file in files)
            {
                string folder = (Path.GetDirectoryName(file) ?? "Assets").Replace('\\', '/');
                if (common == null) common = folder;
                while (common != "Assets" && folder != common && !folder.StartsWith(common + "/", StringComparison.Ordinal))
                    common = (Path.GetDirectoryName(common) ?? "Assets").Replace('\\', '/');
            }
            return common ?? "Assets";
        }

        public static string Destination(ImportRecord record)
        {
            if (!string.IsNullOrEmpty(record.destinationFolderGuid))
            {
                string moved = AssetDatabase.GUIDToAssetPath(record.destinationFolderGuid);
                // A deleted destination must not silently open a different folder at the old path.
                return AssetDatabase.IsValidFolder(moved) ? moved : "";
            }
            return (string.IsNullOrEmpty(record.destinationFolder) ? record.target ?? "" : record.destinationFolder).TrimEnd('/');
        }

        public static void Reset(string historyPath)
        {
            // LocalStore keeps the previous history in .bak; imported assets are never touched.
            LocalStore.Write(historyPath, new ImportHistory());
        }
    }
}
