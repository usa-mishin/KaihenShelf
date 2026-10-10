using KaihenShelf;
using System.Text.Json;

internal static class StorageMigrationTests
{
    public static void Run(Action<string, Action> test, string root)
    {
        void Check(bool condition) { if (!condition) throw new Exception("Migration assertion failed"); }
        test("storage migration moves data and remaps image and history paths with original JSON backup", () =>
        {
            string old = Path.Combine(root, "移行 😀", "VrcKaihenLibrary"), next = Path.Combine(root, "移行 😀", "KaihenShelf");
            Directory.CreateDirectory(Path.Combine(old, "ManualThumbnails"));
            string image = Path.Combine(old, "ManualThumbnails", "test.png"); File.WriteAllBytes(image, new byte[] { 1, 2, 3 });
            string original = JsonSerializer.Serialize(new { thumbnailPath = image, caption = "VrcKaihenLibrary", other = old + "Other/test.png" });
            File.WriteAllText(Path.Combine(old, "state.json"), original);
            Directory.CreateDirectory(Path.Combine(old, "UpgradeBackup")); File.WriteAllText(Path.Combine(old, "UpgradeBackup", "state.json"), original);
            var map = new Dictionary<string, string> { { old, next } };
            StorageMigration.Ensure(old, next, map);
            Check(!Directory.Exists(old));
            Check(File.ReadAllBytes(Path.Combine(next, "ManualThumbnails", "test.png")).SequenceEqual(new byte[] { 1, 2, 3 }));
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(next, "state.json")));
            Check(json.RootElement.GetProperty("thumbnailPath").GetString() == Path.Combine(next, "ManualThumbnails", "test.png"));
            Check(json.RootElement.GetProperty("other").GetString() == old + "Other/test.png");
            Check(json.RootElement.GetProperty("caption").GetString() == "VrcKaihenLibrary");
            Check(File.ReadAllText(Path.Combine(next, "state.json.before-kaihenshelf.bak")) == original);
            Check(File.ReadAllText(Path.Combine(next, "UpgradeBackup", "state.json")) == original);
            string updated = "{\"items\":[]}"; File.WriteAllText(Path.Combine(next, "state.json"), updated);
            StorageMigration.Ensure(old, next, map);
            Check(File.ReadAllText(Path.Combine(next, "state.json")) == updated);
        });
        test("storage migration preserves both directories on conflict", () =>
        {
            string old = Path.Combine(root, "conflict-old"), next = Path.Combine(root, "conflict-new");
            Directory.CreateDirectory(old); Directory.CreateDirectory(next);
            File.WriteAllText(Path.Combine(old, "state.json"), "old"); File.WriteAllText(Path.Combine(next, "state.json"), "new");
            try { StorageMigration.Ensure(old, next, new Dictionary<string, string>()); throw new Exception("Expected conflict"); }
            catch (IOException) { }
            Check(File.ReadAllText(Path.Combine(old, "state.json")) == "old" && File.ReadAllText(Path.Combine(next, "state.json")) == "new");
        });
        test("storage migration resumes after directory rename and handles slash and case variants", () =>
        {
            string old = Path.Combine(root, "resume-old"), next = Path.Combine(root, "resume-new"); Directory.CreateDirectory(next);
            string oldPath = (old + "/Folder/image.png").Replace('\\', '/').ToUpperInvariant();
            File.WriteAllText(Path.Combine(next, "history.json.bak"), JsonSerializer.Serialize(new { cache = oldPath }));
            StorageMigration.Ensure(old, next, new Dictionary<string, string> { { old, next } });
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(next, "history.json.bak")));
            Check(doc.RootElement.GetProperty("cache").GetString() == next.Replace('\\', '/') + "/FOLDER/IMAGE.PNG");
        });
        test("storage migration new installs and second call do not resurrect legacy data", () =>
        {
            string old = Path.Combine(root, "absent-old"), next = Path.Combine(root, "fresh-new");
            StorageMigration.Ensure(old, next, new Dictionary<string, string>());
            Directory.CreateDirectory(old); File.WriteAllText(Path.Combine(old, "history.json"), "legacy");
            StorageMigration.Ensure(old, next, new Dictionary<string, string>());
            Check(!File.Exists(Path.Combine(next, "history.json")) && File.Exists(Path.Combine(old, "history.json")));
        });
    }
}
