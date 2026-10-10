using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KaihenShelf
{
    public sealed class CatalogFilter
    {
        public bool avatars, hideR18, favoritesOnly, freeOnly, excludeShared, excludeNoAvatar;
        public int category, sort;
        public string search = "", shop = "", tag = "", avatarId = "";
    }

    public sealed class CatalogGroup
    {
        public string key;
        public int count;
        public LibraryItem item;
        internal string normalized;
    }

    /// <summary>
    /// A catalog snapshot for repeated UI queries. Recreate for item/metadata edits;
    /// favorite and scanned-file changes have targeted update methods.
    /// </summary>
    public sealed class CatalogViewIndex
    {
        sealed class Entry
        {
            public LibraryItem item;
            public AssetCategory category;
            public string normalizedSearch;
            public HashSet<string> tags;
            public bool free;
            public DateTimeOffset? registered, updated, published;
        }
        struct FilterKey : IEquatable<FilterKey>
        {
            public bool avatars, hideR18, favoritesOnly, freeOnly, excludeShared, excludeNoAvatar;
            public int category, sort;
            public string search, shop, tag, avatarId;
            public FilterKey(CatalogFilter value)
            {
                avatars = value.avatars; hideR18 = value.hideR18; favoritesOnly = value.favoritesOnly;
                freeOnly = value.freeOnly; excludeShared = value.excludeShared; category = value.category; sort = value.sort;
                excludeNoAvatar = value.excludeNoAvatar;
                search = value.search ?? ""; shop = value.shop ?? ""; tag = value.tag ?? ""; avatarId = value.avatarId ?? "";
            }
            public bool Equals(FilterKey other)
            {
                return avatars == other.avatars && hideR18 == other.hideR18 && favoritesOnly == other.favoritesOnly
                    && freeOnly == other.freeOnly && excludeShared == other.excludeShared && excludeNoAvatar == other.excludeNoAvatar && category == other.category && sort == other.sort
                    && search == other.search && shop == other.shop && tag == other.tag && avatarId == other.avatarId;
            }
        }
        readonly MatcherIndex matcher;
        readonly Entry[][] sorted;
        readonly Dictionary<string, HashSet<string>> favorites = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        readonly Dictionary<string, CatalogGroup[]> groups = new Dictionary<string, CatalogGroup[]>(StringComparer.Ordinal);
        FilterKey lastFilter;
        IReadOnlyList<LibraryItem> lastQuery;
        string groupScope, groupSearch;
        bool groupFavorites, groupHideR18;
        int groupSort;
        IReadOnlyList<CatalogGroup> lastGroups;

        public LibraryItem[] Avatars { get { return matcher.Avatars; } }
        public string[] AvatarLabels { get; private set; }
        public string[] Shops { get; private set; }
        public string[] Tags { get; private set; }

        public CatalogViewIndex(LibraryState state)
        {
            matcher = new MatcherIndex(state);
            var entries = state.items.Where(x => x != null).Select(item =>
            {
                string acquisition = item.acquisition ?? "";
                string normalizedAcquisition = Matcher.Normalize(acquisition);
                return new Entry { item = item, category = matcher.Category(item),
                    normalizedSearch = Matcher.Normalize(item.title + " " + string.Join(" ", item.tags ?? new List<string>())),
                    registered = ParseDate(item.registeredUtc), updated = ParseDate(item.updatedUtc), published = ParseDate(item.publishedUtc),
                    tags = new HashSet<string>(item.tags ?? new List<string>(), StringComparer.Ordinal),
                    free = acquisition.Contains("無料") || acquisition.Contains("ギフト") || normalizedAcquisition == "free" || normalizedAcquisition == "gift" };
            }).ToArray();
            sorted = new[] {
                entries.OrderBy(x => x.item.title, StringComparer.CurrentCultureIgnoreCase).ToArray(),
                entries.OrderByDescending(x => x.item.title, StringComparer.CurrentCultureIgnoreCase).ToArray(),
                entries.OrderBy(x => x.item.shop, StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x.item.title).ToArray(),
                entries.OrderByDescending(x => x.item.shop, StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x.item.title).ToArray(),
                entries.OrderByDescending(x => x.registered ?? DateTimeOffset.MinValue).ToArray(),
                entries.OrderBy(x => x.registered ?? DateTimeOffset.MaxValue).ToArray(),
                entries.OrderByDescending(x => x.updated ?? DateTimeOffset.MinValue).ToArray(),
                entries.OrderBy(x => x.updated ?? DateTimeOffset.MaxValue).ToArray(),
                entries.OrderByDescending(x => x.published ?? DateTimeOffset.MinValue).ToArray(),
                entries.OrderBy(x => x.published ?? DateTimeOffset.MaxValue).ToArray()
            };
            AvatarLabels = new[] { "すべて" }.Concat(Avatars.Select(x =>
            {
                string name = state.metadata.FirstOrDefault(meta => meta.id == x.id)?.mainName;
                return string.IsNullOrWhiteSpace(name) ? x.title : name;
            })).ToArray();
            Shops = entries.Select(x => x.item.shop).Where(x => !string.IsNullOrEmpty(x)).Distinct().OrderBy(x => x).ToArray();
            Tags = entries.Where(x => x.category != AssetCategory.Avatar).SelectMany(x => x.tags).Distinct().OrderBy(x => x).ToArray();
            foreach (bool hideRestricted in new[] { false, true })
            {
                var shopGroups = new Dictionary<string, CatalogGroup>(StringComparer.Ordinal);
                var tagGroups = new Dictionary<string, CatalogGroup>(StringComparer.Ordinal);
                foreach (var entry in entries)
                {
                    if (hideRestricted && entry.item.ageRestricted) continue;
                    if (!string.IsNullOrEmpty(entry.item.shop)) AddGroup(shopGroups, entry.item.shop, entry.item);
                    if (entry.category != AssetCategory.Avatar)
                        foreach (string tag in entry.tags) AddGroup(tagGroups, tag, entry.item);
                }
                groups.Add(GroupKey("shops", hideRestricted), shopGroups.Values.OrderBy(x => x.key).ToArray());
                groups.Add(GroupKey("booth-tags", hideRestricted), tagGroups.Values.OrderBy(x => x.key).ToArray());
            }
            UpdateFavorites(state.favorites);
        }

        static DateTimeOffset? ParseDate(string value)
        {
            DateTimeOffset date;
            return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out date)
                ? date : (DateTimeOffset?)null;
        }

        static void AddGroup(Dictionary<string, CatalogGroup> groups, string key, LibraryItem item)
        {
            if (key == null) return;
            CatalogGroup group;
            if (!groups.TryGetValue(key, out group))
            {
                group = new CatalogGroup { key = key, normalized = Matcher.Normalize(key), item = item };
                groups.Add(key, group);
            }
            else if (string.IsNullOrEmpty(group.item.shopThumbnailUrl) && !string.IsNullOrEmpty(item.shopThumbnailUrl)) group.item = item;
            group.count++;
        }
        static string GroupKey(string scope, bool hideR18) { return scope + (hideR18 ? ":safe" : ":all"); }
        public AssetCategory Category(LibraryItem item) { return matcher.Category(item); }
        public string Scope(LibraryItem item) { return Category(item) == AssetCategory.Avatar ? "avatars" : "items"; }
        public LibraryItem Find(string id) { return matcher.Find(id); }
        public bool IsFavorite(string scope, string key)
        { HashSet<string> values; return scope != null && favorites.TryGetValue(scope, out values) && values.Contains(key); }

        public void UpdateFavorites(IEnumerable<Favorite> values)
        {
            favorites.Clear();
            foreach (var value in values ?? Array.Empty<Favorite>())
            {
                if (value == null || value.scope == null || value.key == null) continue;
                HashSet<string> keys;
                if (!favorites.TryGetValue(value.scope, out keys)) { keys = new HashSet<string>(StringComparer.Ordinal); favorites.Add(value.scope, keys); }
                keys.Add(value.key);
            }
            lastQuery = null; lastGroups = null;
        }
        public void SetFilenames(string itemId, IEnumerable<string> files)
        { matcher.SetFilenames(itemId, files); lastQuery = null; }
        static readonly List<AvatarMatch> NoCompatibility = new List<AvatarMatch>();
        public List<AvatarMatch> Match(LibraryItem item, IEnumerable<string> filenames = null)
        { return Categories.HasAvatarCompatibility(Category(item)) ? matcher.Match(item, filenames) : NoCompatibility; }
        public IReadOnlyList<string> PackageAvatarNames(string filename) { return matcher.PackageAvatarNames(filename); }

        /// <summary>Returns the same read-only result until filters/data change.</summary>
        public IReadOnlyList<LibraryItem> Query(CatalogFilter filter)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));
            var key = new FilterKey(filter);
            if (lastQuery != null && lastFilter.Equals(key)) return lastQuery;
            string search = Matcher.Normalize(key.search);
            var result = new List<LibraryItem>();
            foreach (var entry in sorted[key.sort >= 0 && key.sort < sorted.Length ? key.sort : 0])
            {
                bool isAvatar = entry.category == AssetCategory.Avatar;
                if (isAvatar != key.avatars || key.hideR18 && entry.item.ageRestricted) continue;
                if (key.favoritesOnly && !IsFavorite(isAvatar ? "avatars" : "items", entry.item.id)) continue;
                if (search.Length > 0 && entry.normalizedSearch.IndexOf(search, StringComparison.Ordinal) < 0) continue;
                if (!key.avatars)
                {
                    if (key.category != 0 && (int)entry.category != key.category) continue;
                    if (key.shop.Length > 0 && entry.item.shop != key.shop) continue;
                    if (key.tag.Length > 0 && !entry.tags.Contains(key.tag)) continue;
                    if (key.freeOnly && !entry.free) continue;
                    if (key.avatarId.Length > 0)
                    {
                        var compatibility = Match(entry.item);
                        if (compatibility.Count == 0) { if (key.excludeNoAvatar) continue; }
                        else if (key.avatarId.Length > 0 && !compatibility.Any(x => x.avatarId == key.avatarId && (!key.excludeShared || !x.indirect))) continue;
                    }
                }
                result.Add(entry.item);
            }
            lastFilter = key;
            return lastQuery = result.AsReadOnly();
        }

        public IReadOnlyList<CatalogGroup> QueryGroups(string scope, string search, bool favoritesOnly, bool hideR18, int sort = 0)
        {
            search = search ?? "";
            if (lastGroups != null && groupScope == scope && groupSearch == search && groupFavorites == favoritesOnly && groupHideR18 == hideR18 && groupSort == sort) return lastGroups;
            CatalogGroup[] source;
            if (!groups.TryGetValue(GroupKey(scope, hideR18), out source)) source = Array.Empty<CatalogGroup>();
            string normalized = Matcher.Normalize(search);
            var filtered = source.Where(x => x.normalized.IndexOf(normalized, StringComparison.Ordinal) >= 0 && (!favoritesOnly || IsFavorite(scope, x.key)));
            var result = (sort == 1 ? filtered.OrderByDescending(x => x.key, StringComparer.CurrentCultureIgnoreCase)
                : sort == 2 ? filtered.OrderBy(x => x.count).ThenBy(x => x.key, StringComparer.CurrentCultureIgnoreCase)
                : sort == 3 ? filtered.OrderByDescending(x => x.count).ThenBy(x => x.key, StringComparer.CurrentCultureIgnoreCase)
                : filtered.OrderBy(x => x.key, StringComparer.CurrentCultureIgnoreCase)).ToArray();
            groupScope = scope; groupSearch = search; groupFavorites = favoritesOnly; groupHideR18 = hideR18;
            groupSort = sort;
            return lastGroups = Array.AsReadOnly(result);
        }
    }
}
