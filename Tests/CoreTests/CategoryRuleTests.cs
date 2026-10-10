using KaihenShelf;

static class CategoryRuleTests
{
    static void Check(bool value) { if (!value) throw new Exception("Classification assertion failed."); }
    public static void Run(Action<string, Action> test)
    {
        test("shop conditions use exact names or globs with OR matching", () =>
        {
            var rules = new List<CategoryRule> { new() { category = AssetCategory.Hair, conditions = new() {
                new() { source = CategoryConditionSource.Shop, value = "GLAY Unknown" },
                new() { source = CategoryConditionSource.Shop, value = "DanaStudio" },
                new() { source = CategoryConditionSource.Tag, value = "hair" } } } };
            Check(CategoryRules.Classify("", null, rules, " ＧＬＡＹ Unknown ") == AssetCategory.Hair);
            Check(CategoryRules.Classify("", null, rules, "danastudio") == AssetCategory.Hair);
            Check(CategoryRules.Classify("", null, rules, "DanaStudio Extra") == AssetCategory.Other);
            Check(CategoryRules.Classify("", new[] { "hair" }, rules, "Different") == AssetCategory.Hair);
            rules[0].conditions[0].value = "GLAY*Unknown";
            Check(CategoryRules.Classify("", null, rules, "GLAYUnknown") == AssetCategory.Hair);
            Check(CategoryRules.Classify("", null, rules, "GLAY Unknown") == AssetCategory.Hair);
            Check(CategoryRules.Classify("", null, rules, null) == AssetCategory.Other);
        });
        test("shop exclusions veto subcategory tag and shop matches without claiming items alone", () =>
        {
            var rules = CategoryRules.Defaults();
            var clothing = rules.Single(r => r.category == AssetCategory.Clothing);
            clothing.conditions.Add(new() { source = CategoryConditionSource.Tag, value = "hair" });
            clothing.conditions.Add(new() { source = CategoryConditionSource.Shop, value = "GLAY*" });
            clothing.conditions.Add(new() { source = CategoryConditionSource.ShopNotContains, value = "GLAY*Unknown" });
            clothing.conditions.Add(new() { source = CategoryConditionSource.ShopNotContains, value = "DanaStudio" });
            var hair = rules.Single(r => r.category == AssetCategory.Hair);
            hair.conditions.Add(new() { source = CategoryConditionSource.Shop, value = "GLAY*Unknown" });
            hair.conditions.Add(new() { source = CategoryConditionSource.Shop, value = "DanaStudio" });
            foreach (string shop in new[] { "GLAY Unknown", "DanaStudio" })
                Check(CategoryRules.Classify("3D衣装", new[] { "hair" }, rules, shop) == AssetCategory.Hair);
            Check(CategoryRules.Classify("3D衣装", null, rules, "Different") == AssetCategory.Clothing);
            hair.conditions.Add(new() { source = CategoryConditionSource.TagNotContains, value = "ヘアアクセサリー" });
            Check(CategoryRules.Classify("3D衣装", new[] { "ヘアアクセサリー" }, rules, "DanaStudio") == AssetCategory.Other);
            rules = new() { new() { category = AssetCategory.Hair, conditions = new() {
                new() { source = CategoryConditionSource.ShopNotContains, value = "DanaStudio" },
                new() { source = CategoryConditionSource.Shop, value = " " } } } };
            Check(CategoryRules.Classify("", null, rules, "Different") == AssetCategory.Other);
            Check(CategoryRules.Classify("", null, rules, "") == AssetCategory.Other);
        });
        test("shop rules survive draft copy serialization apply and BLM reload with manual overrides", () =>
        {
            var state = new LibraryState { categoryRulesConfigured = true, categoryRulesRevision = 3, categoryRules = CategoryRules.Defaults() };
            var draft = CategoryRules.Copy(state.categoryRules);
            draft.Single(r => r.category == AssetCategory.Clothing).conditions.Add(new() { source = CategoryConditionSource.ShopNotContains, value = "DanaStudio" });
            draft.Single(r => r.category == AssetCategory.Hair).conditions.Add(new() { source = CategoryConditionSource.Shop, value = "DanaStudio" });
            var auto = new LibraryItem { id = "auto", source = "blm", originalCategory = "3D衣装", shop = "DanaStudio" };
            var manual = new LibraryItem { id = "manual", source = "manual", shop = "DanaStudio" };
            var custom = new LibraryItem { id = "custom", source = "blm", originalCategory = "3D衣装", shop = "DanaStudio" };
            state.items.AddRange(new[] { auto, manual, custom }); state.SetCategory(custom, AssetCategory.Accessory);
            state.ApplyCategoryRules(); Check(auto.category == AssetCategory.Clothing);
            Check(!CategoryRules.SameContents(draft, state.categoryRules));
            state.categoryRules = draft; state.ApplyCategoryRules();
            Check(auto.category == AssetCategory.Hair && manual.category == AssetCategory.Hair && state.Category(custom) == AssetCategory.Accessory);
            var options = new System.Text.Json.JsonSerializerOptions { IncludeFields = true };
            var loaded = System.Text.Json.JsonSerializer.Deserialize<LibraryState>(System.Text.Json.JsonSerializer.Serialize(state, options), options);
            Check(CategoryRules.SameContents(loaded.categoryRules, draft) && !loaded.MigrateCategoryRules());
            loaded.MergeBlm(new() { new LibraryItem { id = "custom", source = "blm", originalCategory = "3D衣装", shop = "DanaStudio" } });
            Check(loaded.items.Single(i => i.id == "custom").category == AssetCategory.Hair);
            Check(loaded.Category(loaded.items.Single(i => i.id == "custom")) == AssetCategory.Accessory);
            Check((int)CategoryConditionSource.Subcategory == 0 && (int)CategoryConditionSource.Tag == 1 && (int)CategoryConditionSource.TagNotContains == 2);
            Check(CategoryRules.Defaults().SelectMany(r => r.conditions).All(c => c.source != CategoryConditionSource.Shop && c.source != CategoryConditionSource.ShopNotContains));
        });
        test("tag exclusions veto positive OR matches and allow clothing to hair routing", () =>
        {
            var rules = CategoryRules.Defaults();
            var clothing = rules.Single(r => r.category == AssetCategory.Clothing);
            var hair = rules.Single(r => r.category == AssetCategory.Hair);
            clothing.conditions.Add(new() { source = CategoryConditionSource.TagNotContains, value = "髪" });
            clothing.conditions.Add(new() { source = CategoryConditionSource.TagNotContains, value = "*hair*" });
            hair.conditions.Add(new() { source = CategoryConditionSource.Tag, value = "髪" });
            hair.conditions.Add(new() { source = CategoryConditionSource.Tag, value = "*hair*" });
            Check(CategoryRules.Classify("3D衣装", new[] { "衣装", "髪", "VRChat" }, rules) == AssetCategory.Hair);
            Check(CategoryRules.Classify("3D衣装", new[] { "ＬｏｎｇＨＡＩＲ", "VRChat" }, rules) == AssetCategory.Hair);
            Check(CategoryRules.Classify("3D衣装", new[] { "衣装" }, rules) == AssetCategory.Clothing);
            Check(CategoryRules.Classify("3D衣装", null, rules) == AssetCategory.Clothing);
            Check(CategoryRules.Classify("3D髪型", null, rules) == AssetCategory.Hair);
            clothing.conditions.Reverse();
            Check(CategoryRules.Classify("3D衣装", new[] { "髪" }, rules) == AssetCategory.Hair);
            hair.conditions.Add(new() { source = CategoryConditionSource.TagNotContains, value = "ヘアアクセサリー" });
            Check(CategoryRules.Classify("3D衣装", new[] { "hair", "ヘアアクセサリー" }, rules) == AssetCategory.Other);
        });
        test("empty or exclusion-only rules do not capture unrelated items", () =>
        {
            var rules = new[] { new CategoryRule { category = AssetCategory.Hair, conditions = new() {
                new() { source = CategoryConditionSource.TagNotContains, value = "衣装" },
                new() { source = CategoryConditionSource.Tag, value = " " } } } };
            Check(CategoryRules.Classify("3D衣装", null, rules) == AssetCategory.Other);
            Check(CategoryRules.Classify("3D衣装", new[] { "髪" }, rules) == AssetCategory.Other);
            rules[0].conditions.Add(new() { source = CategoryConditionSource.Tag, value = "髪" });
            rules[0].conditions.Add(new() { source = CategoryConditionSource.TagNotContains, value = " " });
            Check(CategoryRules.Classify("", new[] { "髪" }, rules) == AssetCategory.Hair);
        });
        test("exclusion conditions survive copy serialization and respect manual overrides", () =>
        {
            var state = new LibraryState { categoryRulesConfigured = true, categoryRulesRevision = 3, categoryRules = CategoryRules.Defaults() };
            state.categoryRules.Single(r => r.category == AssetCategory.Clothing).conditions.Add(new() { source = CategoryConditionSource.TagNotContains, value = "髪" });
            state.categoryRules.Single(r => r.category == AssetCategory.Hair).conditions.Add(new() { source = CategoryConditionSource.Tag, value = "髪" });
            var item = new LibraryItem { id = "hair", source = "blm", originalCategory = "3D衣装", tags = new() { "髪" } };
            state.items.Add(item); state.SetCategory(item, AssetCategory.Accessory); state.ApplyCategoryRules();
            Check(item.category == AssetCategory.Hair && state.Category(item) == AssetCategory.Accessory);
            var options = new System.Text.Json.JsonSerializerOptions { IncludeFields = true };
            var loaded = System.Text.Json.JsonSerializer.Deserialize<LibraryState>(System.Text.Json.JsonSerializer.Serialize(state, options), options);
            Check(CategoryRules.SameContents(state.categoryRules, loaded.categoryRules));
            var copy = CategoryRules.Copy(loaded.categoryRules);
            Check(CategoryRules.SameContents(copy, loaded.categoryRules));
            copy.Single(r => r.category == AssetCategory.Clothing).conditions.Last().source = CategoryConditionSource.Tag;
            Check(!CategoryRules.SameContents(copy, loaded.categoryRules));
            Check(!loaded.MigrateCategoryRules());
            Check((int)CategoryConditionSource.Tag == 1);
        });
        test("tag glob supports prefix suffix and interior stars with literal punctuation", () =>
        {
            Check(CategoryRules.TagMatches("skin MATERIAL set", "*material*"));
            Check(CategoryRules.TagMatches("ＭＡＴＣＡＰ", "*matcap*"));
            Check(CategoryRules.TagMatches("肌マテリアル", "肌*"));
            Check(CategoryRules.TagMatches("肌マテリアル", "*マテリアル"));
            Check(CategoryRules.TagMatches("a.b[c]", "a.*[c]"));
            Check(!CategoryRules.TagMatches("axbccc", "a.*[c]"));
            Check(!CategoryRules.TagMatches("material", "material?"));
            Check(!CategoryRules.TagMatches("skin material", "material"));
            Check(!CategoryRules.TagMatches("", "*"));
            var rules = CategoryRules.Defaults();
            Check(CategoryRules.Classify("3Dテクスチャ", new[] { "Matcap set" }, rules) == AssetCategory.Material);
            Check(CategoryRules.Classify("3D衣装", new[] { "Matcap set" }, rules) == AssetCategory.Clothing);
            var state = new LibraryState { categoryRulesRevision = 2, categoryRulesConfigured = true,
                categoryRules = new() { new() { category = AssetCategory.Texture, conditions = new() { new() { value = "3Dテクスチャ" } } }, new() { category = AssetCategory.Material } } };
            Check(state.MigrateCategoryRules() && state.categoryRules[0].category == AssetCategory.Material);
            Check(!state.MigrateCategoryRules());
        });
        test("classification draft detects actual changes and clears when restored", () =>
        {
            var applied = CategoryRules.Defaults();
            var draft = CategoryRules.Defaults();
            Check(CategoryRules.SameContents(draft, applied));
            draft[0].conditions[0].value = "変更";
            Check(!CategoryRules.SameContents(draft, applied));
            draft[0].conditions[0].value = applied[0].conditions[0].value;
            Check(CategoryRules.SameContents(draft, applied));
            draft[0].conditions[0].source = CategoryConditionSource.Tag;
            Check(!CategoryRules.SameContents(draft, applied));
            draft = CategoryRules.Defaults();
            Check(CategoryRules.SameContents(draft, applied));
            draft[0].conditions.Add(new CategoryCondition());
            Check(!CategoryRules.SameContents(draft, applied));
            draft[0].conditions.RemoveAt(draft[0].conditions.Count - 1);
            Check(CategoryRules.SameContents(draft, applied));
            applied[0].conditions[0].value = "カスタム";
            Check(!CategoryRules.SameContents(CategoryRules.Defaults(), applied));
        });
        test("classification defaults use verified BOOTH subcategories only", () =>
        {
            var defaults = CategoryRules.Defaults();
            foreach (var name in Categories.BoothNames)
                Check(CategoryRules.Classify(name, null, defaults) == Categories.Classify(name));
            foreach (var name in Categories.BoothNames)
                Check(CategoryRules.Classify("3Dモデル > " + name, null, defaults) == Categories.Classify("3Dモデル > " + name));
            foreach (var name in new[] { "3D素材・マテリアル", "3Dマテリアル", "アバター", "衣装", "Avatar", "Clothing" })
                Check(CategoryRules.Classify(name, null, defaults) == AssetCategory.Other);
            Check(CategoryRules.Classify("アニメーション", null, defaults) == AssetCategory.Animation);
            Check(CategoryRules.Classify("", new[] { "肌マテリアル" }, defaults) == AssetCategory.Material);
            Check(CategoryRules.Classify("", new[] { "肌マテリアル用", "マテリアル" }, defaults) == AssetCategory.Material);
            Check(CategoryRules.Classify("3Dテクスチャ", new[] { "skin MATERIAL" }, defaults) == AssetCategory.Material);
        });
        test("legacy condition migration preserves real subcategories, custom conditions, tags and manual classifications", () =>
        {
            var state = new LibraryState { categoryRulesConfigured = true, categoryRules = CategoryRules.Defaults() };
            var rule = state.categoryRules.First(x => x.category == AssetCategory.Material);
            rule.conditions.AddRange(new[] { new CategoryCondition { value = "3Dマテリアル" }, new CategoryCondition { value = "Material" },
                new CategoryCondition { source = CategoryConditionSource.Tag, value = "Material" }, new CategoryCondition { value = "イラスト素材" } });
            var item = new LibraryItem { id = "old", source = "blm", originalCategory = "3Dマテリアル", category = AssetCategory.Material };
            state.items.Add(item); state.SetCategory(item, AssetCategory.Hair);
            Check(state.MigrateCategoryRules());
            Check(rule.conditions.Count == 6 && rule.conditions.Any(c => c.value == "Material" && c.source == CategoryConditionSource.Tag)
                && rule.conditions.Any(c => c.value == "イラスト素材"));
            Check(item.category == AssetCategory.Other && state.Category(item) == AssetCategory.Hair);
            Check(!state.MigrateCategoryRules());
            Check(CategoryRules.Classify("アニメーション", null, state.categoryRules) == AssetCategory.Animation);
        });
        test("skin material migration preserves custom rules, deduplicates and does not re-add deleted defaults", () =>
        {
            var state = new LibraryState { categoryRulesConfigured = true, categoryRulesRevision = 1,
                categoryRules = new() { new() { category = AssetCategory.Material, conditions = new() { new() { source = CategoryConditionSource.Tag, value = "カスタム" } } } } };
            var item = new LibraryItem { id = "skin", source = "blm", tags = new() { "肌マテリアル" }, category = AssetCategory.Other };
            state.items.Add(item); state.SetCategory(item, AssetCategory.Texture);
            Check(state.MigrateCategoryRules());
            Check(item.category == AssetCategory.Material && state.Category(item) == AssetCategory.Texture);
            Check(state.categoryRules.Single().conditions.Count == 5 && state.categoryRules[0].conditions[0].value == "カスタム");
            CategoryRules.AddSkinMaterialTag(state.categoryRules);
            Check(state.categoryRules[0].conditions.Count == 5);
            state.categoryRules[0].conditions.RemoveAt(1);
            Check(!state.MigrateCategoryRules() && state.categoryRules[0].conditions.Count == 4);
            var fresh = new LibraryState(); fresh.MigrateCategoryRules();
            Check(fresh.categoryRulesConfigured && CategoryRules.SameContents(fresh.categoryRules, CategoryRules.Defaults()));
        });
        test("classification OR across subcategory and multiple tags uses normalized exact match", () =>
        {
            var rules = new List<CategoryRule> { new() { category = AssetCategory.Shader, conditions = new() {
                new() { value = "エフェクト" }, new() { source = CategoryConditionSource.Tag, value = "Ｓｈａｄｅｒ" },
                new() { source = CategoryConditionSource.Tag, value = "描画" } } } };
            Check(CategoryRules.Classify("カテゴリ > エフェクト", null, rules) == AssetCategory.Shader);
            Check(CategoryRules.Classify("不明", new[] { "shader" }, rules) == AssetCategory.Shader);
            Check(CategoryRules.Classify("不明", new[] { "描画" }, rules) == AssetCategory.Shader);
            Check(CategoryRules.Classify("エフェクト集", new[] { "shader用" }, rules) == AssetCategory.Other);
        });
        test("classification conflict priority, empty conditions, and independent drafts", () =>
        {
            var rules = CategoryRules.Defaults();
            rules[0].conditions.Add(new() { source = CategoryConditionSource.Tag, value = "共通タグ" });
            rules[1].conditions.Add(new() { source = CategoryConditionSource.Tag, value = "共通タグ" });
            Check(CategoryRules.Classify("3D衣装", new[] { "共通タグ" }, rules) == AssetCategory.Avatar);
            var draft = CategoryRules.Copy(rules);
            draft[0].conditions.Clear();
            Check(rules[0].conditions.Count > 0);
            Check(CategoryRules.Classify("", new[] { "" }, new[] { new CategoryRule { category = AssetCategory.Avatar, conditions = new() { new() { value = " " } } } }) == AssetCategory.Other);
        });
        test("applying classification updates catalog but preserves per-item manual category", () =>
        {
            var state = new LibraryState { categoryRulesConfigured = true, categoryRules = CategoryRules.Defaults() };
            var auto = new LibraryItem { id = "auto", source = "blm", originalCategory = "不明", tags = new() { "効果" } };
            var manual = new LibraryItem { id = "manual", source = "blm", originalCategory = "不明", tags = new() { "効果" } };
            state.items.AddRange(new[] { auto, manual }); state.SetCategory(manual, AssetCategory.Hair);
            state.categoryRules.First(x => x.category == AssetCategory.Gimmick).conditions.Add(new() { source = CategoryConditionSource.Tag, value = "効果" });
            state.ApplyCategoryRules();
            var catalog = new CatalogViewIndex(state);
            Check(catalog.Category(auto) == AssetCategory.Gimmick && catalog.Category(manual) == AssetCategory.Hair);
            state.MergeBlm(new() { new LibraryItem { id = "auto", source = "blm", originalCategory = "不明", tags = new() { "効果" } } });
            Check(state.items.Single().category == AssetCategory.Gimmick);
            state.categoryRules.Clear(); state.ApplyCategoryRules();
            Check(state.items.Single().category == AssetCategory.Other);
        });
    }
}
