using System.Diagnostics;
using KaihenShelf;
using KaihenShelf.TestSupport;

static class CatalogIndexTests
{
    static void Check(bool value, string message = "assertion failed") { if (!value) throw new Exception(message); }
    static string[] Signatures(IEnumerable<AvatarMatch> matches) => matches.Select(x => x.avatarId + "|" + x.indirect + "|" + x.reason).ToArray();
    static string[] Ids(IEnumerable<LibraryItem> items) => items.Select(x => x.id).ToArray();

    static LibraryState Fixture(int products = 45, int avatarCount = 8, int descriptionLength = 400)
    {
        var state = new LibraryState { catalogRulesVersion = 1 };
        for (int i = 0; i < avatarCount; i++)
        {
            string id = "avatar" + i;
            state.items.Add(new LibraryItem { id = id, boothId = (300000 + i).ToString(), title = "Avatar" + i.ToString("D3"), shop = "Shop" + i % 100, category = AssetCategory.Avatar, ageRestricted = i % 7 == 0 });
            state.metadata.Add(new ItemMetadata { id = id, identifiersEdited = true, mainName = "Avatar" + i.ToString("D3"),
                identifiers = new List<string> { "Avatar" + i.ToString("D3"), "架空" + i.ToString("D3"), (300000 + i).ToString(), i == 0 ? "萌" : "別名" + i } });
        }
        if (avatarCount >= 4)
        {
            state.metadata[0].sharedBodies.Add("avatar1");
            state.metadata[1].sharedBodies.Add("avatar2");
            state.metadata[2].sharedBodies.Add("avatar3");
        }
        for (int i = 0; i < products; i++)
        {
            string id = "product" + i;
            var item = new LibraryItem { id = id, boothId = (400000 + i).ToString(), title = "衣装 " + i.ToString("D6"), shop = "Shop" + i % 100,
                category = (AssetCategory)(1 + i % 11), description = new string('あ', descriptionLength) + " Avatar" + (i % avatarCount).ToString("D3") + "対応",
                tags = Enumerable.Range(0, 10).Select(n => "タグ" + (i + n) % 20).ToList(), ageRestricted = i % 9 == 0,
                registeredUtc = "2026-09-" + (1 + i % 28).ToString("D2"), updatedUtc = "2026-10-" + (1 + (i * 7) % 28).ToString("D2"),
                acquisition = i % 7 == 0 ? "無料/ギフト" : "フルパック", shopThumbnailUrl = i % 2 == 0 ? "https://example.invalid/" + i : "" };
            if (i % 5 == 0)
            {
                item.variationMetadataKnown = item.hasVariationRows = item.hasPurchasedVariationOrder = true;
                item.purchasedVariationNames.Add("Avatar" + (i % avatarCount).ToString("D3") + "専用");
                item.variationNames.Add("Avatar" + ((i + 1) % avatarCount).ToString("D3") + "専用");
                item.title += " Avatar" + ((i + 2) % avatarCount).ToString("D3");
            }
            state.items.Add(item);
            state.metadata.Add(new ItemMetadata { id = id, categoryEdited = i % 11 == 0, category = AssetCategory.Material,
                compatibleAvatars = i % 13 == 0 ? new List<string> { "avatar0" } : new List<string>(),
                excludedAvatars = i % 17 == 0 ? new List<string> { "avatar2" } : new List<string>() });
            if (i % 3 == 0) state.favorites.Add(new Favorite { scope = "items", key = id });
        }
        state.favorites.Add(new Favorite { scope = "avatars", key = "avatar0" });
        state.favorites.Add(new Favorite { scope = "shops", key = "Shop1" });
        return state;
    }

    static List<LibraryItem> LegacyQuery(LibraryState state, CatalogFilter filter)
    {
        var query = state.items.Where(x => (state.Category(x) == AssetCategory.Avatar) == filter.avatars)
            .Where(x => !filter.hideR18 || !x.ageRestricted)
            .Where(x => !filter.favoritesOnly || state.IsFavorite(state.Scope(x), x.id))
            .Where(x => Matcher.Normalize(x.title + " " + string.Join(" ", x.tags)).Contains(Matcher.Normalize(filter.search)));
        if (!filter.avatars) query = query.Where(x => (filter.category == 0 || (int)state.Category(x) == filter.category)
            && (filter.shop.Length == 0 || x.shop == filter.shop) && (filter.tag.Length == 0 || x.tags.Contains(filter.tag))
            && (!filter.freeOnly || x.acquisition.Contains("無料") || x.acquisition.Contains("ギフト") || Matcher.Normalize(x.acquisition) == "free" || Matcher.Normalize(x.acquisition) == "gift")
            && (filter.avatarId.Length == 0 || !Categories.HasAvatarCompatibility(state.Category(x)) || LegacyMatcherReference.Match(x, state).Count == 0 || LegacyMatcherReference.Match(x, state).Any(m => m.avatarId == filter.avatarId && (!filter.excludeShared || !m.indirect))));
        return (filter.sort == 4 ? query.OrderByDescending(x => x.registeredUtc)
            : filter.sort == 6 ? query.OrderByDescending(x => x.updatedUtc)
            : query.OrderBy(x => x.title, StringComparer.CurrentCultureIgnoreCase)).ToList();
    }

