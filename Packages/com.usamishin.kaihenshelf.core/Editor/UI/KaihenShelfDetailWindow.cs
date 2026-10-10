using System.Linq;
using UnityEditor;
using UnityEngine;

namespace KaihenShelf
{
    // Share the library's catalog, edits and background work instead of loading a second writable state.
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "VrcKaihenLibrary", "VrcKaihenLibrary.Editor", "VrcKaihenLibraryDetailWindow")]
    public sealed class KaihenShelfDetailWindow : EditorWindow
    {
        [SerializeField] KaihenShelfWindow library;
        [SerializeField] string itemId = "";
        [SerializeField] bool initialHeight600Applied;
        const float DefaultHeight = 600;
        GUIStyle contentStyle;

        internal static KaihenShelfDetailWindow ShowFor(KaihenShelfWindow owner, string selectedItemId)
        {
            var window = Resources.FindObjectsOfTypeAll<KaihenShelfDetailWindow>().FirstOrDefault();
            bool created = window == null;
            if (window == null)
            {
                window = CreateInstance<KaihenShelfDetailWindow>();
            }
            window.library = owner;
            window.itemId = selectedItemId;
            window.Show();
            if (created)
            {
                Rect parent = owner.position;
                window.position = new Rect(parent.x + Mathf.Max(0, parent.width - 980), parent.y + 30, 960, DefaultHeight);
            }
            window.Focus();
            window.Repaint();
            return window;
        }

        void OnEnable()
        {
            titleContent = new GUIContent("商品詳細");
            minSize = new Vector2(940, 360);
            // Wait until Unity has restored its saved window layout after a domain reload.
            if (!initialHeight600Applied) EditorApplication.delayCall += ApplyInitialHeight;
            wantsMouseMove = true;
            wantsMouseEnterLeaveWindow = true;
            contentStyle = null;
        }

        void ApplyInitialHeight()
        {
            if (this == null || initialHeight600Applied) return;
            initialHeight600Applied = true;
            if (!docked && !Mathf.Approximately(position.height, DefaultHeight))
                position = new Rect(position.x, position.y, position.width, DefaultHeight);
        }

        void OnGUI()
        {
            if (Event.current.type == EventType.MouseMove || Event.current.type == EventType.MouseLeaveWindow) Repaint();
            if (contentStyle == null)
                contentStyle = new GUIStyle { padding = new RectOffset(8, 8, 8, 8), margin = new RectOffset() };
            using (new EditorGUILayout.VerticalScope(contentStyle, GUILayout.ExpandHeight(true)))
            {
                if (library == null)
                {
                    EditorGUILayout.HelpBox("ライブラリ一覧を開くと、商品詳細の表示を再開できます。", MessageType.Info);
                    if (Clickable.LayoutButton("ライブラリを開く"))
                    {
                        KaihenShelfWindow.Open();
                        library = GetWindow<KaihenShelfWindow>();
                        library.RestoreDetail(itemId);
                    }
                    return;
                }
                library.DrawDetailWindow(position.width, position.height);
            }
        }

        void OnDestroy()
        {
            if (library != null) library.DetailClosed(this);
        }
    }
}
