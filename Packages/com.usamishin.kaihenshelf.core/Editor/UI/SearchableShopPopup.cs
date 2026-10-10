using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace KaihenShelf
{
    sealed class SearchableShopPopup : PopupWindowContent
    {
        readonly string[] shops;
        readonly string current;
        readonly Action<string> selected;
        readonly float width;
        string search = "";
        Vector2 scroll;
        bool focus = true;
        int active = -1;
        public SearchableShopPopup(string[] shops, string current, float width, Action<string> selected)
        { this.shops = shops; this.current = current; this.width = Mathf.Max(260, width); this.selected = selected; }
        public override Vector2 GetWindowSize() { return new Vector2(width, 320); }
        public override void OnOpen() { editorWindow.wantsMouseMove = true; }
        void Select(string shop) { selected(shop); editorWindow.Close(); GUIUtility.ExitGUI(); }
        public override void OnGUI(Rect rect)
        {
            var e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { editorWindow.Close(); return; }
            GUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(6);
                GUI.SetNextControlName("ShopSearch");
                string next = EditorGUILayout.TextField(search, GUI.skin.FindStyle("ToolbarSearchTextField") ?? EditorStyles.textField);
                if (next != search) { search = next; scroll = Vector2.zero; active = -1; }
                GUILayout.Space(6);
            }
            if (focus) { EditorGUI.FocusTextInControl("ShopSearch"); focus = false; }
            var matches = shops.Where(s => Matcher.Normalize(s).Contains(Matcher.Normalize(search))).ToArray();
            if (e.type == EventType.KeyDown && matches.Length > 0)
            {
                if (e.keyCode == KeyCode.DownArrow || e.keyCode == KeyCode.UpArrow)
                {
                    active = Mathf.Clamp(active + (e.keyCode == KeyCode.DownArrow ? 1 : -1), 0, matches.Length - 1);
                    scroll.y = Mathf.Clamp(scroll.y, Mathf.Max(0, (active + 1) * 23 - 278), active * 23);
                    e.Use(); editorWindow.Repaint();
                }
                else if (e.keyCode == KeyCode.Return && active >= 0) Select(matches[active]);
            }
            GUILayout.Space(6);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            var style = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleLeft, padding = new RectOffset(24, 8, 0, 0) };
            for (int i = 0; i < matches.Length; i++)
            {
                Rect row = GUILayoutUtility.GetRect(0, 23, GUILayout.ExpandWidth(true));
                if (e.type == EventType.MouseMove && row.Contains(e.mousePosition)) { active = i; editorWindow.Repaint(); }
                if (i == active) EditorGUI.DrawRect(row, new Color(.24f, .43f, .65f));
                style.normal.textColor = i == active ? Color.white : EditorStyles.label.normal.textColor;
                if (string.Equals(current, matches[i], StringComparison.OrdinalIgnoreCase))
                    GUI.Label(new Rect(row.x + 5, row.y, 18, row.height), "✓", EditorStyles.label);
                if (Clickable.Button(row, new GUIContent(matches[i], matches[i]), style)) Select(matches[i]);
            }
            if (matches.Length == 0) GUILayout.Label("該当するショップがありません。", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndScrollView();
        }
    }
}
