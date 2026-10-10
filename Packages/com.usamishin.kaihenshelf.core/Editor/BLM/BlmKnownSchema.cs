using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace KaihenShelf
{
    public static partial class BlmDatabaseReader
    {
        // Contract derived from the Windows KaihenShelf BoothLibraryReader.
        // Optional display fields may be absent, but all relationship tables must be initialized.
        static readonly Dictionary<string, string[]> KnownColumns = new Dictionary<string, string[]>
        {
            { "preferences", new[] { "item_directory_path" } },
            { "registered_items", new[] { "id", "booth_item_id", "user_item_info_id" } },
            { "booth_items", new[] { "id", "name", "shop_subdomain", "sub_category" } },
            { "overwritten_booth_items", new[] { "booth_item_id" } },
            { "user_item_info", new[] { "id", "name", "sub_category" } },
            { "shops", new[] { "subdomain", "name" } },
            { "sub_categories", new[] { "id", "name", "parent_category_id" } },
            { "parent_categories", new[] { "id", "name" } },
            { "overwritten_booth_item_tags", new[] { "booth_item_id", "tag" } },
            { "booth_item_tag_relations", new[] { "booth_item_id", "tag" } },
            { "booth_item_variations", new[] { "id", "booth_item_id", "variation_name", "order_id" } },
            { "notifications", new[] { "content", "read" } }
        };

        public static bool SupportsKnownSchema(BlmSchema schema)
        {
            return schema != null && KnownColumns.All(table => schema.tables.TryGetValue(table.Key, out var columns) && table.Value.All(columns.Contains));
        }

        public static List<LibraryItem> ReadKnown(string path, bool consent, CancellationToken token)
        {
            // Consent is checked before path normalization, existence checks, schema inspection, and retries.
            if (!consent) throw new InvalidOperationException("BOOTH Library Managerの読み取りには同意が必要です。");
            return Retry(() =>
            {
                using (var db = new ReadOnlySqlite(path, consent))
                {
                    var schema = Schema(db, token);
                    if (!SupportsKnownSchema(schema))
                    {
                        if (schema.tables.Count == 0 || schema.tables.Keys.Any(KnownColumns.ContainsKey))
                            throw new BlmInitializingException("BOOTH Library Managerの商品情報が初期化中、または未対応の形式です。BOOTH Library Managerの更新完了後に再試行してください。");
                        throw new InvalidDataException("未対応のBOOTH Library Managerデータベース形式です。連携設定のDBパスがBOOTH Library Managerのdata.dbを指しているか確認してください。");
                    }
                    string preferenceSql = "SELECT item_directory_path FROM preferences" + (schema.tables["preferences"].Contains("id") ? " ORDER BY id" : "") + " LIMIT 1";
                    var preferences = db.Query(preferenceSql, token, true);
                    if (preferences.Count == 0) throw new BlmInitializingException("BOOTH Library Managerの商品保存先設定が初期化中です。BOOTH Library Managerの更新完了後に再試行してください。");
                    string itemDirectory = preferences[0][0].TrimEnd('\0');
                    if (itemDirectory.Length > 0)
                    {
                        if (!Path.IsPathRooted(itemDirectory) || itemDirectory.IndexOf('\0') >= 0) throw new InvalidDataException("BOOTH Library Managerの商品保存先は絶対パスである必要があります。");
                        itemDirectory = Path.GetFullPath(itemDirectory);
                    }
                    return ReadKnownItems(db, schema, itemDirectory, token);
                }
            }, token);
        }

        static List<LibraryItem> ReadKnownItems(ReadOnlySqlite db, BlmSchema schema, string directory, CancellationToken token)
        {
            Func<string, string, string, string> col = (table, alias, column) => schema.tables[table].Contains(column) ? alias + "." + Quote(column) : "NULL";
            string age = "0";
            var ageColumns = schema.tables["booth_items"].Where(x =>
            {
                string name = x.Replace("_", "").ToLowerInvariant();
                return name.Contains("adult") || name.Contains("r18") || name.Contains("agerestrict");
            }).ToArray();
            // Only schema-confirmed BOOTH gate columns are read. Product wording and tags never set R18.
            var fields = new List<string>
            {
                "r.id", "COALESCE(" + col("overwritten_booth_items", "obi", "name") + ",bi.name,ui.name,'名称未設定')",
                "COALESCE(s.name," + col("user_item_info", "ui", "shop_name") + ",'')",
                "COALESCE(pc.name || ' > ' || sc.name, sc.name, '未分類')",
                "COALESCE(" + col("overwritten_booth_items", "obi", "description") + "," + col("booth_items", "bi", "description") + "," + col("user_item_info", "ui", "description") + ",'')",
                col("booth_items", "bi", "thumbnail_url"), col("user_item_info", "ui", "thumbnail_filename"), "r.booth_item_id",
                col("registered_items", "r", "created_at"),
                "COALESCE(" + col("booth_items", "bi", "updated_at") + "," + col("user_item_info", "ui", "updated_at") + "," + col("registered_items", "r", "updated_at") + ")",
                col("shops", "s", "thumbnail_url"), "bi.id", "r.user_item_info_id", "ui.id", col("booth_items", "bi", "published_at")
            };
            if (ageColumns.Length == 0) fields.Add(age);
            else fields.AddRange(ageColumns.Select(x => "bi." + Quote(x)));
            string sql = "SELECT " + string.Join(",", fields) + @"
                FROM registered_items r
                LEFT JOIN booth_items bi ON bi.id=r.booth_item_id
                LEFT JOIN overwritten_booth_items obi ON obi.booth_item_id=bi.id
                LEFT JOIN shops s ON s.subdomain=bi.shop_subdomain
                LEFT JOIN user_item_info ui ON ui.id=r.user_item_info_id
                LEFT JOIN sub_categories sc ON sc.id=COALESCE(bi.sub_category,ui.sub_category)
                LEFT JOIN parent_categories pc ON pc.id=sc.parent_category_id
                ORDER BY 2 COLLATE NOCASE";
            var result = new List<LibraryItem>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in db.Query(sql, token))
            {
                if (string.IsNullOrWhiteSpace(row[0]) || !ids.Add(row[0])) throw new InvalidDataException("BOOTH Library Managerの商品登録IDが空または重複しています。");
                if ((row[7].Length > 0 && row[11].Length == 0) || (row[12].Length > 0 && row[13].Length == 0))
                    throw new BlmInitializingException("BOOTH Library Managerの商品情報が更新途中です。BOOTH Library Managerの更新完了後に再試行してください。");
                string folder = directory.Length == 0 ? "" : ChildPath(directory, row[0], true);
                string boothId = row[7];
                if (boothId.Length > 0 && !boothId.All(c => c >= '0' && c <= '9')) throw new InvalidDataException("BOOTH Library ManagerのBOOTH商品IDが不正です。");
                result.Add(new LibraryItem
                {
                    id = "blm:" + row[0], title = row[1], shop = row[2], originalCategory = row[3], category = Categories.Classify(row[3]),
                    description = row[4], thumbnailUrl = SafeImageUrl(row[5]),
                    thumbnailPath = row[6].Length > 0 && folder.Length > 0 ? ChildPath(folder, row[6], false) : "",
                    folder = folder, boothId = boothId, url = boothId.Length == 0 ? "" : "https://booth.pm/ja/items/" + boothId,
                    registeredUtc = row[8], updatedUtc = row[9], shopThumbnailUrl = SafeImageUrl(row[10]),
                    publishedUtc = row[14], ageRestricted = row.Skip(15).Any(AgeRestricted), source = "blm", variationMetadataKnown = true
                });
            }

            var byBooth = result.Where(x => x.boothId.Length > 0).GroupBy(x => x.boothId).ToDictionary(x => x.Key, x => x.ToList());
            var tags = ReadKnownTags(db, "booth_item_tag_relations", token);
            var overrides = ReadKnownTags(db, "overwritten_booth_item_tags", token);
            foreach (var group in byBooth)
            {
                List<string> values;
                if (!overrides.TryGetValue(group.Key, out values)) tags.TryGetValue(group.Key, out values);
                foreach (var item in group.Value) item.tags = values == null ? new List<string>() : new List<string>(values);
            }
            foreach (var row in db.Query("SELECT booth_item_id, variation_name, order_id IS NOT NULL FROM booth_item_variations ORDER BY id", token))
            {
                if (!byBooth.TryGetValue(row[0], out var items)) continue;
                foreach (var item in items)
                {
                    item.hasVariationRows = true;
                    if (!string.IsNullOrWhiteSpace(row[1])) item.variationNames.Add(row[1]);
                    if (row[2] != "1") continue;
                    item.hasPurchasedVariationOrder = true;
                    if (!string.IsNullOrWhiteSpace(row[1])) item.purchasedVariationNames.Add(row[1]);
                }
            }
            // CASE prevents malformed notification JSON from reaching json_extract.
            const string updates = @"SELECT DISTINCT v.booth_item_id
                FROM notifications n JOIN booth_item_variations v
                ON v.id=CAST(CASE WHEN json_valid(n.content) THEN json_extract(n.content,'$.data[0]') END AS INTEGER)
                WHERE n.read=0 AND CASE WHEN json_valid(n.content) THEN json_extract(n.content,'$.type') END='latestDownloadableAvailable'";
            foreach (var row in db.Query(updates, token))
                if (byBooth.TryGetValue(row[0], out var items)) foreach (var item in items) item.updateAvailable = true;
            return result;
        }

        static Dictionary<string, List<string>> ReadKnownTags(ReadOnlySqlite db, string table, CancellationToken token)
        {
            // table comes only from the two constants above, after known-schema validation.
            var result = new Dictionary<string, List<string>>();
            foreach (var row in db.Query("SELECT booth_item_id,tag FROM " + Quote(table) + " WHERE tag IS NOT NULL ORDER BY tag", token))
            {
                if (!result.TryGetValue(row[0], out var values)) result[row[0]] = values = new List<string>();
                if (!string.IsNullOrWhiteSpace(row[1]) && !values.Contains(row[1])) values.Add(row[1]);
            }
            return result;
        }

        static string ChildPath(string parent, string relative, bool singleSegment)
        {
            SafePaths.Relative(relative.Replace('\\', '/'));
            string[] parts = relative.Replace('\\', '/').Split('/');
            if (Path.IsPathRooted(relative) || (singleSegment && parts.Length != 1) || parts.Any(x => x.Length == 0 || x == "." || x == ".." || x.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || x.EndsWith(".", StringComparison.Ordinal) || x.EndsWith(" ", StringComparison.Ordinal)))
                throw new InvalidDataException("BOOTH Library Managerの商品フォルダーまたは画像ファイル名が不正です。");
            string root = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string path = Path.GetFullPath(Path.Combine(root, relative));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("BOOTH Library Managerの商品保存先の外を参照するパスです。");
            return path;
        }

        static string SafeImageUrl(string value)
        {
            return ImageSourcePolicy.TryRemote(value, out _) ? value : "";
        }
    }
}
