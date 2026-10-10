using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using KaihenShelf;

internal static class BlmKnownSchemaTests
{
    public static void Run(Action<string, Action> test)
    {
        test("known BLM joins, Unicode BLOB paths, tags, variations, notifications and age gate", () =>
        {
            using var fixture = new Fixture();
            fixture.Seed(true);
            fixture.Exec("ALTER TABLE booth_items ADD COLUMN published_at TEXT; UPDATE booth_items SET published_at='2025-12-01T00:00:00Z' WHERE id=123;");
            byte[] before = Hash(fixture.Path);
            var rows = BlmDatabaseReader.ReadKnown(fixture.Path, true, default);
            Check(rows.Count == 5);
            var first = rows.Single(x => x.id == "blm:registration-1");
            Check(first.title == "上書き衣装" && first.description == "元の商品説明" && first.shop == "Test shop");
            Check(first.folder == System.IO.Path.Combine(Fixture.ItemDirectory, "registration-1"));
            Check(first.originalCategory == "3Dモデル > 3D衣装" && first.category == AssetCategory.Clothing);
            Check(first.url == "https://booth.pm/ja/items/123" && first.boothId == "123");
            Check(first.registeredUtc == "2026-01-01" && first.updatedUtc == "2026-10-01" && first.variationMetadataKnown);
            Check(first.publishedUtc == "2025-12-01T00:00:00Z");
            Check(first.tags.SequenceEqual(new[] { "上書き", "衣装" }));
            Check(first.variationNames.SequenceEqual(new[] { "未購入Maya", "購入Shinano / blue" }));
            Check(first.purchasedVariationNames.SequenceEqual(new[] { "購入Shinano / blue" }));
            Check(first.hasVariationRows && first.hasPurchasedVariationOrder && first.updateAvailable && first.ageRestricted);
            Check(first.shopThumbnailUrl == "https://booth.pximg.net/shop.png");
            var free = rows.Single(x => x.boothId == "124");
            Check(free.hasVariationRows && !free.hasPurchasedVariationOrder && free.purchasedVariationNames.Count == 0 && !free.updateAvailable);
            Check(!free.ageRestricted && free.title.Contains("R18") && free.thumbnailUrl == "");
            var unnamed = rows.Single(x => x.boothId == "125");
            Check(unnamed.hasVariationRows && unnamed.hasPurchasedVariationOrder && unnamed.purchasedVariationNames.Count == 0 && unnamed.variationNames.Count == 0);
            var noVariation = rows.Single(x => x.boothId == "126");
            Check(!noVariation.hasVariationRows && !noVariation.hasPurchasedVariationOrder && !noVariation.ageRestricted);
            var user = rows.Single(x => x.id == "blm:registration-user");
            Check(user.title == "ローカル商品" && user.shop == "Local shop" && user.url == "");
            Check(user.thumbnailPath == System.IO.Path.Combine(Fixture.ItemDirectory, "registration-user", "thumb.png"));
            Check(before.SequenceEqual(Hash(fixture.Path)), "BLM fixture must remain byte-for-byte unchanged");
            using var readOnly = new ReadOnlySqlite(fixture.Path, true);
            Throws<IOException>(() => readOnly.Query("UPDATE registered_items SET id='changed'"));
        });
        test("known BLM text paths and absent optional display columns", () =>
        {
            using var fixture = new Fixture(); fixture.Schema(); fixture.Exec("INSERT INTO preferences VALUES(1," + Sql(Fixture.ItemDirectory) + ");");
            fixture.Exec("INSERT INTO registered_items(id,booth_item_id,user_item_info_id) VALUES('only-user',NULL,1); INSERT INTO user_item_info(id,name,sub_category) VALUES(1,'Local',NULL);");
            var item = BlmDatabaseReader.ReadKnown(fixture.Path, true, default).Single();
            Check(item.title == "Local" && item.folder.EndsWith("only-user") && item.description == "");
            Check(item.publishedUtc == "");
        });
        test("known BLM retains local thumbnail alongside remote URL", () =>
        {
            using var fixture = new Fixture(); fixture.Seed(false);
            fixture.Exec("UPDATE registered_items SET user_item_info_id=10 WHERE id='registration-1';");
            var item = BlmDatabaseReader.ReadKnown(fixture.Path, true, default).Single(x => x.id == "blm:registration-1");
            Check(item.thumbnailUrl.Length > 0 && item.thumbnailPath == System.IO.Path.Combine(Fixture.ItemDirectory, "registration-1", "thumb.png"));
        });
        test("known BLM initialized empty catalog is valid", () =>
        {
            using var fixture = new Fixture(); fixture.Schema(); fixture.Exec("INSERT INTO preferences VALUES(1,'');");
            Check(BlmDatabaseReader.ReadKnown(fixture.Path, true, default).Count == 0);
        });
        test("known BLM consent precedes filesystem and cancellation", () =>
        {
            Throws<InvalidOperationException>(() => BlmDatabaseReader.ReadKnown("does-not-exist.db", false, new CancellationToken(true)));
            Throws<OperationCanceledException>(() => BlmDatabaseReader.ReadKnown("does-not-exist.db", true, new CancellationToken(true)));
        });
        test("known BLM unknown schema is rejected", () =>
        {
            using var fixture = new Fixture(); fixture.Exec("CREATE TABLE products(id TEXT);");
            Check(!BlmDatabaseReader.SupportsKnownSchema(BlmDatabaseReader.Inspect(fixture.Path, true, default)));
            Throws<InvalidDataException>(() => BlmDatabaseReader.ReadKnown(fixture.Path, true, default));
        });
        test("known BLM incomplete schema retries with cancellation", () =>
        {
            using var fixture = new Fixture(); fixture.Exec("CREATE TABLE registered_items(id TEXT);");
            using var cancel = new CancellationTokenSource(100);
            Throws<OperationCanceledException>(() => BlmDatabaseReader.ReadKnown(fixture.Path, true, cancel.Token));
        });
        test("known BLM absent preferences and dangling reference retry with cancellation", () =>
        {
            using var fixture = new Fixture(); fixture.Schema();
            using (var cancel = new CancellationTokenSource(100))
                Throws<OperationCanceledException>(() => BlmDatabaseReader.ReadKnown(fixture.Path, true, cancel.Token));
            fixture.Exec("INSERT INTO preferences VALUES(1,''); INSERT INTO registered_items(id,booth_item_id,user_item_info_id) VALUES('missing-product',123,NULL);");
            using (var cancel = new CancellationTokenSource(100))
                Throws<OperationCanceledException>(() => BlmDatabaseReader.ReadKnown(fixture.Path, true, cancel.Token));
        });
        test("known BLM locked database retry remains cancellable", () =>
        {
            using var fixture = new Fixture(); fixture.Schema(); fixture.Exec("BEGIN EXCLUSIVE;");
            using var cancel = new CancellationTokenSource(100);
            Throws<OperationCanceledException>(() => BlmDatabaseReader.ReadKnown(fixture.Path, true, cancel.Token));
            fixture.Exec("ROLLBACK;");
        });
        test("known BLM malformed UTF16 BLOB rejected", () =>
        {
            using var fixture = new Fixture(); fixture.Schema(); fixture.Exec("INSERT INTO preferences VALUES(1,X'43');");
            Throws<InvalidDataException>(() => BlmDatabaseReader.ReadKnown(fixture.Path, true, default));
        });
        test("known BLM registration and thumbnail cannot escape product folder", () =>
        {
            using var fixture = new Fixture(); fixture.Seed(false);
            fixture.Exec("UPDATE registered_items SET id='../escape' WHERE id='registration-user';");
            Throws<InvalidDataException>(() => BlmDatabaseReader.ReadKnown(fixture.Path, true, default));
            fixture.Exec("UPDATE registered_items SET id='registration-user' WHERE id='../escape'; UPDATE user_item_info SET thumbnail_filename='../outside.png';");
            Throws<InvalidDataException>(() => BlmDatabaseReader.ReadKnown(fixture.Path, true, default));
            fixture.Exec("UPDATE user_item_info SET thumbnail_filename='thumb.png'; UPDATE registered_items SET id='CON' WHERE id='registration-user';");
            Throws<InvalidDataException>(() => BlmDatabaseReader.ReadKnown(fixture.Path, true, default));
        });
        test("known BLM age gate columns are schema allowlisted and safely quoted", () =>
        {
            using var fixture = new Fixture(); fixture.Seed(false);
            fixture.Exec("ALTER TABLE booth_items ADD COLUMN \"r18\"\"flag\" INTEGER; UPDATE booth_items SET \"r18\"\"flag\"=1 WHERE id=126;");
            Check(BlmDatabaseReader.ReadKnown(fixture.Path, true, default).Single(x => x.boothId == "126").ageRestricted);
            fixture.Exec("UPDATE booth_items SET is_adult=2 WHERE id=124;");
            Check(BlmDatabaseReader.ReadKnown(fixture.Path, true, default).Single(x => x.boothId == "124").ageRestricted);
        });
    }

