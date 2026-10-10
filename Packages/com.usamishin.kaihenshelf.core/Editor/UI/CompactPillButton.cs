using UnityEditor;
using UnityEngine;

namespace KaihenShelf
{
    static class CompactPillButton
    {
        static GUIStyle style;
        public static bool Draw(string text, int controlHint)
        {
            if (style == null) style = new GUIStyle(EditorStyles.miniLabel) {
                fontSize = 11, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(8, 8, 1, 1), margin = new RectOffset()
            };
            var content = new GUIContent(text);
            Rect rect = GUILayoutUtility.GetRect(style.CalcSize(content).x, 20, GUILayout.ExpandWidth(false));
            Clickable.Cursor(rect);
            if (Event.current.type == EventType.Repaint)
            {
                bool dark = EditorGUIUtility.isProSkin, hover = rect.Contains(Event.current.mousePosition);
                Color fill = dark ? new Color(.20f, .20f, .20f) : new Color(.78f, .78f, .78f);
                if (hover) fill = dark ? new Color(.27f, .27f, .27f) : new Color(.85f, .85f, .85f);
                GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, fill, 0, rect.height / 2);
                GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0,
                    dark ? new Color(.32f, .32f, .32f) : new Color(.68f, .68f, .68f), 1, rect.height / 2);
                style.normal.textColor = dark ? new Color(.78f, .78f, .78f) : new Color(.25f, .25f, .25f);
                GUI.Label(rect, content, style);
            }
            return Clickable.StableButton(rect, controlHint, GUIContent.none, GUIStyle.none);
        }
    }
}
