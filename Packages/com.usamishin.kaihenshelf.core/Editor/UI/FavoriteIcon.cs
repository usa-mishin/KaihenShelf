using System;
using UnityEngine;

namespace KaihenShelf
{
    // A symmetric icon avoids font side bearings moving the visible star off-center.
    sealed class FavoriteIcon : IDisposable
    {
        readonly Texture2D outline = Create(false), filled = Create(true);

        public void Draw(Rect button, bool selected, float size = 16)
        {
            if (Event.current.type != EventType.Repaint) return;
            var rect = new Rect(button.center.x - size / 2, button.center.y - size / 2, size, size);
            Color before = GUI.color;
            try
            {
                GUI.color *= UnityEditor.EditorGUIUtility.isProSkin ? new Color(.85f, .85f, .85f) : new Color(.15f, .15f, .15f);
                GUI.DrawTexture(rect, selected ? filled : outline);
            }
            finally { GUI.color = before; }
        }

        static Texture2D Create(bool solid)
        {
            const int size = 32, samples = 4;
            var points = new Vector2[10];
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < points.Length; i++)
            {
                float angle = Mathf.PI / 2 + i * Mathf.PI / 5;
                points[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (i % 2 == 0 ? 14 : 6.4f);
                minY = Mathf.Min(minY, points[i].y); maxY = Mathf.Max(maxY, points[i].y);
            }
            for (int i = 0; i < points.Length; i++) points[i] += new Vector2(size / 2f, size / 2f - (minY + maxY) / 2);
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size / 2; x++)
            {
                int covered = 0;
                for (int sy = 0; sy < samples; sy++)
                for (int sx = 0; sx < samples; sx++)
                {
                    var p = new Vector2(x + (sx + .5f) / samples, y + (sy + .5f) / samples);
                    bool inside = false; float distance = float.MaxValue;
                    for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
                    {
                        Vector2 a = points[j], b = points[i], edge = b - a;
                        if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
                        Vector2 nearest = a + edge * Mathf.Clamp01(Vector2.Dot(p - a, edge) / edge.sqrMagnitude);
                        distance = Mathf.Min(distance, (p - nearest).sqrMagnitude);
                    }
                    if ((solid && inside) || distance <= 1) covered++;
                }
                var color = new Color32(255, 255, 255, (byte)(covered * 255 / (samples * samples)));
                pixels[y * size + x] = pixels[y * size + size - 1 - x] = color;
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { name = solid ? "Favorite filled" : "Favorite outline", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(pixels); texture.Apply(false, true);
            return texture;
        }

        public void Dispose()
        {
            UnityEngine.Object.DestroyImmediate(outline);
            UnityEngine.Object.DestroyImmediate(filled);
        }
    }
}
