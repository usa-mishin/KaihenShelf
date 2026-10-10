using KaihenShelf;

static class AvatarRulesTests
{
    static void Check(bool value, string message = "assertion failed") { if (!value) throw new Exception(message); }

    static LibraryState State()
    {
        var state = new LibraryState { catalogRulesVersion = 1 };
        state.items.AddRange(new[] {
            new LibraryItem { id = "a", boothId = "1001", title = "オリジナル3Dモデル『舞夜』 Maya_v1.2", category = AssetCategory.Avatar },
            new LibraryItem { id = "b", boothId = "1002", title = "萌 Moe", category = AssetCategory.Avatar },
            new LibraryItem { id = "c", boothId = "1003", title = "シフォン Chiffon", category = AssetCategory.Avatar }
        });
        AvatarCatalog.SetDefaults(null);
        AvatarCatalog.Synchronize(state);
        return state;
    }

    static LibraryItem Clothing(LibraryState state, params string[] purchased)
    {
        var item = new LibraryItem { id = "outfit", boothId = "2001", source = "blm", category = AssetCategory.Clothing,
            title = "Maya Moe Chiffon 衣装", description = "全ての商品説明: Maya, Moe, Chiffon", tags = new List<string> { "Chiffon" },
            hasVariationRows = true, hasPurchasedVariationOrder = true, variationMetadataKnown = true,
            variationNames = new List<string> { "Maya専用", "Moe専用", "Chiffon専用" }, purchasedVariationNames = purchased.ToList() };
        state.items.Add(item);
        return item;
    }

