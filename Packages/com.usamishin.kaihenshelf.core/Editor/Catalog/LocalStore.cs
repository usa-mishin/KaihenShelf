using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace KaihenShelf
{
    public static class LocalStore
    {
        static readonly string DataBase = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        static string globalRoot, projectDataRoot, projectCacheRoot;
        static Dictionary<string, string> MigrationPaths => new Dictionary<string, string> {
            { Path.Combine(DataBase, "VrcKaihenLibrary", "Unity"), Path.Combine(DataBase, "KaihenShelf", "Unity") },
            { Path.Combine(ProjectRoot, "UserSettings", "VrcKaihenLibrary"), Path.Combine(ProjectRoot, "UserSettings", "KaihenShelf") },
            { Path.Combine(ProjectRoot, "Library", "VrcKaihenLibrary"), Path.Combine(ProjectRoot, "Library", "KaihenShelf") },
            { "Library/VrcKaihenLibrary", "Library/KaihenShelf" },
            { "UserSettings/VrcKaihenLibrary", "UserSettings/KaihenShelf" }
        };
        public static string Root => globalRoot ?? (globalRoot = StorageMigration.Ensure(Path.Combine(DataBase, "VrcKaihenLibrary", "Unity"), Path.Combine(DataBase, "KaihenShelf", "Unity"), MigrationPaths));
        public static string ProjectDataRoot => projectDataRoot ?? (projectDataRoot = StorageMigration.Ensure(Path.Combine(ProjectRoot, "UserSettings", "VrcKaihenLibrary"), Path.Combine(ProjectRoot, "UserSettings", "KaihenShelf"), MigrationPaths));
        public static string ProjectCacheRoot => projectCacheRoot ?? (projectCacheRoot = StorageMigration.Ensure(Path.Combine(ProjectRoot, "Library", "VrcKaihenLibrary"), Path.Combine(ProjectRoot, "Library", "KaihenShelf"), MigrationPaths));
        public static string StatePath { get { return Path.Combine(Root, "state.json"); } }
        public static string ProjectRoot { get { return Directory.GetParent(Application.dataPath).FullName; } }
        public static string HistoryPath { get { return Path.Combine(ProjectDataRoot, "history.json"); } }
        public static LibraryState Load()
        {
            var state = Read<LibraryState>(StatePath) ?? new LibraryState();
            if (state.formatVersion != 1) throw new InvalidDataException("未対応の保存形式です。既存のデータは保持されています。");
            if (string.IsNullOrEmpty(state.blmPath)) state.blmPath = BlmDatabaseReader.DefaultPath;
            return state;
        }
        public static T Read<T>(string path) where T : class
        { return File.Exists(path) ? JsonUtility.FromJson<T>(File.ReadAllText(path)) ?? throw new InvalidDataException("JSONを読み取れません: " + path) : null; }
        public static void Write<T>(string path, T value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonUtility.ToJson(value, true), new System.Text.UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
    [Serializable] public sealed class ImportRecord
    {
        public string id;
        public string itemId;
        public string source;
        public string cache;
        public string target;
        public string utc;
        public string status;
        public string message;
        public string productName;
        public int importedCount;
        public bool hasImportedCount;
        public string destinationFolder;
        public string destinationFolderGuid;
    }
    [Serializable] public sealed class ImportHistory { public List<ImportRecord> records = new List<ImportRecord>(); }
}
