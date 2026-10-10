using KaihenShelf;

internal static class PackageFileCardTests
{
    static CatalogFile File(string relative) => new CatalogFile { path = Path.GetFullPath(Path.Combine("synthetic", relative)), relative = relative, kind = "UnityPackage", modifiedUtc = new DateTime(2026, 1, 1) };
    static void Check(bool ok) { if (!ok) throw new Exception("Package card assertion failed"); }
    public static void Run(Action<string, Action> test)
    {
        LibraryState AvatarState()
        {
            var state = new LibraryState();
            state.items.Add(new LibraryItem { id = "a", title = "商品名", category = AssetCategory.Avatar });
            state.items.Add(new LibraryItem { id = "b", title = "別の商品名", category = AssetCategory.Avatar });
            state.metadata.Add(new ItemMetadata { id = "a", mainName = "マヌカ", identifiers = new List<string> { "Manuka", "まぬか", "123456", "avatar", "https://example.test" }, sharedBodies = new List<string> { "b" } });
            state.metadata.Add(new ItemMetadata { id = "b", mainName = "しなの", identifiers = new List<string> { "Shinano" } });
            return state;
        }
        test("package avatar badges normalize aliases and Japanese suffixes", () =>
        {
            var index = new CatalogViewIndex(AvatarState());
            Check(index.PackageAvatarNames("ＭＡＮＵＫＡ_Serendipity.unitypackage").SequenceEqual(new[] { "マヌカ" }));
            Check(index.PackageAvatarNames("まぬか対応_Dress.unitypackage").SequenceEqual(new[] { "マヌカ" }));
            Check(index.PackageAvatarNames("Shinano専用_Dress.UNITYPACKAGE").SequenceEqual(new[] { "しなの" }));
        });
        test("package avatar badges ignore directories IDs generic words partial names and other file kinds", () =>
        {
            var index = new CatalogViewIndex(AvatarState());
            foreach (var name in new[] { "Manuka/Dress.unitypackage", "123456_avatar.unitypackage", "SuperManuka.unitypackage", "ManukaPlus.unitypackage", "Manuka.png", "https_example.test.unitypackage" })
                Check(index.PackageAvatarNames(name).Count == 0);
        });
        test("package avatar badges deduplicate aliases and do not infer shared bodies", () =>
        {
            var index = new CatalogViewIndex(AvatarState());
            Check(index.PackageAvatarNames("Manuka_まぬか.unitypackage").SequenceEqual(new[] { "マヌカ" }));
            Check(index.PackageAvatarNames("Shinano_Manuka.unitypackage").OrderBy(x => x).SequenceEqual(new[] { "しなの", "マヌカ" }.OrderBy(x => x)));
        });
        test("package avatar badge cache is renewed with edited catalog metadata", () =>
        {
            var state = AvatarState(); var before = new CatalogViewIndex(state);
            Check(before.PackageAvatarNames("Manuka.unitypackage").Count == 1);
            state.metadata[0].mainName = "別名"; state.metadata[0].identifiers.Clear();
            var after = new CatalogViewIndex(state);
            Check(after.PackageAvatarNames("Manuka.unitypackage").Count == 0);
            Check(after.PackageAvatarNames("別名.unitypackage").SequenceEqual(new[] { "別名" }));
        });
        test("material badge matches EXE filename-only case-insensitive rule", () =>
        {
            Check(new PackageFileCard { file = File("Serendipity_MATERIAL_Pack.unitypackage") }.material);
            Check(!new PackageFileCard { file = File("Material/Dress.unitypackage") }.material);
            Check(!new PackageFileCard { file = File("Dress_mat.unitypackage") }.material);
            var other = File("Material.png"); other.kind = "Texture";
            Check(!new PackageFileCard { file = other }.material);
        });
        test("package cards compare numeric versions and keep avatar variants separate", () =>
        {
            var cards = PackageFileCards.Build(new[] { File("Dress_Manuka_v1.9.unitypackage"), File("Dress_Manuka_v1.10.unitypackage"), File("Dress_Shinano_v1.2.unitypackage") });
            Check(cards.Count == 2 && cards[0].version == "v1.10" && cards[0].older.Single().oldVersion);
            Check(cards[0].older[0].version == "v1.9" && cards[1].older.Count == 0);
        });
        test("plus signs distinguish volumes while versions within a volume still group", () =>
        {
            var cards = PackageFileCards.Build(new[] {
                File("PixcelBootPlus+_v1.01.unitypackage"), File("PixcelBootPlus+_v1.02.unitypackage"),
                File("PixcelBootPlus++_v1.00.unitypackage"), File("PixcelBootPlus+++_v1.00.unitypackage"),
                File("PixcelBootPlus＋＋_v1.01.unitypackage") });
            Check(cards.Count == 3 && cards.All(x => !x.oldVersion));
            Check(cards.Single(x => x.version == "v1.02").older.Single().version == "v1.01");
            Check(cards.Single(x => x.file.relative.Contains("＋＋")).older.Single().version == "v1.00");
            Check(cards.Single(x => x.file.relative.Contains("+++")).older.Count == 0);
        });
        test("package cards infer parent versions and suppress redundant duplicate badges", () =>
        {
            var cards = PackageFileCards.Build(new[] { File("Download_v1.0/Dress.unitypackage"), File("Download_v2.0/Dress.unitypackage") });
            Check(cards.Count == 1 && cards[0].version == "v2.0" && cards[0].older.Single().oldVersion);
            Check(!cards[0].duplicateName && !cards[0].older[0].duplicateName);
        });
        test("Mochi Fitter releases show original labels and group older releases", () =>
        {
            var old = File("MochiFitter65r/MochiFitter65r.unitypackage");
            var latest = File("MochiFitter66r/MochiFitter66r.unitypackage");
            old.modifiedUtc = latest.modifiedUtc.AddDays(1);
            var cards = PackageFileCards.Build(new[] { old, latest });
            Check(cards.Count == 1 && cards[0].file.path == latest.path && cards[0].version == "66r");
            Check(cards[0].older.Single().version == "65r" && cards[0].older[0].oldVersion);
        });
        test("Mochi Fitter releases compare numerically and normalize case and fullwidth", () =>
        {
            var cards = PackageFileCards.Build(new[] { File("mochifitter99R.unitypackage"), File("ＭｏｃｈｉＦｉｔｔｅｒ１００ｒ.unitypackage") });
            Check(cards.Count == 1 && cards[0].version == "100r" && cards[0].older.Single().version == "99r");
        });
        test("Mochi Fitter release exception does not match other tools or extensions", () =>
        {
            var names = new[] { "OtherTool66r", "MyMochiFitter66r", "MochiFitter66rAddon", "MochiFitter66", "MochiFitter66r_beta", "MochiFitter999999999999999999999r" };
            var cards = PackageFileCards.Build(names.Select(x => File(x + ".unitypackage")));
            Check(cards.Count == names.Length && cards.All(x => x.version == "" && !x.oldVersion && x.older.Count == 0));
        });
        test("Mochi Fitter release can come from matching parent folder", () =>
        {
            var cards = PackageFileCards.Build(new[] { File("MochiFitter65r/MochiFitter.unitypackage"), File("MochiFitter66r/MochiFitter.unitypackage") });
            Check(cards.Count == 1 && cards[0].version == "66r" && cards[0].older.Single().version == "65r");
        });
        test("same package filename without version evidence remains a duplicate not an old version", () =>
        {
            var cards = PackageFileCards.Build(new[] { File("Red/Dress.unitypackage"), File("Blue/DRESS.unitypackage") });
            Check(cards.Count == 1 && cards[0].older.Count == 1);
            Check(new[] { cards[0], cards[0].older[0] }.All(x => x.duplicateName && !x.oldVersion && !x.duplicateDownload && x.version == ""));
            Check(cards[0].file.path != cards[0].older[0].file.path);
        });
        test("download copy folders group identical names without claiming older versions", () =>
        {
            var cards = PackageFileCards.Build(new[] { File("Dress/Dress.unitypackage"), File("Dress (1)/Dress.unitypackage"), File("Dress (2)/Dress.unitypackage") });
            Check(cards.Count == 1 && cards[0].duplicateName && cards[0].older.Count == 2);
            Check(cards[0].older.All(x => x.duplicateDownload && x.duplicateName && !x.oldVersion));
        });
        test("version spelling supports fullwidth ver underscores and ignores lone model numbers", () =>
        {
            var cards = PackageFileCards.Build(new[] { File("Dress_ｖ１．２.unitypackage"), File("Dress_ver.1_3.unitypackage"), File("Model_2024.unitypackage"), File("Dress_ver.999999999999999999999.unitypackage") });
            Check(cards.Any(x => x.version == "v1.3" && x.older.Any(y => y.version == "v1.2")));
            Check(cards.Single(x => x.file.relative.StartsWith("Model")).version == "");
        });
        test("equal versions are not ordered by timestamps alone", () =>
        {
            var a = File("Red/Dress_v1.0.unitypackage"); var b = File("Blue/Dress_v1.0.unitypackage"); b.modifiedUtc = a.modifiedUtc.AddDays(1);
            var cards = PackageFileCards.Build(new[] { a, b });
            Check(cards.Count == 1 && cards[0].file.path == b.path && cards[0].older.Single().file.path == a.path);
            Check(cards[0].duplicateName && cards[0].older[0].duplicateName && !cards[0].oldVersion && !cards[0].older[0].oldVersion);
        });
        test("same-name grouping retains old-version trees and every original file exactly once", () =>
        {
            var input = new[] { File("A/Dress_v2.0.unitypackage"), File("B/Dress_v2.0.unitypackage"), File("A/Dress_v1.0.unitypackage"), File("Hat_v1.0.unitypackage") };
            var roots = PackageFileCards.Build(input);
            var seen = new System.Collections.Generic.HashSet<string>();
            void Visit(PackageFileCard card)
            {
                Check(seen.Add(card.file.path));
                foreach (var child in card.older) Visit(child);
            }
            foreach (var root in roots) Visit(root);
            Check(roots.Count == 2 && seen.SetEquals(input.Select(x => x.path)));
            var dress = roots.Single(x => x.version == "v2.0");
            Check(dress.older.Count == 2 && dress.older.Count(x => x.oldVersion) == 1);
        });
    }
}
