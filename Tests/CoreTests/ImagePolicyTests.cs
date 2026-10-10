using KaihenShelf;

internal static class ImagePolicyTests
{
    public static void Run(Action<string, Action> test)
    {
        test("thumbnail URLs allow only HTTPS BOOTH hosts", () =>
        {
            foreach (string url in new[] { "https://booth.pximg.net/c/300x300/image.png", "https://booth.pm/image.jpg", "https://shop.booth.pm/image.png", "https://BOOTH.PXIMG.NET:443/image.png" })
                Check(ImageSourcePolicy.TryRemote(url, out _), url);
            foreach (string url in new[] { "http://booth.pximg.net/image.png", "https://localhost/image.png", "https://127.0.0.1/image.png", "https://[::1]/image.png", "https://booth.pximg.net.attacker.example/a", "https://evilbooth.pm/a", "https://booth.pximg.net@attacker.example/a", "https://attacker@booth.pximg.net/a", "https://booth.pximg.net:8443/a", "https://booth.pximg.net./a", "file:///C:/image.png", "data:image/png;base64,a", "https://booth.pximg.net/a#fragment", " https://booth.pximg.net/a", "https://booth.pximg.net/\na", "https://booth.pximg.net\\@attacker.example/a" })
                Check(!ImageSourcePolicy.TryRemote(url, out _), url);
        });
        test("thumbnail PNG header enforces dimensions pixel and byte limits", () =>
        {
            Check(ImageHeader.TryRead(Png(512, 256), out int w, out int h) && w == 512 && h == 256);
            foreach (var image in new[] { Png(0, 10), Png(50000, 10), Png(4096, 4096), Png(uint.MaxValue, 1), new byte[ImageHeader.MaxBytes + 1], Png(12, 12).Take(25).ToArray() })
                Check(!ImageHeader.TryRead(image, out _, out _));
            var invalid = Png(10, 10); invalid[15] = 0; Check(!ImageHeader.TryRead(invalid, out _, out _));
        });
        test("thumbnail JPEG header handles baseline progressive segments and truncation", () =>
        {
            foreach (byte marker in new byte[] { 0xc0, 0xc2 })
                Check(ImageHeader.TryRead(Jpeg(192, 96, marker), out int w, out int h) && w == 192 && h == 96);
            foreach (var image in new[] { Jpeg(5000, 20, 0xc0), Jpeg(4096, 4096, 0xc0), Jpeg(0, 20, 0xc0), Jpeg(100, 100, 0xc3), Jpeg(100, 100, 0xc0).Take(12).ToArray(), new byte[] { 0xff, 0xd8, 0xff, 0xe0, 0, 0, 0, 0, 0, 0 } })
                Check(!ImageHeader.TryRead(image, out _, out _));
        });
        test("thumbnail unsupported formats and repeated conflicting JPEG frames are rejected", () =>
        {
            Check(!ImageHeader.TryRead(System.Text.Encoding.ASCII.GetBytes("GIF89a123456789"), out _, out _));
            Check(!ImageHeader.TryRead(System.Text.Encoding.ASCII.GetBytes("RIFF1234WEBP1234"), out _, out _));
            var first = Jpeg(100, 100, 0xc0); var second = Jpeg(120, 120, 0xc0);
            var multiple = first.Take(first.Length - 8).Concat(second.Skip(2)).ToArray();
            Check(!ImageHeader.TryRead(multiple, out _, out _));
        });
        test("local texture previews accept 4K without relaxing remote thumbnail limits", () =>
        {
            var png = Png(4096, 4096); var jpg = Jpeg(4096, 4096, 0xc2);
            Check(ImageHeader.TryReadLocalTexture(png, out int w, out int h) && w == 4096 && h == 4096);
            Check(ImageHeader.TryReadLocalTexture(jpg, out _, out _));
            Check(!ImageHeader.TryRead(png, out _, out _) && !ImageHeader.TryRead(jpg, out _, out _));
            Check(!ImageHeader.TryReadLocalTexture(Png(8192, 8192), out _, out _));
            Check(!ImageHeader.TryReadLocalTexture(Png(0, 4096), out _, out _));
            Check(!ImageHeader.TryReadLocalTexture(new byte[32 * 1024 * 1024 + 1], out _, out _));
        });
    }
    static byte[] Png(uint width, uint height)
    {
        var bytes = new byte[33]; Array.Copy(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82 }, bytes, 16);
        for (int i = 0; i < 4; i++) { bytes[16 + i] = (byte)(width >> (24 - 8 * i)); bytes[20 + i] = (byte)(height >> (24 - 8 * i)); }
        bytes[24] = 8; bytes[25] = 6; return bytes;
    }
    static byte[] Jpeg(int width, int height, byte marker)
    {
        return new byte[] { 255, 216, 255, 224, 0, 4, 0, 0, 255, marker, 0, 11, 8, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 1, 1, 0x11, 0, 255, 218, 0, 6, 1, 1, 0, 0 };
    }
    static void Check(bool value, string message = "image policy assertion failed") { if (!value) throw new Exception(message); }
}
