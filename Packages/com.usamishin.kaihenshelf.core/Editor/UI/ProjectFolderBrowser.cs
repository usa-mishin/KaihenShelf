using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace KaihenShelf
{
    public static class ProjectFolderBrowser
    {
        const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly Type BrowserType = typeof(Editor).Assembly.GetType("UnityEditor.ProjectBrowser");

        static bool Visible(EditorWindow window)
        {
            var parent = typeof(EditorWindow).GetField("m_Parent", Instance)?.GetValue(window);
            return parent != null && ReferenceEquals(parent.GetType().GetProperty("actualView", Instance)?.GetValue(parent), window);
        }

        public static EditorWindow Open(string path, Action<Exception> onError = null)
        {
            var folder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(path);
            if (!AssetDatabase.IsValidFolder(path) || folder == null) throw new InvalidOperationException("インポート先のフォルダが見つかりません。");
            // Unity has no public API for navigating inside one specific Project tab.
            var initialized = BrowserType?.GetMethod("Initialized", Instance, null, Type.EmptyTypes, null);
            var mode = BrowserType?.GetMethod("InitViewMode", Instance);
            var show = BrowserType?.GetMethod("ShowFolderContents", Instance, null, new[] { typeof(int), typeof(bool) }, null);
            if (initialized == null || mode == null || show == null) throw new NotSupportedException("このUnityバージョンではProject内のフォルダ表示に対応していません。");
            var browsers = Resources.FindObjectsOfTypeAll(BrowserType).OfType<EditorWindow>().ToArray();
            var last = BrowserType.GetField("s_LastInteractedProjectBrowser", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as EditorWindow;
            var window = last != null && Visible(last) ? last : browsers.FirstOrDefault(Visible);
            if (window == null)
            {
                window = (EditorWindow)ScriptableObject.CreateInstance(BrowserType);
                window.titleContent = new GUIContent("Project");
                window.Show();
            }
            var target = window;
            Action navigate = () =>
            {
                var twoColumns = Enum.Parse(mode.GetParameters()[0].ParameterType, "TwoColumns");
                var currentMode = BrowserType.GetField("m_ViewMode", Instance)?.GetValue(target);
                if (!Equals(currentMode, twoColumns)) mode.Invoke(target, new[] { twoColumns });
                show.Invoke(target, new object[] { folder.GetInstanceID(), true });
                target.Focus(); target.Repaint();
            };
            if ((bool)initialized.Invoke(window, null)) navigate();
            else
            {
                // A new Project window initializes its GUI styles during its first OnGUI.
                // Calling Init from delayCall/update (or batch mode) can leave Unity's list area null.
                double deadline = EditorApplication.timeSinceStartup + 10;
                EditorApplication.CallbackFunction ready = null;
                ready = () =>
                {
                    if (target == null) { EditorApplication.update -= ready; return; }
                    try
                    {
                        if (!(bool)initialized.Invoke(target, null))
                        {
                            if (EditorApplication.timeSinceStartup < deadline) { target.Repaint(); return; }
                            throw new InvalidOperationException("Projectウィンドウの準備が完了しませんでした。もう一度お試しください。");
                        }
                        EditorApplication.update -= ready; navigate();
                    }
                    catch (Exception ex) { EditorApplication.update -= ready; if (onError != null) onError(ex.GetBaseException()); else Debug.LogException(ex.GetBaseException()); }
                };
                EditorApplication.update += ready;
            }
            return window;
        }
    }
}
