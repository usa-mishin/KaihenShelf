using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace KaihenShelf
{
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "VrcKaihenLibrary", "VrcKaihenLibrary.Editor", "FolderThumbnailWindow")]
    public sealed class FolderThumbnailWindow : EditorWindow
    {
        [SerializeField] string folder, search = "", status = "";
        LibraryItem[] items;
        [SerializeField] int selected;
        static LibraryItem[] Items() => LocalStore.Load().items.Where(x => !x.unpurchased && !string.IsNullOrWhiteSpace(x.title) &&
            (!string.IsNullOrEmpty(x.thumbnailPath) || !string.IsNullOrEmpty(x.thumbnailUrl))).OrderBy(x => x.title).ToArray();
        [MenuItem("Assets/KaihenShelf Image/フォルダ画像を設定")]
        static void Open()
        {
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (!AssetDatabase.IsValidFolder(path) || path == "Assets") return;
            var window = GetWindow<FolderThumbnailWindow>(true, "フォルダ画像を設定");
            window.folder = path; window.items = Items(); window.search = ""; window.status = "";
            var assigned = FolderThumbnails.Assignment(path);
            window.selected = Math.Max(0, Array.FindIndex(window.items, x => assigned != null && x.id == assigned.owner));
            var main = EditorGUIUtility.GetMainWindowPosition();
            window.minSize = new Vector2(450, 235); window.position = new Rect(main.center.x - 265, main.center.y - 125, 530, 250);
        }
        [MenuItem("Assets/KaihenShelf Image/フォルダ画像を設定", true)]
        static bool CanOpen() => Selection.activeObject != null && AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(Selection.activeObject)) && AssetDatabase.GetAssetPath(Selection.activeObject) != "Assets";
        void OnGUI()
        {
            if (items == null) items = Items();
            using (new EditorGUILayout.VerticalScope(new GUIStyle { padding = new RectOffset(10, 10, 8, 8) }))
            {
            GUILayout.Label(folder ?? "フォルダを選び直してください", EditorStyles.wordWrappedLabel);
            string next = EditorGUILayout.TextField("商品名・ショップ検索", search);
            if (next != search) { search = next; selected = 0; }
            var matches = items.Where(x => (x.title + " " + x.shop).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
            if (matches.Length > 0) selected = Clickable.Popup("商品", Math.Min(selected, matches.Length - 1), matches.Select(x => x.title + " / " + x.shop).ToArray());
            else GUILayout.Label("該当する商品がありません");
            EditorGUILayout.HelpBox("このフォルダの画像を置き換えます。画像はこのPCのプロジェクト設定に保存し、Assetsには追加しません。", MessageType.Info);
            if (status.Length > 0) GUILayout.Label(status, EditorStyles.wordWrappedLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(folder)))
                    if (Clickable.LayoutButton("画像を解除")) { FolderThumbnails.Remove(folder); status = "画像を解除しました。"; }
                using (new EditorGUI.DisabledScope(matches.Length == 0 || string.IsNullOrEmpty(folder)))
                    if (Clickable.LayoutButton("設定"))
                    {
                        var item = matches[selected];
                        if (string.IsNullOrEmpty(item.thumbnailPath) && string.IsNullOrEmpty(item.thumbnailUrl)) status = "この画像は登録されていません。";
                        else try { FolderThumbnails.Register(folder, item, true); status = "設定しました。画像を取得後に表示します。"; }
                        catch (Exception ex) { status = ex.Message; }
                    }
            }
            }
        }
    }
}