    static void Check(bool value, string message = "assertion failed") { if (!value) throw new Exception(message); }
    static void Throws<T>(Action run) where T : Exception { try { run(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    static string Sql(string value) => "'" + value.Replace("'", "''") + "'";
    static byte[] Hash(string path) { using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); return SHA256.HashData(stream); }

    sealed class Fixture : IDisposable
    {
        const string Dll = "winsqlite3.dll";
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_open_v2(byte[] path, out IntPtr db, int flags, IntPtr vfs);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_close_v2(IntPtr db);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_exec(IntPtr db, byte[] sql, IntPtr callback, IntPtr context, out IntPtr error);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern void sqlite3_free(IntPtr pointer);
        IntPtr database;
        readonly string root;
        public string Path { get; }
        public static string ItemDirectory => @"C:\BLM テスト 😀\商品";
        public Fixture()
        {
            root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "KaihenShelf-blm-fixture-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root); Path = System.IO.Path.Combine(root, "synthetic.db");
            if (sqlite3_open_v2(Encoding.UTF8.GetBytes(Path + "\0"), out database, 2 | 4, IntPtr.Zero) != 0) throw new Exception("Synthetic SQLite fixture could not be created");
        }
        public void Exec(string sql)
        {
            int code = sqlite3_exec(database, Encoding.UTF8.GetBytes(sql + "\0"), IntPtr.Zero, IntPtr.Zero, out var error);
            string message = error == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(error);
            if (error != IntPtr.Zero) sqlite3_free(error);
            if (code != 0) throw new Exception("Fixture SQL failed: " + message);
        }
        public void Schema()
        {
            Exec(@"CREATE TABLE preferences(id INTEGER,item_directory_path);
                CREATE TABLE registered_items(id TEXT,booth_item_id INTEGER,user_item_info_id INTEGER);
                CREATE TABLE booth_items(id INTEGER,name TEXT,shop_subdomain TEXT,sub_category INTEGER);
                CREATE TABLE overwritten_booth_items(booth_item_id INTEGER);
                CREATE TABLE user_item_info(id INTEGER,name TEXT,sub_category INTEGER);
                CREATE TABLE shops(subdomain TEXT,name TEXT);
                CREATE TABLE sub_categories(id INTEGER,name TEXT,parent_category_id INTEGER);
                CREATE TABLE parent_categories(id INTEGER,name TEXT);
                CREATE TABLE booth_item_tag_relations(booth_item_id INTEGER,tag TEXT);
                CREATE TABLE overwritten_booth_item_tags(booth_item_id INTEGER,tag TEXT);
                CREATE TABLE booth_item_variations(id INTEGER,booth_item_id INTEGER,variation_name TEXT,order_id INTEGER);
                CREATE TABLE notifications(content TEXT,read INTEGER);");
        }
        public void Seed(bool blob)
        {
            Schema();
            string directoryValue = blob ? "X'" + Convert.ToHexString(Encoding.Unicode.GetBytes(ItemDirectory + "\0")) + "'" : Sql(ItemDirectory);
            Exec("INSERT INTO preferences VALUES(1," + directoryValue + ");");
            Exec(@"ALTER TABLE registered_items ADD COLUMN created_at TEXT;
                ALTER TABLE registered_items ADD COLUMN updated_at TEXT;
                ALTER TABLE booth_items ADD COLUMN description TEXT;
                ALTER TABLE booth_items ADD COLUMN thumbnail_url TEXT;
                ALTER TABLE booth_items ADD COLUMN updated_at TEXT;
                ALTER TABLE booth_items ADD COLUMN is_adult INTEGER;
                ALTER TABLE overwritten_booth_items ADD COLUMN name TEXT;
                ALTER TABLE overwritten_booth_items ADD COLUMN description TEXT;
                ALTER TABLE user_item_info ADD COLUMN description TEXT;
                ALTER TABLE user_item_info ADD COLUMN thumbnail_filename TEXT;
                ALTER TABLE user_item_info ADD COLUMN shop_name TEXT;
                ALTER TABLE user_item_info ADD COLUMN updated_at TEXT;
                ALTER TABLE shops ADD COLUMN thumbnail_url TEXT;
                INSERT INTO parent_categories VALUES(1,'3Dモデル');
                INSERT INTO sub_categories VALUES(2,'3D衣装',1);
                INSERT INTO shops VALUES('example','Test shop','https://booth.pximg.net/shop.png');
                INSERT INTO booth_items VALUES(123,'元の商品名','example',2,'元の商品説明','https://booth.pximg.net/image.png','2026-10-01',1);
                INSERT INTO booth_items VALUES(124,'R18という文字のみ','example',2,'','https://attacker.example/image.png','2026-10-01',0);
                INSERT INTO booth_items VALUES(125,'購入名欠損','example',2,'',NULL,NULL,0);
                INSERT INTO booth_items VALUES(126,'variationなし','example',2,'',NULL,NULL,0);
                INSERT INTO overwritten_booth_items VALUES(123,'上書き衣装',NULL);
                INSERT INTO user_item_info VALUES(10,'ローカル商品',2,'ローカル説明','thumb.png','Local shop','2026-10-03');
                INSERT INTO registered_items VALUES('registration-1',123,NULL,'2026-01-01','2026-09-01');
                INSERT INTO registered_items VALUES('registration-2',124,NULL,'2026-01-02','2026-09-02');
                INSERT INTO registered_items VALUES('registration-3',125,NULL,'2026-01-03','2026-09-03');
                INSERT INTO registered_items VALUES('registration-4',126,NULL,'2026-01-04','2026-09-04');
                INSERT INTO registered_items VALUES('registration-user',NULL,10,'2026-01-04','2026-09-04');
                INSERT INTO booth_item_tag_relations VALUES(123,'元タグ');
                INSERT INTO booth_item_tag_relations VALUES(124,'R18');
                INSERT INTO overwritten_booth_item_tags VALUES(123,'上書き');
                INSERT INTO overwritten_booth_item_tags VALUES(123,'衣装');
                INSERT INTO overwritten_booth_item_tags VALUES(123,'衣装');
                INSERT INTO booth_item_variations VALUES(1001,123,'未購入Maya',NULL);
                INSERT INTO booth_item_variations VALUES(1002,123,'購入Shinano / blue',55);
                INSERT INTO booth_item_variations VALUES(1003,124,'Free',NULL);
                INSERT INTO booth_item_variations VALUES(1004,125,NULL,56);
                INSERT INTO notifications VALUES('{""type"":""latestDownloadableAvailable"",""data"":[1002]}',0);
                INSERT INTO notifications VALUES('{""type"":""latestDownloadableAvailable"",""data"":[1003]}',1);
                INSERT INTO notifications VALUES('{""type"":""unrelated"",""data"":[1004]}',0);
                INSERT INTO notifications VALUES('malformed JSON',0);");
        }
        public void Dispose()
        {
            if (database != IntPtr.Zero) { sqlite3_close_v2(database); database = IntPtr.Zero; }
            string expectedPrefix = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "KaihenShelf-blm-fixture-");
            if (!System.IO.Path.GetFullPath(root).StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase)) throw new Exception("Refusing fixture cleanup outside synthetic test directory");
            Directory.Delete(root, true);
        }
    }
}
