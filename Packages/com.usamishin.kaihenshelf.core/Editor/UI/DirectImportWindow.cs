using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace KaihenShelf
{
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "VrcKaihenLibrary", "VrcKaihenLibrary.Editor", "DirectImportWindow")]
    public sealed class DirectImportWindow : EditorWindow
    {
        [SerializeField] DirectImportPlan plan;
        [SerializeField] ImportRecord record;
        [SerializeField] string result = "";
        [SerializeField] bool finished;
        [SerializeField] TreeViewState treeState;
        ImportTree tree;
        GUIStyle titleStyle, topStyle, bottomStyle;
        bool IsBatch => plan.stage.sources.Count > 0;
        public static bool Pending { get { return Resources.FindObjectsOfTypeAll<DirectImportWindow>().Any(x => x.plan != null && !x.finished); } }
        public static void Open(DirectPackageStage stage, ImportRecord import)
        {
            var window = GetWindowWithRect<DirectImportWindow>(new Rect(100, 100, 500, 550), true, "Import Unity Package");
            window.titleContent = new GUIContent(stage.texturesOnly ? "Import Textures" : "Import Unity Package");
            window.minSize = new Vector2(350, 350);
            window.plan = DirectImporter.Plan(stage, import.target.Substring(7));
            var item = LocalStore.Load().items.FirstOrDefault(x => x.id == import.itemId);
            if (item != null)
                window.plan.thumbnailProductFolder = FolderThumbnailRules.ImportRoot(window.plan.entries.Where(x => !x.asset.folder && x.conflict.Length == 0).Select(x => x.destination).ToArray(),
                    import.target, item.shop, stage.sources.Count > 0 ? import.productName : stage.texturesOnly ? Path.GetFileName(import.source) : Path.GetFileNameWithoutExtension(import.source));
            window.record = import; window.result = ""; window.finished = false;
            window.treeState = new TreeViewState();
            window.tree = new ImportTree(window.treeState, window.plan);
            window.tree.ExpandAll();
        }
        void OnDestroy()
        {
            if (plan == null) return;
            if (!finished && result.Length == 0)
            {
                if (plan.entries.Where(x => !x.asset.folder).All(x => x.unchanged && x.conflict.Length == 0)) ImportCoordinator.DirectNothingToImport();
                else ImportCoordinator.DirectConfirmationClosed();
            }
            try { plan.stage.Cleanup(); } catch (Exception ex) { Debug.LogWarning("一時データを保持しました: " + ex.Message); }
        }
        void OnGUI()
        {
            if (plan == null) return;
            if (titleStyle == null)
            {
                titleStyle = new GUIStyle(GUI.skin.FindStyle("LargeBoldLabel") ?? EditorStyles.largeLabel);
                topStyle = new GUIStyle(GUI.skin.FindStyle("OT TopBar") ?? EditorStyles.helpBox);
                bottomStyle = new GUIStyle(GUI.skin.FindStyle("ProjectBrowserBottomBarBg") ?? GUIStyle.none);
            }
            var files = plan.entries.Where(x => !x.asset.folder).ToArray();
            if (files.All(x => x.unchanged && x.conflict.Length == 0))
            {
                GUILayout.Label("Nothing to import!", EditorStyles.boldLabel);
                GUILayout.Label(IsBatch ? "All assets from these packages are already in your project." : plan.stage.texturesOnly ? "All textures from this folder are already in your project." : "All assets from this package are already in your project.", EditorStyles.wordWrappedLabel);
                DrawThumbnailOptions();
                GUILayout.FlexibleSpace();
                using (new EditorGUILayout.VerticalScope(bottomStyle))
                {
                    GUILayout.Space(8);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.FlexibleSpace();
                        if (Clickable.LayoutButton("OK"))
                        {
                            finished = true;
                            try { FolderThumbnails.RegisterImport(record, plan); }
                            catch (Exception ex) { Debug.LogWarning("フォルダ画像の設定をスキップしました: " + ex.Message); }
                            ImportCoordinator.DirectNothingToImport();
                            Close(); GUIUtility.ExitGUI();
                        }
                        GUILayout.Space(10);
                    }
                    GUILayout.Space(5);
                }
                return;
            }
            if (tree == null) tree = new ImportTree(treeState ?? (treeState = new TreeViewState()), plan);
            var header = GUILayoutUtility.GetRect(0, 100000, 52, 52);
            GUI.Label(header, GUIContent.none, topStyle);
            GUI.Label(new Rect(header.x + 5, header.y, header.width - 10, header.height),
                new GUIContent(IsBatch ? "UnityPackage 一括インポート（" + plan.stage.sources.Count + "個）" : plan.stage.texturesOnly ? Path.GetFileName(record.source) : Path.GetFileNameWithoutExtension(record.source),
                    "インポート先: " + record.target + "\n同じGUIDは現在の場所で更新します。" + (IsBatch ? "\n" + string.Join("\n", plan.stage.sources.Select(s => Path.GetFileName(s.path))) : "")), titleStyle);
            if (plan.stage.containsScripts) EditorGUILayout.HelpBox("スクリプト等を含みます。導入するとUnity内で実行される場合があります。", MessageType.Warning);
            int selected = files.Count(x => x.selected && !x.unchanged);
            GUILayout.Space(8);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(10);
                if (Clickable.LayoutButton("All", GUILayout.Width(50))) tree.SetAll(true);
                if (Clickable.LayoutButton("None", GUILayout.Width(50))) tree.SetAll(false);
                GUILayout.FlexibleSpace();
                GUILayout.Space(10);
            }
            GUILayout.Space(5);
            var treeRect = GUILayoutUtility.GetRect(0, 100000, 100, 100000, GUILayout.ExpandHeight(true));
            tree.OnGUI(treeRect);
            DrawThumbnailOptions();
            var blocked = DirectImporter.Chosen(plan).FirstOrDefault(x => x.conflict.Length > 0);
            if (blocked != null) EditorGUILayout.HelpBox(blocked.destination + "\n" + blocked.conflict, MessageType.Error);
            if (result.Length > 0) EditorGUILayout.HelpBox(result, MessageType.Warning);
            using (new EditorGUILayout.VerticalScope(bottomStyle))
            {
                GUILayout.Space(8);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(10); GUILayout.FlexibleSpace();
                    if (Clickable.LayoutButton("Cancel")) { Close(); GUIUtility.ExitGUI(); }
                    using (new EditorGUI.DisabledScope(finished || selected == 0 || blocked != null || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode))
                        if (Clickable.LayoutButton("Import"))
                        {
                            try { result = ImportCoordinator.CommitDirect(record, plan); finished = true; }
                            catch (Exception ex) { result = ex.Message; }
                            if (finished) { Close(); GUIUtility.ExitGUI(); }
                        }
                    GUILayout.Space(10);
                }
                GUILayout.Space(5);
            }
        }

        void DrawThumbnailOptions()
        {
            var options = new[] { "設定しない" }.Concat(FolderThumbnailRules.Candidates(plan.entries.Where(x => !x.asset.folder && x.selected && x.conflict.Length == 0).Select(x => x.destination))).ToArray();
            GUILayout.Space(4);
            plan.thumbnailProductFolder = ThumbnailPopup("商品サムネイル設定", plan.thumbnailProductFolder, options);
            GUILayout.Space(4);
        }
        static string ThumbnailPopup(string label, string path, string[] options)
        {
            int index = Math.Max(0, Array.IndexOf(options, path));
            int next = Clickable.Popup(label, index, options);
            return next == 0 ? "" : options[next];
        }

        // Public TreeView API only: Unity's PackageImport window is internal.
        sealed class ImportTree : TreeView
        {
            sealed class Node : TreeViewItem
            {
                public string path;
                public DirectImportEntry entry;
                public bool folder;
                public int selectable, enabled;
            }
            readonly DirectImportPlan plan;
            readonly Dictionary<string, Node> folders = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
            int nextId;
            GUIStyle lineStyle;
            GUIContent newBadge, changedBadge, conflictBadge;
            public string SelectionDescription
            {
                get
                {
                    var selection = GetSelection();
                    var node = selection.Count == 0 ? null : FindItem(selection[0], rootItem) as Node;
                    return node == null ? "フォルダー単位で選択・解除できます。" : node.path + (string.IsNullOrEmpty(node.entry?.conflict) ? "" : "\n" + node.entry.conflict);
                }
            }
            public ImportTree(TreeViewState state, DirectImportPlan importPlan) : base(state)
            {
                plan = importPlan; rowHeight = 16; showAlternatingRowBackgrounds = false;
                baseIndent = 4; depthIndentWidth = 18;
                Reload();
            }
            protected override TreeViewItem BuildRoot()
            {
                folders.Clear(); nextId = 1;
                var root = new Node { id = 0, depth = -1, path = "", displayName = "Root", folder = true };
                folders.Add("", root);
                folders.Add("Assets", root);
                foreach (var entry in plan.entries.OrderBy(x => x.destination, StringComparer.OrdinalIgnoreCase))
                {
                    if (entry.asset.folder) { Folder(entry.destination).entry = entry; continue; }
                    int slash = entry.destination.LastIndexOf('/');
                    var parent = Folder(slash < 0 ? "" : entry.destination.Substring(0, slash));
                    parent.AddChild(new Node { id = nextId++, displayName = Path.GetFileName(entry.destination), path = entry.destination,
                        entry = entry, icon = FileIcon(entry.destination) });
                }
                Sort(root); SetupDepthsFromParentsAndChildren(root); Count(root);
                return root;
            }
            Node Folder(string path)
            {
                if (folders.TryGetValue(path, out var found)) return found;
                int slash = path.LastIndexOf('/');
                var parent = Folder(slash < 0 ? "" : path.Substring(0, slash));
                var node = new Node { id = nextId++, displayName = Path.GetFileName(path), path = path, folder = true,
                    icon = EditorGUIUtility.IconContent("Folder Icon").image as Texture2D };
                folders.Add(path, node); parent.AddChild(node); return node;
            }
            static Texture2D FileIcon(string path)
            {
                var cached = AssetDatabase.GetCachedIcon(path) as Texture2D;
                if (cached != null) return cached;
                string ext = Path.GetExtension(path).ToLowerInvariant();
                string name = ext == ".prefab" ? "Prefab Icon" : ext == ".mat" ? "Material Icon" : ext == ".anim" ? "AnimationClip Icon"
                    : ext == ".fbx" || ext == ".obj" ? "Mesh Icon" : ext == ".cs" ? "cs Script Icon"
                    : ext == ".png" || ext == ".jpg" || ext == ".tga" || ext == ".psd" || ext == ".exr" ? "Texture2D Icon" : "DefaultAsset Icon";
                return EditorGUIUtility.IconContent(name).image as Texture2D;
            }
            static void Sort(Node node)
            {
                if (!node.hasChildren) return;
                node.children = node.children.OrderBy(x => ((Node)x).folder ? 0 : 1).ThenBy(x => x.displayName, StringComparer.OrdinalIgnoreCase).ToList();
                foreach (Node child in node.children) Sort(child);
            }
            static void Count(Node node)
            {
                node.selectable = !node.folder && !node.entry.unchanged ? 1 : 0;
                node.enabled = node.selectable != 0 && node.entry.selected ? 1 : 0;
                if (!node.hasChildren) return;
                foreach (Node child in node.children) { Count(child); node.selectable += child.selectable; node.enabled += child.enabled; }
            }
            static void Set(Node node, bool value)
            {
                if (!node.folder && !node.entry.unchanged) node.entry.selected = value;
                if (node.hasChildren) foreach (Node child in node.children) Set(child, value);
            }
            public void SetAll(bool value) { Set((Node)rootItem, value); Count((Node)rootItem); }
            protected override bool CanMultiSelect(TreeViewItem item) { return false; }
            protected override void RowGUI(RowGUIArgs args)
            {
                if (lineStyle == null)
                {
                    lineStyle = new GUIStyle(GUI.skin.FindStyle("TV Line") ?? EditorStyles.label);
                    lineStyle.padding.left = 0;
                    newBadge = new GUIContent(EditorGUIUtility.IconContent("PackageBadgeNew").image, "This is a new asset.");
                    changedBadge = new GUIContent(EditorGUIUtility.IconContent("playLoopOff").image, "This asset is new or has changed.");
                    conflictBadge = new GUIContent(EditorGUIUtility.IconContent("console.warnicon.sml").image, "配置先が競合しています。上書きできません。");
                }
                var node = (Node)args.item;
                var rect = args.rowRect; rect.x += GetContentIndent(node); rect.width = 18;
                bool mixed = EditorGUI.showMixedValue;
                EditorGUI.showMixedValue = node.enabled > 0 && node.enabled < node.selectable;
                if (node.folder || !node.entry.unchanged)
                using (new EditorGUI.DisabledScope(node.selectable == 0))
                {
                    EditorGUI.BeginChangeCheck();
                    bool value = Clickable.Checkbox(rect, node.enabled > 0);
                    if (EditorGUI.EndChangeCheck()) { Set(node, value); Count((Node)rootItem); }
                }
                EditorGUI.showMixedValue = mixed;
                rect.x += 18; rect.height = rowHeight + 5;
                rect.width = Math.Max(0, args.rowRect.xMax - rect.x - 48);
                var label = new GUIContent(node.displayName, node.icon, node.path + (string.IsNullOrEmpty(node.entry?.conflict) ? "" : "\n" + node.entry.conflict));
                var iconSize = EditorGUIUtility.GetIconSize();
                EditorGUIUtility.SetIconSize(new Vector2(16, 16));
                if (Event.current.type == EventType.Repaint) lineStyle.Draw(rect, label, false, false, args.selected, args.focused);
                EditorGUIUtility.SetIconSize(iconSize);
                GUI.Label(rect, new GUIContent("", label.tooltip), GUIStyle.none);
                GUIContent badge = !string.IsNullOrEmpty(node.entry?.conflict) ? conflictBadge : node.folder || node.entry.unchanged ? null : node.entry.update ? changedBadge : newBadge;
                if (badge != null && badge.image != null)
                {
                    float width = badge == newBadge ? badge.image.width : rowHeight;
                    float height = badge == newBadge ? badge.image.height : rowHeight;
                    var badgeRect = new Rect(args.rowRect.xMax - badge.image.width - 6, args.rowRect.y, width, height);
                    EditorGUIUtility.SetIconSize(Vector2.zero);
                    GUI.Label(badgeRect, badge, GUIStyle.none);
                    EditorGUIUtility.SetIconSize(iconSize);
                }
            }
        }
    }
}
