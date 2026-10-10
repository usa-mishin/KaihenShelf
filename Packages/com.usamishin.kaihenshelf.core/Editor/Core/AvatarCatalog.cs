using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace KaihenShelf
{
    // JsonUtility can parse the shipped top-level array by wrapping it as {"entries": ...}.
    [Serializable] public sealed class AvatarCatalogDocument { public DefaultAvatarEntry[] entries; }
    [Serializable] public sealed class DefaultAvatarEntry
    {
        public long boothItemId;
        public string displayName = "";
        public string primaryIdentifier = "";
        public string[] identifiers = Array.Empty<string>();
    }

    public static class AvatarCatalog
    {
        static Dictionary<string, DefaultAvatarEntry> defaults = new Dictionary<string, DefaultAvatarEntry>(StringComparer.Ordinal);
        static readonly HashSet<string> IgnoredWords = new HashSet<string>(new[]
        {
            "オリジナル", "モデル", "アバター", "キャラクター", "キャラクター素体", "対応", "専用", "商品", "販売", "ダウンロード",
            "女性型", "男性型", "想定", "搭載", "もちふぃった", "avatar", "avatars", "original", "model", "character",
            "vrchat", "vrc", "pc", "quest", "mobile", "android", "ios", "cluster", "版", "re", "unity", "unitypackage", "sdk",
            "physbone", "physbones", "humanoid", "blender", "fbx", "vrm", "ver", "version", "無料", "無料あり", "有料", "セール", "セール中", "free", "sale", "off", "期間限定"
        }, StringComparer.OrdinalIgnoreCase);
        static readonly Regex Words = new Regex(@"[\p{L}\p{N}_-]+", RegexOptions.CultureInvariant);
        static readonly Regex Version = new Regex(@"^(?:v(?:er(?:sion)?)?\.?\d+(?:[._-]\d+)*[a-z]?|\d+(?:[._-]\d+)+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex PriceOrDate = new Regex(@"^(?:\d+(?:\.\d+)?(?:円|%|％|off)|\d{4}年?\d{1,2}月?\d{0,2}日?)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex Promotion = new Regex(@"(?:発売記念|販売記念|期間限定|数量限定|周年記念|記念セール|セール価格|割引|キャンペーン|今だけ|月末まで|sale|anniversary|limited|discount)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex GenericOnly = new Regex(@"^(?:(?:[_-]?(?:vrchat|vrc|pc|quest|mobile|android|ios|cluster|avatar|model|unity|sdk|physbones?|humanoid|blender|fbx|vrm))|(?:[_-]?(?:対応|専用|向け|用|版)))+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        // Names such as Menno3D are valid; only model boilerplate is excluded.
        static readonly Regex GenericModelPhrase = new Regex(@"(?<![\p{L}\p{N}])(?:オリジナル|original)?3d(?:モデル|model|アバター|avatar)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex TrailingVersion = new Regex(@"(?:[_-]?v(?:er(?:sion)?)?\.?\d+(?:[._-]\d+)*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex DecoratedReading = new Regex(@"[（(](?<reading>[^）)]*[↑↓←→][^）)]*)[）)]", RegexOptions.CultureInvariant);
        static readonly Regex Decorations = new Regex(@"[^\p{L}\p{N}_-]+", RegexOptions.CultureInvariant);

        public static int DefaultCount { get { return defaults.Count; } }
        internal static IEnumerable<string> DefaultIdentifiers
        {
            get { return defaults.SelectMany(pair => new[] { pair.Key, pair.Value.primaryIdentifier }
                .Concat(pair.Value.identifiers ?? Array.Empty<string>())).Where(IsUsefulIdentifier); }
        }

        public static void SetDefaults(IEnumerable<DefaultAvatarEntry> entries)
        {
            var loaded = new Dictionary<string, DefaultAvatarEntry>(StringComparer.Ordinal);
            foreach (var entry in entries ?? Array.Empty<DefaultAvatarEntry>())
                if (entry != null && entry.boothItemId > 0 && IsUsefulIdentifier(entry.primaryIdentifier))
                    loaded[entry.boothItemId.ToString(CultureInfo.InvariantCulture)] = entry;
            defaults = loaded;
        }

        public static bool IsUsefulIdentifier(string value)
        {
            value = Matcher.Normalize(value);
            if (value.Length == 0 || Uri.IsWellFormedUriString(value, UriKind.Absolute)) return false;
            if (value.All(char.IsDigit)) return true; // BOOTH item IDs are explicit matching evidence.
            if (!value.Any(char.IsLetter) || (value.Length < 2 && !ContainsJapanese(value))) return false;
            return !IgnoredWords.Contains(value) && !GenericModelPhrase.IsMatch(value) && !Version.IsMatch(value)
                && !PriceOrDate.IsMatch(value) && !Promotion.IsMatch(value) && !GenericOnly.IsMatch(value);
        }

        static bool ContainsJapanese(string value)
        { return value.Any(c => (c >= '\u3040' && c <= '\u30ff') || (c >= '\u3400' && c <= '\u9fff')); }

        static string Clean(string value)
        { return TrailingVersion.Replace(value.Normalize(NormalizationForm.FormKC).Trim().Trim('_', '-'), "").Trim().Trim('_', '-'); }

        static IEnumerable<string> NameCandidates(string title)
        {
            string normalized = (title ?? "").Normalize(NormalizationForm.FormKC);
            foreach (Match match in DecoratedReading.Matches(normalized))
                yield return Clean(Decorations.Replace(match.Groups["reading"].Value, ""));
            foreach (Match match in Words.Matches(DecoratedReading.Replace(normalized, " ")))
                yield return Clean(match.Value);
        }

        public static void Populate(LibraryItem item, ItemMetadata metadata)
        {
            if (metadata.identifiersEdited) return;
            List<string> names;
            DefaultAvatarEntry known;
            if (defaults.TryGetValue(item.boothId ?? "", out known))
            {
                metadata.mainName = known.primaryIdentifier;
                names = new[] { known.primaryIdentifier }.Concat(known.identifiers ?? Array.Empty<string>()).Where(IsUsefulIdentifier).ToList();
            }
            else
            {
                // Keep generated names for uncatalogued avatars across title changes and syncs.
                if (metadata.identifiers != null && metadata.identifiers.Any(x => !string.IsNullOrWhiteSpace(x))) return;
                names = NameCandidates(item.title).Where(x => !x.All(char.IsDigit) && IsUsefulIdentifier(x)).ToList();
                metadata.mainName = names.FirstOrDefault(ContainsJapanese) ?? names.FirstOrDefault() ?? "";
            }
            if (!string.IsNullOrWhiteSpace(item.boothId) && item.boothId.All(char.IsDigit)) names.Add(item.boothId);
            metadata.identifiers = names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static void Synchronize(LibraryState state)
        {
            var metadata = new Dictionary<string, ItemMetadata>(StringComparer.Ordinal);
            foreach (var entry in state.metadata)
                if (!metadata.ContainsKey(entry.id)) metadata.Add(entry.id, entry);
            var nonAvatarIds = new HashSet<string>(StringComparer.Ordinal);
            // 0.1.0 had no automatic defaults and did not mark main-name edits manual.
            // Preserve all its nonempty avatar names exactly before applying new rules.
            if (state.catalogRulesVersion == 0)
            {
                foreach (var entry in state.metadata)
                    if (!string.IsNullOrWhiteSpace(entry.mainName) || (entry.identifiers != null && entry.identifiers.Count > 0)) entry.identifiersEdited = true;
                state.catalogRulesVersion = 1;
            }
            foreach (var item in state.items)
            {
                if (item.originalCategory == null) item.originalCategory = "";
                if (item.shopThumbnailUrl == null) item.shopThumbnailUrl = "";
                if (item.thumbnailPath == null) item.thumbnailPath = "";
                if (item.tags == null) item.tags = new List<string>();
                if (item.variationNames == null) item.variationNames = new List<string>();
                if (item.purchasedVariationNames == null) item.purchasedVariationNames = new List<string>();
                ItemMetadata entry;
                metadata.TryGetValue(item.id, out entry);
                var category = entry != null && entry.categoryEdited ? entry.category : item.category;
                if (category == AssetCategory.Avatar)
                {
                    if (entry == null)
                    {
                        entry = new ItemMetadata { id = item.id };
                        state.metadata.Add(entry); metadata.Add(item.id, entry);
                    }
                    Populate(item, entry);
                }
                else nonAvatarIds.Add(item.id);
            }
            // A BLM refresh can temporarily omit products. Retain their manual metadata
            // and relations; only confirmed reclassification invalidates an avatar.
            foreach (var entry in state.metadata)
            {
                if (entry.identifiers == null) entry.identifiers = new List<string>();
                if (entry.sharedBodies == null) entry.sharedBodies = new List<string>();
                if (entry.compatibleAvatars == null) entry.compatibleAvatars = new List<string>();
                if (entry.excludedAvatars == null) entry.excludedAvatars = new List<string>();
                if (nonAvatarIds.Contains(entry.id))
                {
                    entry.mainName = "";
                    entry.identifiers.Clear();
                    entry.identifiersEdited = false;
                    entry.sharedBodies.Clear();
                }
                entry.sharedBodies.RemoveAll(nonAvatarIds.Contains);
                entry.compatibleAvatars.RemoveAll(nonAvatarIds.Contains);
                entry.excludedAvatars.RemoveAll(nonAvatarIds.Contains);
            }
            var matcher = new MatcherIndex(state);
            foreach (var item in state.items) item.acquisition = matcher.Acquisition(item);
        }
    }

    public static class AcquisitionClassifier
    {
        public const string FullPack = "フルパック";
        public const string AvatarSpecific = "単体購入";
        public const string FreeGift = "無料/ギフト";
        // Match shared wording, not shops or product IDs. NFKC is applied before
        // matching; punctuation/emoji may separate words without changing meaning.
        const string PackSeparator = @"[\s\p{P}\p{S}\p{M}\p{Cs}]*";
        const string PackNoun = @"(?:packs?|packages?|pakages?|sets?|bundles?|versions?|editions?)";
        static readonly Regex FullPackName = new Regex(@"(?<![a-z0-9])(?:full" + PackSeparator
            + @"(?:avatars?" + PackSeparator + @")?" + PackNoun
            + @"|complete" + PackSeparator + PackNoun
            + @"|all(?:" + PackSeparator + @"(?:avatars?|in" + PackSeparator + @"one))?)(?![a-z0-9])"
            + @"|フル" + PackSeparator + @"(?:パック|セット|パッケージ|バージョン|版)"
            + @"|ふる" + PackSeparator + @"(?:ぱっく|せっと|ぱっけーじ|ばーじょん|版)"
            + @"|コンプリート" + PackSeparator + @"(?:版|セット|パック|パッケージ)"
            + @"|完全" + PackSeparator + @"(?:版|セット|パック)"
            + @"|全" + PackSeparator + @"(?:アバター|種類?|部" + PackSeparator + @"(?:入り|セット)|\d+" + PackSeparator + @"種類?)"
            + @"|(?<![a-z0-9])full" + PackSeparator + @"(?:max|maximum)" + PackSeparator + @"\d+(?![a-z0-9])"
            + @"|^" + PackSeparator + @"full(?:" + PackSeparator + @"\d+" + PackSeparator
            + @"(?:種類?|点|items?|types?|avatars?))?" + PackSeparator + @"$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static bool Supports(AssetCategory category)
        {
            return category == AssetCategory.Clothing || category == AssetCategory.Hair || category == AssetCategory.Accessory
                || category == AssetCategory.Texture || category == AssetCategory.Material || category == AssetCategory.Gimmick || category == AssetCategory.Animation;
        }

        public static string Classify(LibraryItem item, LibraryState state)
        {
            return Classify(item, state.Category(item));
        }

        internal static string Classify(LibraryItem item, AssetCategory category)
        {
            // Flat mappings and older state files have no authoritative variation data.
            // Keep their explicit acquisition value instead of guessing from absent fields.
            if (!item.variationMetadataKnown && !item.hasVariationRows && !item.hasPurchasedVariationOrder
                && (item.purchasedVariationNames == null || item.purchasedVariationNames.Count == 0)) return item.acquisition ?? "不明";
            if (!Supports(category) || string.IsNullOrWhiteSpace(item.boothId)) return "";
            if (item.hasVariationRows && !item.hasPurchasedVariationOrder) return FreeGift;
            var purchased = (item.purchasedVariationNames ?? new List<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(Matcher.Normalize).ToList();
            if (!item.hasVariationRows || purchased.Count == 0 || purchased.Any(x => FullPackName.IsMatch(x))) return FullPack;
            return AvatarSpecific;
        }
    }
}
