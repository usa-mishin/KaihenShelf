using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace KaihenShelf
{
    // Monochrome line icons, generated once with antialiasing and tinted for the editor theme.
    sealed class DetailActionIcons : IDisposable
    {
        readonly Texture2D globe = Create(0), pencil = Create(1), avatar = Create(4), tag = Create(3), download = Create(5);
        readonly Texture2D search = Create(2), trash = Create(21);
        readonly Texture2D expand = Create(11), collapse = Create(12);
        readonly Texture2D[] fileTabs = { Create(6), Create(7), Create(8), Create(9), Create(10) };
        readonly Texture2D[] navigation = { Create(4), Create(13), Create(14), Create(3), Create(20), Create(16), Create(17), Create(18), Create(19) };

        public void DrawNavigation(Rect rect, int index)
        {
            if (Event.current.type != EventType.Repaint) return;
            var before = GUI.color;
            try
            {
                GUI.color *= EditorGUIUtility.isProSkin ? new Color(.85f, .85f, .85f) : new Color(.2f, .2f, .2f);
                GUI.DrawTexture(rect, navigation[index], ScaleMode.ScaleToFit);
            }
            finally { GUI.color = before; }
        }

        public void DrawFileTab(Rect rect, int index)
        {
            if (Event.current.type != EventType.Repaint) return;
            var before = GUI.color;
            try
            {
                GUI.color *= EditorGUIUtility.isProSkin ? new Color(.85f, .85f, .85f) : new Color(.2f, .2f, .2f);
                GUI.DrawTexture(rect, fileTabs[index], ScaleMode.ScaleToFit);
            }
            finally { GUI.color = before; }
        }

        public void DrawSearch(Rect rect)
        {
            if (Event.current.type != EventType.Repaint) return;
            var before = GUI.color;
            try
            {
                GUI.color *= EditorGUIUtility.isProSkin ? new Color(.85f, .85f, .85f) : new Color(.2f, .2f, .2f);
                GUI.DrawTexture(rect, search, ScaleMode.ScaleToFit);
            }
            finally { GUI.color = before; }
        }
        public void DrawExpand(Rect rect, bool open)
        {
            if (Event.current.type != EventType.Repaint) return;
            var before = GUI.color;
            try
            {
                GUI.color *= EditorGUIUtility.isProSkin ? new Color(.85f, .85f, .85f) : new Color(.2f, .2f, .2f);
                GUI.DrawTexture(rect, open ? expand : collapse, ScaleMode.ScaleToFit);
            }
            finally { GUI.color = before; }
        }

        public void DrawDelete(Rect rect)
        {
            if (Event.current.type != EventType.Repaint) return;
            var before = GUI.color;
            try
            {
                GUI.color *= EditorGUIUtility.isProSkin ? new Color(.85f, .85f, .85f) : new Color(.2f, .2f, .2f);
                GUI.DrawTexture(rect, trash, ScaleMode.ScaleToFit);
            }
            finally { GUI.color = before; }
        }

        public void DrawDownload(Rect rect)
        {
            if (Event.current.type != EventType.Repaint) return;
            var before = GUI.color;
            try { GUI.color = Color.white; GUI.DrawTexture(rect, download, ScaleMode.ScaleToFit); }
            finally { GUI.color = before; }
        }

        public void DrawTag(Rect rect)
        {
            if (Event.current.type != EventType.Repaint) return;
            var before = GUI.color;
            try { GUI.color = Color.white; GUI.DrawTexture(rect, tag, ScaleMode.ScaleToFit); }
            finally { GUI.color = before; }
        }

        public void DrawAvatar(Rect rect)
        {
            if (Event.current.type != EventType.Repaint) return;
            var before = GUI.color;
            try { GUI.color = Color.white; GUI.DrawTexture(rect, avatar, ScaleMode.ScaleToFit); }
            finally { GUI.color = before; }
        }

        public void Draw(Rect rect, bool edit)
        {
            if (Event.current.type != EventType.Repaint) return;
            var before = GUI.color;
            try
            {
                GUI.color *= EditorGUIUtility.isProSkin ? new Color(.85f, .85f, .85f) : new Color(.2f, .2f, .2f);
                GUI.DrawTexture(rect, edit ? pencil : globe, ScaleMode.ScaleToFit);
            }
            finally { GUI.color = before; }
        }

        static Texture2D Create(int kind)
        {
            var segments = new List<Vector2>();
            void Line(float x1, float y1, float x2, float y2)
            { segments.Add(new Vector2(x1, y1)); segments.Add(new Vector2(x2, y2)); }
            void Ellipse(float rx, float ry, float cx = 16, float cy = 16)
            {
                for (int i = 0; i < 48; i++)
                {
                    float a = i * Mathf.PI * 2 / 48, b = (i + 1) * Mathf.PI * 2 / 48;
                    Line(cx + Mathf.Cos(a) * rx, cy + Mathf.Sin(a) * ry, cx + Mathf.Cos(b) * rx, cy + Mathf.Sin(b) * ry);
                }
            }
            if (kind == 21) // Flat trash can with single-color strokes, matching the other actions.
            {
                Line(5, 24, 27, 24);
                Line(12, 24, 12, 28); Line(12, 28, 20, 28); Line(20, 28, 20, 24);
                Line(8, 21, 10, 4); Line(10, 4, 22, 4); Line(22, 4, 24, 21);
                Line(13, 19, 14, 8); Line(19, 19, 18, 8);
            }
            else if (kind == 13) // Library shelves, matching the desktop app's library symbol.
            {
                foreach (float x in new[] { 4f, 12f, 20f })
                {
                    Line(x, 5, x, 27); Line(x, 27, x + 6, 27); Line(x + 6, 27, x + 6, 5); Line(x + 6, 5, x, 5);
                    Line(x, 10, x + 6, 10);
                }
            }
            else if (kind == 14) // Shopping bag.
            {
                Line(5, 23, 27, 23); Line(27, 23, 27, 4); Line(27, 4, 5, 4); Line(5, 4, 5, 23);
                Line(11, 20, 11, 27); Line(11, 27, 21, 27); Line(21, 27, 21, 20);
            }
            else if (kind == 15) // Folder.
            {
                Line(3, 5, 29, 5); Line(29, 5, 29, 24); Line(29, 24, 15, 24);
                Line(15, 24, 12, 28); Line(12, 28, 3, 28); Line(3, 28, 3, 5); Line(3, 20, 29, 20);
            }
            else if (kind == 16) // Gear.
            {
                Ellipse(5, 5);
                for (int i = 0; i < 48; i++)
                {
                    float a = i * Mathf.PI * 2 / 48, b = (i + 1) * Mathf.PI * 2 / 48;
                    float ra = i % 6 < 3 ? 13 : 10, rb = (i + 1) % 6 < 3 ? 13 : 10;
                    Line(16 + Mathf.Cos(a) * ra, 16 + Mathf.Sin(a) * ra, 16 + Mathf.Cos(b) * rb, 16 + Mathf.Sin(b) * rb);
                }
            }
            else if (kind == 17) // History.
            {
                for (int i = 0; i < 40; i++)
                {
                    float a = -.7f + i * 5.3f / 40, b = -.7f + (i + 1) * 5.3f / 40;
                    Line(16 + Mathf.Cos(a) * 12, 16 + Mathf.Sin(a) * 12, 16 + Mathf.Cos(b) * 12, 16 + Mathf.Sin(b) * 12);
                }
                Line(3, 21, 3, 28); Line(3, 21, 10, 21); Line(16, 24, 16, 16); Line(16, 16, 22, 12);
            }
            else if (kind == 18) // Open guide, following the desktop app's path icon.
            {
                Line(4, 29, 16, 22); Line(16, 22, 28, 29); Line(28, 29, 28, 13);
                Line(28, 13, 16, 3); Line(16, 3, 4, 13); Line(4, 13, 4, 29); Line(16, 22, 16, 3);
            }
            else if (kind == 20) // Classification branches.
            {
                Line(4, 25, 12, 25); Line(12, 25, 12, 17); Line(12, 17, 4, 17); Line(4, 17, 4, 25);
                Line(8, 17, 8, 8); Line(8, 8, 20, 8); Line(12, 21, 20, 21);
                Line(20, 25, 28, 25); Line(28, 25, 28, 17); Line(28, 17, 20, 17); Line(20, 17, 20, 25);
                Line(20, 12, 28, 12); Line(28, 12, 28, 4); Line(28, 4, 20, 4); Line(20, 4, 20, 12);
            }
            else if (kind == 19) // Shield.
            {
                Line(16, 29, 28, 24); Line(28, 24, 27, 12); Line(27, 12, 23, 7);
                Line(23, 7, 16, 3); Line(16, 3, 9, 7); Line(9, 7, 5, 12); Line(5, 12, 4, 24); Line(4, 24, 16, 29);
            }
            else if (kind == 11 || kind == 12)
            {
                Line(3, 24, 24, 24); Line(24, 24, 24, 3); Line(24, 3, 3, 3); Line(3, 3, 3, 24);
                Line(8, 29, 29, 29); Line(29, 29, 29, 8);
                Line(8, 13.5f, 19, 13.5f);
                if (kind == 11) Line(13.5f, 8, 13.5f, 19);
            }
            else if (kind == 6 || kind == 9)
            {
                Line(4, 23, 16, 29); Line(16, 29, 28, 23); Line(28, 23, 28, 9);
                Line(28, 9, 16, 3); Line(16, 3, 4, 9); Line(4, 9, 4, 23);
                Line(4, 23, 16, 17); Line(16, 17, 28, 23); Line(16, 17, 16, 3);
                if (kind == 6) { Line(10, 26, 22, 20); Line(22, 20, 22, 14); }
            }
            else if (kind == 7)
            {
                Line(4, 27, 28, 27); Line(28, 27, 28, 5); Line(28, 5, 4, 5); Line(4, 5, 4, 27);
                Ellipse(2.5f, 2.5f, 11, 20);
                Line(5, 7, 13, 15); Line(13, 15, 18, 10); Line(18, 10, 23, 17); Line(23, 17, 28, 11);
            }
            else if (kind == 8)
            {
                // Flat paintbrush: tapered handle, ferrule and a pointed bristle tuft.
                Line(14, 13, 25, 28); Line(25, 28, 29, 24); Line(29, 24, 18, 10); Line(18, 10, 14, 13);
                Line(16, 16, 21, 12);
                Line(14, 13, 9, 12); Line(9, 12, 7, 6); Line(7, 6, 3, 4);
                Line(3, 4, 11, 3); Line(11, 3, 16, 6); Line(16, 6, 18, 10);
            }
            else if (kind == 10)
            {
                Ellipse(2, 2, 6, 16); Ellipse(2, 2, 16, 16); Ellipse(2, 2, 26, 16);
            }
            else if (kind == 5)
            {
                Line(16, 28, 16, 10); Line(9, 17, 16, 10); Line(16, 10, 23, 17);
                Line(5, 10, 5, 4); Line(5, 4, 27, 4); Line(27, 4, 27, 10);
            }
            else if (kind == 4)
            {
                Ellipse(5, 5, 16, 23);
                for (int i = 0; i < 24; i++)
                {
                    float a = i * Mathf.PI / 24, b = (i + 1) * Mathf.PI / 24;
                    Line(16 + Mathf.Cos(a) * 11, 7 + Mathf.Sin(a) * 7, 16 + Mathf.Cos(b) * 11, 7 + Mathf.Sin(b) * 7);
                }
                Line(5, 7, 5, 4); Line(5, 4, 27, 4); Line(27, 4, 27, 7);
            }
            else if (kind == 3)
            {
                Line(4, 28, 17, 28); Line(17, 28, 29, 16); Line(29, 16, 16, 3);
                Line(16, 3, 4, 15); Line(4, 15, 4, 28);
                Line(9, 23, 12, 23); Line(12, 23, 12, 20); Line(12, 20, 9, 20); Line(9, 20, 9, 23);
            }
            else if (kind == 2)
            {
                Ellipse(8, 8, 13, 19);
                Line(19, 13, 28, 4);
            }
            else if (kind == 1)
            {
                Line(6, 6, 8, 14); Line(8, 14, 23, 29); Line(23, 29, 29, 23);
                Line(29, 23, 14, 8); Line(14, 8, 6, 6); Line(8, 14, 14, 8);
                Line(20, 26, 26, 20); Line(11, 11, 23, 23); Line(5, 3, 28, 3);
            }
            else
            {
                Ellipse(12, 12); Ellipse(5, 12);
                Line(4, 16, 28, 16); Line(5.6f, 10, 26.4f, 10); Line(5.6f, 22, 26.4f, 22);
            }
            const int size = 32, samples = 4;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                int covered = 0;
                for (int sy = 0; sy < samples; sy++) for (int sx = 0; sx < samples; sx++)
                {
                    var p = new Vector2(x + (sx + .5f) / samples, y + (sy + .5f) / samples);
                    for (int i = 0; i < segments.Count; i += 2)
                    {
                        var a = segments[i]; var edge = segments[i + 1] - a;
                        var nearest = a + edge * Mathf.Clamp01(Vector2.Dot(p - a, edge) / edge.sqrMagnitude);
                        if ((p - nearest).sqrMagnitude <= 1) { covered++; break; }
                    }
                }
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(covered * 255 / (samples * samples)));
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) {
                name = kind >= 6 ? "Flat file tab " + (kind - 6) : kind == 5 ? "Flat download" : kind == 4 ? "Flat avatar" : kind == 3 ? "Flat tag" : kind == 2 ? "Flat search" : kind == 1 ? "Flat edit" : "Flat globe", hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixels32(pixels); texture.Apply(false, true);
            return texture;
        }

        public void Dispose() { UnityEngine.Object.DestroyImmediate(globe); UnityEngine.Object.DestroyImmediate(pencil); UnityEngine.Object.DestroyImmediate(avatar); UnityEngine.Object.DestroyImmediate(tag); UnityEngine.Object.DestroyImmediate(download); UnityEngine.Object.DestroyImmediate(search); UnityEngine.Object.DestroyImmediate(trash); UnityEngine.Object.DestroyImmediate(expand); UnityEngine.Object.DestroyImmediate(collapse); foreach (var texture in fileTabs) UnityEngine.Object.DestroyImmediate(texture); foreach (var texture in navigation) UnityEngine.Object.DestroyImmediate(texture); }
    }
}
