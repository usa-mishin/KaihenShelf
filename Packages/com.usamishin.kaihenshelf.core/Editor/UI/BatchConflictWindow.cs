using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace KaihenShelf
{
    public sealed class BatchConflictWindow : EditorWindow
    {
        [SerializeField] DirectPackageStage stage;
        [SerializeField] ImportRecord record;
        [SerializeField] Vector2 scroll;
        [SerializeField] string error = "";
        public static bool Pending => Resources.FindObjectsOfTypeAll<BatchConflictWindow>().Any(x => x.stage != null);

        public static void Open(DirectPackageStage prepared, ImportRecord import)
        {
            var window = CreateInstance<BatchConflictWindow>();
            window.titleContent = new GUIContent("共通ファイルの選択");
            window.stage = prepared; window.record = import;
            window.minSize = new Vector2(560, 360);
            window.position = new Rect(140, 140, 720, 560);
            window.ShowUtility(); window.Focus();
        }
        void OnDestroy()
        {
            if (stage == null) return;
            try { stage.Cleanup(); }
            catch (Exception ex) { Debug.LogWarning("一時データを保持しました: " + ex.Message); }
            ImportCoordinator.DirectConfirmationClosed();
        }
        void OnGUI()
        {
            if (stage == null) return;
            GUILayout.Space(12);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(12);
                using (new EditorGUILayout.VerticalScope())
                {
                    GUILayout.Label("採用するパッケージを選択", EditorStyles.boldLabel);
                    GUILayout.Space(6);
                    GUILayout.Label("複数のパッケージに、同じGUIDで内容の異なる共通ファイルがあります。\n採用した設定は、このファイルを参照するすべてのアセットに適用されます。", EditorStyles.wordWrappedLabel);
                    GUILayout.Space(10);
                    scroll = EditorGUILayout.BeginScrollView(scroll);
                    foreach (var conflict in stage.batchConflicts)
                    {
                        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                        {
                            GUILayout.Space(6);
                            GUILayout.Label(Path.GetFileName(conflict.path), EditorStyles.boldLabel);
                            GUILayout.Label(new GUIContent(conflict.path, "GUID: " + conflict.guid), EditorStyles.wordWrappedMiniLabel);
                            GUILayout.Space(6);
                            for (int i = 0; i < conflict.variants.Count; i++)
                            {
                                var variant = conflict.variants[i];
                                Rect rect = GUILayoutUtility.GetRect(0, 28, GUILayout.ExpandWidth(true));
                                if (Clickable.Toggle(rect, conflict.selected == i, new GUIContent(Path.GetFileName(variant.source), variant.source), GUI.skin.button))
                                    conflict.selected = i;
                            }
                            GUILayout.Space(6);
                        }
                        GUILayout.Space(6);
                    }
                    EditorGUILayout.EndScrollView();
                    if (error.Length > 0) EditorGUILayout.HelpBox(error, MessageType.Error);
                    GUILayout.Space(8);
                    int chosen = stage.batchConflicts.Count(x => x.selected >= 0);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label(chosen + " / " + stage.batchConflicts.Count + "件を選択", EditorStyles.miniLabel);
                        GUILayout.FlexibleSpace();
                        if (Clickable.LayoutButton("キャンセル", GUILayout.Height(28))) { Close(); GUIUtility.ExitGUI(); }
                        using (new EditorGUI.DisabledScope(chosen != stage.batchConflicts.Count || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode))
                        {
                            if (Clickable.LayoutButton("インポート内容を確認", GUILayout.Height(28)))
                            {
                                try
                                {
                                    DirectImportWindow.Open(stage, record);
                                    ImportCoordinator.ConflictsResolved();
                                    stage = null; // The import confirmation now owns cleanup.
                                }
                                catch (Exception ex) { error = ex.Message; }
                                if (stage == null) { Close(); GUIUtility.ExitGUI(); }
                            }
                        }
                    }
                    GUILayout.Space(12);
                }
                GUILayout.Space(12);
            }
        }
    }
}
