using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore.Text;

namespace KaihenShelf
{
    // IMGUI's legacy text renderer drops supplementary Unicode. Draw those glyphs
    // through TextCore without normalizing or replacing the stored product name.
    sealed class ProductText : IDisposable
    {
        readonly List<FontAsset> assets = new List<FontAsset>();
        readonly Dictionary<uint, Character> characters = new Dictionary<uint, Character>();
        readonly Dictionary<Texture2D, Texture2D> bitmaps = new Dictionary<Texture2D, Texture2D>();
        readonly HashSet<Texture2D> dirty = new HashSet<Texture2D>();
        public ProductText()
        {
            var installed = new HashSet<string>(Font.GetOSInstalledFontNames(), StringComparer.OrdinalIgnoreCase);
            foreach (string family in new[] { "Cambria Math", "Segoe UI Symbol", "Segoe UI Emoji" })
            {
                if (!installed.Contains(family)) continue;
                var font = FontAsset.CreateFontAsset(family, "Regular", 48);
                if (font == null) continue;
                font.name = "Product text " + family;
                font.hideFlags = HideFlags.HideAndDontSave;
                assets.Add(font);
            }
        }
        Character Get(uint code)
        {
            if (characters.TryGetValue(code, out var found)) return found;
            foreach (var asset in assets)
            {
                asset.TryAddCharacters(new[] { code });
                if (asset.characterLookupTable.TryGetValue(code, out found))
                { dirty.Add(asset.atlasTextures[found.glyph.atlasIndex]); break; }
            }
            characters[code] = found;
            return found;
        }
        public float Width(string text, GUIStyle style)
        {
            float width = 0;
            Walk(text, (run, code) =>
            {
                var ch = code == 0 ? null : Get(code);
                width += code == 0 ? style.CalcSize(new GUIContent(run)).x : ch != null ? ch.glyph.metrics.horizontalAdvance * style.fontSize / 48f : style.fontSize;
            });
            return width;
        }
        public void Draw(Rect rect, string text, GUIStyle style)
        {
            if (Event.current.type != EventType.Repaint) return;
            GUI.BeginGroup(rect);
            try
            {
            float x = 0;
            Walk(text, (run, code) =>
            {
                if (code == 0)
                {
                    float width = style.CalcSize(new GUIContent(run)).x;
                    GUI.Label(new Rect(x, 0, width + 2, rect.height), run, style); x += width;
                    return;
                }
                var ch = Get(code);
                if (ch == null) { GUI.Label(new Rect(x, 0, style.fontSize, rect.height), "□", style); x += style.fontSize; return; }
                float scale = style.fontSize / 48f;
                var glyph = ch.glyph; var metrics = glyph.metrics; var gr = glyph.glyphRect;
                var asset = (FontAsset)ch.textAsset;
                var texture = Bitmap(asset.atlasTextures[glyph.atlasIndex]);
                Color before = GUI.color;
                try
                {
                GUI.color *= UnityEditor.EditorGUIUtility.isProSkin ? new Color(.82f, .82f, .82f) : new Color(.1f, .1f, .1f);
                GUI.DrawTextureWithTexCoords(new Rect(x + metrics.horizontalBearingX * scale,
                    style.fontSize - metrics.horizontalBearingY * scale, gr.width * scale, gr.height * scale), texture,
                    new Rect((float)gr.x / texture.width, (float)gr.y / texture.height, (float)gr.width / texture.width, (float)gr.height / texture.height));
                }
                finally { GUI.color = before; }
                x += metrics.horizontalAdvance * scale;
            });
            }
            finally { GUI.EndGroup(); }
        }
        Texture2D Bitmap(Texture2D atlas)
        {
            if (!bitmaps.TryGetValue(atlas, out var bitmap))
            {
                bitmap = new Texture2D(atlas.width, atlas.height, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                bitmaps.Add(atlas, bitmap); dirty.Add(atlas);
            }
            if (dirty.Remove(atlas))
            {
                var pixels = atlas.GetPixels32();
                // Convert the signed distance field to white coverage for IMGUI's texture shader.
                for (int i = 0; i < pixels.Length; i++)
                    pixels[i] = new Color32(255, 255, 255, (byte)Mathf.Clamp((pixels[i].a - 112) * 255 / 32, 0, 255));
                bitmap.SetPixels32(pixels); bitmap.Apply(false, false);
            }
            return bitmap;
        }
        static void Walk(string text, Action<string, uint> draw)
        {
            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (!char.IsSurrogatePair(text, i)) continue;
                if (i > start) draw(text.Substring(start, i - start), 0);
                draw(null, (uint)char.ConvertToUtf32(text, i)); i++; start = i + 1;
            }
            if (start < text.Length) draw(text.Substring(start), 0);
        }
        public void Dispose()
        {
            foreach (var bitmap in bitmaps.Values) UnityEngine.Object.DestroyImmediate(bitmap);
            bitmaps.Clear(); dirty.Clear();
            foreach (var asset in assets)
            {
                foreach (var texture in asset.atlasTextures) if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
                if (asset.material != null) UnityEngine.Object.DestroyImmediate(asset.material);
                UnityEngine.Object.DestroyImmediate(asset);
            }
            assets.Clear(); characters.Clear();
        }
    }
}
