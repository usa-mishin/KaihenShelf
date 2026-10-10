using System.Linq;
using UnityEditor;
using UnityEngine;

namespace KaihenShelf
{
    // Keep a stable item ID; selecting another detail must not retarget an open editor.
    // All edits use the owner's state and its existing save lifecycle.
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "VrcKaihenLibrary", "VrcKaihenLibrary.Editor", "VrcKaihenLibraryEditWindow")]
    public sealed class KaihenShelfEditWindow : EditorWindow
    {
        [SerializeField] KaihenShelfWindow library;
        [SerializeField] string itemId = "";
        [SerializeField] Vector2 scroll;
        GUIStyle contentStyle;

        internal static KaihenShelfEditWindow ShowFor(KaihenShelfWindow owner, string id, string title)
        {
            var window = Resources.FindObjectsOfTypeAll<KaihenShelfEditWindow>().FirstOrDefault();
            bool created = window == null;
            if (created) window = CreateInstance<KaihenShelfEditWindow>();
            if (window.itemId != id) window.scroll = Vector2.zero;
            window.library = owner;
            window.itemId = id;
            window.titleContent = new GUIContent(title);
            window.Show();
            if (created) window.position = new Rect(owner.position.x + 140, owner.position.y + 50, 520, 580);
            window.Focus();
            window.Repaint();
            return window;
        }

        void OnEnable() { minSize = new Vector2(420, 300); contentStyle = null; }

        void OnGUI()
        {
            if (contentStyle == null) contentStyle = new GUIStyle { padding = new RectOffset(12, 12, 12, 12) };
            using (new EditorGUILayout.VerticalScope(contentStyle))
            {
                if (library == null)
                {
                    EditorGUILayout.HelpBox("ライブラリを開いて編集を再開してください。", MessageType.Info);
                    if (Clickable.LayoutButton("ライブラリを開く")) { KaihenShelfWindow.Open(); library = GetWindow<KaihenShelfWindow>(); }
                    return;
                }
                scroll = EditorGUILayout.BeginScrollView(scroll);
                string title = library.DrawEditWindow(itemId);
                if (titleContent.text != title) titleContent = new GUIContent(title);
                EditorGUILayout.EndScrollView();
                GUILayout.Space(8);
                GUILayout.Label("変更は自動で保存されます。", EditorStyles.miniLabel);
                if (Clickable.LayoutButton("閉じる", GUILayout.Height(28)))
                {
                    // Finish the click in this window before destroying its GUIView.
                    EditorApplication.delayCall -= CloseAfterGUI;
                    EditorApplication.delayCall += CloseAfterGUI;
                    GUIUtility.ExitGUI();
                }
            }
        }

        void CloseAfterGUI() { if (this != null) Close(); }
        void OnDisable() { EditorApplication.delayCall -= CloseAfterGUI; }
        void OnDestroy() { if (library != null) library.EditClosed(this); }
    }
}
