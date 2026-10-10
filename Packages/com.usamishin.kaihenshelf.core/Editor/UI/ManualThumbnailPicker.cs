using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace KaihenShelf
{
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "VrcKaihenLibrary", "VrcKaihenLibrary.Editor", "ManualThumbnailPicker")]
    sealed class ManualThumbnailPicker : EditorWindow
    {
        string folder, search = "", error = "";
        CatalogFile chosen;
        CatalogFile[] files;
        Task<FileScan> scan;
        CancellationTokenSource cancellation;
        Action<string> selected;
        ThumbnailCache previews;
        Vector2 scroll;
        public static void Open(string folder, Action<string> selected)
        {
            var window = CreateInstance<ManualThumbnailPicker>();
            window.titleContent = new GUIContent("サムネイルを選択");
            window.folder = folder; window.selected = selected;
            window.minSize = new Vector2(560, 350); window.position = new Rect(180, 180, 720, 460);
            window.previews = new ThumbnailCache(window.Repaint, true);
            window.cancellation = new CancellationTokenSource();
            var token = window.cancellation.Token;
            window.scan = Task.Run(() => FileCatalog.Scan(folder, token), token);
            window.ShowUtility();
        }
        void Update()
        {
            if (scan == null || !scan.IsCompleted) return;
            if (scan.IsFaulted) error = scan.Exception.GetBaseException().Message;
            else if (!scan.IsCanceled)
            {
                files = scan.Result.files.Where(f => ManualItemEditing.IsThumbnailTexture(f.path)).ToArray();
                if (scan.Result.warnings.Count > 0) error = string.Join("\n", scan.Result.warnings);
            }
            scan = null; Repaint();
        }
        void OnDisable()
        {
            cancellation?.Cancel(); cancellation?.Dispose(); previews?.Dispose();
            scan?.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        }
        void OnGUI()
        {
            if (previews == null || selected == null)
            {
                EditorGUILayout.HelpBox("スクリプトが更新されました。情報を編集から画像の選択を開き直してください。", MessageType.Info);
                if (Clickable.LayoutButton("閉じる")) Close();
                return;
            }
            GUILayout.Space(8);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(8);
                using (new EditorGUILayout.VerticalScope())
                {
                    GUILayout.Label("フォルダ内のテクスチャ", EditorStyles.boldLabel);
                    GUILayout.Label("PNG / JPEG・32MB・4096 × 4096pxまで。元画像は変更せず、縮小して保存します。", EditorStyles.wordWrappedMiniLabel);
                    GUILayout.Space(6);
                    search = EditorGUILayout.TextField(search, GUI.skin.FindStyle("ToolbarSearchTextField") ?? EditorStyles.textField);
                    if (scan != null) GUILayout.Label("フォルダを確認しています…");
                    if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Info);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
                        if (files != null)
                        {
                            var matches = files.Where(f => Matcher.Normalize(f.relative).Contains(Matcher.Normalize(search))).ToArray();
                            foreach (var file in matches)
                            {
                                Rect row = GUILayoutUtility.GetRect(0, 26, GUILayout.ExpandWidth(true));
                                bool isSelected = chosen == file;
                                if (isSelected) EditorGUI.DrawRect(row, new Color(.24f, .43f, .65f));
                                var style = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleLeft, padding = new RectOffset(6, 6, 0, 0) };
                                if (isSelected) style.normal.textColor = Color.white;
                                using (new EditorGUI.DisabledScope(file.bytes > ManualItemEditing.MaxThumbnailBytes))
                                    if (Clickable.Button(row, new GUIContent(file.relative, file.relative + (file.bytes > ManualItemEditing.MaxThumbnailBytes ? "（32MBを超えています）" : "")), style)) { chosen = file; error = ""; }
                            }
                            if (matches.Length == 0) GUILayout.Label("選択できるテクスチャがありません。", EditorStyles.wordWrappedMiniLabel);
                        }
                        EditorGUILayout.EndScrollView();
                        using (new EditorGUILayout.VerticalScope(GUILayout.Width(192)))
                        {
                            Rect preview = GUILayoutUtility.GetRect(192, 192, GUILayout.Width(192));
                            EditorGUI.DrawRect(preview, new Color(.18f, .18f, .18f));
                            if (chosen != null)
                            {
                                var image = previews.Get(chosen.path, "", false, out string status);
                                if (image != null) GUI.DrawTexture(preview, image, ScaleMode.ScaleToFit);
                                else GUI.Label(preview, status, EditorStyles.centeredGreyMiniLabel);
                                GUILayout.Label(chosen.relative, EditorStyles.wordWrappedMiniLabel);
                            }
                            else GUI.Label(preview, "画像を選択", EditorStyles.centeredGreyMiniLabel);
                        }
                    }
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.FlexibleSpace();
                        if (Clickable.LayoutButton("キャンセル", GUILayout.Width(100), GUILayout.Height(26))) { Close(); GUIUtility.ExitGUI(); }
                        using (new EditorGUI.DisabledScope(chosen == null))
                            if (Clickable.LayoutButton("この画像に設定", GUILayout.Width(130), GUILayout.Height(26)))
                            {
                                string output = null;
                                try { output = ManualThumbnailImage.Create(folder, chosen.path, Path.Combine(LocalStore.Root, "ManualThumbnails")); }
                                catch (Exception ex) { error = ex.Message; }
                                if (output != null) { selected?.Invoke(output); Close(); GUIUtility.ExitGUI(); }
                            }
                    }
                }
                GUILayout.Space(8);
            }
            GUILayout.Space(8);
        }
    }
}