    public static void Run(Action<string, Action> test)
    {
        test("uncatalogued automatic identifiers survive title changes and repeated sync", () =>
        {
            var state = State();
            var meta = state.Meta("a");
            string name = meta.mainName;
            var identifiers = meta.identifiers.ToArray();
            state.items[0].title = "別の商品名 Renamed_v9.0";
            AvatarCatalog.Synchronize(state); AvatarCatalog.Synchronize(state);
            Check(meta.mainName == name && meta.identifiers.SequenceEqual(identifiers));
            Check(!meta.identifiersEdited, "automatic identifiers remain eligible for future catalog defaults");
            meta.identifiers.Clear();
            AvatarCatalog.Synchronize(state);
            Check(meta.mainName == "別の商品名" && meta.identifiers.Contains("Renamed"), "missing identifiers are generated");
        });
        test("BOOTH subcategories use confirmed category", () =>
        {
            Check(Categories.Classify("3Dモデル / 3Dキャラクター") == AssetCategory.Avatar);
            Check(Categories.Classify("3Dモデル / 3Dテクスチャ") == AssetCategory.Texture);
            Check(Categories.Classify("3D素材・マテリアル") == AssetCategory.Other);
            Check(Categories.Classify("3D靴") == AssetCategory.Clothing);
            Check(Categories.Classify("3Dモーション・アニメーション") == AssetCategory.Animation);
            Check(Categories.Classify("マテリアル入りの3D衣装") == AssetCategory.Clothing);
            Check(Categories.Classify("Maya対応 衣装の商品名") == AssetCategory.Other);
        });
        test("fallback names keep Japanese single characters and names containing 3D", () =>
        {
            AvatarCatalog.SetDefaults(null);
            var meta = new ItemMetadata();
            AvatarCatalog.Populate(new LibraryItem { boothId = "9001", title = "【オリジナル3Dモデル】萌 Moe_v2.0 VRChat/Quest対応 女性型 Blender 無料あり セール中 1000円" }, meta);
            Check(meta.mainName == "萌");
            Check(meta.identifiers.SequenceEqual(new[] { "萌", "Moe", "9001" }), string.Join(",", meta.identifiers));
            Check(Matcher.ValidIdentifier("萌") && Matcher.ValidIdentifier("Menno3D"));
            Check(!Matcher.ValidIdentifier("オリジナル3Dモデル") && !Matcher.ValidIdentifier("VRChat用") && !Matcher.ValidIdentifier("https://booth.pm/items/9001"));
        });
        test("known BOOTH IDs take precedence and catalog updates preserve manual names", () =>
        {
            var state = State();
            AvatarCatalog.SetDefaults(new[] { new DefaultAvatarEntry { boothItemId = 1001, primaryIdentifier = "正式名", identifiers = new[] { "Official" } } });
            AvatarCatalog.Synchronize(state);
            Check(state.Meta("a").mainName == "正式名" && state.Meta("a").identifiers.SequenceEqual(new[] { "正式名", "Official", "1001" }));
            state.Meta("a").mainName = "手動名"; state.Meta("a").identifiersEdited = true;
            AvatarCatalog.SetDefaults(new[] { new DefaultAvatarEntry { boothItemId = 1001, primaryIdentifier = "新版名", identifiers = new[] { "Updated" } } });
            AvatarCatalog.Synchronize(state);
            Check(state.Meta("a").mainName == "手動名" && state.Meta("a").identifiers.Contains("Official"));
            AvatarCatalog.SetDefaults(null);
        });
        test("generic-only avatar titles do not become matching names", () =>
        {
            var state = new LibraryState { catalogRulesVersion = 1 };
            state.items.Add(new LibraryItem { id = "generic", title = "オリジナル3Dモデル VRChat用", category = AssetCategory.Avatar });
            AvatarCatalog.Synchronize(state);
            Check(state.Meta("generic").mainName == "" && state.Meta("generic").identifiers.Count == 0);
            Check(Matcher.Match(new LibraryItem { id = "item", title = "オリジナル3Dモデル VRChat用" }, state).Count == 0);
        });
        test("legacy names migrate as manual before automatic defaults", () =>
        {
            var state = new LibraryState();
            var old = new LibraryItem { id = "old", title = "自動名", category = AssetCategory.Avatar };
            var fresh = new LibraryItem { id = "fresh", title = "新規名 NewName", category = AssetCategory.Avatar };
            state.items.AddRange(new[] { old, fresh });
            state.Meta("old").mainName = "前バージョンで手動編集";
            state.Meta("old").identifiers.Add("VRChat");
            AvatarCatalog.Synchronize(state);
            Check(state.catalogRulesVersion == 1 && state.Meta("old").identifiersEdited);
            Check(state.Meta("old").mainName == "前バージョンで手動編集" && state.Meta("old").identifiers.Contains("VRChat"));
            Check(!state.Meta("fresh").identifiersEdited && state.Meta("fresh").mainName == "新規名");
        });
        test("purchased variation is the only evidence for avatar-specific clothing", () =>
        {
            var state = State(); var item = Clothing(state, "ＭＡＹＡ専用");
            Check(AcquisitionClassifier.Classify(item, state) == AcquisitionClassifier.AvatarSpecific);
            var matches = Matcher.Match(item, state, new[] { "Moe_Chiffon.unitypackage" });
            Check(matches.Count == 1 && matches[0].avatarId == "a", "unpurchased avatar leaked into matches");
        });
        test("single Japanese name works in purchased variations", () =>
        {
            var state = State(); var item = Clothing(state, "萌用");
            Check(AcquisitionClassifier.Classify(item, state) == AcquisitionClassifier.AvatarSpecific);
            Check(Matcher.Match(item, state).Single().avatarId == "b");
        });
        test("unowned full pack variation does not change acquisition", () =>
        {
            var state = State(); var item = Clothing(state, "Maya"); item.variationNames.Add("Full Pack");
            Check(AcquisitionClassifier.Classify(item, state) == AcquisitionClassifier.AvatarSpecific);
            Check(Matcher.Match(item, state).Single().avatarId == "a");
        });
        test("full-pack wording must be purchased and word bounded", () =>
        {
            var state = State(); var item = Clothing(state, "Maya Small サイズ");
            Check(AcquisitionClassifier.Classify(item, state) == AcquisitionClassifier.AvatarSpecific);
            foreach (string name in new[] { "FULL_PACK", "Complete Set", "All Avatars", "フルセット", "全アバター" })
            {
                item.purchasedVariationNames = new List<string> { "Maya", name };
                Check(AcquisitionClassifier.Classify(item, state) == AcquisitionClassifier.FullPack, name);
            }
        });
        test("full pack variants observed in imported purchase names are recognized", () =>
        {
            var state = State(); var item = Clothing(state);
            foreach (string name in new[] { "Full", "♡Full♡", "🎁 Full", "FULL(60種)", "FULL(45種)",
                "[Full Avatar Package]", "Full_Pakage", "Full Pakage", "♡ふるぱっく♡", "全部セット(ALL)",
                "有料版【全118種】", "完全版！！", "コンプリート版", "💝𝐅𝐮𝐥𝐥“𝐌𝐚𝐱𝟏𝟎𝟎”⁺Blink",
                "✧𝗙𝘂𝗹𝗹 𝘀𝗲𝘁✧", "ＦｕｌｌＳｅｔ (2090円)" })
            {
                item.purchasedVariationNames = new List<string> { name };
                Check(AcquisitionClassifier.Classify(item, state) == AcquisitionClassifier.FullPack, name);
                var matcher = new MatcherIndex(state);
                Check(matcher.Acquisition(item) == AcquisitionClassifier.FullPack, name);
                Check(matcher.Match(item).Count == 3, "full pack should use product-wide compatibility: " + name);
            }
        });
        test("shared full pack wording tolerates casing separators and decorations across shops", () =>
        {
            var state = State(); var item = Clothing(state);
            foreach (string name in new[] { "ｆｕｌｌ＿ｐａｃｋ", "FuLl—PaCk", "FULL・PACKAGE", "Full (Avatar) Package",
                "𝐅𝐮𝐥𝐥 ✨ 𝐒𝐞𝐭", "Full.Bundle", "Full Edition", "COMPLETE-PACKAGE", "Complete bundle",
                "ALL_IN_ONE", "ALL—AVATARS", "フル・セット", "ふる☆ぱっく", "フル版", "コンプリート・パック",
                "完全 セット", "全部・入り", "全種セット", "全１２種類", "FULL［60 TYPES］", "Full Maximum 100" })
            {
                item.purchasedVariationNames = new List<string> { name };
                item.shop = "Any shop";
                Check(AcquisitionClassifier.Classify(item, state) == AcquisitionClassifier.FullPack, name);
                item.shop = "別のショップ"; item.boothId = "999999";
                Check(new MatcherIndex(state).Acquisition(item) == AcquisitionClassifier.FullPack, name);
            }
        });
        test("ambiguous names and unpurchased full variants stay single purchases", () =>
        {
            var state = State(); var item = Clothing(state);
            item.title = "Full Pack 全118種 フルセット";
            item.variationNames.AddRange(new[] { "Full", "Full_Pakage", "ふるぱっく" });
            foreach (string name in new[] { "通常版", "支援版", "Maya", "Milfy & Eku", "Full Body for Maya",
                "Full makeup only", "Small", "Beautiful", "Fullness", "Full body set for Maya", "Maya用セット",
                "全身用", "うさ耳セット", "Hair+ACC", "30種類", "追加プリセット①" })
            {
                item.purchasedVariationNames = new List<string> { name };
                Check(AcquisitionClassifier.Classify(item, state) == AcquisitionClassifier.AvatarSpecific, name);
                Check(new MatcherIndex(state).Acquisition(item) == AcquisitionClassifier.AvatarSpecific, name);
            }
        });
        test("named purchased variations are single purchases without an avatar name", () =>
        {
            var state = State(); var item = Clothing(state);
            foreach (string name in new[] { "通常版", "UnknownAvatar専用", "Small", "12345" })
            {
                item.purchasedVariationNames = new List<string> { name };
                Check(AcquisitionClassifier.Classify(item, state) == AcquisitionClassifier.AvatarSpecific, name);
                var matcher = new MatcherIndex(state);
                Check(matcher.Acquisition(item) == AcquisitionClassifier.AvatarSpecific, name);
                Check(matcher.Match(item).Count == 3,
                    "variations without a known avatar should use product evidence: " + name);
            }
            state.items.RemoveAll(x => x.category == AssetCategory.Avatar);
            Check(AcquisitionClassifier.Classify(item, state) == AcquisitionClassifier.AvatarSpecific);
        });
        test("makeup content variation falls back to tags with shared bodies and manual overrides", () =>
        {
            var state = State(); var item = Clothing(state, "【MAKEUP+EYE】");
            item.category = AssetCategory.Texture; item.title = "Makeup+Eye+Body blush";
            item.description = ""; item.variationNames.Clear(); item.tags = new() { "Maya", "萌" };
            state.Meta("b").sharedBodies.Add("c");
            var index = new MatcherIndex(state);
            Check(index.Acquisition(item) == AcquisitionClassifier.AvatarSpecific);
            var matches = index.Match(item);
            Check(matches.Count == 3 && matches.Count(m => !m.indirect) == 2);
            Check(matches.Single(m => m.avatarId == "c").indirect);
            Check(matches.Where(m => !m.indirect).All(m => m.reason.StartsWith("識別タグ:")));
            Check(ReferenceEquals(matches, index.Match(item)), "cached fallback must stay stable");
            state.Meta(item.id).excludedAvatars.Add("b");
            state.Meta(item.id).compatibleAvatars.Add("c");
            matches = new MatcherIndex(state).Match(item);
            Check(!matches.Any(m => m.avatarId == "b") && matches.Single(m => m.avatarId == "c").reason == "手動指定");
        });
        test("excluding a purchased avatar does not enable unpurchased avatars", () =>
        {
            var state = State(); var item = Clothing(state, "Maya", "【MAKEUP+EYE】");
            state.Meta(item.id).excludedAvatars.Add("a");
            var index = new MatcherIndex(state);
            Check(index.Match(item).Count == 0);
            index.SetFilenames(item.id, new[] { "Moe.unitypackage" });
            Check(index.Match(item).Count == 0);
        });
        test("known catalog avatar absent from the library prevents fallback to other avatars", () =>
        {
            var state = State(); var item = Clothing(state, "真冬 / Mafuyu");
            AvatarCatalog.SetDefaults(new[] { new DefaultAvatarEntry { boothItemId = 9999, primaryIdentifier = "真冬", identifiers = new[] { "Mafuyu" } } });
            try { Check(new MatcherIndex(state).Match(item).Count == 0); }
            finally { AvatarCatalog.SetDefaults(null); }
        });
        test("purchased avatar honorifics keep matching restricted to purchased names", () =>
        {
            var state = State(); var item = Clothing(state, "萌ちゃん/舞夜ちゃん用");
            var result = new MatcherIndex(state).Match(item);
            Check(result.Count == 2 && result.All(m => m.avatarId != "c" && m.reason.StartsWith("購入済み商品種類:")));
        });
        test("fallback filename updates invalidate cached matches without stale product evidence", () =>
        {
            var state = State(); var item = Clothing(state, "通常版");
            item.title = ""; item.description = ""; item.tags.Clear(); item.variationNames.Clear();
            var index = new MatcherIndex(state);
            Check(index.Match(item).Count == 0);
            index.SetFilenames(item.id, new[] { "Moe.unitypackage" });
            Check(index.Match(item).Single().avatarId == "b");
            Check(index.Match(item, new[] { "Maya.unitypackage" }).Single().avatarId == "a");
            Check(index.Match(item).Single().avatarId == "b");
            index.SetFilenames(item.id, null);
            Check(index.Match(item).Count == 0);
        });
        test("absent or blank purchased variation names remain full packs", () =>
        {
            var state = State(); var item = Clothing(state);
            foreach (var names in new[] { null, new List<string>(), new List<string> { null, "", " \t", "　" } })
            {
                item.purchasedVariationNames = names;
                Check(AcquisitionClassifier.Classify(item, state) == AcquisitionClassifier.FullPack);
                Check(new MatcherIndex(state).Acquisition(item) == AcquisitionClassifier.FullPack);
            }
            item.hasVariationRows = false; item.hasPurchasedVariationOrder = false;
            Check(AcquisitionClassifier.Classify(item, state) == AcquisitionClassifier.FullPack);
        });
        test("free or gift requires variation rows with no purchased order", () =>
        {
            var state = State(); var item = Clothing(state); item.hasPurchasedVariationOrder = false;
            Check(AcquisitionClassifier.Classify(item, state) == AcquisitionClassifier.FreeGift);
            item.hasVariationRows = false;
            Check(AcquisitionClassifier.Classify(item, state) == AcquisitionClassifier.FullPack);
            item.hasVariationRows = true; item.hasPurchasedVariationOrder = true;
            Check(AcquisitionClassifier.Classify(item, state) == AcquisitionClassifier.FullPack);
        });
        test("unsupported categories have no acquisition badge", () =>
        {
            var state = State(); var item = Clothing(state, "Maya"); item.category = AssetCategory.Tool;
            Check(AcquisitionClassifier.Classify(item, state) == "");
        });
        test("BLM local user items have no inferred purchased badge", () =>
        {
            var state = State(); var item = Clothing(state); item.boothId = "";
            Check(AcquisitionClassifier.Classify(item, state) == "");
        });
        test("legacy acquisition and absent optional fields survive catalog sync", () =>
        {
            var state = State(); var item = new LibraryItem { id = "legacy", boothId = "9001", source = "blm", category = AssetCategory.Clothing, acquisition = "無料", variationNames = null, purchasedVariationNames = null, originalCategory = null, shopThumbnailUrl = null, thumbnailPath = null };
            state.items.Add(item); AvatarCatalog.Synchronize(state);
            Check(item.acquisition == "無料" && item.variationNames.Count == 0 && item.purchasedVariationNames.Count == 0);
            Check(item.originalCategory == "" && item.shopThumbnailUrl == "" && item.thumbnailPath == "");
            item.variationMetadataKnown = true; AvatarCatalog.Synchronize(state);
            Check(item.acquisition == AcquisitionClassifier.FullPack);
        });
        test("full packs can match product-wide variation names", () =>
        {
            var state = State(); var item = Clothing(state, "Full Pack"); item.title = "衣装"; item.description = ""; item.tags.Clear();
            Check(Matcher.Match(item, state).Count == 3);
        });
        test("shared bodies are reciprocal and transitive while exclusions win", () =>
        {
            var state = State(); var item = Clothing(state, "Maya");
            state.Meta("a").sharedBodies.Add("b"); state.Meta("b").sharedBodies.Add("c");
            var matches = Matcher.Match(item, state);
            Check(matches.Count == 3 && matches.Single(x => x.avatarId == "c").indirect);
            state.Meta(item.id).excludedAvatars.Add("b");
            Check(Matcher.Match(item, state).All(x => x.avatarId != "b"));
            state.Meta(item.id).excludedAvatars.Add("a");
            Check(Matcher.Match(item, state).Count == 0);
            state.Meta(item.id).compatibleAvatars.Add("c");
            Check(Matcher.Match(item, state).Single().avatarId == "c");
        });
        test("reclassification clears avatar data and inbound relations", () =>
        {
            var state = State(); var item = Clothing(state, "Maya");
            state.Meta("b").sharedBodies.Add("a"); state.Meta(item.id).compatibleAvatars.Add("a"); state.Meta(item.id).excludedAvatars.Add("a");
            state.SetCategory(state.items[0], AssetCategory.Tool);
            Check(state.Meta("a").categoryEdited && state.Category(state.items[0]) == AssetCategory.Tool);
            Check(state.Meta("a").mainName == "" && state.Meta("a").identifiers.Count == 0);
            Check(!state.Meta("b").sharedBodies.Contains("a") && !state.Meta(item.id).compatibleAvatars.Contains("a") && !state.Meta(item.id).excludedAvatars.Contains("a"));
        });
        test("stale reclassified avatar profile is cleaned on load", () =>
        {
            var state = State(); state.Meta("a").categoryEdited = true; state.Meta("a").category = AssetCategory.Tool;
            state.Meta("b").sharedBodies.Add("a"); AvatarCatalog.Synchronize(state);
            Check(state.Meta("a").identifiers.Count == 0 && !state.Meta("b").sharedBodies.Contains("a"));
        });
        test("temporarily absent BLM avatars retain manual metadata and relations", () =>
        {
            var state = State(); state.items[0].source = "blm"; state.Meta("a").identifiersEdited = true; state.Meta("a").mainName = "手動名";
            state.Meta("b").sharedBodies.Add("a"); state.Meta("c").compatibleAvatars.Add("a");
            state.MergeBlm(new List<LibraryItem>()); AvatarCatalog.Synchronize(state);
            Check(state.Meta("a").mainName == "手動名" && state.Meta("a").identifiersEdited);
            Check(state.Meta("b").sharedBodies.Contains("a") && state.Meta("c").compatibleAvatars.Contains("a"));
            state.MergeBlm(new List<LibraryItem> { new LibraryItem { id = "a", source = "blm", title = "舞夜 Maya", category = AssetCategory.Avatar } });
            AvatarCatalog.Synchronize(state); Check(state.Meta("a").mainName == "手動名");
        });
        test("unpurchased profile migration preserves edited identifiers and body relations", () =>
        {
            var state = State(); state.items[0].unpurchased = true; state.Meta("a").mainName = "手動名"; state.Meta("a").identifiersEdited = true;
            state.Meta("b").sharedBodies.Add("a"); state.ToggleFavorite("avatars", "a");
            state.MergeBlm(new List<LibraryItem> { new LibraryItem { id = "purchased", boothId = "1001", source = "blm", category = AssetCategory.Avatar } });
            AvatarCatalog.Synchronize(state);
            Check(state.Meta("purchased").mainName == "手動名" && state.Meta("purchased").identifiersEdited);
            Check(state.Meta("b").sharedBodies.Contains("purchased") && state.IsFavorite("avatars", "purchased"));
        });
        test("avatar import target is always Assets root", () =>
        {
            var state = new LibraryState(); state.targets.Add(new ImportTarget { category = AssetCategory.Avatar, subfolder = "Wrong" });
            Check(state.Target(AssetCategory.Avatar) == "");
        });
        test("matching uses NFKC and avoids substring aliases or IDs", () =>
        {
            Check(Matcher.ContainsIdentifier("ＭＡＹＡ対応", "Maya"));
            Check(!Matcher.ContainsIdentifier("Mayamaya", "Maya"));
            Check(!Matcher.ContainsIdentifier("萌芽", "萌"));
            Check(Matcher.ContainsIdentifier("https://booth.pm/items/1234", "1234"));
            Check(!Matcher.ContainsIdentifier("https://booth.pm/items/12345", "1234"));
        });
    }
}
