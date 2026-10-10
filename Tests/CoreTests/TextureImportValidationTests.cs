using System.IO.Compression;
using System.Text;
using KaihenShelf;

internal static class TextureImportValidationTests
{
    public static void Run(Action<string, Action> test, string temp)
    {
        string root = Path.Combine(temp, "image-validation"); Directory.CreateDirectory(root);
        foreach (string ext in new[] { ".png", ".jpg", ".tga", ".exr" })
        {
            string extension = ext;
            test("texture header accepts Unity-generated " + ext, () => TextureImportValidation.Check(Path.Combine(AppContext.BaseDirectory, "Fixtures/image" + extension), "日本語😀" + extension));
            test("texture header rejects non-image and truncated " + ext, () =>
            {
                string p = Path.Combine(root, "bad" + extension); File.WriteAllText(p, "not an image"); Reject(() => TextureImportValidation.Check(p, p));
                File.WriteAllBytes(p, File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures/image" + extension)).Take(12).ToArray());
                Reject(() => TextureImportValidation.Check(p, p));
            });
        }
        test("PNG header size limit before pixel allocation", () =>
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures/image.png"));
            // A structurally recognizable header with a dimension bomb must never be decoded.
            bytes[16] = 0x7f; bytes[17] = bytes[18] = bytes[19] = 0xff;
            string p = Path.Combine(root, "huge.png"); File.WriteAllBytes(p, bytes); Reject(() => TextureImportValidation.Check(p, p));
        });
        test("PNG payload truncated after valid IHDR is rejected", () =>
        {
            string p = Path.Combine(root, "header-only.png"); File.WriteAllBytes(p, File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures/image.png")).Take(33).ToArray());
            Reject(() => TextureImportValidation.Check(p, p));
        });
        test("corrupt image inside UnityPackage rejects and cleans stage", () =>
        {
            string p = Path.Combine(root, "bad.unitypackage"), guid = Guid.NewGuid().ToString("N"), cache = Path.Combine(root, "stage");
            using (var output = File.Create(p)) using (var gzip = new GZipStream(output, CompressionLevel.Fastest))
            {
                PackageArchive.WriteEntry(gzip, guid + "/pathname", Encoding.UTF8.GetBytes("Assets/broken.png"));
                PackageArchive.WriteEntry(gzip, guid + "/asset", new byte[] { 1, 2, 3 });
                PackageArchive.WriteEntry(gzip, guid + "/asset.meta", Encoding.UTF8.GetBytes("fileFormatVersion: 2\nguid: " + guid + "\n"));
                gzip.Write(new byte[1024]);
            }
            Reject(() => DirectPackageStage.Prepare(p, cache));
            if (Directory.Exists(cache) && Directory.GetDirectories(cache).Length != 0) throw new Exception("staging leaked");
        });
        test("corrupt texture folder rejects and cleans stage", () =>
        {
            string source = Path.Combine(root, "source"), assets = Path.Combine(root, "Assets"), cache = Path.Combine(root, "cache");
            Directory.CreateDirectory(source); Directory.CreateDirectory(assets); File.WriteAllText(Path.Combine(source, "bad.png"), "broken");
            Reject(() => TextureFolderStage.Prepare(source, assets, cache, "", "Shop"));
            if (Directory.Exists(cache) && Directory.GetDirectories(cache).Length != 0) throw new Exception("staging leaked");
        });
    }
    static void Reject(Action action) { try { action(); } catch (InvalidDataException) { return; } throw new Exception("Expected invalid image rejection"); }
}
