using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using KaihenShelf;

string temp = Path.Combine(Path.GetTempPath(), "KaihenShelf-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
int passed = 0, failed = 0;
void Test(string name, Action run) { try { run(); Console.WriteLine("PASS " + name); passed++; } catch (Exception ex) { Console.WriteLine("FAIL " + name + ": " + ex); failed++; } }
void Check(bool value, string message = "assertion failed") { if (!value) throw new Exception(message); }
void Throws<T>(Action run) where T : Exception { try { run(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
byte[] Tar(Action<Stream> write)
{
    using var result = new MemoryStream();
    using (var gzip = new GZipStream(result, CompressionLevel.Optimal, true)) { write(gzip); gzip.Write(new byte[1024]); }
    return result.ToArray();
}
const string guid = "0123456789abcdef0123456789abcdef";
byte[] Valid(string path = "Assets/日本語 😀/test.txt") => Tar(t => { PackageArchive.WriteEntry(t, guid + "/pathname", Encoding.UTF8.GetBytes(path)); PackageArchive.WriteEntry(t, guid + "/asset", Encoding.UTF8.GetBytes("hello")); PackageArchive.WriteEntry(t, guid + "/asset.meta", Encoding.UTF8.GetBytes("guid: " + guid)); });
PackageReport Inspect(byte[] bytes) { using var input = new MemoryStream(bytes); return PackageArchive.Transform(input, null, ""); }
try
{
    Test("manual shop choices omit blanks and deduplicate; selection and free text update shop thumbnail", () =>
    {
        var item = new LibraryItem { id = "manual", shop = "Old" };
        var shops = new[] { item, new LibraryItem { shop = "", id = "blank" }, new LibraryItem { shop = "Shop", shopThumbnailUrl = "https://booth.pximg.net/shop.jpg", id = "a" }, new LibraryItem { shop = "shop", id = "b" } };
        Check(ManualItemEditing.Shops(shops).Length == 2);
        ManualItemEditing.SetShop(item, "Shop", shops); Check(item.shop == "Shop" && item.shopThumbnailUrl.EndsWith("shop.jpg"));
        ManualItemEditing.SetShop(item, "Free input", shops); Check(item.shop == "Free input" && item.shopThumbnailUrl == "");
    });
    Test("manual thumbnail accepts nested Unicode PNG and rejects outside or invalid images", () =>
    {
        string folder = Path.Combine(temp, "manual-product"), nested = Path.Combine(folder, "画像 😀"); Directory.CreateDirectory(nested);
        byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aDTsAAAAASUVORK5CYII=");
        string file = Path.Combine(nested, "サムネイル.PNG"); File.WriteAllBytes(file, png);
        Check(ManualItemEditing.ValidateThumbnail(folder, file) == Path.GetFullPath(file));
        Check(File.ReadAllBytes(file).SequenceEqual(png));
        string outside = Path.Combine(temp, "outside.png"); File.WriteAllBytes(outside, png);
        Throws<InvalidDataException>(() => ManualItemEditing.ValidateThumbnail(folder, outside));
        string similar = folder + "-other"; Directory.CreateDirectory(similar); File.WriteAllBytes(Path.Combine(similar, "image.png"), png);
        Throws<InvalidDataException>(() => ManualItemEditing.ValidateThumbnail(folder, Path.Combine(similar, "image.png")));
        File.WriteAllText(Path.Combine(folder, "invalid.png"), "not an image");
        Throws<InvalidDataException>(() => ManualItemEditing.ValidateThumbnail(folder, Path.Combine(folder, "invalid.png")));
        File.WriteAllBytes(Path.Combine(folder, "image.gif"), png);
        Throws<InvalidDataException>(() => ManualItemEditing.ValidateThumbnail(folder, Path.Combine(folder, "image.gif")));
    });
    Test("manual thumbnail allows 4K and over 8MB while preserving network image limits", () =>
    {
        string folder = Path.Combine(temp, "manual-large"); Directory.CreateDirectory(folder);
        var png = new byte[9 * 1024 * 1024];
        Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aDTsAAAAASUVORK5CYII=").CopyTo(png, 0);
        png[18] = 16; png[19] = 0; png[22] = 16; png[23] = 0;
        string file = Path.Combine(folder, "4K.png"); File.WriteAllBytes(file, png);
        Check(ManualItemEditing.ValidateThumbnail(folder, file) == file);
        Check(!ImageHeader.TryRead(png, out _, out _));
        png[19] = 1; File.WriteAllBytes(file, png);
        Throws<InvalidDataException>(() => ManualItemEditing.ValidateThumbnail(folder, file));
        using (var output = File.Create(file)) output.SetLength(ManualItemEditing.MaxThumbnailBytes + 1L);
        Throws<InvalidDataException>(() => ManualItemEditing.ValidateThumbnail(folder, file));
        Check(ManualItemEditing.IsThumbnailTexture("画像.JPEG"));
        Check(!ManualItemEditing.IsThumbnailTexture("編集.psd") && !ManualItemEditing.IsThumbnailTexture("資料.pdf"));
    });
    BatchPackageTests.Run(Test, temp);
    Test("standard tar dot prefix accepted without weakening traversal checks", () =>
    {
        byte[] bytes = Tar(t => { PackageArchive.WriteHeader(t, "./", 0, true); PackageArchive.WriteHeader(t, "./" + guid + "/", 0, true); PackageArchive.WriteEntry(t, "./" + guid + "/pathname", Encoding.UTF8.GetBytes("Assets/a.txt")); PackageArchive.WriteEntry(t, "./" + guid + "/asset", Encoding.UTF8.GetBytes("hello")); PackageArchive.WriteEntry(t, "./" + guid + "/asset.meta", Encoding.UTF8.GetBytes("fileFormatVersion: 2\nguid: " + guid + "\n")); });
        string path = Path.Combine(temp, "dot.unitypackage"); File.WriteAllBytes(path, bytes);
        var stage = DirectPackageStage.Prepare(path, Path.Combine(temp, "dot-stage"));
        Check(stage.assets.Count == 1 && File.ReadAllText(stage.Payload(stage.assets[0], false)) == "hello"); stage.Cleanup();
        Throws<InvalidDataException>(() => Inspect(Tar(t => PackageArchive.WriteEntry(t, "./../escape/asset", new byte[] { 1 }))));
    });
    Test("dot-prefixed duplicate tar entries still rejected", () =>
    {
        Throws<InvalidDataException>(() => Inspect(Tar(t => { PackageArchive.WriteEntry(t, guid + "/asset", new byte[] { 1 }); PackageArchive.WriteEntry(t, "./" + guid + "/asset", new byte[] { 1 }); })));
    });
    Test("Unity ignored names rejected in source and target paths", () =>
    {
        foreach (string path in new[] { "Assets/.hidden.png", "Assets/Foo~/a.png", "Assets/cvs/a.png", "Assets/file.tmp" }) Throws<InvalidDataException>(() => SafePaths.AssetPath(path, ""));
        Throws<InvalidDataException>(() => SafePaths.AssetPath("Assets/a.png", "Ignored~"));
    });
    Test("thumbnail destination prefers nested prefab over more files", () =>
    {
        Check(FolderThumbnailRules.PreferredChild(new[] { "Assets/Shop/Product/Sub/A.PREFAB", "Assets/Shop/Common/a.png", "Assets/Shop/Common/b.png" }, "Assets/Shop") == "Assets/Shop/Product");
    });
    Test("thumbnail destination counts recursive files without metadata or duplicates", () =>
    {
        Check(FolderThumbnailRules.PreferredChild(new[] { "Assets/Shop/Lop Ear Mine/a.mat", "Assets/Shop/Lop Ear Mine/Sub/b.png", "Assets/Shop/TM_matcaps/a.png",
            "Assets/Shop/TM_matcaps/a.png", "Assets/Shop/TM_matcaps/a.png.meta", "Assets/Shop/TM_matcaps/dir.meta", "Assets/Other/c.prefab", "Assets/Shop/readme.txt" }, "Assets/Shop") == "Assets/Shop/Lop Ear Mine");
    });
    Test("thumbnail destination ranks prefab folders by count then deterministic name", () =>
    {
        var tied = new[] { "Assets/Shop/Beta/a.prefab", "Assets/Shop/Alpha/a.prefab" };
        Check(FolderThumbnailRules.PreferredChild(tied, "Assets/Shop") == "Assets/Shop/Alpha");
        Check(FolderThumbnailRules.PreferredChild(tied.Reverse(), "Assets/Shop") == "Assets/Shop/Alpha");
        Check(FolderThumbnailRules.PreferredChild(tied.Concat(new[] { "Assets/Shop/Beta/b.png" }), "Assets/Shop") == "Assets/Shop/Beta");
        Check(FolderThumbnailRules.PreferredChild(new[] { "Assets/Shop/Beta/a.png", "Assets/Shop/Alpha/a.png" }, "Assets/Shop") == "Assets/Shop/Alpha");
    });
    Test("thumbnail destination ignores flat and unrelated files", () =>
    {
        Check(FolderThumbnailRules.PreferredChild(new[] { "Assets/Shop/readme.txt", "Assets/Shop2/X/a.prefab", "Assets/Shop/A/file.meta" }, "Assets/Shop") == "");
    });
    Test("product thumbnails start at import root and resolve only unambiguous children", () =>
    {
        var files = new[] { "Assets/衣装/Shop/A/a.png", "Assets/衣装/Shop/A/Textures/b.png" };
        Check(FolderThumbnailRules.ImportRoot(files, "Assets/衣装/", "Shop", "A") == "Assets/衣装/Shop");
        Check(FolderThumbnailRules.SingleChild(files, "Assets/衣装/Shop") == "Assets/衣装/Shop/A");
        Check(FolderThumbnailRules.SingleChild(new[] { "Assets/Shop/a.txt", "Assets/Shop/A/a.txt" }, "Assets/Shop") == "");
        Check(FolderThumbnailRules.SingleChild(new[] { "Assets/Shop/A/a.txt", "Assets/Shop/B/b.txt" }, "Assets/Shop") == "");
        Check(FolderThumbnailRules.ImportRoot(new[] { "Assets/Tool/File.cs" }, "Assets/", "Other", "Download") == "Assets/Tool");
        Check(FolderThumbnailRules.ImportRoot(new[] { "Assets/a.txt" }, "Assets/", "Other", "Download") == "");
    });
    Test("folder thumbnails identify shop and product without assigning dependencies", () =>
    {
        FolderThumbnailRules.Suggest(new[] { "Assets/衣装/IKUSIA/rurune/Body.prefab", "Assets/lilToon/shader.shader" }, "ＩＫＵＳＩＡ", "rurune", out var shop, out var product);
        Check(shop == "Assets/衣装/IKUSIA" && product == "Assets/衣装/IKUSIA/rurune");
    });
    Test("folder thumbnails leave ambiguous products and generic folders unassigned", () =>
    {
        FolderThumbnailRules.Suggest(new[] { "Assets/Shop/A/a.png", "Assets/Shop/B/b.png" }, "Shop", "FullPack", out var shop, out var product);
        Check(shop == "Assets/Shop" && product == "");
        FolderThumbnailRules.Suggest(new[] { "Assets/Shop/Textures/a.png" }, "Shop", "Textures", out shop, out product);
        Check(shop == "Assets/Shop" && product == "");
    });
    Test("folder thumbnails avoid duplicate shops and Assets root", () =>
    {
        var files = new[] { "Assets/A/Shop/P/a.png", "Assets/B/Shop/P/b.png" };
        FolderThumbnailRules.Suggest(files, "Shop", "P", out var shop, out var product);
        Check(shop == "" && product == "");
        Check(!FolderThumbnailRules.Candidates(files).Contains("Assets"));
        Check(!FolderThumbnailRules.Under("Assets/Shop2/a.png", "Assets/Shop"));
    });
    Test("direct stage extracts original bytes and cleans temporary files", () =>
    {
        string source = Path.Combine(temp, "direct.unitypackage"); File.WriteAllBytes(source, Valid());
        using var stageInput = File.OpenRead(source);
        var before = DirectPackageStage.Hash(source);
        var stage = DirectPackageStage.Prepare(source, Path.Combine(temp, "direct-stage"));
        Check(stage.assets.Count == 1 && !stage.assets[0].folder);
        Check(File.ReadAllText(stage.Payload(stage.assets[0], false)) == "hello" && DirectPackageStage.MetaGuid(stage.Payload(stage.assets[0], true)) == guid);
        Check(DirectPackageStage.Hash(source) == before);
        string directory = stage.directory; stage.Cleanup(); Check(!Directory.Exists(directory));
    });
    Test("direct stage rejects mismatched GUID and removes failed extraction", () =>
    {
        string source = Path.Combine(temp, "bad-meta.unitypackage"), root = Path.Combine(temp, "bad-meta-stage");
        File.WriteAllBytes(source, Tar(t => { PackageArchive.WriteEntry(t, guid + "/pathname", Encoding.UTF8.GetBytes("Assets/test.txt")); PackageArchive.WriteEntry(t, guid + "/asset", Encoding.UTF8.GetBytes("x")); PackageArchive.WriteEntry(t, guid + "/asset.meta", Encoding.UTF8.GetBytes("guid: " + new string('a',32))); }));
        Throws<InvalidDataException>(() => DirectPackageStage.Prepare(source, root));
        Check(Directory.GetDirectories(root).Length == 0);
    });
    Test("direct stage rejects absent meta and file-parent conflicts", () =>
    {
        string source = Path.Combine(temp, "missing-meta.unitypackage");
        File.WriteAllBytes(source, Tar(t => { PackageArchive.WriteEntry(t, guid + "/pathname", Encoding.UTF8.GetBytes("Assets/test.txt")); PackageArchive.WriteEntry(t, guid + "/asset", Encoding.UTF8.GetBytes("x")); }));
        Throws<InvalidDataException>(() => DirectPackageStage.Prepare(source, Path.Combine(temp, "missing-meta")));
        File.WriteAllBytes(source, Tar(t => { int i = 1; foreach (string path in new[] { "Assets/shop", "Assets/shop/test.txt" }) { string g = (i++).ToString("x32"); PackageArchive.WriteEntry(t, g + "/pathname", Encoding.UTF8.GetBytes(path)); PackageArchive.WriteEntry(t, g + "/asset", Encoding.UTF8.GetBytes("x")); PackageArchive.WriteEntry(t, g + "/asset.meta", Encoding.UTF8.GetBytes("guid: " + g)); } }));
        Throws<InvalidDataException>(() => DirectPackageStage.Prepare(source, Path.Combine(temp, "file-parent")));
    });
    Test("direct stage recognizes folder meta and pre-cancellation leaves no staging", () =>
    {
        string source = Path.Combine(temp, "folder.unitypackage"), root = Path.Combine(temp, "folder-stage");
        File.WriteAllBytes(source, Tar(t => { PackageArchive.WriteEntry(t, guid + "/pathname", Encoding.UTF8.GetBytes("Assets/shop")); PackageArchive.WriteEntry(t, guid + "/asset.meta", Encoding.UTF8.GetBytes("guid: " + guid + "\nfolderAsset: yes\n")); }));
        var stage = DirectPackageStage.Prepare(source, root); Check(stage.assets.Single().folder); stage.Cleanup();
        Throws<OperationCanceledException>(() => DirectPackageStage.Prepare(source, root, new CancellationToken(true)));
        Check(Directory.GetDirectories(root).Length == 0);
        Throws<IOException>(() => DirectPackageStage.RequireSpace(root, long.MaxValue - DirectPackageStage.ReserveBytes));
    });
    Test("valid Unicode package", () => Check(Inspect(Valid()).assets == 1));
    Test("relocation preserves content and GUID", () =>
    {
        using var input = new MemoryStream(Valid()); using var output = new MemoryStream();
        var report = PackageArchive.Transform(input, output, "Clothing");
        Check(report.paths.Single() == "Assets/Clothing/日本語 😀/test.txt");
        var bytes = output.ToArray(); Check(Inspect(bytes).assets == 1);
        using var gzip = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress); using var reader = new StreamReader(gzip);
        string text = reader.ReadToEnd(); Check(text.Contains("hello") && text.Contains("guid: " + guid));
    });
    foreach (string path in new[] { "../evil", "Assets/../evil", "/Assets/a", "C:/evil", "//host/a", "Assets\\a", "Packages/a", "Assets/CON.txt", "Assets/a:stream", "Assets/a.", "Assets/a//b", "Assets/COM¹.txt", "Assets/a?/b" })
        Test("reject path " + path, () => Throws<InvalidDataException>(() => Inspect(Valid(path))));
    Test("reject links", () =>
    {
        byte[] bytes = Tar(t => { using var h = new MemoryStream(); PackageArchive.WriteHeader(h, guid + "/asset", 0, false); byte[] header = h.ToArray(); header[156] = (byte)'2'; for (int i = 148; i < 156; i++) header[i] = 32; var sum = Encoding.ASCII.GetBytes(Convert.ToString(header.Sum(x => (int)x), 8).PadLeft(6, '0') + "\0 "); Array.Copy(sum, 0, header, 148, 8); t.Write(header); });
        Throws<InvalidDataException>(() => Inspect(bytes));
    });
    Test("reject oversized payload", () => Throws<InvalidDataException>(() => Inspect(Tar(t => PackageArchive.WriteHeader(t, guid + "/asset", PackageArchive.MaxExpandedBytes + 1, false)))));
    Test("reject small compressed package expansion bomb", () => Throws<InvalidDataException>(() => Inspect(Tar(t => PackageArchive.WriteHeader(t, guid + "/asset", PackageArchive.MinimumExpansionAllowance + 1, false)))));
    Test("reject duplicate entry", () => Throws<InvalidDataException>(() => Inspect(Tar(t => { PackageArchive.WriteEntry(t, guid + "/pathname", Encoding.UTF8.GetBytes("Assets/a")); PackageArchive.WriteEntry(t, guid + "/pathname", Encoding.UTF8.GetBytes("Assets/a")); }))));
    Test("reject duplicate destination", () => Throws<InvalidDataException>(() => Inspect(Tar(t => { PackageArchive.WriteEntry(t, guid + "/pathname", Encoding.UTF8.GetBytes("Assets/a")); PackageArchive.WriteEntry(t, new string('a', 32) + "/pathname", Encoding.UTF8.GetBytes("Assets/A")); }))));
    Test("reject missing pathname", () => Throws<InvalidDataException>(() => Inspect(Tar(t => PackageArchive.WriteEntry(t, guid + "/asset", new byte[1])))));
    Test("reject unexpected entry", () => Throws<InvalidDataException>(() => Inspect(Tar(t => PackageArchive.WriteEntry(t, guid + "/something", new byte[1])))));
    Test("reject bad checksum", () => Throws<InvalidDataException>(() => Inspect(Tar(t => t.Write(new byte[512].Select((x, i) => i == 0 ? (byte)1 : x).ToArray())))));
    Test("reject truncated TAR", () => Throws<EndOfStreamException>(() => Inspect(Tar(t => PackageArchive.WriteHeader(t, guid + "/asset", 4096, false)))));
    Test("entry count bound", () => Throws<InvalidDataException>(() => Inspect(Tar(t => { for (int i = 0; i <= PackageArchive.MaxEntries; i++) PackageArchive.WriteEntry(t, i.ToString("x32") + "/asset", Array.Empty<byte>()); }))));
    Test("cancellation", () => { using var input = new MemoryStream(Valid()); Throws<OperationCanceledException>(() => PackageArchive.Transform(input, null, "", new CancellationToken(true))); });
    Test("ASCII cache, source unchanged, target invalidates cache", () =>
    {
        string source = Path.Combine(temp, "日本語 😀.unitypackage"); File.WriteAllBytes(source, Valid()); byte[] before = SHA256.HashData(File.ReadAllBytes(source));
        string first = PackageArchive.Prepare(source, Path.Combine(temp, "cache"), "", default, out _);
        string second = PackageArchive.Prepare(source, Path.Combine(temp, "cache"), "衣装", default, out _);
        Check(first != second && Path.GetFileName(first).All(c => c < 128)); Check(before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(source))));
        Check(PackageArchive.Prepare(source, Path.Combine(temp, "cache"), "", default, out _ ) == first);
    });
    Test("file scan excludes cache", () =>
    {
        string folder = Path.Combine(temp, "product"); Directory.CreateDirectory(Path.Combine(folder, ".VrcKaihenManagerImportCache")); File.WriteAllText(Path.Combine(folder, ".VrcKaihenManagerImportCache", "hidden.txt"), "x"); File.WriteAllText(Path.Combine(folder, "product.txt"), "x");
        Check(FileCatalog.Scan(folder).files.Count == 1);
    });
    Test("original import copy preserves compressed bytes and full GUID manifest", () =>
    {
        byte[] bytes = Tar(t => { for (int i = 0; i < 240; i++) { string g = i.ToString("x32"); PackageArchive.WriteEntry(t, g + "/pathname", Encoding.UTF8.GetBytes("Assets/Shop/" + i + ".txt")); PackageArchive.WriteEntry(t, g + "/asset", Encoding.UTF8.GetBytes("data")); } });
        string source = Path.Combine(temp, "original.unitypackage"); File.WriteAllBytes(source, bytes);
        string copy = PackageArchive.PrepareOriginal(source, Path.Combine(temp, "original-cache"), default, out var report);
        Check(File.ReadAllBytes(copy).SequenceEqual(bytes) && File.ReadAllBytes(source).SequenceEqual(bytes));
        Check(report.manifest.Count == 240 && report.manifest.Last().guid == 239.ToString("x32") && report.manifest.Last().path == "Assets/Shop/239.txt");
        Throws<OperationCanceledException>(() => PackageArchive.PrepareOriginal(source, Path.Combine(temp, "original-cache"), new CancellationToken(true), out _));
        File.WriteAllBytes(source, Valid("../invalid"));
        Throws<InvalidDataException>(() => PackageArchive.PrepareOriginal(source, Path.Combine(temp, "original-cache"), default, out _));
    });
    Test("category import defaults retain explicit custom and root settings", () =>
    {
        var state = new LibraryState();
        foreach (AssetCategory c in Enum.GetValues(typeof(AssetCategory)))
        {
            string expected = c == AssetCategory.Avatar || c == AssetCategory.Tool || c == AssetCategory.World || c == AssetCategory.Other ? "" : Categories.Labels[(int)c];
            Check(state.Target(c) == expected);
        }
        state.targets.Add(new ImportTarget { category = AssetCategory.Clothing, subfolder = "Custom" });
        state.targets.Add(new ImportTarget { category = AssetCategory.Hair, subfolder = "" });
        Check(state.Target(AssetCategory.Clothing) == "Custom" && state.Target(AssetCategory.Hair) == "");
    });
    Test("NFKC and 3D names", () => { Check(Matcher.Normalize("ＭＡＹＡ") == "maya"); Check(Matcher.ValidIdentifier("My3DAvatar")); Check(!Matcher.ValidIdentifier("VRChat用")); });
    Test("direct, shared body and manual exclusion", () =>
    {
        var state = new LibraryState(); var a = new LibraryItem { id = "a", title = "A", category = AssetCategory.Avatar }; var b = new LibraryItem { id = "b", title = "B", category = AssetCategory.Avatar }; var item = new LibraryItem { id = "i", title = "衣装 ＭＡＹＡ" };
        state.items.AddRange(new[] { a, b, item }); state.Meta("a").identifiers.Add("Maya"); state.Meta("b").sharedBodies.Add("a");
        var result = Matcher.Match(item, state); Check(result.Count == 2 && result.Single(x => x.avatarId == "b").indirect);
        state.Meta("i").excludedAvatars.Add("a"); Check(Matcher.Match(item, state).Count == 0);
        item.title = "mayamaya"; state.Meta("i").excludedAvatars.Clear(); Check(Matcher.Match(item, state).Count == 0);
    });
    Test("unpurchased migration and favorites scope", () =>
    {
        var state = new LibraryState(); var pending = new LibraryItem { id = "old", boothId = "123", unpurchased = true, category = AssetCategory.Avatar };
        state.items.Add(pending); state.Meta("old").identifiers.Add("Example"); state.Meta("other").sharedBodies.Add("old"); state.ToggleFavorite("avatars", "old");
        state.MergeBlm(new List<LibraryItem> { new LibraryItem { id = "new", boothId = "123", source = "blm" } });
        Check(state.items.Count == 1 && state.Meta("new").identifiers.Contains("Example") && state.Meta("other").sharedBodies.Contains("new")); Check(state.IsFavorite("avatars", "new") && !state.IsFavorite("items", "new")); Check(state.Category(state.items[0]) == AssetCategory.Avatar);
    });
    Test("BOOTH URLs restricted", () => { Check(BlmDatabaseReader.BoothId("https://test.booth.pm/ja/items/123") == "123"); Check(BlmDatabaseReader.BoothId("https://booth.pm.attacker.example/items/123") == ""); });
    Test("tag JSON parser", () => { Check(BlmDatabaseReader.ParseTags("[\"衣装\", \"\\u30de\\u30e4\", \"衣装\"]").SequenceEqual(new[] { "衣装", "マヤ" })); Throws<InvalidDataException>(() => BlmDatabaseReader.ParseTags("[{\"tag\":\"a\"}]")); });
    Test("consent gate precedes filesystem access", () => Throws<InvalidOperationException>(() => new ReadOnlySqlite("does-not-exist.db", false)));
    Test("read-only SQLite queries and source byte integrity", () =>
    {
        string path = Path.Combine(temp, "fixture.db"); Native.Create(path, "CREATE TABLE products(id TEXT,title TEXT,folder TEXT,tags TEXT,age TEXT); INSERT INTO products VALUES('1','テスト','C:\\fixture','[\"衣装\"]','R-18');");
        byte[] before = SHA256.HashData(File.ReadAllBytes(path));
        var schema = BlmDatabaseReader.Inspect(path, true, default); Check(schema.tables["products"].Contains("title"));
        using (var db = new ReadOnlySqlite(path, true))
        {
            var rows = db.Query("SELECT title FROM products"); Check(rows.Count == 1 && rows[0][0] == "テスト");
            Throws<IOException>(() => db.Query("DELETE FROM products"));
        }
        Check(before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))));
    });
    Test("cancellable missing DB retry", () => { using var cancellation = new CancellationTokenSource(100); Throws<OperationCanceledException>(() => BlmDatabaseReader.Inspect(Path.Combine(temp, "missing.db"), true, cancellation.Token)); Check(!File.Exists(Path.Combine(temp, "missing.db"))); });
    BlmKnownSchemaTests.Run(Test);
    AvatarRulesTests.Run(Test);
    ImagePolicyTests.Run(Test);
    PhotoshopPreviewTests.Run(Test);
    ShellFileIconTests.Run(Test);
    TextureFolderTests.Run(Test, temp);
    TextureImportValidationTests.Run(Test, temp);
    PackageFileCardTests.Run(Test);
    StorageMigrationTests.Run(Test, temp);
    CategoryRuleTests.Run(Test);
    CatalogIndexTests.Run(Test);
    if (Environment.GetEnvironmentVariable("VRC_LIBRARY_BENCHMARK") == "1") CatalogIndexTests.Benchmark();
}
finally
{
    // Own random test directory only; no real BLM/product data is used by this suite.
    if (Path.GetFullPath(temp).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) && Path.GetFileName(temp).StartsWith("KaihenShelf-tests-", StringComparison.Ordinal)) Directory.Delete(temp, true);
}
Console.WriteLine($"{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;

static class Native
{
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_open(byte[] path, out IntPtr db);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_exec(IntPtr db, byte[] sql, IntPtr callback, IntPtr argument, IntPtr error);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_close(IntPtr db);
    public static void Create(string path, string sql)
    {
        if (sqlite3_open(Encoding.UTF8.GetBytes(path + "\0"), out var db) != 0) throw new Exception("fixture open failed");
        try { if (sqlite3_exec(db, Encoding.UTF8.GetBytes(sql + "\0"), IntPtr.Zero, IntPtr.Zero, IntPtr.Zero) != 0) throw new Exception("fixture creation failed"); } finally { sqlite3_close(db); }
    }
}