    public static void Run(Action<string, Action> test)
    {
        test("no-avatar filtering distinguishes zero results from excluded shared-body results", () =>
        {
            var state = Fixture(0, 4);
            state.items.Add(new LibraryItem { id = "none", title = "Unknown", category = AssetCategory.Clothing });
            state.items.Add(new LibraryItem { id = "tool", title = "Tool", category = AssetCategory.Tool });
            state.items.Add(new LibraryItem { id = "direct", title = "Avatar000", category = AssetCategory.Clothing });
            var index = new CatalogViewIndex(state);
            var filter = new CatalogFilter { avatarId = "avatar1" };
            Check(index.Query(filter).Count == 3);
            filter.excludeShared = true;
            Check(string.Join(",", Ids(index.Query(filter)).OrderBy(x => x)) == "none,tool");
            filter.excludeNoAvatar = true;
            Check(index.Query(filter).Count == 0);
            filter.avatarId = "";
            Check(index.Query(filter).Count == 3, "no-avatar exclusion is inactive without an avatar filter");
            filter.excludeNoAvatar = false;
            Check(index.Query(filter).Count == 3, "checkbox must invalidate query cache");
        });
        test("shop and tag sort modes use names and counts", () =>
        {
            var state = new LibraryState();
            foreach (string name in new[] { "Alpha", "Bravo", "Bravo", "Charlie", "Charlie", "Charlie" })
                state.items.Add(new LibraryItem { id = state.items.Count.ToString(), shop = name, tags = new() { name }, category = AssetCategory.Clothing });
            var index = new CatalogViewIndex(state);
            foreach (string scope in new[] { "shops", "booth-tags" })
                for (int sort = 0; sort < 4; sort++)
                    Check(string.Join(",", index.QueryGroups(scope, "", false, false, sort).Select(x => x.key)) == (sort % 2 == 0 ? "Alpha,Bravo,Charlie" : "Charlie,Bravo,Alpha"));
        });
        test("imported badges include successful receipts and texture descendants only", () =>
        {
            var index = new ImportedHistoryIndex();
            string folder = Path.Combine(Path.GetTempPath(), "Receipt", "Textures");
            index.Add("item", folder, "配置完了");
            index.Add("failed", folder, "要確認");
            index.Add("pending", folder, "配置中");
            Check(index.ContainsItem("item") && !index.ContainsItem("failed") && !index.ContainsItem("pending"));
            Check(index.ContainsSource("item", folder.ToUpperInvariant(), true));
            Check(index.ContainsSource("item", Path.Combine(folder, "Body"), true));
            Check(!index.ContainsSource("item", folder + "Extra", true));
            Check(!index.ContainsSource("item", Path.GetDirectoryName(folder), true));
            Check(!index.ContainsSource("other", folder, true));
            string package = Path.Combine(folder, "A.unitypackage");
            index.Add("package", package, "配置完了");
            Check(index.ContainsSource("package", package, false));
            Check(!index.ContainsSource("package", Path.Combine(folder, "B.unitypackage"), false));
            Check(!new ImportedHistoryIndex().ContainsItem("item"));
        });
        test("avatar-independent categories pass avatar filters but retain other filters", () =>
        {
            var state = Fixture(0, 2);
            foreach (var category in new[] { AssetCategory.Tool, AssetCategory.Shader, AssetCategory.World, AssetCategory.Other, AssetCategory.Clothing })
                state.items.Add(new LibraryItem { id = category.ToString(), category = category, title = "Avatar001", shop = "Shop", tags = new() { "tag" } });
            var index = new CatalogViewIndex(state);
            var query = new CatalogFilter { avatarId = "avatar0", excludeShared = true };
            var result = index.Query(query);
            Check(result.Count == 4 && result.All(item => !Categories.HasAvatarCompatibility(index.Category(item))));
            Check(result.All(item => index.Match(item).Count == 0));
            query.category = (int)AssetCategory.Shader;
            Check(index.Query(query).Single().id == "Shader");
            query.shop = "Another"; Check(index.Query(query).Count == 0);
            query.shop = "Shop"; query.tag = "missing"; Check(index.Query(query).Count == 0);
            query.tag = "tag"; query.favoritesOnly = true; Check(index.Query(query).Count == 0);
            query.favoritesOnly = false; query.freeOnly = true; Check(index.Query(query).Count == 0);
            query.freeOnly = false; query.search = "missing"; Check(index.Query(query).Count == 0);
            state.SetCategory(state.items.Single(item => item.id == "Shader"), AssetCategory.Clothing);
            index = new CatalogViewIndex(state); query.search = "";
            Check(index.Query(query).Count == 0);
        });
        test("indexed matcher preserves legacy evidence, ordering, purchased-only and shared-body behavior", () =>
        {
            var state = Fixture(); var index = new MatcherIndex(state);
            foreach (var item in state.items)
            {
                var old = Signatures(LegacyMatcherReference.Match(item, state));
                Check(Signatures(index.Match(item)).SequenceEqual(old), item.id + " base differs");
                string[] files = { "Avatar004/萌_衣装.unitypackage", "300006" };
                Check(Signatures(index.Match(item, files)).SequenceEqual(Signatures(LegacyMatcherReference.Match(item, state, files))), item.id + " files differ");
            }
        });
        test("literal identifier scanner preserves Unicode regex boundaries", () =>
        {
            string[] names = { "Maya", "萌", "12", "１２", "١٢", "My3DAvatar", "架空001", "Ａｖａｔａｒ", "a_b", "foo bar", "foo \nbar", "https://booth.pm/items/123", "a.b", "e\u0301" };
            string[] surrounds = { "", " ", "x", "0", "²", "Ⅲ", "あ", "対応", "専用", "用", "対応衣装", "_", "-", "\n", "\u0301", "😀" };
            foreach (string name in names)
                foreach (string left in surrounds)
                    foreach (string right in surrounds)
                    {
                        string text = left + name + right;
                        Check(Matcher.ContainsIdentifier(text, name) == LegacyMatcherReference.ContainsIdentifier(text, name), "boundary differs: " + text + " / " + name);
                    }
        });
        test("indexed queries preserve legacy filters and retained sort orders", () =>
        {
            var state = Fixture(); var index = new CatalogViewIndex(state);
            var random = new Random(12873);
            for (int i = 0; i < 100; i++)
            {
                var filter = new CatalogFilter { avatars = i % 9 == 0, hideR18 = i % 3 == 0, favoritesOnly = i % 4 == 0,
                    freeOnly = i % 8 == 0, excludeShared = i % 2 == 0, sort = new[] { 0, 4, 6 }[i % 3],
                    category = i % 7 == 0 ? random.Next(1, 12) : 0, search = i % 5 == 0 ? "衣装" : "",
                    shop = i % 11 == 0 ? "Shop" + random.Next(10) : "", tag = i % 6 == 0 ? "タグ" + random.Next(20) : "",
                    avatarId = i % 4 == 1 ? "avatar" + random.Next(8) : "" };
                Check(Ids(index.Query(filter)).SequenceEqual(Ids(LegacyQuery(state, filter))), "query " + i);
            }
        });
        test("app sort menu orders names, shops and actual timestamps with unknown dates last", () =>
        {
            foreach (bool avatars in new[] { false, true })
            {
                var category = avatars ? AssetCategory.Avatar : AssetCategory.Clothing;
                var state = new LibraryState();
                state.items.Add(new LibraryItem { id = "a", title = "alpha", shop = "Zoo", category = category,
                    registeredUtc = "2026-01-01T10:00:00+09:00", updatedUtc = "2026-01-03", publishedUtc = "2026-01-02" });
                state.items.Add(new LibraryItem { id = "b", title = "Bravo", shop = "alpha", category = category,
                    registeredUtc = "2026-01-01T02:00:00Z", updatedUtc = "2026-01-01", publishedUtc = "2026-01-03" });
                state.items.Add(new LibraryItem { id = "c", title = "charlie", shop = "ALPHA", category = category,
                    registeredUtc = "2026-01-02", updatedUtc = "2026-01-02", publishedUtc = "2026-01-01" });
                state.items.Add(new LibraryItem { id = "d", title = "Delta", shop = "Middle", category = category,
                    registeredUtc = "invalid", updatedUtc = "", publishedUtc = null });
                string[] expected = { "abcd", "dcba", "bcda", "adbc", "cbad", "abcd", "acbd", "bcad", "bacd", "cabd" };
                var index = new CatalogViewIndex(state);
                for (int sort = 0; sort < (avatars ? 8 : 10); sort++)
                    Check(string.Concat(Ids(index.Query(new CatalogFilter { avatars = avatars, sort = sort }))) == expected[sort], "sort " + sort);
            }
        });
        test("unchanged UI queries and matches reuse cached results", () =>
        {
            var state = Fixture(); var index = new CatalogViewIndex(state); var filter = new CatalogFilter();
            var first = index.Query(filter); var item = first[0]; var matches = index.Match(item);
            for (int i = 0; i < 10000; i++) Check(ReferenceEquals(first, index.Query(filter)) && ReferenceEquals(matches, index.Match(item)));
            Check(ReferenceEquals(first, index.Query(new CatalogFilter())), "equivalent filter should reuse result");
        });
        test("favorite updates refresh scoped queries while retaining expensive matches", () =>
        {
            var state = Fixture(); var index = new CatalogViewIndex(state); var item = state.items[9]; var match = index.Match(item);
            var filter = new CatalogFilter { favoritesOnly = true }; var before = index.Query(filter);
            state.ToggleFavorite("items", item.id); index.UpdateFavorites(state.favorites);
            Check(index.Query(filter).Any(x => x.id == item.id) == state.IsFavorite("items", item.id));
            Check(!ReferenceEquals(before, index.Query(filter)) && ReferenceEquals(match, index.Match(item)));
            Check(index.Query(new CatalogFilter { avatars = true, favoritesOnly = true }).Single().id == "avatar0");
        });
        test("file scan invalidates only the affected match and avatar filter", () =>
        {
            var state = Fixture(); var item = state.items[9]; item.title = "blank"; item.description = ""; item.tags.Clear();
            var index = new CatalogViewIndex(state); var untouched = index.Match(state.items[10]);
            var filter = new CatalogFilter { avatarId = "avatar7", excludeNoAvatar = true };
            Check(!index.Query(filter).Any(x => x.id == item.id));
            index.SetFilenames(item.id, new[] { "Avatar007.unitypackage" });
            Check(index.Query(filter).Any(x => x.id == item.id));
            Check(ReferenceEquals(untouched, index.Match(state.items[10])));
            index.SetFilenames(item.id, null); Check(!index.Query(filter).Any(x => x.id == item.id));
        });
        test("metadata and category edits become visible after rebuilding snapshot", () =>
        {
            var state = Fixture(); var item = state.items[9]; var before = new CatalogViewIndex(state);
            state.SetCategory(item, AssetCategory.Avatar); state.Meta(item.id).mainName = "NewAvatar";
            var after = new CatalogViewIndex(state);
            Check(before.Category(item) != AssetCategory.Avatar && after.Category(item) == AssetCategory.Avatar);
            Check(!before.Avatars.Contains(item) && after.Avatars.Contains(item));
            Check(after.Query(new CatalogFilter { avatars = true }).Contains(item));
        });
        test("indexed shop and tag groups preserve age filtering, counts, search and favorites", () =>
        {
            var state = Fixture(); state.items[9].tags.Add(state.items[9].tags[0]); var index = new CatalogViewIndex(state);
            foreach (bool hide in new[] { false, true })
            {
                var source = state.items.Where(x => !hide || !x.ageRestricted);
                var expectedShops = source.Where(x => x.shop.Length > 0).GroupBy(x => x.shop).OrderBy(x => x.Key).Select(x => x.Key + "=" + x.Count());
                Check(index.QueryGroups("shops", "", false, hide).Select(x => x.key + "=" + x.count).SequenceEqual(expectedShops));
                var expectedTags = source.Where(x => state.Category(x) != AssetCategory.Avatar).SelectMany(x => x.tags.Distinct()).GroupBy(x => x).OrderBy(x => x.Key).Select(x => x.Key + "=" + x.Count());
                Check(index.QueryGroups("booth-tags", "", false, hide).Select(x => x.key + "=" + x.count).SequenceEqual(expectedTags));
            }
            Check(index.QueryGroups("shops", "ｓｈｏｐ１", true, false).Single().key == "Shop1");
            var group = index.QueryGroups("shops", "", false, false).First(x => x.key == "Shop0");
            Check(group.item.shopThumbnailUrl.Length > 0);
            Check(index.AvatarLabels.Length == index.Avatars.Length + 1 && index.AvatarLabels[0] == "すべて");
        });
        test("overlapping aliases and normalized duplicate aliases retain legacy evidence priority", () =>
        {
            var state = Fixture(2, 4); state.metadata[0].identifiers = new List<string> { "maya", "ｍａｙａ", "maya outfit", "萌", "12" };
            state.metadata[1].identifiers = new List<string> { "maya", "outfit", "萌" };
            var item = state.items[5]; item.description = "ＭＡＹＡ outfit と 萌対応 312 12";
            Check(Signatures(new MatcherIndex(state).Match(item)).SequenceEqual(Signatures(LegacyMatcherReference.Match(item, state))));
        });
        test("avatar filter uses main names and preserves avatar ID mapping", () =>
        {
            var state = Fixture();
            state.metadata[0].mainName = "エク"; state.metadata[1].mainName = "エク";
            state.metadata[2].mainName = " "; state.metadata.RemoveAll(x => x.id == "avatar3");
            var index = new CatalogViewIndex(state);
            for (int i = 0; i < index.Avatars.Length; i++)
            {
                var avatar = index.Avatars[i];
                string name = state.metadata.Find(x => x.id == avatar.id)?.mainName;
                Check(index.AvatarLabels[i + 1] == (string.IsNullOrWhiteSpace(name) ? avatar.title : name));
            }
            Check(index.AvatarLabels.Count(x => x == "エク") == 2);
        });
        test("catalog indexing does not create user metadata entries", () =>
        {
            var state = Fixture(); state.metadata.RemoveAll(x => x.id.StartsWith("product")); int count = state.metadata.Count;
            var index = new CatalogViewIndex(state); foreach (var item in state.items) index.Match(item);
            Check(state.metadata.Count == count);
        });
    }

