using UnityEditor;
using UnityEngine;

namespace KaihenShelf
{
    // Keep Unity's controls, layout and keyboard behavior; add a cursor only to enabled hit areas.
    static class Clickable
    {
        // Named controls must not inherit another button's hotControl after dynamic content changes.
        public static bool StableButton(Rect rect, int hint, GUIContent content, GUIStyle style = null)
        {
            int id = GUIUtility.GetControlID(hint, FocusType.Keyboard);
            var e = Event.current;
            bool inside = rect.Contains(e.mousePosition);
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (GUI.enabled && inside && e.button == 0) { GUIUtility.hotControl = id; GUIUtility.keyboardControl = id; e.Use(); }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id && e.button == 0) { GUIUtility.hotControl = 0; e.Use(); return GUI.enabled && inside; }
                    break;
                case EventType.KeyDown:
                    if (GUI.enabled && GUIUtility.keyboardControl == id && (e.keyCode == KeyCode.Space || e.keyCode == KeyCode.Return)) { e.Use(); return true; }
                    break;
                case EventType.Repaint:
                    (style ?? GUI.skin.button).Draw(rect, content, inside && GUI.enabled, GUIUtility.hotControl == id, false, GUIUtility.keyboardControl == id);
                    break;
            }
            Cursor(rect);
            return false;
        }
        public static void Cursor(Rect rect)
        {
            if (GUI.enabled && Event.current.type == EventType.Repaint)
                EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
        }
        static void LastCursor()
        { if (GUI.enabled && Event.current.type == EventType.Repaint) Cursor(GUILayoutUtility.GetLastRect()); }
        public static bool Button(Rect rect, string text, GUIStyle style = null)
        { return Button(rect, new GUIContent(text), style); }
        public static bool Button(Rect rect, GUIContent content, GUIStyle style = null)
        { bool result = GUI.Button(rect, content, style ?? GUI.skin.button); Cursor(rect); return result; }
        public static bool LayoutButton(string text, params GUILayoutOption[] options)
        { bool result = GUILayout.Button(text, options); LastCursor(); return result; }
        public static bool LayoutButton(string text, GUIStyle style, params GUILayoutOption[] options)
        { bool result = GUILayout.Button(text, style, options); LastCursor(); return result; }
        public static bool Toggle(Rect rect, bool value, GUIContent content, GUIStyle style)
        { bool result = GUI.Toggle(rect, value, content, style); Cursor(rect); return result; }
        public static bool LayoutToggle(bool value, string text, params GUILayoutOption[] options)
        {
            Rect rect = GUILayoutUtility.GetRect(new GUIContent(text), EditorStyles.toggle, options);
            return CenteredToggle(rect, value, text);
        }
        public static bool LayoutToggle(bool value, string text, string style, params GUILayoutOption[] options)
        { bool result = GUILayout.Toggle(value, text, style, options); LastCursor(); return result; }
        public static bool ToggleLeft(string text, bool value, params GUILayoutOption[] options)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight, options);
            return CenteredToggle(rect, value, text);
        }
        static bool CenteredToggle(Rect rect, bool value, string text)
        {
            // Keep the native toggle's keyboard/hit behavior and center its 18px contents within taller rows.
            Rect content = new Rect(rect.x, rect.center.y - 9, rect.width, 18);
            bool result = EditorGUI.ToggleLeft(content, text, value);
            Cursor(content);
            return result;
        }
        public static bool ToggleField(string text, bool value, params GUILayoutOption[] options)
        {
            bool result = EditorGUILayout.Toggle(text, value, options);
            if (GUI.enabled && Event.current.type == EventType.Repaint)
            {
                Rect rect = GUILayoutUtility.GetLastRect(); rect.xMin += EditorGUIUtility.labelWidth; rect.width = 16;
                Cursor(rect);
            }
            return result;
        }
        public static bool Checkbox(Rect rect, bool value)
        { bool result = EditorGUI.Toggle(rect, value); Cursor(rect); return result; }
        public static int Popup(string label, int index, string[] choices, params GUILayoutOption[] options)
        {
            int result = EditorGUILayout.Popup(label, index, choices, options);
            if (GUI.enabled && Event.current.type == EventType.Repaint)
            {
                Rect rect = GUILayoutUtility.GetLastRect(); rect.xMin += EditorGUIUtility.labelWidth;
                Cursor(rect);
            }
            return result;
        }
        public static int Popup(int index, string[] choices, GUIStyle style, params GUILayoutOption[] options)
        { int result = EditorGUILayout.Popup(index, choices, style, options); LastCursor(); return result; }
        public static bool Foldout(Rect rect, bool value, GUIContent content, bool toggleOnLabelClick)
        {
            bool result = EditorGUI.Foldout(rect, value, content, toggleOnLabelClick);
            if (!toggleOnLabelClick) rect.width = 16;
            Cursor(rect); return result;
        }
    }
}
