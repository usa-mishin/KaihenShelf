using System;
using System.IO;
using System.Threading;

namespace KaihenShelf
{
    // Adobe PSD/PSB image resource 1036: 28-byte header followed by an RGB JPEG.
    // Seek past layer/composite data; never import or load the original document.
    public static class PhotoshopPreview
    {
        public static byte[] Read(Stream input, CancellationToken token = default)
        {
            if (!input.CanRead || !input.CanSeek) return null;
            try
            {
                token.ThrowIfCancellationRequested();
                input.Position = 0;
                var header = Bytes(input, 26, input.Length, token);
                if (U32(header, 0) != 0x38425053 || (U16(header, 4) != 1 && U16(header, 4) != 2)) return null;
                Skip(input, U32(Bytes(input, 4, input.Length, token), 0), input.Length);
                long size = U32(Bytes(input, 4, input.Length, token), 0);
                long end = input.Position + size;
                if (end > input.Length) return null;
                int blocks = 0;
                while (input.Position < end && ++blocks <= 4096)
                {
                    token.ThrowIfCancellationRequested();
                    var block = Bytes(input, 7, end, token);
                    if (U32(block, 0) != 0x3842494d) return null;
                    int id = U16(block, 4), nameLength = block[6];
                    Skip(input, nameLength + ((nameLength + 1) & 1), end);
                    long length = U32(Bytes(input, 4, end, token), 0);
                    long start = input.Position;
                    if (length > end - start || (length & 1) > end - start - length) return null;
                    if (id == 1036 && length >= 28 && length <= ImageHeader.MaxBytes + 28)
                    {
                        var info = Bytes(input, 28, start + length, token);
                        uint compressed = U32(info, 20);
                        if (U32(info, 0) == 1 && U16(info, 24) == 24 && U16(info, 26) == 1
                            && compressed > 0 && compressed <= ImageHeader.MaxBytes && compressed <= length - 28)
                        {
                            var jpeg = Bytes(input, (int)compressed, start + length, token);
                            if (jpeg.Length >= 2 && jpeg[0] == 255 && jpeg[1] == 216
                                && ImageHeader.TryRead(jpeg, out int width, out int height)
                                && width == U32(info, 4) && height == U32(info, 8)) return jpeg;
                        }
                    }
                    input.Position = start + length + (length & 1);
                }
            }
            catch (IOException) { }
            return null;
        }
        static void Skip(Stream input, long count, long end)
        {
            if (count > end - input.Position) throw new EndOfStreamException();
            input.Position += count;
        }
        static byte[] Bytes(Stream input, int count, long end, CancellationToken token)
        {
            if (count > end - input.Position) throw new EndOfStreamException();
            var bytes = new byte[count]; int offset = 0;
            while (offset < count)
            {
                token.ThrowIfCancellationRequested();
                int read = input.Read(bytes, offset, count - offset);
                if (read == 0) throw new EndOfStreamException();
                offset += read;
            }
            return bytes;
        }
        static ushort U16(byte[] b, int i) { return (ushort)((b[i] << 8) | b[i + 1]); }
        static uint U32(byte[] b, int i) { return ((uint)b[i] << 24) | ((uint)b[i + 1] << 16) | ((uint)b[i + 2] << 8) | b[i + 3]; }
    }
}