    // Explicit opt-in keeps timing diagnostics out of ordinary regression runs.
    public static void Benchmark()
    {
        foreach (int count in new[] { 5000, 20000 })
        {
            var state = Fixture(count, 121); var stopwatch = Stopwatch.StartNew();
            AvatarCatalog.Synchronize(state); double synchronize = stopwatch.Elapsed.TotalMilliseconds;
            stopwatch.Restart();
            var index = new CatalogViewIndex(state); double build = stopwatch.Elapsed.TotalMilliseconds;
            var filter = new CatalogFilter { hideR18 = true };
            stopwatch.Restart(); var result = index.Query(filter); double query = stopwatch.Elapsed.TotalMilliseconds;
            stopwatch.Restart(); for (int i = 0; i < 100; i++) index.Query(filter); double repeated = stopwatch.Elapsed.TotalMilliseconds;
            stopwatch.Restart(); var first = index.Match(result[0]); double oneMatch = stopwatch.Elapsed.TotalMilliseconds;
            stopwatch.Restart(); foreach (var item in result.Take(60)) index.Match(item); double page = stopwatch.Elapsed.TotalMilliseconds;
            filter.avatarId = "avatar7"; stopwatch.Restart(); var matched = index.Query(filter); double avatarFilter = stopwatch.Elapsed.TotalMilliseconds;
            stopwatch.Restart(); for (int i = 0; i < 100; i++) index.Query(filter); double avatarWarm = stopwatch.Elapsed.TotalMilliseconds;
            stopwatch.Restart(); var legacyOne = LegacyMatcherReference.Match(result[0], state); double legacyMatch = stopwatch.Elapsed.TotalMilliseconds;
            filter.avatarId = ""; stopwatch.Restart(); var legacy = LegacyQuery(state, filter); double legacyQuery = stopwatch.Elapsed.TotalMilliseconds;
            Check(Ids(result).SequenceEqual(Ids(legacy)) && Signatures(first).SequenceEqual(Signatures(legacyOne)));
            Console.WriteLine($"PERF products={count} avatars=121 description=400chars synchronize={synchronize:F2}ms build={build:F2}ms query={query:F2}ms query_warm100={repeated:F3}ms match_cold1={oneMatch:F3}ms match_first60={page:F2}ms avatar_filter_cold={avatarFilter:F2}ms avatar_filter_warm100={avatarWarm:F3}ms legacy_match1={legacyMatch:F2}ms legacy_query={legacyQuery:F2}ms matched={matched.Count}");
        }
    }
}
