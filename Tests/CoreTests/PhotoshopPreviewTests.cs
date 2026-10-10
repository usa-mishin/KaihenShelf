using KaihenShelf;

internal static class PhotoshopPreviewTests
{
    static readonly byte[] Jpeg = { 255, 216, 255, 192, 0, 11, 8, 0, 16, 0, 32, 1, 1, 0x11, 0, 255, 218, 0, 6, 1, 1, 0, 0 };
    public static void Run(Action<string, Action> test)
    {
        test("PSD and PSB embedded RGB previews preserve JPEG bytes and skip unrelated resources", () =>
        {
            foreach (int version in new[] { 1, 2 })
            foreach (string name in new[] { "", "a", "ab" })
            {
                using var stream = new MemoryStream(Document(version, 1036, name));
                Check(PhotoshopPreview.Read(stream).SequenceEqual(Jpeg));
                Check(stream.Position < stream.Length); // Composite tail remains unread.
            }
        });
        test("PSD previews reject truncated or oversized resources without returning partial images", () =>
        {
            byte[] document = Document(1, 1036, "");
            for (int length = 0; length < document.Length - 100; length++)
                Check(PhotoshopPreview.Read(new MemoryStream(document.Take(length).ToArray())) == null);
            document[30] = document[31] = document[32] = document[33] = 255;
            Check(PhotoshopPreview.Read(new MemoryStream(document)) == null);
        });
        test("PSD previews reject legacy BGR resources and mismatched dimensions", () =>
        {
            Check(PhotoshopPreview.Read(new MemoryStream(Document(1, 1033, ""))) == null);
            Check(PhotoshopPreview.Read(new MemoryStream(Document(1, 1036, "", 33))) == null);
            Check(PhotoshopPreview.Read(new MemoryStream(Document(3, 1036, ""))) == null);
        });
        test("PSD preview cancellation is observed before document reads", () =>
        {
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            try { PhotoshopPreview.Read(new MemoryStream(Document(1, 1036, "")), cancel.Token); }
            catch (OperationCanceledException) { return; }
            throw new Exception("Cancellation ignored");
        });
        test("editable image extensions stay separate from importable textures", () =>
        {
            foreach (string ext in new[] { ".psd", ".PSB", ".clip", ".sai", ".sai2" }) Check(FileCatalog.Kind(ext) == "画像編集データ");
            Check(FileCatalog.Kind(".png") == "テクスチャ");
        });
    }
    static byte[] Document(int version, int id, string name, int width = 32)
    {
        using var resources = new MemoryStream();
        Block(resources, 1000, "skip", new byte[] { 1, 2, 3 });
        using var payload = new MemoryStream();
        U32(payload, 1); U32(payload, (uint)width); U32(payload, 16); U32(payload, 96); U32(payload, 1536); U32(payload, (uint)Jpeg.Length);
        U16(payload, 24); U16(payload, 1); payload.Write(Jpeg);
        Block(resources, id, name, payload.ToArray());
        using var doc = new MemoryStream();
        U32(doc, 0x38425053); U16(doc, version); doc.Write(new byte[20]);
        U32(doc, 0); U32(doc, (uint)resources.Length); doc.Write(resources.ToArray()); doc.Write(new byte[100]);
        return doc.ToArray();
    }
    static void Block(Stream target, int id, string name, byte[] payload)
    {
        U32(target, 0x3842494d); U16(target, id); target.WriteByte((byte)name.Length);
        target.Write(System.Text.Encoding.ASCII.GetBytes(name));
        if ((name.Length + 1) % 2 != 0) target.WriteByte(0);
        U32(target, (uint)payload.Length); target.Write(payload); if (payload.Length % 2 != 0) target.WriteByte(0);
    }
    static void U16(Stream s, int n) { s.WriteByte((byte)(n >> 8)); s.WriteByte((byte)n); }
    static void U32(Stream s, uint n) { for (int i = 24; i >= 0; i -= 8) s.WriteByte((byte)(n >> i)); }
    static void Check(bool ok) { if (!ok) throw new Exception("Photoshop preview assertion failed"); }
}
