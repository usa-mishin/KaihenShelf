using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace KaihenShelf
{
    /// <summary>A reusable snapshot of avatar rules. Rebuild after catalog or metadata edits.</summary>
    public sealed class MatcherIndex
    {
        sealed class ItemRules
        {
            public LibraryItem item;
            public AssetCategory category;
            public HashSet<string> included;
            public HashSet<string> excluded;
            public string acquisition;
            public string normalizedText;
            public bool purchasedOnly;
        }
        sealed class AvatarRules
        {
            public LibraryItem item;
            public string displayName;
            public List<int> neighbors = new List<int>();
        }
        sealed class Alias
        {
            public string text, normalized;
            public int avatar, priority;
            public bool digits;
        }
        sealed class Node
        {
            public readonly Dictionary<char, int> next = new Dictionary<char, int>();
            public readonly List<int> outputs = new List<int>();
            public int failure;
        }
        sealed class CachedMatch { public string filenames; public List<AvatarMatch> result; }
        readonly Dictionary<string, ItemMetadata> metadata = new Dictionary<string, ItemMetadata>(StringComparer.Ordinal);
        readonly Dictionary<string, ItemRules> items = new Dictionary<string, ItemRules>(StringComparer.Ordinal);
        readonly Dictionary<string, int> avatarIds = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly List<AvatarRules> avatars = new List<AvatarRules>();
        readonly List<Alias> aliases = new List<Alias>();
        readonly List<Node> trie = new List<Node> { new Node() };
        readonly Dictionary<string, CachedMatch> matches = new Dictionary<string, CachedMatch>(StringComparer.Ordinal);
        readonly Dictionary<string, IReadOnlyList<string>> packageNames = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        readonly Dictionary<string, string> scannedFiles = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly string[] defaultAvatarIdentifiers;
        static readonly Regex PurchasedHonorific = new Regex(@"ちゃん(?=(?:対応|専用|用)?(?:[^\p{L}\p{N}]|$))", RegexOptions.CultureInvariant);
        public LibraryItem[] Avatars { get; private set; }

        public MatcherIndex(LibraryState state)
        {
            defaultAvatarIdentifiers = AvatarCatalog.DefaultIdentifiers.Select(Matcher.Normalize).Distinct().ToArray();
            foreach (var meta in state.metadata)
                if (meta != null && meta.id != null && !metadata.ContainsKey(meta.id)) metadata.Add(meta.id, meta);
            foreach (var item in state.items)
            {
                if (item == null || item.id == null || items.ContainsKey(item.id)) continue;
                var rules = Rules(item);
                if (rules.category != AssetCategory.Avatar) continue;
                var meta = Meta(item.id);
                int index = avatars.Count;
                avatarIds.Add(item.id, index);
                avatars.Add(new AvatarRules { item = item, displayName = string.IsNullOrWhiteSpace(meta.mainName) ? item.title : meta.mainName });
                int priority = 0;
                foreach (string value in (meta.identifiers ?? new List<string>()).Concat(new[] { meta.mainName }).Where(x => !string.IsNullOrWhiteSpace(x)).OrderByDescending(x => x.Length))
                {
                    string normalized = Matcher.Normalize(value);
                    if (normalized.Length == 0 || Uri.IsWellFormedUriString(normalized, UriKind.Absolute)) continue;
                    var alias = new Alias { avatar = index, priority = priority++, text = value, normalized = normalized,
                        digits = normalized.All(char.IsDigit) };
                    AddAlias(alias);
                }
            }
            Avatars = avatars.Select(x => x.item).ToArray();
            for (int i = 0; i < avatars.Count; i++)
                foreach (string body in Meta(avatars[i].item.id).sharedBodies ?? new List<string>())
                {
                    int target;
                    if (!avatarIds.TryGetValue(body, out target) || target == i) continue;
                    if (!avatars[i].neighbors.Contains(target)) avatars[i].neighbors.Add(target);
                    if (!avatars[target].neighbors.Contains(i)) avatars[target].neighbors.Add(i);
                }
            BuildFailureLinks();
        }

        ItemMetadata Meta(string id)
        {
            ItemMetadata value;
            if (metadata.TryGetValue(id, out value)) return value;
            value = new ItemMetadata { id = id };
            metadata.Add(id, value);
            return value;
        }
        ItemRules Rules(LibraryItem item)
        {
            ItemRules rules;
            if (items.TryGetValue(item.id, out rules)) return rules;
            var meta = Meta(item.id);
            rules = new ItemRules { item = item, category = meta.categoryEdited ? meta.category : item.category,
                included = new HashSet<string>(meta.compatibleAvatars ?? new List<string>(), StringComparer.Ordinal),
                excluded = new HashSet<string>(meta.excludedAvatars ?? new List<string>(), StringComparer.Ordinal) };
            items.Add(item.id, rules);
            return rules;
        }
        public AssetCategory Category(LibraryItem item) { return Rules(item).category; }
        public LibraryItem Find(string id) { ItemRules value; return id != null && items.TryGetValue(id, out value) ? value.item : null; }

        void AddAlias(Alias alias)
        {
            int node = 0;
            foreach (char character in alias.normalized)
            {
                int next;
                if (!trie[node].next.TryGetValue(character, out next))
                {
                    next = trie.Count;
                    trie[node].next.Add(character, next);
                    trie.Add(new Node());
                }
                node = next;
            }
            trie[node].outputs.Add(aliases.Count);
            aliases.Add(alias);
        }
        void BuildFailureLinks()
        {
            var queue = new Queue<int>();
            foreach (int child in trie[0].next.Values) queue.Enqueue(child);
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                foreach (var edge in trie[current].next)
                {
                    int fallback = trie[current].failure;
                    int target;
                    while (fallback != 0 && !trie[fallback].next.ContainsKey(edge.Key)) fallback = trie[fallback].failure;
                    if (trie[fallback].next.TryGetValue(edge.Key, out target)) trie[edge.Value].failure = target;
                    trie[edge.Value].outputs.AddRange(trie[trie[edge.Value].failure].outputs);
                    queue.Enqueue(edge.Value);
                }
            }
        }
        void Scan(string text, int[] best)
        {
            if (aliases.Count == 0) return;
            int node = 0;
            for (int position = 0; position < text.Length; position++)
            {
                int next;
                while (node != 0 && !trie[node].next.ContainsKey(text[position])) node = trie[node].failure;
                node = trie[node].next.TryGetValue(text[position], out next) ? next : 0;
                foreach (int index in trie[node].outputs)
                {
                    var alias = aliases[index];
                    if (!Matcher.IdentifierAt(text, alias.normalized, position + 1 - alias.normalized.Length, alias.digits)) continue;
                    int old = best[alias.avatar];
                    if (old < 0 || alias.priority < aliases[old].priority) best[alias.avatar] = index;
                }
            }
        }

        public string Acquisition(LibraryItem item)
        {
            var rules = Rules(item);
            if (rules.acquisition == null) rules.acquisition = AcquisitionClassifier.Classify(rules.item, rules.category);
            return rules.acquisition;
        }
        /// <summary>Filename-only badges: no numeric IDs, product text, or shared-body inference.</summary>
        public IReadOnlyList<string> PackageAvatarNames(string filename)
        {
            filename = Path.GetFileName(filename ?? "");
            if (!filename.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase)) return Array.Empty<string>();
            IReadOnlyList<string> cached;
            if (packageNames.TryGetValue(filename, out cached)) return cached;
            string stem = Matcher.Normalize(Path.GetFileNameWithoutExtension(filename));
            var result = aliases.Where(alias => !alias.digits && !alias.normalized.StartsWith("http", StringComparison.Ordinal)
                    && Matcher.ValidIdentifier(alias.text) && Matcher.ContainsNormalized(stem, alias.normalized))
                .Select(alias => avatars[alias.avatar].displayName).Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToArray();
            cached = Array.AsReadOnly(result);
            packageNames.Add(filename, cached);
            return cached;
        }
        static string ProductText(LibraryItem item)
        {
            return item.title + "\n" + item.description + "\n" + string.Join("\n", item.tags ?? new List<string>())
                + "\n" + string.Join("\n", item.variationNames ?? new List<string>()) + "\n";
        }

        public void SetFilenames(string itemId, IEnumerable<string> filenames)
        {
            if (filenames == null) scannedFiles.Remove(itemId);
            else scannedFiles[itemId] = string.Join("\n", filenames);
            matches.Remove(itemId);
        }

        /// <summary>Results are cached; callers must treat returned lists as read-only.</summary>
        public List<AvatarMatch> Match(LibraryItem item, IEnumerable<string> filenames = null)
        {
            var rules = Rules(item);
            if (rules.normalizedText == null)
            {
                string purchasedText = Matcher.Normalize(string.Join("\n", item.purchasedVariationNames ?? new List<string>()));
                string withoutHonorific = PurchasedHonorific.Replace(purchasedText, "");
                if (withoutHonorific != purchasedText) purchasedText += "\n" + withoutHonorific;
                if (Acquisition(item) == AcquisitionClassifier.AvatarSpecific)
                {
                    var purchasedMatches = new int[avatars.Count];
                    for (int i = 0; i < purchasedMatches.Length; i++) purchasedMatches[i] = -1;
                    Scan(purchasedText, purchasedMatches);
                    // A named variation can describe content (MAKEUP+EYE, 通常版), not an avatar.
                    // Restrict evidence only when it identifies an avatar, before manual exclusions.
                    // Catalog names also protect purchases for avatars not present in the user's library.
                    rules.purchasedOnly = purchasedMatches.Any(index => index >= 0)
                        || defaultAvatarIdentifiers.Any(name => Matcher.ContainsNormalized(purchasedText, name));
                }
                rules.normalizedText = rules.purchasedOnly ? purchasedText : Matcher.Normalize(ProductText(item));
            }
            bool purchasedOnly = rules.purchasedOnly;
            string files;
            if (purchasedOnly) files = "";
            else if (filenames != null) files = string.Join("\n", filenames);
            else if (!scannedFiles.TryGetValue(item.id, out files)) files = "";
            CachedMatch cached;
            if (matches.TryGetValue(item.id, out cached) && cached.filenames == files) return cached.result;
            string text;
            if (purchasedOnly || files.Length == 0)
                text = rules.normalizedText;
            else text = Matcher.Normalize(ProductText(item) + files);
            var best = new int[avatars.Count];
            for (int i = 0; i < best.Length; i++) best[i] = -1;
            Scan(text, best);
            var result = new List<AvatarMatch>();
            var visited = new bool[avatars.Count];
            var queue = new Queue<int>();
            for (int i = 0; i < avatars.Count; i++)
            {
                string id = avatars[i].item.id;
                if (id == item.id || rules.excluded.Contains(id)) continue;
                bool manual = rules.included.Contains(id);
                if (!manual && best[i] < 0) continue;
                result.Add(new AvatarMatch { avatarId = id, reason = manual ? "手動指定" : (purchasedOnly ? "購入済み商品種類: " : "識別タグ: ") + aliases[best[i]].text });
                visited[i] = true; queue.Enqueue(i);
            }
            while (queue.Count > 0)
            {
                int origin = queue.Dequeue();
                foreach (int sibling in avatars[origin].neighbors)
                {
                    string id = avatars[sibling].item.id;
                    if (id == item.id || visited[sibling]) continue;
                    visited[sibling] = true; queue.Enqueue(sibling);
                    if (rules.excluded.Contains(id)) continue;
                    result.Add(new AvatarMatch { avatarId = id, indirect = true, reason = "共通素体: " + avatars[origin].displayName });
                }
            }
            matches[item.id] = new CachedMatch { filenames = files, result = result };
            return result;
        }
    }
}
