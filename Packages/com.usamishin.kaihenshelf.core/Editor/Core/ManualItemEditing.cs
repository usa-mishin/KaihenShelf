using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KaihenShelf
{
    public static class ManualItemEditing
    {
        public const int MaxThumbnailBytes = 32 * 1024 * 1024;
        public static bool IsThumbnailTexture(string path)
        { return new[] { ".png", ".jpg", ".jpeg" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase); }
        public static string[] Shops(IEnumerable<LibraryItem> items)
        {
            return items.Select(i => i.shop).Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }
        public static void SetShop(LibraryItem item, string shop, IEnumerable<LibraryItem> items)
        {
            item.shop = shop;
            item.shopThumbnailUrl = items.FirstOrDefault(other => other.id != item.id
                && string.Equals(other.shop, shop, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(other.shopThumbnailUrl))?.shopThumbnailUrl ?? "";
        }
        public static string ValidateThumbnail(string folder, string file)
        {
            string root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string path = Path.GetFullPath(file);
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("アイテムの保存フォルダ内の画像を選択してください。");
            if (!IsThumbnailTexture(path))
                throw new InvalidDataException("PNGまたはJPEG画像を選択してください。");
            if (!File.Exists(path)) throw new FileNotFoundException("画像が見つかりません。", path);
            for (string current = path; current != null && current.Length >= root.TrimEnd(Path.DirectorySeparatorChar).Length; current = Path.GetDirectoryName(current))
                if (SafePaths.IsReparse(current)) throw new InvalidDataException("リンク先の画像は選択できません。保存フォルダ内の画像を選択してください。");
            using (var input = File.OpenRead(path))
            {
                if (input.Length > MaxThumbnailBytes) throw new InvalidDataException("画像は32MB以下を選択してください。");
                var bytes = new byte[(int)input.Length];
                int offset = 0;
                while (offset < bytes.Length) { int n = input.Read(bytes, offset, bytes.Length - offset); if (n == 0) throw new EndOfStreamException(); offset += n; }
                if (!ImageHeader.TryReadLocalTexture(bytes, out _, out _)) throw new InvalidDataException("この画像は表示できません。PNG/JPEG、各辺4096px以下の画像を選択してください。");
            }
            return path;
        }
    }
}
