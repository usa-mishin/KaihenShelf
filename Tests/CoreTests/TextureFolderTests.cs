using KaihenShelf;

internal static class TextureFolderTests
{
    public static void Run(Action<string, Action> test, string temp)
    {
        string root = Path.Combine(temp, "texture-folder"), source = Path.Combine(root, "source", "瞳"), assets = Path.Combine(root, "project", "Assets"), cache = Path.Combine(root, "project", "Library", "stage");
        Directory.CreateDirectory(Path.Combine(source, "入れ子")); Directory.CreateDirectory(assets);
        foreach (string name in new[] { "a.png", "入れ子/b.JPG", "入れ子/c.tga", "入れ子/d.exr", "no.psd", "no.txt", "a.png.meta" })
            {
            string extension = Path.GetExtension(name).ToLowerInvariant();
            if (extension == ".png" || extension == ".jpg" || extension == ".tga" || extension == ".exr")
                File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "image" + extension), Path.Combine(source, name));
            else File.WriteAllText(Path.Combine(source, name), name);
        }
        test("texture folders filter formats and preserve nested paths without changing sources", () =>
        {
            var before = Directory.GetFiles(source, "*", SearchOption.AllDirectories).ToDictionary(x => x, DirectPackageStage.Hash);
            var stage = TextureFolderStage.Prepare(source, assets, cache, "テクスチャ", "ショップ");
            try
            {
                Check(stage.texturesOnly && stage.assets.Count == 4);
                Check(stage.assets.Any(x => x.path == "Assets/テクスチャ/ショップ/瞳/入れ子/b.JPG"));
                Check(stage.assets.All(x => x.path.StartsWith("Assets/テクスチャ/ショップ/瞳/")));
                foreach (var asset in stage.assets) Check(DirectPackageStage.MetaGuid(stage.Payload(asset, true)) == asset.guid);
                foreach (var pair in before) Check(DirectPackageStage.Hash(pair.Key) == pair.Value);
            }
            finally { stage.Cleanup(); }
            Check(!Directory.Exists(stage.directory));
        });
        test("texture updates retain destination GUID and full importer settings", () =>
        {
            string dest = Path.Combine(assets, "ショップ", "瞳", "a.png"); Directory.CreateDirectory(Path.GetDirectoryName(dest));
            File.WriteAllText(dest, "old");
            string meta = "fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\nTextureImporter:\n  sRGBTexture: 0\n";
            File.WriteAllText(dest + ".meta", meta);
            var stage = TextureFolderStage.Prepare(source, assets, cache, "", "ショップ");
            try
            {
                var asset = stage.assets.Single(x => x.path.EndsWith("/a.png"));
                Check(asset.guid == "0123456789abcdef0123456789abcdef");
                Check(File.ReadAllText(stage.Payload(asset, true)) == meta);
                Check(File.ReadAllText(dest) == "old");
            }
            finally { stage.Cleanup(); }
        });
        test("texture preparation rejects invalid destinations and nested Assets source", () =>
        {
            Reject(() => TextureFolderStage.Prepare(source, assets, cache, "../escape", "ショップ"));
            Reject(() => TextureFolderStage.Prepare(assets, assets, cache, "", "ショップ"));
            Reject(() => TextureFolderStage.Prepare(root, assets, cache, "", "ショップ"));
        });
        test("texture preparation rejects orphan metadata and cleans staging", () =>
        {
            string dest = Path.Combine(assets, "孤立", "瞳", "a.png"); Directory.CreateDirectory(Path.GetDirectoryName(dest));
            File.WriteAllText(dest + ".meta", "orphan");
            Reject(() => TextureFolderStage.Prepare(source, assets, cache, "", "孤立"));
            Check(Directory.GetDirectories(cache).Length == 0);
        });
        test("texture preparation cancellation leaves no staging", () =>
        {
            Reject(() => TextureFolderStage.Prepare(source, assets, cache, "", "ショップ", new CancellationToken(true)));
            Check(Directory.GetDirectories(cache).Length == 0);
        });
        test("texture shop names remain one safe path component", () =>
        {
            Check(TextureFolderStage.FolderName("A/B:C?") == "A_B_C_");
            Check(TextureFolderStage.FolderName("CON") == "_CON");
            Check(TextureFolderStage.FolderName("") == "ショップ名なし");
        });
    }
    static void Check(bool value) { if (!value) throw new Exception("Texture folder assertion failed"); }
    static void Reject(Action action) { try { action(); } catch (InvalidDataException) { return; } catch (IOException) { return; } catch (OperationCanceledException) { return; } throw new Exception("Expected rejection"); }
}
