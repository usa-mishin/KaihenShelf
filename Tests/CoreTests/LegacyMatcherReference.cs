// Frozen pre-index matcher used only as an independent regression/performance oracle.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using KaihenShelf;
namespace KaihenShelf.TestSupport
{
    static class LegacyMatcherReference
    {
        public static string Normalize(string value) { return (value ?? "").Normalize(NormalizationForm.FormKC).ToLowerInvariant().Trim(); }
        public static bool ValidIdentifier(string value) { return AvatarCatalog.IsUsefulIdentifier(value); }
        public static bool ContainsIdentifier(string text, string identifier)
        {
            var word = Normalize(identifier);
            if (word.Length == 0 || Uri.IsWellFormedUriString(word, UriKind.Absolute)) return false;
            text = Normalize(text);
            if (word.All(char.IsDigit)) return Regex.IsMatch(text, @"(?<!\d)" + Regex.Escape(word) + @"(?!\d)", RegexOptions.CultureInvariant);
            return Regex.IsMatch(text, @"(?<![\p{L}\p{N}])" + Regex.Escape(word) + @"(?=(?:\s*(?:対応|専用|用))?(?![\p{L}\p{N}]))", RegexOptions.CultureInvariant);
        }
        public static List<AvatarMatch> Match(LibraryItem item, LibraryState state, IEnumerable<string> filenames = null)
        {
            bool purchasedOnly = AcquisitionClassifier.Classify(item, state) == AcquisitionClassifier.AvatarSpecific;
            // A purchased single-avatar variation is the only evidence of entitlement.
            // Product-wide names, unpurchased variations and files can name other avatars.
            var text = purchasedOnly
                ? string.Join("\n", item.purchasedVariationNames ?? new List<string>())
                : item.title + "\n" + item.description + "\n" + string.Join("\n", item.tags ?? new List<string>()) + "\n" + string.Join("\n", item.variationNames ?? new List<string>()) + "\n" + string.Join("\n", filenames ?? Array.Empty<string>());
            var result = new List<AvatarMatch>();
            var manual = state.Meta(item.id);
            var avatars = state.items.Where(x => state.Category(x) == AssetCategory.Avatar && x.id != item.id).ToList();
            foreach (var avatar in avatars)
            {
                if (manual.excludedAvatars.Contains(avatar.id)) continue;
                var meta = state.Meta(avatar.id);
                // Manually entered names are retained verbatim; automatic-only exclusions
                // belong in AvatarCatalog, not here.
                var names = (meta.identifiers ?? new List<string>()).Concat(new[] { meta.mainName }).Where(x => !string.IsNullOrWhiteSpace(x)).OrderByDescending(x => x.Length);
                string match = names.FirstOrDefault(x => ContainsIdentifier(text, x));
                if (manual.compatibleAvatars.Contains(avatar.id) || match != null)
                    result.Add(new AvatarMatch { avatarId = avatar.id, reason = manual.compatibleAvatars.Contains(avatar.id) ? "手動指定" : (purchasedOnly ? "購入済み商品種類: " : "識別タグ: ") + match });
            }
            var direct = new HashSet<string>(result.Select(x => x.avatarId));
            var related = avatars.ToDictionary(x => x.id, x => new HashSet<string>());
            foreach (var avatar in avatars)
                foreach (var body in state.Meta(avatar.id).sharedBodies)
                    if (related.ContainsKey(body) && body != avatar.id) { related[avatar.id].Add(body); related[body].Add(avatar.id); }
            var visited = new HashSet<string>(direct);
            var queue = new Queue<string>(direct);
            while (queue.Count > 0)
            {
                string origin = queue.Dequeue();
                foreach (var sibling in related[origin])
                {
                    if (!visited.Add(sibling)) continue;
                    queue.Enqueue(sibling);
                    if (manual.excludedAvatars.Contains(sibling)) continue;
                    var originMeta = state.Meta(origin);
                    string name = string.IsNullOrWhiteSpace(originMeta.mainName) ? avatars.Find(x => x.id == origin).title : originMeta.mainName;
                    result.Add(new AvatarMatch { avatarId = sibling, indirect = true, reason = "共通素体: " + name });
                }
            }
            return result;
        }
    }
}
