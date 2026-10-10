using System;
using System.IO;
using System.Text;
using System.Threading;

namespace KaihenShelf
{
    // Streaming header checks only: Unity remains responsible for decoding the pixels.
    // Never allocate an image-sized buffer while inspecting a product file.
    public static class TextureImportValidation
    {
        public const int MaxDimension = 16384;
        public const long MaxPixels = 16384L * 16384;
        static void Need(bool valid) { if (!valid) throw new InvalidDataException("画像ヘッダーが不正、または画像データが途中で切れています。"); }
        static void Size(long w, long h)
        {
            Need(w > 0 && h > 0);
            if (w > MaxDimension || h > MaxDimension || w * h > MaxPixels)
                throw new InvalidDataException("画像サイズが自動インポートの上限（縦横16,384px）を超えています。画像を縮小してから取り込んでください。");
        }
        static uint Big(BinaryReader r) => (uint)r.ReadByte() << 24 | (uint)r.ReadByte() << 16 | (uint)r.ReadByte() << 8 | r.ReadByte();
        static void Skip(BinaryReader r, long count)
        { Need(count >= 0 && count <= r.BaseStream.Length - r.BaseStream.Position); r.BaseStream.Seek(count, SeekOrigin.Current); }
        static string CString(BinaryReader r)
        {
            var value = new StringBuilder();
            for (int i = 0; i < 256; i++) { byte b = r.ReadByte(); if (b == 0) return value.ToString(); value.Append((char)b); }
            throw new InvalidDataException("EXRの属性名が長すぎます。");
        }
        public static void Check(string file, string assetName, CancellationToken token = default)
        {
            string extension = Path.GetExtension(assetName).ToLowerInvariant();
            if (extension != ".png" && extension != ".jpg" && extension != ".jpeg" && extension != ".tga" && extension != ".exr") return;
            token.ThrowIfCancellationRequested();
            try
            {
                using (var stream = File.OpenRead(file))
                using (var r = new BinaryReader(stream))
                {
                    if (extension == ".png")
                    {
                        Need(r.ReadUInt64() == 0x0a1a0a0d474e5089UL);
                        bool header = false, pixels = false;
                        for (int chunks = 0; chunks < 100000; chunks++)
                        {
                            token.ThrowIfCancellationRequested();
                            uint length = Big(r); string type = Encoding.ASCII.GetString(r.ReadBytes(4));
                            Need(length <= stream.Length - stream.Position - 4);
                            if (!header) { Need(type == "IHDR" && length == 13); long w = Big(r), h = Big(r); Size(w, h); Skip(r, 5); header = true; }
                            else { Need(type != "IHDR"); Skip(r, length); }
                            r.ReadUInt32(); // CRC/pixel integrity are checked by Unity's decoder.
                            if (type == "IDAT" && length > 0) pixels = true;
                            if (type == "IEND") { Need(length == 0 && pixels); return; }
                        }
                        throw new InvalidDataException("PNGのチャンク数が上限を超えています。");
                    }
                    if (extension == ".jpg" || extension == ".jpeg")
                    {
                        Need(r.ReadByte() == 255 && r.ReadByte() == 216); bool frame = false;
                        while (stream.Position < stream.Length && stream.Position < 16 * 1024 * 1024)
                        {
                            token.ThrowIfCancellationRequested(); Need(r.ReadByte() == 255);
                            int marker; do { marker = r.ReadByte(); } while (marker == 255);
                            Need(marker != 0 && marker != 216 && marker != 217);
                            if (marker == 1 || marker >= 208 && marker <= 215) continue;
                            int length = (r.ReadByte() << 8) | r.ReadByte(); Need(length >= 2);
                            if (marker == 218) { Need(frame && length + 2 <= stream.Length - stream.Position); return; }
                            if (marker >= 192 && marker <= 207 && marker != 196 && marker != 200 && marker != 204)
                            {
                                Need(length >= 8); r.ReadByte(); int h = (r.ReadByte() << 8) | r.ReadByte(), w = (r.ReadByte() << 8) | r.ReadByte();
                                Size(w, h); frame = true; Skip(r, length - 7);
                            }
                            else Skip(r, length - 2);
                        }
                        throw new InvalidDataException("JPEGの画像ヘッダーを読み取れません。");
                    }
                    if (extension == ".tga")
                    {
                        int id = r.ReadByte(), map = r.ReadByte(), type = r.ReadByte(); r.ReadUInt16(); int colors = r.ReadUInt16(), colorBits = r.ReadByte();
                        Skip(r, 4); int w = r.ReadUInt16(), h = r.ReadUInt16(), bits = r.ReadByte(); r.ReadByte();
                        Need(map <= 1 && (type == 1 || type == 2 || type == 3 || type == 9 || type == 10 || type == 11));
                        Need(bits == 8 || bits == 15 || bits == 16 || bits == 24 || bits == 32); Size(w, h);
                        Skip(r, id + (map == 1 ? colors * ((colorBits + 7) / 8) : 0));
                        long pixels = (long)w * h; int bytes = (bits + 7) / 8;
                        if (type < 9) Skip(r, pixels * bytes);
                        else while (pixels > 0)
                        {
                            token.ThrowIfCancellationRequested(); int packet = r.ReadByte(), count = (packet & 127) + 1; Need(count <= pixels);
                            Skip(r, (packet < 128 ? count : 1) * bytes); pixels -= count;
                        }
                        return;
                    }
                    Need(r.ReadUInt32() == 20000630); int version = r.ReadInt32() & 255; Need(version == 1 || version == 2);
                    bool dimensions = false;
                    while (stream.Position < Math.Min(stream.Length, 1024 * 1024))
                    {
                        token.ThrowIfCancellationRequested(); string name = CString(r);
                        if (name.Length == 0) { Need(dimensions && stream.Length - stream.Position >= 8); return; }
                        string type = CString(r); int size = r.ReadInt32(); Need(size >= 0);
                        if (name == "dataWindow")
                        {
                            Need(type == "box2i" && size == 16); long x = r.ReadInt32(), y = r.ReadInt32(), right = r.ReadInt32(), bottom = r.ReadInt32();
                            Size(right - x + 1, bottom - y + 1); dimensions = true;
                        }
                        else Skip(r, size);
                    }
                    throw new InvalidDataException("EXRの画像ヘッダーを読み取れません。");
                }
            }
            catch (InvalidDataException ex) { throw new InvalidDataException(assetName + ": " + ex.Message, ex); }
            catch (EndOfStreamException ex) { throw new InvalidDataException(assetName + ": 画像データが途中で切れています。", ex); }
        }
    }
}
