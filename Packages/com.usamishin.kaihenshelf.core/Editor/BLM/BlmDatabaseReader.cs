using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace KaihenShelf
{
    public sealed class BlmSchema { public Dictionary<string, List<string>> tables = new Dictionary<string, List<string>>(); }
    public sealed class BlmBusyException : IOException { public BlmBusyException(string message) : base(message) { } }
    public sealed class BlmInitializingException : IOException { public BlmInitializingException(string message) : base(message) { } }

    // No writable open flags, SQL provided by a user, or BOOTH Library Manager filesystem mutations.
    public sealed class ReadOnlySqlite : IDisposable
    {
        IntPtr database;
        const string Dll = "winsqlite3.dll";
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_open_v2(byte[] path, out IntPtr db, int flags, IntPtr vfs);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_close_v2(IntPtr db);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_busy_timeout(IntPtr db, int milliseconds);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int length, out IntPtr statement, IntPtr tail);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_step(IntPtr statement);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_finalize(IntPtr statement);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_column_count(IntPtr statement);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr sqlite3_column_text(IntPtr statement, int column);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_column_type(IntPtr statement, int column);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr sqlite3_column_blob(IntPtr statement, int column);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_column_bytes(IntPtr statement, int column);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr sqlite3_errmsg(IntPtr db);

        public ReadOnlySqlite(string file, bool consent)
        {
            if (!consent) throw new InvalidOperationException("BOOTH Library Managerの読み取りには同意が必要です。");
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) throw new PlatformNotSupportedException("BOOTH Library Manager連携はWindows専用です。");
            if (!File.Exists(file)) throw new FileNotFoundException("BOOTH Library Manager DBが見つかりません。BOOTH Library Managerでデータを取得してから再試行してください。", file);
            try
            {
                Check(sqlite3_open_v2(Utf8(Path.GetFullPath(file)), out database, 0x00000001 | 0x00010000, IntPtr.Zero)); // READONLY | FULLMUTEX
                Check(sqlite3_busy_timeout(database, 1000));
                Query("PRAGMA query_only=ON");
                Query("BEGIN"); // Stable read snapshot across schema and rows.
            }
            catch { Dispose(); throw; }
        }
        internal List<string[]> Query(string sql, CancellationToken token = default, bool utf16Blob = false)
        {
            IntPtr statement = IntPtr.Zero;
            var rows = new List<string[]>();
            long totalBytes = 0;
            try
            {
                token.ThrowIfCancellationRequested();
                Check(sqlite3_prepare_v2(database, Utf8(sql), -1, out statement, IntPtr.Zero));
                int code;
                while ((code = sqlite3_step(statement)) == 100)
                {
                    token.ThrowIfCancellationRequested();
                    if (rows.Count >= 100000) throw new InvalidDataException("BOOTH Library Managerの行数上限を超えました。");
                    var row = new string[sqlite3_column_count(statement)];
                    for (int i = 0; i < row.Length; i++)
                    {
                        bool blob = sqlite3_column_type(statement, i) == 4;
                        // Obtain the pointer before its byte count: SQLite can convert numeric/text storage here.
                        IntPtr pointer = blob ? sqlite3_column_blob(statement, i) : sqlite3_column_text(statement, i);
                        int size = sqlite3_column_bytes(statement, i);
                        if (size > 4 * 1024 * 1024) throw new InvalidDataException("BOOTH Library Managerの列が大きすぎます。");
                        totalBytes += size;
                        if (totalBytes > 256L * 1024 * 1024) throw new InvalidDataException("BOOTH Library Managerの読み取りデータ量の上限を超えました。");
                        var bytes = new byte[size];
                        if (size > 0) Marshal.Copy(pointer, bytes, 0, size);
                        if (blob && utf16Blob)
                        {
                            if ((size & 1) != 0) throw new InvalidDataException("BOOTH Library Managerの商品保存先の文字コードが不正です。");
                            try { row[i] = new UnicodeEncoding(false, false, true).GetString(bytes).TrimStart('\uFEFF').TrimEnd('\0'); }
                            catch (DecoderFallbackException) { throw new InvalidDataException("BOOTH Library Managerの商品保存先の文字コードが不正です。"); }
                        }
                        else row[i] = Encoding.UTF8.GetString(bytes);
                    }
                    rows.Add(row);
                }
                if (code != 101) Check(code);
                token.ThrowIfCancellationRequested();
                return rows;
            }
            finally { if (statement != IntPtr.Zero) sqlite3_finalize(statement); }
        }
        void Check(int code)
        {
            if (code == 0) return;
            string message = "SQLite: " + (database == IntPtr.Zero ? code.ToString() : Marshal.PtrToStringAnsi(sqlite3_errmsg(database)));
            if ((code & 255) == 5 || (code & 255) == 6) throw new BlmBusyException(message);
            throw new IOException(message);
        }
        static byte[] Utf8(string text) { return Encoding.UTF8.GetBytes(text + "\0"); }
        public void Dispose() { if (database != IntPtr.Zero) { sqlite3_close_v2(database); database = IntPtr.Zero; } }
    }

    public static partial class BlmDatabaseReader
    {
        public const string ConsentVersion = "unity-local-v3";
        public static string DefaultPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "pm.booth.library-manager", "data.db"); } }
        static string Quote(string value) { return "\"" + value.Replace("\"", "\"\"") + "\""; }
        public static T Retry<T>(Func<T> read, CancellationToken token)
        {
            for (int attempt = 0; ; attempt++)
            {
                token.ThrowIfCancellationRequested();
                try { return read(); }
                catch (Exception ex) when ((ex is BlmBusyException || ex is BlmInitializingException || ex is FileNotFoundException) && attempt < 9)
                { if (token.WaitHandle.WaitOne(2000)) token.ThrowIfCancellationRequested(); }
            }
        }
        static BlmSchema Schema(ReadOnlySqlite db, CancellationToken token)
        {
            var schema = new BlmSchema();
            foreach (var row in db.Query("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name", token))
                schema.tables[row[0]] = db.Query("PRAGMA table_info(" + Quote(row[0]) + ")", token).Select(x => x[1]).ToList();
            return schema;
        }
        public static BlmSchema Inspect(string path, bool consent, CancellationToken token)
        { return Retry(() => { using (var db = new ReadOnlySqlite(path, consent)) return Schema(db, token); }, token); }

        static bool Flag(string text) { var s = Matcher.Normalize(text); return s == "1" || s == "true" || s == "yes"; }
        static bool AgeRestricted(string text)
        {
            var s = Matcher.Normalize(text);
            if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)) return number != 0;
            return Flag(s) || s == "r18" || s == "r-18" || s == "r18g" || s == "r-18g";
        }
        public static string BoothId(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !(uri.Host == "booth.pm" || uri.Host.EndsWith(".booth.pm", StringComparison.OrdinalIgnoreCase))) return "";
            return Regex.Match(uri.AbsolutePath, @"/items/(\d+)(?:/|$)").Groups[1].Value;
        }
        public static List<string> ParseTags(string value)
        {
            value = value.Trim();
            if (!value.StartsWith("[", StringComparison.Ordinal)) return value.Split(new[] { ',', '\n', '\r', ';', '、' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).Distinct().ToList();
            // Accept only a JSON string array. Nested BOOTH Library Manager schemas need a version-specific adapter.
            var result = new List<string>(); int i = 1;
            Skip(value, ref i);
            if (i < value.Length && value[i] == ']') { i++; Skip(value, ref i); if (i != value.Length) throw new InvalidDataException("タグJSONが不正です。"); return result; }
            while (i < value.Length)
            {
                Skip(value, ref i);
                if (i >= value.Length || value[i++] != '"') throw new InvalidDataException("タグ列は文字列配列または区切りテキストを指定してください。");
                var text = new StringBuilder(); bool closed = false;
                while (i < value.Length)
                {
                    char c = value[i++]; if (c == '"') { closed = true; break; }
                    if (c < 32) throw new InvalidDataException("タグJSONが不正です。");
                    if (c == '\\')
                    {
                        if (i >= value.Length) throw new InvalidDataException();
                        c = value[i++];
                        if (c == 'u') { if (i + 4 > value.Length) throw new InvalidDataException(); text.Append((char)Convert.ToInt32(value.Substring(i, 4), 16)); i += 4; continue; }
                        switch (c) { case 'n': c = '\n'; break; case 'r': c = '\r'; break; case 't': c = '\t'; break; case 'b': c = '\b'; break; case 'f': c = '\f'; break; case '"': case '\\': case '/': break; default: throw new InvalidDataException(); }
                    }
                    text.Append(c);
                }
                if (!closed) throw new InvalidDataException();
                result.Add(text.ToString()); Skip(value, ref i);
                if (i < value.Length && value[i] == ']') { i++; Skip(value, ref i); if (i != value.Length) throw new InvalidDataException(); return result.Distinct().ToList(); }
                if (i >= value.Length || value[i++] != ',') throw new InvalidDataException("タグJSONが不正です。");
            }
            throw new InvalidDataException("タグJSONが不正です。");
        }
        static void Skip(string text, ref int i) { while (i < text.Length && char.IsWhiteSpace(text[i])) i++; }
    }
}
