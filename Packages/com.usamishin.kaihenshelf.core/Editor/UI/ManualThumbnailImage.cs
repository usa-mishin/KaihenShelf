using System;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;

namespace KaihenShelf
{
    public static class ManualThumbnailImage
    {
        // Create once on selection; regular cards only load this small, persistent PNG.
        public static string Create(string folder, string path, string destination)
        {
            ManualItemEditing.ValidateThumbnail(folder, path);
            byte[] bytes;
            using (var input = File.OpenRead(path))
            {
                if (input.Length > ManualItemEditing.MaxThumbnailBytes) throw new InvalidDataException("画像は32MB以下を選択してください。");
                bytes = new byte[(int)input.Length];
                int offset = 0;
                while (offset < bytes.Length) { int count = input.Read(bytes, offset, bytes.Length - offset); if (count == 0) throw new EndOfStreamException(); offset += count; }
            }
            if (!ImageHeader.TryReadLocalTexture(bytes, out int width, out int height)) throw new InvalidDataException("画像の形式またはサイズを確認してください。");
            Texture2D source = null, thumbnail = null;
            try
            {
                source = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                if (!ImageConversion.LoadImage(source, bytes, false) || source.width != width || source.height != height)
                    throw new InvalidDataException("画像を読み込めません。");
                float scale = Mathf.Min(1, 256f / Mathf.Max(width, height));
                int w = Mathf.Max(1, Mathf.RoundToInt(width * scale)), h = Mathf.Max(1, Mathf.RoundToInt(height * scale));
                thumbnail = new Texture2D(w, h, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                var pixels = new Color32[w * h];
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                    pixels[y * w + x] = source.GetPixelBilinear((x + .5f) / w, (y + .5f) / h);
                thumbnail.SetPixels32(pixels); thumbnail.Apply();
                byte[] png = thumbnail.EncodeToPNG();
                string name;
                using (var hash = SHA256.Create()) name = BitConverter.ToString(hash.ComputeHash(png)).Replace("-", "").ToLowerInvariant() + ".png";
                Directory.CreateDirectory(destination);
                string output = Path.Combine(destination, name), temporary = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllBytes(temporary, png);
                    if (File.Exists(output)) File.Replace(temporary, output, null);
                    else File.Move(temporary, output);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
                return output;
            }
            finally
            {
                if (source != null) UnityEngine.Object.DestroyImmediate(source);
                if (thumbnail != null) UnityEngine.Object.DestroyImmediate(thumbnail);
            }
        }
    }
}
