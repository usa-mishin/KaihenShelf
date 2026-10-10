using System;

namespace KaihenShelf
{
    public static class ImageSourcePolicy
    {
        public static bool TryRemote(string value, out Uri uri)
        {
            uri = null;
            if (string.IsNullOrWhiteSpace(value) || value.Length > 8192 || value != value.Trim()) return false;
            foreach (char c in value) if (char.IsControl(c) || c == '\\') return false;
            if (!Uri.TryCreate(value, UriKind.Absolute, out var candidate) || candidate.Scheme != "https"
                || !candidate.IsDefaultPort || candidate.UserInfo.Length != 0 || candidate.Fragment.Length != 0) return false;
            string host = candidate.DnsSafeHost.ToLowerInvariant();
            if (host != "booth.pximg.net" && host != "booth.pm" && !host.EndsWith(".booth.pm", StringComparison.Ordinal)) return false;
            uri = candidate; return true;
        }
    }

    public static class ImageHeader
    {
        public const int MaxBytes = 8 * 1024 * 1024;
        public const int MaxDimension = 4096;
        public const long MaxPixels = 8L * 1024 * 1024;
        public static bool TryRead(byte[] bytes, out int width, out int height)
        { return TryRead(bytes, MaxBytes, MaxPixels, out width, out height); }
        public static bool TryReadLocalTexture(byte[] bytes, out int width, out int height)
        { return TryRead(bytes, 32 * 1024 * 1024, 4096L * 4096, out width, out height); }
        static bool TryRead(byte[] bytes, int maxBytes, long maxPixels, out int width, out int height)
        {
            width = height = 0;
            if (bytes == null || bytes.Length < 10 || bytes.Length > maxBytes) return false;
            if (bytes.Length >= 33 && bytes[0] == 137 && bytes[1] == 80 && bytes[2] == 78 && bytes[3] == 71
                && bytes[4] == 13 && bytes[5] == 10 && bytes[6] == 26 && bytes[7] == 10)
            {
                if (Big32(bytes, 8) != 13 || bytes[12] != 73 || bytes[13] != 72 || bytes[14] != 68 || bytes[15] != 82) return false;
                uint w = Big32(bytes, 16), h = Big32(bytes, 20);
                if (w > MaxDimension || h > MaxDimension) return false;
                width = (int)w; height = (int)h; return Valid(width, height, maxPixels);
            }
            if (bytes[0] != 255 || bytes[1] != 216) return false;
            int position = 2; bool found = false;
            while (position < bytes.Length)
            {
                if (bytes[position++] != 255) return false;
                while (position < bytes.Length && bytes[position] == 255) position++;
                if (position >= bytes.Length) return false;
                int marker = bytes[position++];
                if (marker == 0 || marker == 216 || marker == 217) return false;
                if (marker == 1 || marker >= 208 && marker <= 215) continue;
                if (position + 2 > bytes.Length) return false;
                int length = (bytes[position] << 8) | bytes[position + 1];
                if (length < 2 || length > bytes.Length - position) return false;
                if (marker == 218) return found;
                bool frame = marker >= 192 && marker <= 207 && marker != 196 && marker != 200 && marker != 204;
                if (frame)
                {
                    if (marker != 192 && marker != 193 && marker != 194 || length < 8 || bytes[position + 2] != 8) return false;
                    int h = (bytes[position + 3] << 8) | bytes[position + 4], w = (bytes[position + 5] << 8) | bytes[position + 6];
                    if (!Valid(w, h, maxPixels) || found && (w != width || h != height)) return false;
                    width = w; height = h; found = true;
                }
                position += length;
            }
            return false;
        }
        static bool Valid(int width, int height, long maxPixels) { return width > 0 && height > 0 && width <= MaxDimension && height <= MaxDimension && (long)width * height <= maxPixels; }
        static uint Big32(byte[] bytes, int at) { return (uint)bytes[at] << 24 | (uint)bytes[at + 1] << 16 | (uint)bytes[at + 2] << 8 | bytes[at + 3]; }
    }
}
