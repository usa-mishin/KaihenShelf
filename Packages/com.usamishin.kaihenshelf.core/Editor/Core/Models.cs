using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KaihenShelf
{
    public enum AssetCategory { Avatar, Clothing, Hair, Accessory, Texture, Material, Gimmick, Animation, Tool, Shader, World, Other }

    public static class Categories
    {
        public static bool HasAvatarCompatibility(AssetCategory category)
        { return category >= AssetCategory.Clothing && category <= AssetCategory.Animation; }
        public static readonly string[] Labels = { "アバター", "衣装", "髪型", "アクセサリー", "テクスチャ", "マテリアル", "ギミック", "アニメーション", "ツール", "シェーダー", "ワールド", "その他" };
        // Keep serialized enum values stable; ordering is a presentation/classification concern.
        public static readonly AssetCategory[] DisplayOrder = { AssetCategory.Avatar, AssetCategory.Clothing, AssetCategory.Hair,
            AssetCategory.Accessory, AssetCategory.Material, AssetCategory.Texture, AssetCategory.Gimmick, AssetCategory.Animation,
            AssetCategory.Tool, AssetCategory.Shader, AssetCategory.World, AssetCategory.Other };
        // Confirmed against https://booth.pm/ja/browse/3Dモデル on 2026-10-08 (including its category links).
        internal static readonly string[] BoothNames = { "3Dキャラクター", "3D衣装", "3D髪型", "3D装飾品", "3D靴", "3D小道具", "3Dテクスチャ", "3Dツール・システム", "3Dモーション・アニメーション", "3D環境・ワールド", "アニメーション" };
        internal static readonly AssetCategory[] BoothCategories = { AssetCategory.Avatar, AssetCategory.Clothing, AssetCategory.Hair, AssetCategory.Accessory, AssetCategory.Clothing, AssetCategory.Gimmick, AssetCategory.Texture, AssetCategory.Tool, AssetCategory.Animation, AssetCategory.World, AssetCategory.Animation };
        public static AssetCategory Classify(string value)
        {
            var text = Matcher.Normalize(value);
            // Read BOOTH's confirmed subcategory before considering app category labels.
            for (int i = 0; i < BoothNames.Length; i++)
                if (text.IndexOf(Matcher.Normalize(BoothNames[i]), StringComparison.Ordinal) >= 0) return BoothCategories[i];
            for (int i = 0; i < Labels.Length; i++)
                if (text == Matcher.Normalize(Labels[i]) || text == Matcher.Normalize(((AssetCategory)i).ToString())) return (AssetCategory)i;
            return AssetCategory.Other;
        }
    }

    [Serializable] public sealed class LibraryItem
    {
        public string id = "";
        public string boothId = "";
        public string title = "";
        public string shop = "";
        public string url = "";
        public string thumbnailUrl = "";
        public string shopThumbnailUrl = "";
        public string thumbnailPath = "";
        public string folder = "";
        public string description = "";
        public List<string> tags = new List<string>();
        public string originalCategory = "";
        public List<string> variationNames = new List<string>();
        public List<string> purchasedVariationNames = new List<string>();
        public bool hasVariationRows;
        public bool hasPurchasedVariationOrder;
        public bool variationMetadataKnown;
        public string acquisition = "不明";
        public bool updateAvailable;
        public bool ageRestricted;
        public bool unpurchased;
        public string source = "manual";
        public string registeredUtc = "";
        public string updatedUtc = "";
        public string publishedUtc = "";
        public AssetCategory category = AssetCategory.Other;
    }

    [Serializable] public sealed class ItemMetadata
    {
        public string id = "";
        public bool categoryEdited;
        public AssetCategory category;
        public bool identifiersEdited;
        public string mainName = "";
        public List<string> identifiers = new List<string>();
        public List<string> sharedBodies = new List<string>();
        public List<string> compatibleAvatars = new List<string>();
        public List<string> excludedAvatars = new List<string>();
    }

    [Serializable] public sealed class Favorite { public string scope; public string key; }
    [Serializable] public sealed class ImportTarget { public AssetCategory category; public string subfolder = ""; }
    [Serializable] public sealed class LibraryState
    {
        public int formatVersion = 1;
        public string consentVersion = "";
        public string consentUtc = "";
        public bool firstLoadCompleted;
        public bool hideR18 = true;
        public string blmPath = "";
        public int catalogRulesVersion;
        public List<LibraryItem> items = new List<LibraryItem>();
        public List<ItemMetadata> metadata = new List<ItemMetadata>();
        public List<Favorite> favorites = new List<Favorite>();
        public List<ImportTarget> targets = new List<ImportTarget>();
        public bool categoryRulesConfigured;
        public int categoryRulesRevision;
        public List<CategoryRule> categoryRules = new List<CategoryRule>();
        public bool MigrateCategoryRules()
        {
            if (categoryRulesRevision >= 3) return false;
            if (categoryRulesConfigured)
            {
                if (categoryRulesRevision < 1) CategoryRules.RemoveInvalidLegacyConditions(categoryRules);
                if (categoryRulesRevision < 2) CategoryRules.AddSkinMaterialTag(categoryRules);
                CategoryRules.AddMaterialPatterns(categoryRules);
                CategoryRules.PrioritizeMaterial(categoryRules);
            }
            else { categoryRules = CategoryRules.Defaults(); categoryRulesConfigured = true; }
            ApplyCategoryRules();
            categoryRulesRevision = 3;
            return true;
        }
        public void ApplyCategoryRules()
        {
            if (!categoryRulesConfigured) return;
            foreach (var item in items.Where(x => !x.unpurchased && (x.source == "blm" || !string.IsNullOrEmpty(x.originalCategory) || x.tags != null && x.tags.Count > 0)))
                item.category = CategoryRules.Classify(item.originalCategory, item.tags, categoryRules);
        }
        public ItemMetadata Meta(string id)
        {
            var entry = metadata.Find(x => x.id == id);
            if (entry != null) return entry;
            entry = new ItemMetadata { id = id }; metadata.Add(entry); return entry;
        }
        public AssetCategory Category(LibraryItem item)
        {
            var entry = metadata.Find(x => x.id == item.id);
            return entry != null && entry.categoryEdited ? entry.category : item.category;
        }
        public string Scope(LibraryItem item) { return Category(item) == AssetCategory.Avatar ? "avatars" : "items"; }
        public bool IsFavorite(string scope, string key) { return favorites.Any(x => x.scope == scope && x.key == key); }
        public void ToggleFavorite(string scope, string key)
        {
            if (IsFavorite(scope, key)) favorites.RemoveAll(x => x.scope == scope && x.key == key);
            else favorites.Add(new Favorite { scope = scope, key = key });
        }
        public string Target(AssetCategory category)
        {
            if (category == AssetCategory.Avatar) return "";
            var saved = targets.Find(x => x.category == category);
            if (saved != null) return saved.subfolder ?? "";
            switch (category)
            {
                case AssetCategory.Clothing: case AssetCategory.Hair: case AssetCategory.Accessory:
                case AssetCategory.Texture: case AssetCategory.Material: case AssetCategory.Gimmick:
                case AssetCategory.Animation: case AssetCategory.Shader: return Categories.Labels[(int)category];
                default: return "";
            }
        }
        public void SetCategory(LibraryItem item, AssetCategory category)
        {
            bool wasAvatar = Category(item) == AssetCategory.Avatar;
            var entry = Meta(item.id);
            entry.categoryEdited = true;
            entry.category = category;
            if (wasAvatar && category != AssetCategory.Avatar)
            {
                entry.mainName = "";
                entry.identifiers.Clear();
                entry.identifiersEdited = false;
                entry.sharedBodies.Clear();
                foreach (var other in metadata)
                {
                    other.sharedBodies.RemoveAll(x => x == item.id);
                    other.compatibleAvatars.RemoveAll(x => x == item.id);
                    other.excludedAvatars.RemoveAll(x => x == item.id);
                }
            }
        }
        public void MergeBlm(List<LibraryItem> incoming)
        {
            if (categoryRulesConfigured)
                foreach (var item in incoming) item.category = CategoryRules.Classify(item.originalCategory, item.tags, categoryRules);
            foreach (var item in incoming)
            {
                var pending = items.Find(x => x.unpurchased && item.boothId.Length > 0 && x.boothId == item.boothId);
                if (pending == null) continue;
                // A purchased item remains an avatar even when the BLM category column is unmapped.
                if (item.category == AssetCategory.Other && Category(pending) == AssetCategory.Avatar) item.category = AssetCategory.Avatar;
                var old = metadata.Find(x => x.id == pending.id);
                if (old != null)
                {
                    metadata.RemoveAll(x => x.id == item.id);
                    old.id = item.id;
                }
                foreach (var entry in metadata)
                {
                    Replace(entry.sharedBodies, pending.id, item.id);
                    Replace(entry.compatibleAvatars, pending.id, item.id);
                    Replace(entry.excludedAvatars, pending.id, item.id);
                }
                foreach (var fav in favorites.Where(x => x.key == pending.id && (x.scope == "avatars" || x.scope == "items"))) fav.key = item.id;
                items.Remove(pending);
            }
            items.RemoveAll(x => x.source == "blm");
            items.AddRange(incoming);
        }
        static void Replace(List<string> values, string from, string to)
        { for (int i = 0; i < values.Count; i++) if (values[i] == from) values[i] = to; }
    }

    public sealed class AvatarMatch
    {
        public string avatarId;
        public string reason;
        public bool indirect;
    }

    public static class Matcher
    {
        public static string Normalize(string value) { return (value ?? "").Normalize(NormalizationForm.FormKC).ToLowerInvariant().Trim(); }
        public static bool ValidIdentifier(string value) { return AvatarCatalog.IsUsefulIdentifier(value); }
        public static bool ContainsIdentifier(string text, string identifier)
        {
            var word = Normalize(identifier);
            if (word.Length == 0 || Uri.IsWellFormedUriString(word, UriKind.Absolute)) return false;
            return ContainsNormalized(Normalize(text), word);
        }
        internal static bool ContainsNormalized(string text, string word)
        {
            if (word.Length == 0) return false;
            bool digits = word.All(char.IsDigit);
            int start = 0;
            while ((start = text.IndexOf(word, start, StringComparison.Ordinal)) >= 0)
            {
                if (IdentifierAt(text, word, start, digits)) return true;
                start++;
            }
            return false;
        }
        internal static bool IdentifierAt(string text, string word, int start, bool digits)
        {
            int end = start + word.Length;
            if (digits) return (start == 0 || !char.IsDigit(text[start - 1])) && (end == text.Length || !char.IsDigit(text[end]));
            if (start > 0 && WordCharacter(text[start - 1])) return false;
            if (end == text.Length || !WordCharacter(text[end])) return true;
            // Same Unicode boundaries and Japanese suffixes as the original regex.
            foreach (string suffix in JapaneseSuffixes)
                if (end + suffix.Length <= text.Length && string.CompareOrdinal(text, end, suffix, 0, suffix.Length) == 0
                    && (end + suffix.Length == text.Length || !WordCharacter(text[end + suffix.Length]))) return true;
            return false;
        }
        static readonly string[] JapaneseSuffixes = { "対応", "専用", "用" };
        static bool WordCharacter(char value) { return char.IsLetter(value) || char.IsNumber(value); }
        public static List<AvatarMatch> Match(LibraryItem item, LibraryState state, IEnumerable<string> filenames = null)
        {
            // One-off compatibility API; catalog callers reuse a MatcherIndex.
            return new MatcherIndex(state).Match(item, filenames);
        }
    }
}
