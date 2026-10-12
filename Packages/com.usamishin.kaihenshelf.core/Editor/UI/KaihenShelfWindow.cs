using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace KaihenShelf
{
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "VrcKaihenLibrary", "VrcKaihenLibrary.Editor", "VrcKaihenLibraryWindow")]
    public sealed class KaihenShelfWindow : EditorWindow
    {
        static readonly string[] Pages = { "アバター", "アイテム・アセット", "ショップ", "BOOTHタグ", "インポート先・分類設定", "表示・連携設定", "インポート履歴", "使い方・ポリシー" };
        static readonly int[] SidebarOrder = { 0, 1, 2, 3, 6, 4, 5, 7 };
        static readonly string[] FileTabs = { "UnityPackage", "テクスチャ", "画像編集データ", "3Dデータ", "その他ファイル" };
        int fileTab;
        bool batchSelecting;
        bool? pendingBatchSelecting;
        readonly HashSet<string> batchSelection = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        LibraryState state;
        string loadError = "", message = "", search = "", shop = "", tag = "", avatar = "", fileSearch = "";
        int page, category, listPage;
        string boothCategory = "";
        BoothAgeFilter ageFilter = BoothAgeFilter.AllAges;
        static readonly string[] AgeFilterLabels = { "全年齢のみ", "R18商品のみ", "指定なし" };
        const int HistoryPageSize = 20;
        int historyPage;
        Vector2 historyScroll;
        [NonSerialized] ImportHistory historyView;
        long historyViewStamp, historyViewLength;
        string historyViewError = "";
        Dictionary<AssetCategory, string> targetDraft;
        List<CategoryRule> classificationDraft;
        List<CategoryRule> classificationApplied;
        GUIStyle classificationNoticeStyle;
        bool classificationDraftChanged;
        float groupViewportWidth;
        [SerializeField] int sort = 4, avatarSort = 4, shopSort, tagSort;
        [SerializeField] int sortOptionsVersion;
        [SerializeField] bool initialHeight600Applied;
        bool favorites, freeOnly, excludeShared, excludeNoAvatar, tagsOpen, consentCheck, dirty;
        bool consentDetails;
        Vector2 consentScroll;
        Vector2 listScroll, detailScroll, filesScroll, settingsScroll;
        string selectedId = "";
        TextureFolder textureTree;
        FileScan textureTreeScan;
        string textureTreeQuery, textureTreeKind;
        int textureFolderCount;
        bool? pendingFolderExpansion;
        readonly HashSet<string> expandedTextureFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string scanItemId = "";
        FileScan scan;
        [NonSerialized] string scanAttemptId = "";
        Task work;
        Action complete;
        CancellationTokenSource cancellation;
        [SerializeField] KaihenShelfDetailWindow detailWindow;
        [SerializeField] KaihenShelfEditWindow editWindow;
        CatalogViewIndex catalog;
        CatalogViewIndex Catalog
        {
            get
            {
                if (catalog == null)
                {
                    catalog = new CatalogViewIndex(state);
                    if (scan != null && !string.IsNullOrEmpty(scanItemId)) catalog.SetFilenames(scanItemId, scan.files.Select(x => x.relative));
                }
                return catalog;
            }
        }
        ThumbnailCache thumbnails;
        ThumbnailCache textureThumbnails;
        FileTypeIconCache fileTypeIcons;
        Texture2D textureHoverImage;
        string textureHoverName;
        Rect textureHoverScreenRect;
        const float CardWidth = 190, CardPadding = 6, CardGap = 6;
        const float MinimumWindowWidth = 1000;
        const float DefaultWindowHeight = 600;
        float cardViewportWidth;
        int cardColumns = 4;
        GUIStyle titleStyle, cardStyle, fileTabStyle, detailTitleStyle, productShopStyle, categoryBadgeStyle;
        GUIStyle favoriteButtonStyle, cardFooterLabelStyle, cardDetailButtonStyle;
        GUIStyle packageTitleStyle, packageMetaStyle, packageDateStyle, packageBadgeStyle;
        GUIStyle fileSearchLabelStyle, fileSearchFieldStyle;
        GUIStyle fileCardTitleStyle;
        GUIStyle detailActionStyle, detailTagStyle, detailLinkStyle, detailNoticeStyle;
        Font productFont;
        ProductText productText;
        FavoriteIcon favoriteIcon;
        DetailActionIcons detailActionIcons;
        readonly Dictionary<string, string[]> cardTitles = new Dictionary<string, string[]>();
        string lastImportStatus = "";
        double nextImportAnimationRepaint;
        double lastChangedTime;
        static readonly string[] SortLabels = {
            "↑ 商品名（A-Z）", "↓ 商品名（Z-A）", "↑ ショップ名（A-Z）", "↓ ショップ名（Z-A）",
            "↓ 登録日時（新しい順）", "↑ 登録日時（古い順）", "↓ 更新日時（新しい順）", "↑ 更新日時（古い順）",
            "↓ 公開日時（新しい順）", "↑ 公開日時（古い順）"
        };
        static readonly string[] AvatarSortLabels = SortLabels.Take(8).ToArray();
        static readonly string[] GroupSortLabels = { "↑ 名前（A-Z）", "↓ 名前（Z-A）", "↑ アイテム数（少ない順）", "↓ アイテム数（多い順）" };
        const float NavigationHeight = 24;
        GUIStyle sortPopupStyle, navigationLabelStyle;
        static readonly AssetCategory[] TabCategories = Categories.DisplayOrder;
        static readonly string[] CategoryLabels = new[] { "すべて" }.Concat(TabCategories.Skip(1).Select(c => Categories.Labels[(int)c])).ToArray();
        readonly Dictionary<string[], string[]> popupLabels = new Dictionary<string[], string[]>();

        [MenuItem("KaihenShelf/KaihenShelf")]
        public static void Open()
        {
            // Only new windows need initial bounds; changing position during reload can detach the DockArea.
            var window = Resources.FindObjectsOfTypeAll<KaihenShelfWindow>().FirstOrDefault();
            if (window == null)
            {
                window = CreateInstance<KaihenShelfWindow>();
                window.titleContent = new GUIContent("KaihenShelf");
                window.position = new Rect(100, 100, MinimumWindowWidth, DefaultWindowHeight);
            }
            window.minSize = new Vector2(MinimumWindowWidth, 500);
            window.maxSize = new Vector2(10000, 10000);
            window.Show(); window.Focus();
            window.thumbnails?.RetryFailed();
        }
        void OnEnable()
        {
            titleContent = new GUIContent("KaihenShelf");
            // Hot reload can restore file stamps without the nonserialized index contents.
            importedHistory = new ImportedHistoryIndex();
            importedHistoryStamp = long.MinValue; importedHistoryLength = -1; nextHistoryCheck = 0;
            historyView = null; historyViewError = "";
            page = Mathf.Clamp(page, 0, Pages.Length - 1);
            minSize = new Vector2(MinimumWindowWidth, 500);
            maxSize = new Vector2(10000, 10000);
            if (!initialHeight600Applied) EditorApplication.delayCall += ApplyInitialHeight;
            cardViewportWidth = 0; cardColumns = 4;
            // Migrate the four-option menu without treating its index as a new option.
            if (sortOptionsVersion < 1) { sort = sort == 0 ? 0 : sort == 3 ? 6 : 4; sortOptionsVersion = 1; }
            sortPopupStyle = navigationLabelStyle = null;
            sidebarLabelStyle = null;
            classificationDraft = classificationApplied = null; classificationDraftChanged = false;
            targetDraft = null;
            classificationNoticeStyle = null;
            groupViewportWidth = 0;
            groupTitleStyle = null;
            loadError = "";
            state = null;
            catalog = null;
            titleStyle = cardStyle = fileTabStyle = detailTitleStyle = productShopStyle = categoryBadgeStyle = null;
            favoriteButtonStyle = cardFooterLabelStyle = cardDetailButtonStyle = null;
            packageTitleStyle = packageMetaStyle = packageDateStyle = packageBadgeStyle = null;
            fileSearchLabelStyle = fileSearchFieldStyle = null;
            fileCardTitleStyle = null;
            textureFolderButtonStyle = textureFolderButtonLabelStyle = null;
            batchActionLabelStyle = packageSelectionStyle = null;
            detailActionStyle = detailTagStyle = detailLinkStyle = detailNoticeStyle = null;
            cardTitles.Clear();
            thumbnails?.Dispose(); thumbnails = new ThumbnailCache(RepaintViews);
            textureThumbnails?.Dispose(); textureThumbnails = null;
            fileTypeIcons?.Dispose(); fileTypeIcons = null;
            try
            {
                state = LocalStore.Load();
                if (state.MigrateCategoryRules()) dirty = true;
                AvatarDefaultsLoader.Load(out string warning);
                if (warning.Length > 0) message = warning;
                if (state.consentVersion == BlmDatabaseReader.ConsentVersion) RefreshCatalog();
            }
            catch (Exception ex) { loadError = ex.Message; }
            EditorApplication.update += Poll;
        }
        void ApplyInitialHeight()
        {
            if (this == null || initialHeight600Applied) return;
            initialHeight600Applied = true;
            if (!docked && !Mathf.Approximately(position.height, DefaultWindowHeight))
                position = new Rect(position.x, position.y, position.width, DefaultWindowHeight);
        }
        void OnDisable()
        {
            cancellation?.Cancel(); Save(); EditorApplication.update -= Poll;
            thumbnails?.Dispose(); thumbnails = null;
            textureThumbnails?.Dispose(); textureThumbnails = null;
            fileTypeIcons?.Dispose(); fileTypeIcons = null;
            if (productFont != null) DestroyImmediate(productFont);
            productFont = null;
            productText?.Dispose(); productText = null;
            favoriteIcon?.Dispose(); favoriteIcon = null;
            detailActionIcons?.Dispose(); detailActionIcons = null;
        }
        void RepaintViews() { Repaint(); if (detailWindow != null) detailWindow.Repaint(); if (editWindow != null) editWindow.Repaint(); }
        internal void EditClosed(KaihenShelfEditWindow window)
        {
            if (editWindow == window) editWindow = null;
            Save();
        }
        void OpenEdit(LibraryItem item)
        { editWindow = KaihenShelfEditWindow.ShowFor(this, item.id, EditTitle(item)); }
        string EditTitle(LibraryItem item) { return "情報を編集"; }
        internal void DetailClosed(KaihenShelfDetailWindow window)
        {
            if (detailWindow != window) return;
            detailWindow = null;
            textureThumbnails?.Dispose(); textureThumbnails = null;
            textureHoverImage = null;
            fileTypeIcons?.Dispose(); fileTypeIcons = null;
        }
        internal void RestoreDetail(string itemId)
        {
            if (state == null || state.consentVersion != BlmDatabaseReader.ConsentVersion) return;
            var item = Catalog.Find(itemId);
            if (item != null) Select(item);
        }
        ImportedHistoryIndex importedHistory = new ImportedHistoryIndex();
        long importedHistoryStamp = long.MinValue, importedHistoryLength = -1;
        ImportedHistoryIndex Imported { get { RefreshImportedHistory(); return importedHistory; } }
        double nextHistoryCheck;
        void RefreshImportedHistory()
        {
            if (EditorApplication.timeSinceStartup < nextHistoryCheck) return;
            nextHistoryCheck = EditorApplication.timeSinceStartup + .5;
            try
            {
                var info = new FileInfo(LocalStore.HistoryPath);
                long stamp = info.Exists ? info.LastWriteTimeUtc.Ticks : 0, length = info.Exists ? info.Length : 0;
                if (stamp == importedHistoryStamp && length == importedHistoryLength) return;
                var replacement = new ImportedHistoryIndex();
                foreach (var record in (LocalStore.Read<ImportHistory>(LocalStore.HistoryPath) ?? new ImportHistory()).records)
                    replacement.Add(record.itemId, record.source, record.status);
                importedHistory = replacement; importedHistoryStamp = stamp; importedHistoryLength = length;
                RepaintViews();
            }
            catch (Exception) { /* A history writer can hold the file briefly; retry on the next update. */ }
        }
        static void DrawImportedChip(Rect rect, string label, bool subdued = false)
        {
            if (Event.current.type != EventType.Repaint) return;
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, new Color32(32, 32, 32, subdued ? (byte)165 : (byte)255), 0, rect.height / 2);
            var style = new GUIStyle(GUI.skin.label) {
                alignment = TextAnchor.MiddleCenter, fontSize = 11, fontStyle = FontStyle.Bold,
                // The single Japanese glyph appears left of its layout center in the editor font.
                padding = new RectOffset(), margin = new RectOffset(), contentOffset = label == "済" ? new Vector2(2, 0) : Vector2.zero,
                fixedWidth = 0, fixedHeight = 0, wordWrap = false, normal = { textColor = subdued ? new Color(.8f, .8f, .8f) : Color.white }
            };
            GUI.Label(rect, new GUIContent(label, "このプロジェクトに正常にインポートした履歴があります。"), style);
        }
        void OnInspectorUpdate()
        {
            RefreshImportedHistory();
            if (EditorApplication.timeSinceStartup - lastChangedTime > .5) Save();
            if (lastImportStatus != ImportCoordinator.Status) { lastImportStatus = ImportCoordinator.Status; RepaintViews(); }
        }
        void Changed(bool rebuildCatalog = true) { dirty = true; lastChangedTime = EditorApplication.timeSinceStartup; if (rebuildCatalog) { catalog = null; popupLabels.Clear(); } RepaintViews(); }
        void ToggleFavorite(string scope, string key)
        { state.ToggleFavorite(scope, key); catalog?.UpdateFavorites(state.favorites); Changed(false); }
        void Save()
        {
            if (!dirty || state == null || loadError.Length > 0) return;
            try { LocalStore.Write(LocalStore.StatePath, state); FolderThumbnails.RefreshConsent(); dirty = false; }
            catch (Exception ex) { message = "保存エラー: " + ex.Message; }
        }
        void Run<T>(Func<CancellationToken, T> action, Action<T> then, string status)
        {
            if (work != null) return;
            cancellation = new CancellationTokenSource(); var token = cancellation.Token;
            var task = Task.Run(() => action(token), token); work = task;
            complete = () => then(task.Result); message = status; RepaintViews();
        }
        void Poll()
        {
            if (ImportCoordinator.CanCancelPreparation && EditorApplication.timeSinceStartup >= nextImportAnimationRepaint)
            {
                nextImportAnimationRepaint = EditorApplication.timeSinceStartup + 1.0 / 15;
                RepaintViews();
            }
            thumbnails?.SetRemoteEnabled(state != null && state.consentVersion == BlmDatabaseReader.ConsentVersion);
            TryScanSelected();
            if (work == null || !work.IsCompleted) return;
            var task = work; var action = complete; work = null; complete = null;
            try
            {
                if (task.IsCanceled || cancellation != null && cancellation.IsCancellationRequested) message = "処理を取り消しました。";
                else if (task.IsFaulted) message = "エラー: " + task.Exception.GetBaseException().Message;
                else { message = "完了しました。"; action(); }
            }
            catch (Exception ex) { message = "エラー: " + ex.Message; }
            cancellation?.Dispose(); cancellation = null; RepaintViews();
        }
        void TryScanSelected()
        {
            if (work != null || detailWindow == null || state == null || state.consentVersion != BlmDatabaseReader.ConsentVersion ||
                string.IsNullOrEmpty(selectedId) || scan != null || scanAttemptId == selectedId) return;
            var item = Catalog.Find(selectedId);
            if (item == null || item.unpurchased || string.IsNullOrEmpty(item.folder)) return;
            scanAttemptId = item.id;
            ScanFiles(item);
        }
        void OnGUI()
        {
            if (state == null) { EditorGUILayout.HelpBox("設定を読み取れません。ファイルを上書きせず停止しました。\n" + loadError + "\n" + LocalStore.StatePath, MessageType.Error); return; }
            if (state.consentVersion != BlmDatabaseReader.ConsentVersion) { DrawConsent(); return; }
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("KaihenShelf", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace(); GUILayout.Label("BOOTH Library Manager連携・非公式  |  0.1.147", EditorStyles.miniLabel);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(170)))
                {
                    GUILayout.Space(10);
                    foreach (int i in SidebarOrder) DrawSidebarPage(i);
                    GUILayout.FlexibleSpace();
                    using (new EditorGUI.DisabledScope(work != null))
                    {
                        if (Clickable.LayoutButton("フォルダから追加登録")) AddFolder();
                        if (Clickable.LayoutButton("未購入アバター追加")) UnpurchasedWindow.Show(AddUnpurchased);
                        if (Clickable.LayoutButton("最新データを取得して更新")) ReloadBlm();
                    }
                }
                GUILayout.Space(6);
                using (new EditorGUILayout.VerticalScope())
                {
                    GUILayout.Space(10);
                    if (page <= 1) DrawLibrary();
                    else if (page <= 3) DrawGroups();
                    else if (page == 6) DrawHistory();
                    else
                    {
                        settingsScroll = EditorGUILayout.BeginScrollView(settingsScroll);
                        if (page == 4) DrawTargets();
                        if (page == 5) DrawSettings();
                        if (page == 7) DrawHelp();
                        EditorGUILayout.EndScrollView();
                    }
                }
            }
            DrawStatus();
        }
        GUIStyle sidebarLabelStyle;
        void DrawSidebarPage(int index)
        {
            if (detailActionIcons == null) detailActionIcons = new DetailActionIcons();
            if (sidebarLabelStyle == null) sidebarLabelStyle = new GUIStyle(EditorStyles.label) {
                alignment = TextAnchor.MiddleLeft, fontSize = 11, padding = new RectOffset(), margin = new RectOffset()
            };
            Rect rect = GUILayoutUtility.GetRect(0, 30, GUILayout.ExpandWidth(true));
            if (Clickable.Toggle(rect, page == index, new GUIContent("", Pages[index]), GUI.skin.button) && page != index)
            { GUI.FocusControl(null); page = index; ResetListPosition(); settingsScroll = Vector2.zero; search = ""; }
            detailActionIcons.DrawNavigation(new Rect(rect.x + 10, rect.center.y - 8, 16, 16), index);
            GUI.Label(new Rect(rect.x + 34, rect.y, rect.width - 38, rect.height), Pages[index], sidebarLabelStyle);
        }
        void DrawStatus()
        {
            string full = string.Join("\n", new[] { message, ImportCoordinator.Status }.Where(text => !string.IsNullOrWhiteSpace(text)));
            if (full.Length == 0 && work == null) return;
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                GUILayout.Label(new GUIContent(full.Replace("\r", "").Replace("\n", " / "), full),
                    EditorStyles.miniLabel, GUILayout.Height(18), GUILayout.MinWidth(0), GUILayout.ExpandWidth(true));
                if (work != null && Clickable.LayoutButton("取消", GUILayout.Width(50), GUILayout.Height(18))) cancellation.Cancel();
                if (ImportCoordinator.CanCancelPreparation && Clickable.LayoutButton("準備取消", GUILayout.Width(75), GUILayout.Height(18))) ImportCoordinator.CancelPreparation();
            }
        }
        void DrawConsent()
        {
            if (ConsentScreen.Draw(ref consentScroll, ref consentCheck, ref consentDetails, !string.IsNullOrEmpty(state.consentVersion), DrawPolicy))
                { state.consentVersion = BlmDatabaseReader.ConsentVersion; state.consentUtc = DateTime.UtcNow.ToString("o"); RefreshCatalog(); Save(); page = 0; ReloadBlm(); }
        }
        void DrawPolicy()
        {
            EditorGUILayout.HelpBox("BOOTH Library Manager連携・非公式\nBOOTH、BOOTH Library Manager、ピクシブ株式会社、VRChat、Unityが提供・保証・承認する製品ではありません。", MessageType.Info);
            DrawGuideSection("読み取る情報", "同意後、BOOTH Library ManagerがPC内に保存した商品名・説明・ショップ・分類・タグ・URL・画像URL・保存先・日時・年齢制限・購入済みバリエーション・更新通知を読み取ります。DBは読み取り専用で開きます。商品詳細では選択した商品フォルダを走査し、ファイル名・更新日時・サイズと、表示可能な画像やPSD／PSBの埋め込みプレビューを読み取ります。インポート時は選択ファイルとUnity内の配置先・GUID・既存ファイルを検査します。");
            DrawGuideSection("保存・変更する範囲", "商品情報のキャッシュ、分類条件、アバター設定、お気に入り、未購入アバター、インポート先設定、同意の版番号と日時は %LOCALAPPDATA%\\KaihenShelf\\Unity\\state.json に保存します。この設定は同じPCのUnityプロジェクト間で共通です。直前のJSONは .bak に保存します。\nインポート履歴は現在のプロジェクトの UserSettings/KaihenShelf/history.json、フォルダ画像の設定と縮小画像は同じ場所の FolderThumbnails に保存します。画像はAssetsへ追加せず、ビルドに含めません。作業用の一時展開・バックアップ・配置記録は Library/KaihenShelf に保存します。");
            DrawGuideSection("元データとインポート", "この拡張機能はBOOTH Library ManagerのDB・元の商品ファイル・Windowsアプリ版のlibrary.dbを書き換えません。インポートはユーザー操作と専用の確認画面を経由し、選択したファイルを現在のUnityプロジェクトのAssetsへ新規作成・更新します。Unity標準に近い表示ですが、この拡張機能の確認画面です。\n一時展開データは通常の終了・キャンセル時に削除します。書き込み失敗時は復元を試み、復元に失敗した場合はバックアップを保持して場所を表示します。強制終了時は一時データが残る場合があります。履歴のリセットや同意取消は、導入済みアセットや保存済み画像を削除しません。");
            DrawGuideSection("通信と外部アプリ", "商品画像はローカルを優先し、取得できない場合やショップ画像の表示時は保存済みのHTTPS画像URLから取得します。接続先は booth.pximg.net、booth.pm とそのサブドメインに限定し、リダイレクトには追従しません。画像配信先には画像URL・IPアドレスなど通常の接続情報が伝わります。ログイン情報やCookieを画像取得に送信しません。\n通常のサムネイルとプレビューは上限を設けたメモリ内キャッシュです。フォルダ画像に設定した商品画像だけは、最大128pxの縮小画像を上記のプロジェクト内へ保存します。「商品ページ」は既定ブラウザーでBOOTHを開き、ファイルカードはPCの既定アプリへ元ファイルのパスを渡します。外部アプリの通信や編集・保存は、そのアプリの動作に従います。");
            DrawGuideSection("取得しない情報・停止方法", "BOOTHのパスワード、ブラウザーCookie、VRChatアカウントは読み取りません。この拡張機能による広告、テレメトリー、自動クラッシュ送信、独自サーバーへの商品データ送信はありません。\n表示・連携設定の「同意を取り消して読み取りを停止」で利用を停止できます。保存済みの設定・商品情報・履歴・フォルダ画像は残ります。複数のUnity Editorで同時に設定を編集すると最後の保存が優先されるため、設定の編集は1つのEditorで行ってください。");
            DrawGuideSection("自動判定・動作の範囲", "分類・対応アバター・購入形態・旧バージョン・同名判定は、取得できた情報に基づく補助機能です。誤判定は商品情報の手動編集などで確認・修正してください。パッケージの検査は危険なパスや容量などを確認しますが、アセットのコードの安全性、固定パスへの依存、必要なシェーダー・SDKの充足、導入後の正常動作を保証するものではありません。商品ごとの利用規約・ライセンスと配布元の導入手順に従って利用してください。");
        }

        void AddFolder()
        {
            string folder = EditorUtility.OpenFolderPanel("商品フォルダーを選択", "", "");
            if (folder.Length == 0) return;
            folder = Path.GetFullPath(folder);
            var existing = state.items.Find(x => string.Equals(x.folder, folder, StringComparison.OrdinalIgnoreCase));
            if (existing != null) { Select(existing); return; }
            var item = new LibraryItem { id = "manual:" + Guid.NewGuid().ToString("N"), title = new DirectoryInfo(folder).Name, folder = folder, registeredUtc = DateTime.UtcNow.ToString("o") };
            state.items.Add(item); Changed(); page = 1; ResetListPosition(); Select(item); FirstLoad(); OpenEdit(item);
        }
        void AddUnpurchased(LibraryItem item)
        {
            if (state.items.Any(x => x.boothId == item.boothId)) { message = "このBOOTH商品は登録済みです。"; return; }
            state.items.Add(item); RefreshCatalog(); page = 0; ResetListPosition(); Select(item); OpenEdit(item);
        }
        void ReloadBlm()
        {
            thumbnails?.RetryFailed();
            string path = state.blmPath;
            bool consent = state.consentVersion == BlmDatabaseReader.ConsentVersion;
            Run(token =>
            {
                try { return BlmDatabaseReader.ReadKnown(path, consent, token); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("商品を読み込めませんでした。「表示・連携設定」でBOOTH Library Managerの保存場所（DB）を確認し、「最新データを取得して更新」を押してください。 " + ex.Message);
                }
            }, items =>
            {
                state.MergeBlm(items); RefreshCatalog();
                message = items.Count + "件の商品情報を読み取りました。";
                scan = null; scanItemId = ""; FirstLoad();
            }, "BOOTH Library Managerを読み取り中… ロック中・初期化中は2秒間隔、最大10回再試行します。");
        }
        void RefreshCatalog() { AvatarCatalog.Synchronize(state); Changed(); }
        void FirstLoad() { if (!state.firstLoadCompleted) { state.firstLoadCompleted = true; page = 0; Changed(); } }
        List<AvatarMatch> Matches(LibraryItem item) { return Catalog.Match(item); }
        void Select(LibraryItem item)
        {
            selectedId = item.id; fileSearch = ""; tagsOpen = false;
            batchSelecting = false; pendingBatchSelecting = null; batchSelection.Clear();
            pendingFolderExpansion = null;
            detailScroll = filesScroll = Vector2.zero;
            scan = null; scanItemId = ""; scanAttemptId = "";
            detailWindow = KaihenShelfDetailWindow.ShowFor(this, item.id);
            TryScanSelected();
            RepaintViews();
        }
        void EnsureNavigationStyles()
        {
            if (sortPopupStyle == null) sortPopupStyle = new GUIStyle(EditorStyles.popup) { fixedHeight = NavigationHeight, alignment = TextAnchor.MiddleLeft };
            if (navigationLabelStyle == null) navigationLabelStyle = new GUIStyle(EditorStyles.label) {
                fontSize = 13, fixedHeight = NavigationHeight, alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(2, 2, 0, 0), margin = new RectOffset(0, 0, 2, 2)
            };
        }
        void ResetListPosition() { listPage = 0; listScroll = Vector2.zero; }
        void DrawLibrary()
        {
            GUILayout.Label(Pages[page], EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            DrawCompactSearch("商品名・タグ検索");
            if (page == 1)
            {
                DrawCategoryTabs();
            }
            if (EditorGUI.EndChangeCheck()) ResetListPosition();
            int currentSort = page == 0 ? avatarSort : sort;
            var filter = new CatalogFilter { avatars = page == 0, hideR18 = state.hideR18, favoritesOnly = favorites,
                freeOnly = freeOnly, excludeShared = excludeShared, excludeNoAvatar = excludeNoAvatar, category = category, sort = currentSort, search = search, shop = shop, tag = tag, avatarId = avatar,
                boothCategory = boothCategory, age = ageFilter };
            var items = Catalog.Query(filter);
            int pages = Math.Max(1, (items.Count + 59) / 60); listPage = Mathf.Clamp(listPage, 0, pages - 1);
            using (new EditorGUILayout.HorizontalScope())
            {
                EnsureNavigationStyles();
                GUILayout.Label(items.Count + "件", navigationLabelStyle, GUILayout.Height(NavigationHeight)); GUILayout.FlexibleSpace();
                int nextSort = Clickable.Popup(currentSort, page == 0 ? AvatarSortLabels : SortLabels, sortPopupStyle, GUILayout.Width(225), GUILayout.Height(NavigationHeight));
                if (nextSort != currentSort)
                {
                    if (page == 0) avatarSort = nextSort; else sort = nextSort;
                    ResetListPosition(); filter.sort = nextSort;
                    items = Catalog.Query(filter);
                }
                GUILayout.Space(8);
                if (Clickable.LayoutButton("‹", GUILayout.Width(25), GUILayout.Height(NavigationHeight))) { listPage = Math.Max(0, listPage - 1); listScroll = Vector2.zero; }
                EnsureNavigationStyles();
                GUILayout.Label((listPage + 1) + " / " + pages, navigationLabelStyle, GUILayout.Height(NavigationHeight));
                if (Clickable.LayoutButton("›", GUILayout.Width(25), GUILayout.Height(NavigationHeight))) { listPage = Math.Min(pages - 1, listPage + 1); listScroll = Vector2.zero; }
            }
            GUILayout.Space(6);
            // Keep the control structure stable between Layout and Repaint. Measure the
            // actual scroll viewport after drawing, then use it at the next Layout.
            if (Event.current.type == EventType.Layout && cardViewportWidth > 0)
                cardColumns = Mathf.Max(1, Mathf.FloorToInt((cardViewportWidth + CardGap) / (CardWidth + CardGap)));
            int columns = cardColumns;
            listScroll = EditorGUILayout.BeginScrollView(listScroll, false, true);
            var visible = items.Skip(listPage * 60).Take(60).ToArray();
            EnsureProductStyles();
            if (titleStyle == null)
                titleStyle = new GUIStyle(EditorStyles.boldLabel) { wordWrap = false, richText = false,
                    font = productFont, fontSize = 12,
                    alignment = TextAnchor.UpperLeft, clipping = TextClipping.Clip,
                    padding = new RectOffset(), margin = new RectOffset(), fixedWidth = 0, fixedHeight = 0 };
            if (cardStyle == null)
                cardStyle = new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset((int)CardPadding, (int)CardPadding, (int)CardPadding, (int)CardPadding), margin = new RectOffset() };
            GUILayout.Space(3);
            for (int i = 0; i < visible.Length; i += columns)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int j = 0; j < columns && i + j < visible.Length; j++)
                    {
                        if (j > 0) GUILayout.Space(CardGap);
                        var item = visible[i + j];
                        using (new EditorGUILayout.VerticalScope(cardStyle, GUILayout.Width(CardWidth)))
                        {
                            if (DrawThumbnail(item.thumbnailPath, item.thumbnailUrl, CardWidth - CardPadding * 2, listScroll.y, true, badge: Catalog.Category(item), updateAvailable: item.updateAvailable, width: CardWidth - CardPadding * 2, imported: Imported.ContainsItem(item.id))) Select(item);
                            GUILayout.Space(4);
                            Rect titleRect = GUILayoutUtility.GetRect(CardWidth - CardPadding * 2, titleStyle.lineHeight * 2);
                            string[] lines = CardTitle(item.title);
                            // Draw explicit lines: some Unity Editor fonts clip wrapped button text to one line.
                            for (int line = 0; line < 2; line++)
                                productText.Draw(new Rect(titleRect.x, titleRect.y + titleStyle.lineHeight * line, titleRect.width, titleStyle.lineHeight), lines[line], titleStyle);
                            if (Clickable.Button(titleRect, new GUIContent("", item.title), GUIStyle.none)) Select(item);
                            Clickable.Cursor(titleRect);
                            GUILayout.Space(3);
                            DrawProductLabel(item.shop.Length == 0 ? "ショップ情報なし" : item.shop, productShopStyle, CardWidth - CardPadding * 2, false);
                            GUILayout.Space(3);
                            DrawCardFooter(item);
                        }
                    }
                    GUILayout.FlexibleSpace();
                }
                GUILayout.Space(CardGap);
            }
            if (items.Count == 0) EditorGUILayout.HelpBox("表示する商品がありません。商品フォルダーを登録するか、設定からBOOTH Library Manager連携を行ってください。", MessageType.Info);
            EditorGUILayout.EndScrollView();
            if (Event.current.type == EventType.Repaint)
            {
                Rect viewport = GUILayoutUtility.GetLastRect();
                cardViewportWidth = Mathf.Max(0, viewport.width - GUI.skin.verticalScrollbar.fixedWidth - GUI.skin.verticalScrollbar.margin.left);
                int measuredColumns = Mathf.Max(1, Mathf.FloorToInt((cardViewportWidth + CardGap) / (CardWidth + CardGap)));
                if (measuredColumns != cardColumns) Repaint();
            }
        }
        void DrawCardFooter(LibraryItem item)
        {
            const float height = 24, gap = 6, detailWidth = 42;
            if (favoriteButtonStyle == null)
            {
                favoriteButtonStyle = new GUIStyle(GUI.skin.button)
                {
                    alignment = TextAnchor.MiddleCenter, fontSize = 16,
                    padding = new RectOffset(), margin = new RectOffset(), contentOffset = Vector2.zero
                };
                cardDetailButtonStyle = new GUIStyle(favoriteButtonStyle) { fontSize = 11 };
                cardFooterLabelStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleLeft, fontSize = 11,
                    padding = new RectOffset(), margin = new RectOffset(), contentOffset = Vector2.zero
                };
            }
            // One shared row rectangle keeps the label and both buttons vertically centered.
            Rect row = GUILayoutUtility.GetRect(0, height, GUILayout.ExpandWidth(true));
            bool favorite = Catalog.IsFavorite(Catalog.Scope(item), item.id);
            var favoriteRect = new Rect(row.x, row.y, height, height);
            if (Clickable.Button(favoriteRect,
                new GUIContent("", favorite ? "お気に入りを解除" : "お気に入りに追加"), favoriteButtonStyle))
                ToggleFavorite(Catalog.Scope(item), item.id);
            if (Event.current.type == EventType.Repaint)
            {
                if (favoriteIcon == null) favoriteIcon = new FavoriteIcon();
                favoriteIcon.Draw(favoriteRect, favorite);
            }
            string status = item.unpurchased ? "未購入" : "";
            if (page == 1) { int count = Matches(item).Count; status += (status.Length > 0 ? " " : "") + (count > 0 ? count + "アバター対応" : "対応アバターなし"); }
            GUI.Label(new Rect(row.x + height + gap, row.y, row.width - height - detailWidth - gap * 2, height), status, cardFooterLabelStyle);
            if (Clickable.Button(new Rect(row.xMax - detailWidth, row.y, detailWidth, height), "詳細", cardDetailButtonStyle)) Select(item);
        }
        // Match the Windows app's Models/LibraryItem.cs GetCategoryBrush palette.
        static Color32 CategoryColor(AssetCategory category)
        {
            switch (category)
            {
                case AssetCategory.Avatar: return new Color32(111, 78, 220, 255);
                case AssetCategory.Clothing: return new Color32(218, 74, 132, 255);
                case AssetCategory.Hair: return new Color32(184, 105, 55, 255);
                case AssetCategory.Accessory: return new Color32(15, 142, 153, 255);
                case AssetCategory.Texture: return new Color32(47, 126, 213, 255);
                case AssetCategory.Material: return new Color32(82, 99, 176, 255);
                case AssetCategory.Gimmick: return new Color32(220, 112, 24, 255);
                case AssetCategory.Animation: return new Color32(32, 155, 96, 255);
                case AssetCategory.Tool: return new Color32(87, 99, 112, 255);
                case AssetCategory.Shader: return new Color32(127, 76, 170, 255);
                case AssetCategory.World: return new Color32(27, 128, 131, 255);
                default: return new Color32(105, 105, 105, 255);
            }
        }
        Rect DrawCategoryBadge(AssetCategory category, Rect? overlay = null)
        {
            if (categoryBadgeStyle == null)
            {
                categoryBadgeStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                {
                    fontSize = 11, alignment = TextAnchor.MiddleCenter,
                    padding = new RectOffset(8, 8, 3, 3), margin = new RectOffset(),
                    richText = false, wordWrap = false
                };
                categoryBadgeStyle.normal.textColor = Color.white;
            }
            var content = new GUIContent(Categories.Labels[(int)category]);
            float width = categoryBadgeStyle.CalcSize(content).x;
            Rect rect = overlay.HasValue
                ? new Rect(overlay.Value.x + 6, overlay.Value.y + 6, width, 22)
                : GUILayoutUtility.GetRect(width, 22, GUILayout.ExpandWidth(false));
            if (Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, CategoryColor(category), 0, 10);
                GUI.Label(rect, content, categoryBadgeStyle);
            }
            return rect;
        }
        static readonly Color32 BoothBrandColor = new Color32(252, 77, 80, 255);
        Rect DrawUpdateBadge(Rect thumbnail, Rect categoryBadge)
        {
            var content = new GUIContent("BLM更新あり", "BOOTH Library Managerに未読の更新通知があります。");
            float width = categoryBadgeStyle.CalcSize(content).x;
            var rect = new Rect(thumbnail.xMax - 6 - width, thumbnail.y + 6, width, 22);
            // Long category names must not cover the update badge on narrow cards.
            if (rect.x < categoryBadge.xMax + 4) rect.y = categoryBadge.yMax + 4;
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, BoothBrandColor, 0, 10);
            GUI.Label(rect, content, categoryBadgeStyle);
            return rect;
        }
        string[] CardTitle(string title)
        {
            title = title ?? "";
            if (cardTitles.TryGetValue(title, out var cached)) return cached;
            string text = title.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
            float width = CardWidth - CardPadding * 2;
            int firstLength = TitlePrefixLength(text, width, "");
            string second = text.Substring(firstLength).TrimStart();
            if (productText.Width(second, titleStyle) > width)
                second = second.Substring(0, TitlePrefixLength(second, width, "…")).TrimEnd() + "…";
            var lines = new[] { text.Substring(0, firstLength).TrimEnd(), second };
            if (cardTitles.Count >= 1024) cardTitles.Clear();
            cardTitles[title] = lines;
            return lines;
        }
        void EnsureProductStyles()
        {
            if (productText == null) productText = new ProductText();
            if (productFont == null && detailTitleStyle == null)
            {
                var installed = new HashSet<string>(Font.GetOSInstalledFontNames(), StringComparer.OrdinalIgnoreCase);
                var names = new[] { "Yu Gothic UI", "Segoe UI", "Cambria Math", "Segoe UI Symbol", "Segoe UI Emoji" }.Where(installed.Contains).ToArray();
                if (names.Length > 0)
                {
                    productFont = Font.CreateDynamicFontFromOSFont(names, 14);
                    productFont.hideFlags = HideFlags.HideAndDontSave;
                }
            }
            if (detailTitleStyle == null)
                detailTitleStyle = new GUIStyle(EditorStyles.label) { font = productFont, fontSize = 15, richText = false, padding = new RectOffset(), margin = new RectOffset(), alignment = TextAnchor.UpperLeft };
            if (productShopStyle == null)
                productShopStyle = new GUIStyle(EditorStyles.miniLabel) { font = productFont, fontSize = 11, richText = false, padding = new RectOffset(), margin = new RectOffset(), alignment = TextAnchor.UpperLeft };
        }
        void DrawProductLabel(string text, GUIStyle style, float width, bool wrap, int minimumLines = 1)
        {
            text = text ?? "";
            var lines = new List<string>();
            foreach (string paragraph in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                string remaining = paragraph;
                while (wrap && productText.Width(remaining, style) > width)
                {
                    int count = TitlePrefixLength(remaining, width, "", style);
                    if (count == 0) count = StringInfo.GetNextTextElement(remaining).Length;
                    lines.Add(remaining.Substring(0, count)); remaining = remaining.Substring(count);
                }
                lines.Add(remaining);
            }
            float lineHeight = Mathf.Max(style.lineHeight, style.fontSize + 5);
            Rect rect = GUILayoutUtility.GetRect(0, Mathf.Max(minimumLines, lines.Count) * lineHeight, GUILayout.ExpandWidth(true));
            for (int i = 0; i < lines.Count; i++)
                productText.Draw(new Rect(rect.x, rect.y + i * lineHeight, rect.width, lineHeight), lines[i], style);
        }
        int TitlePrefixLength(string text, float width, string suffix, GUIStyle style = null)
        {
            // Measure actual glyph widths and keep surrogate pairs / combining characters intact.
            int[] boundaries = StringInfo.ParseCombiningCharacters(text);
            int low = 0, high = boundaries.Length;
            while (low < high)
            {
                int middle = (low + high + 1) / 2;
                int length = middle == boundaries.Length ? text.Length : boundaries[middle];
                if (productText.Width(text.Substring(0, length).TrimEnd() + suffix, style ?? titleStyle) <= width) low = middle;
                else high = middle - 1;
            }
            return low == boundaries.Length ? text.Length : boundaries[low];
        }
        string FilterPopup(Rect rect, string label, string current, string[] values)
        {
            if (!popupLabels.TryGetValue(values, out var labels)) { labels = new[] { "すべて" }.Concat(values).ToArray(); popupLabels[values] = labels; }
            int index = Array.IndexOf(values, current) + 1; int next = FilterPopup(rect, label, index, labels); return next == 0 ? "" : values[next - 1];
        }
        int FilterPopup(Rect rect, string label, int index, string[] labels)
        {
            EnsureNavigationStyles();
            int next = EditorGUI.Popup(rect, label, index, labels, sortPopupStyle);
            rect.xMin += EditorGUIUtility.labelWidth; Clickable.Cursor(rect);
            return next;
        }
        bool DrawThumbnail(string local, string remote, float height, float scrollY, bool clickable, float viewportHeight = 0, AssetCategory? badge = null, bool updateAvailable = false, float width = 0, bool imported = false)
        {
            Rect rect = width > 0
                ? GUILayoutUtility.GetRect(width, height, GUILayout.Width(width), GUILayout.Height(height))
                : GUILayoutUtility.GetRect(100, height, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint && rect.yMax >= scrollY && rect.y <= scrollY + (viewportHeight > 0 ? viewportHeight : position.height))
            {
                string status = "画像なし";
                var texture = thumbnails?.Get(local, remote, state.consentVersion == BlmDatabaseReader.ConsentVersion, out status);
                EditorGUI.DrawRect(rect, new Color32(46, 46, 46, 255));
            if (texture != null) GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit);
                else
                {
                    EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin ? new Color(.12f, .12f, .12f) : new Color(.84f, .84f, .84f));
                    GUI.Label(rect, new GUIContent(status, status), EditorStyles.centeredGreyMiniLabel);
                }
                if (badge.HasValue)
                {
                    Rect categoryRect = DrawCategoryBadge(badge.Value, rect);
                    Rect updateRect = updateAvailable ? DrawUpdateBadge(rect, categoryRect) : default(Rect);
                    if (imported)
                    {
                        float chipWidth = categoryBadgeStyle.CalcSize(new GUIContent("インポート済")).x;
                        var chip = new Rect(rect.xMax - 6 - chipWidth, rect.y + 6, chipWidth, 22);
                        if (updateAvailable) chip.y = updateRect.yMax + 4;
                        else if (chip.x < categoryRect.xMax + 4) chip.y = categoryRect.yMax + 4;
                        DrawImportedChip(chip, "インポート済");
                    }
                }
            }
            if (!clickable) return false;
            Clickable.Cursor(rect);
            return Clickable.Button(rect, GUIContent.none, GUIStyle.none);
        }
        void DrawCompactSearch(string label)
        {
            EnsureNavigationStyles();
            bool ageNotice = page == 1 && state.hideR18 && ageFilter != BoothAgeFilter.AllAges;
            Rect box = GUILayoutUtility.GetRect(0, page == 1 ? (ageNotice ? 172 : 150) : 48, GUILayout.ExpandWidth(true));
            GUI.Box(box, GUIContent.none, EditorStyles.helpBox);
            float divider = box.xMax - 92;
            EditorGUI.DrawRect(new Rect(divider, box.y + 10, 1, box.height - 20), new Color(.5f, .5f, .5f, .35f));
            Rect content = new Rect(box.x + 12, box.y + 12, divider - box.x - 24, 24);
            float labelWidth = label == "商品名・タグ検索" ? 96 : 78;
            GUI.Label(new Rect(content.x, content.y, labelWidth, 24), label, new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleLeft });
            float toggleWidth = page == 1 ? 206 : 108;
            Rect field = new Rect(content.x + labelWidth, content.y, Mathf.Max(50, content.width - labelWidth - toggleWidth - 8), 24);
            search = EditorGUI.TextField(field, search);
            Rect favoriteRect = new Rect(field.xMax + 8, content.y + 3, 106, 18);
            favorites = EditorGUI.ToggleLeft(favoriteRect, "お気に入り", favorites); Clickable.Cursor(favoriteRect);
            if (page == 1)
            {
                Rect freeRect = new Rect(favoriteRect.xMax, content.y + 3, 100, 18);
                freeOnly = EditorGUI.ToggleLeft(freeRect, "無料／ギフト", freeOnly); Clickable.Cursor(freeRect);
                float previousLabelWidth = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = 88;
                try
                {
                    float columnWidth = (content.width - 16) / 2;
                    Rect left = new Rect(content.x, content.y + 34, columnWidth, NavigationHeight);
                    Rect right = new Rect(left.xMax + 16, left.y, columnWidth, NavigationHeight);
                    shop = FilterPopup(left, "ショップ", shop, Catalog.Shops);
                    tag = FilterPopup(right, "BOOTHタグ", tag, Catalog.Tags);
                    left.y += 34; right.y += 34;
                    boothCategory = FilterPopup(left, "BOOTHカテゴリ", boothCategory, Catalog.BoothCategories);
                    ageFilter = (BoothAgeFilter)FilterPopup(right, "年齢制限", (int)ageFilter, AgeFilterLabels);
                    left.y += 34; right.y += 34;
                    var avatars = Catalog.Avatars;
                    int ai = Array.FindIndex(avatars, x => x.id == avatar) + 1;
                    int next = FilterPopup(left, "対応アバター", ai, Catalog.AvatarLabels);
                    avatar = next <= 0 ? "" : avatars[next - 1].id;
                    if (avatar.Length == 0) excludeShared = excludeNoAvatar = false;
                    Rect shared = new Rect(right.x, right.y + 3, 112, 18);
                    using (new EditorGUI.DisabledScope(avatar.Length == 0))
                    { excludeShared = EditorGUI.ToggleLeft(shared, "共通素体を除外", excludeShared); Clickable.Cursor(shared); }
                    Rect none = new Rect(shared.xMax + 4, shared.y, right.width - shared.width - 4, 18);
                    using (new EditorGUI.DisabledScope(avatar.Length == 0))
                    { excludeNoAvatar = EditorGUI.ToggleLeft(none, "対応アバターなしを除外", excludeNoAvatar); Clickable.Cursor(none); }
                    if (ageNotice) GUI.Label(new Rect(content.x, left.yMax + 4, content.width, 18),
                        "R18商品は表示設定で非表示になっています（表示・連携設定で解除）。", EditorStyles.miniLabel);
                }
                finally { EditorGUIUtility.labelWidth = previousLabelWidth; }
            }
            if (Clickable.Button(new Rect(divider + 12, box.center.y - 12, 68, 24), "リセット"))
            { GUI.FocusControl(null); search = shop = tag = avatar = boothCategory = ""; ageFilter = BoothAgeFilter.AllAges; favorites = freeOnly = excludeShared = excludeNoAvatar = false; category = 0; ResetListPosition(); GUI.changed = true; }
            GUILayout.Space(8);
        }
        void DrawCategoryTabs()
        {
            var style = new GUIStyle(EditorStyles.label) { fontSize = 11, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(12, 12, 0, 0), margin = new RectOffset() };
            var widths = CategoryLabels.Select(label => style.CalcSize(new GUIContent(label)).x).ToArray();
            float available = position.width - 200;
            int start = 0;
            while (start < CategoryLabels.Length)
            {
                int end = start; float used = 0;
                while (end < CategoryLabels.Length)
                {
                    float width = widths[end];
                    if (end > start && used + width > available) break;
                    used += width + 4; end++;
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int i = start; i < end; i++)
                    {
                        var label = new GUIContent(CategoryLabels[i]);
                        Rect rect = GUILayoutUtility.GetRect(widths[i], 28, GUILayout.ExpandWidth(false));
                        bool selected = category == (int)TabCategories[i], hover = rect.Contains(Event.current.mousePosition);
                        Color accent = i == 0 ? new Color32(111, 78, 220, 255) : CategoryColor(TabCategories[i]);
                        if (Event.current.type == EventType.Repaint)
                        {
                            if (selected || hover) GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0,
                                selected ? accent : new Color(.5f, .5f, .5f, .15f), 0, 2);
                            if (hover || selected) EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 2, rect.width, 2), accent);
                        }
                        style.normal.textColor = selected ? Color.white : EditorStyles.label.normal.textColor;
                        style.fontStyle = selected ? FontStyle.Bold : FontStyle.Normal;
                        if (Clickable.Toggle(rect, selected, label, style) && !selected)
                        { category = (int)TabCategories[i]; GUI.changed = true; GUI.FocusControl(null); }
                        GUILayout.Space(4);
                    }
                    GUILayout.FlexibleSpace();
                }
                start = end;
            }
            Rect selectionLine = GUILayoutUtility.GetRect(0, 2, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(selectionLine, category == 0 ? new Color32(111, 78, 220, 255) : CategoryColor((AssetCategory)category));
            GUILayout.Space(6);
        }
        GUIStyle groupTitleStyle;
        void DrawGroups()
        {
            EditorGUI.BeginChangeCheck();
            GUILayout.Label(Pages[page], EditorStyles.boldLabel);
            DrawCompactSearch(page == 2 ? "ショップ検索" : "タグ検索");
            if (EditorGUI.EndChangeCheck()) ResetListPosition();
            bool shops = page == 2;
            string scope = shops ? "shops" : "booth-tags";
            int currentSort = shops ? shopSort : tagSort;
            var groups = Catalog.QueryGroups(scope, search, favorites, state.hideR18, currentSort);
            int pages = Math.Max(1, (groups.Count + 59) / 60); listPage = Mathf.Clamp(listPage, 0, pages - 1);
            using (new EditorGUILayout.HorizontalScope())
            {
                EnsureNavigationStyles();
                GUILayout.Label(groups.Count + "件", navigationLabelStyle, GUILayout.Height(NavigationHeight)); GUILayout.FlexibleSpace();
                int nextSort = Clickable.Popup(currentSort, GroupSortLabels, sortPopupStyle, GUILayout.Width(225), GUILayout.Height(NavigationHeight));
                if (nextSort != currentSort)
                { if (shops) shopSort = nextSort; else tagSort = nextSort; ResetListPosition(); groups = Catalog.QueryGroups(scope, search, favorites, state.hideR18, nextSort); }
                GUILayout.Space(8);
                if (Clickable.LayoutButton("‹", GUILayout.Width(25), GUILayout.Height(NavigationHeight))) { listPage = Math.Max(0, listPage - 1); listScroll = Vector2.zero; }
                GUILayout.Label((listPage + 1) + " / " + pages, navigationLabelStyle, GUILayout.Height(NavigationHeight));
                if (Clickable.LayoutButton("›", GUILayout.Width(25), GUILayout.Height(NavigationHeight))) { listPage = Math.Min(pages - 1, listPage + 1); listScroll = Vector2.zero; }
            }
            EnsureProductStyles();
            GUILayout.Space(6);
            if (groupTitleStyle == null) groupTitleStyle = new GUIStyle(EditorStyles.boldLabel) { font = productFont, fontSize = 12, wordWrap = false, clipping = TextClipping.Clip, padding = new RectOffset(), margin = new RectOffset() };
            if (favoriteIcon == null) favoriteIcon = new FavoriteIcon();
            if (detailActionIcons == null) detailActionIcons = new DetailActionIcons();
            float available = groupViewportWidth > 0 ? groupViewportWidth : position.width - 200;
            const float gap = 8;
            int columns = Mathf.Max(1, Mathf.FloorToInt((available + gap) / ((shops ? 248 : 184) + gap)));
            var visible = groups.Skip(listPage * 60).Take(60).ToArray();
            string selected = null;
            listScroll = EditorGUILayout.BeginScrollView(listScroll);
            for (int i = 0; i < visible.Length; i += columns)
            {
                Rect row = GUILayoutUtility.GetRect(0, shops ? 84 : 64, GUILayout.ExpandWidth(true));
                float width = (row.width - gap * (columns - 1)) / columns;
                for (int j = 0; j < columns && i + j < visible.Length; j++)
                {
                    var group = visible[i + j];
                    Rect card = new Rect(row.x + j * (width + gap), row.y, width, row.height);
                    bool hover = card.Contains(Event.current.mousePosition);
                    if (Event.current.type == EventType.Repaint)
                    {
                        Color background = EditorGUIUtility.isProSkin ? (hover ? new Color32(62,62,62,255) : new Color32(68,68,68,255))
                            : (hover ? new Color32(184,184,184,255) : new Color32(200,200,200,255));
                        GUI.DrawTexture(card, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, background, 0, 4);
                        GUI.DrawTexture(card, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, new Color(0,0,0,.28f), 1, 4);
                    }
                    Rect star = new Rect(card.xMax - 38, card.center.y - 15, 30, 30);
                    bool favorite = Catalog.IsFavorite(scope, group.key);
                    if (Clickable.Button(star, new GUIContent("", favorite ? "お気に入りを解除" : "お気に入りに追加"), GUIStyle.none)) ToggleFavorite(scope, group.key);
                    favoriteIcon.Draw(star, favorite, 18);
                    Rect content = new Rect(card.x, card.y, card.width - 42, card.height);
                    if (Clickable.Button(content, new GUIContent("", group.key + "でアイテムを絞り込む"), GUIStyle.none)) selected = group.key;
                    float left = card.x + 12;
                    if (shops)
                    {
                        Rect thumbnail = new Rect(left, card.center.y - 30, 60, 60);
                        if (Event.current.type == EventType.Repaint && card.yMax >= listScroll.y && card.y <= listScroll.y + position.height)
                        {
                            string status;
                            var texture = thumbnails.Get("", group.item?.shopThumbnailUrl, state.consentVersion == BlmDatabaseReader.ConsentVersion, out status);
                            EditorGUI.DrawRect(thumbnail, EditorGUIUtility.isProSkin ? new Color(.16f,.16f,.16f) : new Color(.75f,.75f,.75f));
                            if (texture != null) GUI.DrawTexture(thumbnail, texture, ScaleMode.ScaleToFit);
                            else detailActionIcons.DrawNavigation(new Rect(thumbnail.center.x - 12, thumbnail.center.y - 12, 24, 24), 2);
                        }
                        left += 70;
                    }
                    else
                    {
                        detailActionIcons.DrawNavigation(new Rect(left, card.center.y - 8, 16, 16), 3);
                        left += 24;
                    }
                    float textWidth = Mathf.Max(1, star.x - left - 6);
                    DrawGroupTitle(new Rect(left, card.y + (shops ? 12 : 7), textWidth, 34), group.key);
                    GUI.Label(new Rect(left, card.yMax - 23, textWidth, 18), group.count + "件", EditorStyles.miniLabel);
                }
                GUILayout.Space(gap);
            }
            if (groups.Count == 0) EditorGUILayout.HelpBox("表示する" + (shops ? "ショップ" : "BOOTHタグ") + "がありません。", MessageType.Info);
            EditorGUILayout.EndScrollView();
            if (Event.current.type == EventType.Repaint)
            {
                float measured = Mathf.Max(1, GUILayoutUtility.GetLastRect().width - GUI.skin.verticalScrollbar.fixedWidth - GUI.skin.verticalScrollbar.margin.left);
                if (Mathf.Abs(groupViewportWidth - measured) > 1) { groupViewportWidth = measured; Repaint(); }
            }
            if (selected != null)
            {
                GUI.FocusControl(null); shop = shops ? selected : ""; tag = shops ? "" : selected;
                avatar = boothCategory = ""; ageFilter = BoothAgeFilter.AllAges; category = 0; freeOnly = excludeShared = excludeNoAvatar = false;
                page = 1; search = ""; favorites = false; ResetListPosition();
            }
        }
        void DrawGroupTitle(Rect rect, string value)
        {
            if (Event.current.type != EventType.Repaint) return;
            string remaining = (value ?? "").Replace('\r', ' ').Replace('\n', ' ');
            for (int line = 0; line < 2 && remaining.Length > 0; line++)
            {
                string text = remaining;
                if (productText.Width(remaining, groupTitleStyle) > rect.width)
                {
                    string suffix = line == 1 ? "…" : "";
                    int length = TitlePrefixLength(remaining, rect.width, suffix, groupTitleStyle);
                    text = remaining.Substring(0, length).TrimEnd() + suffix;
                    remaining = remaining.Substring(length).TrimStart();
                }
                else remaining = "";
                productText.Draw(new Rect(rect.x, rect.y + line * 17, rect.width, 17), text, groupTitleStyle);
            }
        }
        internal void DrawDetailWindow(float viewportWidth, float viewportHeight)
        {
            if (Event.current.type == EventType.Repaint) textureHoverImage = null;
            if (state == null)
            {
                EditorGUILayout.HelpBox("ライブラリを読み取れません。\n" + loadError, MessageType.Error); return;
            }
            if (state.consentVersion != BlmDatabaseReader.ConsentVersion)
            { EditorGUILayout.HelpBox("ライブラリ画面で連携内容を確認してください。", MessageType.Info); return; }
            var item = Catalog.Find(selectedId);
            if (item == null) { EditorGUILayout.HelpBox("一覧から商品を選択してください。", MessageType.Info); return; }
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(DetailLeftWidth(viewportWidth))))
                    DrawDetail(item, viewportHeight);
                GUILayout.Space(10);
                using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
                {
                    if (fileTabStyle == null)
                        fileTabStyle = new GUIStyle(GUI.skin.button)
                        {
                            alignment = TextAnchor.MiddleCenter, fontSize = 11,
                            fixedWidth = 0, fixedHeight = 0,
                            padding = new RectOffset(10, 10, 8, 8), margin = new RectOffset()
                        };
                    Rect tabRow = GUILayoutUtility.GetRect(0, 36, GUILayout.ExpandWidth(true));
                    const float tabGap = 6;
                    float tabWidth = (tabRow.width - tabGap * (FileTabs.Length - 1)) / FileTabs.Length;
                    int nextTab = fileTab;
                    for (int i = 0; i < FileTabs.Length; i++)
                    {
                        var tabRect = new Rect(tabRow.x + i * (tabWidth + tabGap), tabRow.y, tabWidth, tabRow.height);
                        if (DrawFileTab(tabRect, i) && fileTab != i) nextTab = i;
                    }
                    if (nextTab != fileTab)
                    {
                        fileTab = nextTab;
                        pendingFolderExpansion = null;
                        filesScroll = Vector2.zero;
                        GUI.FocusControl(null);
                    }
                    GUILayout.Space(8);
                    if (fileSearchLabelStyle == null)
                    {
                        fileSearchLabelStyle = new GUIStyle(EditorStyles.label) {
                            alignment = TextAnchor.MiddleLeft, padding = new RectOffset(), margin = new RectOffset()
                        };
                        fileSearchFieldStyle = new GUIStyle(EditorStyles.textField) { alignment = TextAnchor.MiddleLeft };
                    }
                    var searchLabel = new GUIContent("ファイル名検索");
                    const float searchIconSize = 16, searchIconGap = 2, searchFieldGap = 12;
                    Rect searchRow = GUILayoutUtility.GetRect(0, 20, GUILayout.ExpandWidth(true));
                    float labelWidth = fileSearchLabelStyle.CalcSize(searchLabel).x;
                    var labelRect = new Rect(searchRow.x + searchIconSize + searchIconGap, searchRow.y, labelWidth, searchRow.height);
                    detailActionIcons.DrawSearch(new Rect(searchRow.x, searchRow.center.y - searchIconSize / 2, searchIconSize, searchIconSize));
                    GUI.Label(labelRect, searchLabel, fileSearchLabelStyle);
                    float fieldX = labelRect.xMax + searchFieldGap;
                    fileSearch = EditorGUI.TextField(new Rect(fieldX, searchRow.y, searchRow.xMax - fieldX, searchRow.height), fileSearch, fileSearchFieldStyle);
                    filesScroll = EditorGUILayout.BeginScrollView(filesScroll);
                    DrawFiles(item);
                    EditorGUILayout.EndScrollView();
                    if (Event.current.type == EventType.Repaint && !GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition)) textureHoverImage = null;
                }
            }
            DrawStatus();
            DrawTextureTooltip(viewportWidth, viewportHeight);
        }
        static float DetailLeftWidth(float viewportWidth) { return Mathf.Clamp((viewportWidth - 40) * .37f, 300, 350); }
        bool DrawFileTab(Rect rect, int index)
        {
            bool selected = Clickable.Toggle(rect, fileTab == index, new GUIContent("", FileTabs[index]), fileTabStyle);
            var label = new GUIContent(FileTabs[index]);
            var style = EditorStyles.miniLabel;
            float textWidth = style.CalcSize(label).x;
            float x = rect.center.x - (16 + 5 + textWidth) / 2;
            if (detailActionIcons == null) detailActionIcons = new DetailActionIcons();
            detailActionIcons.DrawFileTab(new Rect(x, rect.center.y - 8, 16, 16), index);
            GUI.Label(new Rect(x + 21, rect.center.y - 9, textWidth, 18), label, style);
            return selected;
        }
        bool? pendingTagsOpen;
        void DrawDetail(LibraryItem item, float viewportHeight)
        {
            if (Event.current.type == EventType.Layout && pendingTagsOpen.HasValue) { tagsOpen = pendingTagsOpen.Value; pendingTagsOpen = null; }
            EnsureProductStyles();
            detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
            DrawDetailThumbnail(item);
            GUILayout.Space(10);
            DrawDetailTags(item);
            GUILayout.Space(4);
            float textWidth = DetailLeftWidth(detailWindow.position.width) - 22;
            DrawProductLabel(item.title, detailTitleStyle, textWidth, true);
            GUILayout.Space(5);
            DrawDetailShop(item);
            GUILayout.Space(6);
            DrawDetailActions(item);
            GUILayout.Space(8);
            string purchased = item.unpurchased ? "" : item.acquisition == AcquisitionClassifier.FreeGift ? "（無料/ギフト）"
                : item.purchasedVariationNames.Count > 0 ? "（購入済み：" + string.Join(" / ", item.purchasedVariationNames) + "）" : "";
            if (Catalog.Category(item) != AssetCategory.Avatar)
            {
                DrawDetailHeading("対応アバター", purchased, false, textWidth);
                DrawDetailLinks(DetailCompatibilityLinks(Matches(item)), textWidth);
            }
            else if (Catalog.Category(item) == AssetCategory.Avatar)
            {
                DrawDetailHeading("アバター識別タグ", purchased, false, textWidth);
                var avatarMeta = state.metadata.FirstOrDefault(meta => meta.id == item.id);
                string mainName = avatarMeta?.mainName ?? "";
                var identifiers = (avatarMeta?.identifiers ?? new List<string>())
                    .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct().ToList();
                if (!string.IsNullOrWhiteSpace(mainName) && !identifiers.Contains(mainName)) identifiers.Insert(0, mainName);
                var identifierLinks = new List<DetailLink>();
                foreach (string identifier in identifiers)
                {
                    if (identifierLinks.Count > 0) identifierLinks.Add(new DetailLink(" / "));
                    identifierLinks.Add(identifier == mainName
                        ? new DetailLink(identifier, () => FilterFromDetail("", item.id), mainName + "対応のアイテム・アセットを表示")
                        : new DetailLink(identifier));
                }
                if (identifierLinks.Count == 0) identifierLinks.Add(new DetailLink("未設定"));
                var sharedNames = Catalog.Avatars.Where(other => other.id != item.id
                    && ((avatarMeta?.sharedBodies?.Contains(other.id) ?? false)
                        || (state.metadata.FirstOrDefault(meta => meta.id == other.id)?.sharedBodies?.Contains(item.id) ?? false)))
                    .Select(other => AvatarDisplayName(other.id)).ToList();
                if (sharedNames.Count > 0)
                    identifierLinks.Add(new DetailLink("（" + string.Join("・", sharedNames) + "と共通素体）"));
                DrawDetailLinks(identifierLinks, textWidth);
            }
            else if (purchased.Length > 0) GUILayout.Label(purchased, EditorStyles.wordWrappedMiniLabel);
            GUILayout.Space(8);
            var boothTags = item.tags.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct().ToList();
            DrawDetailHeading("BOOTHタグ", "（" + boothTags.Count + "）", true, textWidth);
            var tagLinks = new List<DetailLink>();
            foreach (string value in boothTags.Take(tagsOpen ? boothTags.Count : 10))
            {
                if (tagLinks.Count > 0) tagLinks.Add(new DetailLink(" / "));
                string selectedTag = value;
                tagLinks.Add(new DetailLink(value, () => FilterFromDetail(selectedTag, ""), value + "で絞り込み"));
            }
            if (tagLinks.Count == 0) tagLinks.Add(new DetailLink("タグなし"));
            DrawDetailLinks(tagLinks, textWidth);
            if (boothTags.Count > 10)
            {
                GUILayout.Space(2);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (DrawDetailTagToggle(tagsOpen ? "閉じる" : "すべて見る"))
                    { pendingTagsOpen = !tagsOpen; RepaintViews(); }
                    GUILayout.FlexibleSpace();
                }
            }
            if (!item.unpurchased)
            {
                if (item.updateAvailable) DrawDetailUpdateNotice(textWidth);
            }
            if (item.source == "manual")
            {
                GUILayout.Space(12);
                if (TextureFolderButton(GUILayoutUtility.GetRect(0, 24, GUILayout.ExpandWidth(true)), "登録を削除する", "Delete"))
                {
                    state.SetCategory(item, AssetCategory.Other);
                    state.items.Remove(item); state.metadata.RemoveAll(x => x.id == item.id);
                    state.favorites.RemoveAll(x => x.key == item.id && (x.scope == "items" || x.scope == "avatars"));
                    selectedId = ""; RefreshCatalog();
                }
            }
            EditorGUILayout.EndScrollView();
        }
        bool DrawDetailTagToggle(string text) { return CompactPillButton.Draw(text, 110070); }
        void DrawDetailUpdateNotice(float width)
        {
            const string text = "BOOTH Library Managerにて未読の更新通知があります";
            if (detailNoticeStyle == null) detailNoticeStyle = new GUIStyle(EditorStyles.wordWrappedLabel) {
                fontSize = 12, fontStyle = FontStyle.Bold, padding = new RectOffset(), margin = new RectOffset(),
                normal = { textColor = Color.white }
            };
            GUILayout.Space(6);
            float textHeight = detailNoticeStyle.CalcHeight(new GUIContent(text), width - 49);
            Rect rect = GUILayoutUtility.GetRect(0, Mathf.Max(16, textHeight) + 18, GUILayout.ExpandWidth(true));
            if (Event.current.type != EventType.Repaint) return;
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, BoothBrandColor, 0, 7);
            detailActionIcons.DrawDownload(new Rect(rect.x + 12, rect.center.y - 8, 16, 16));
            GUI.Label(new Rect(rect.x + 37, rect.center.y - textHeight / 2, rect.width - 49, textHeight), text, detailNoticeStyle);
        }
        void DrawDetailThumbnail(LibraryItem item)
        {
            string status = "画像なし";
            var texture = thumbnails?.Get(item.thumbnailPath, item.thumbnailUrl,
                state.consentVersion == BlmDatabaseReader.ConsentVersion, out status);
            Rect area = GUILayoutUtility.GetRect(0, 240, GUILayout.ExpandWidth(true));
            Rect rect = new Rect(area.center.x - 120, area.y, 240, 240);
            if (Event.current.type != EventType.Repaint) return;
            EditorGUI.DrawRect(area, new Color32(46, 46, 46, 255));
            if (texture != null) GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit);
            else
            {
                GUI.Label(rect, new GUIContent(status, status), EditorStyles.centeredGreyMiniLabel);
            }
        }
        void DrawDetailShop(LibraryItem item)
        {
            if (string.IsNullOrWhiteSpace(item.shop)) return;
            if (favoriteIcon == null) favoriteIcon = new FavoriteIcon();
            Rect rect = GUILayoutUtility.GetRect(0, 29, GUILayout.ExpandWidth(true));
            bool hover = rect.Contains(Event.current.mousePosition);
            string tooltip = item.shop + "のアイテム・アセットを表示";
            Clickable.Cursor(rect);
            if (Event.current.type == EventType.Repaint)
            {
                bool dark = EditorGUIUtility.isProSkin;
                Color fill = dark ? new Color(.23f, .23f, .23f) : new Color(.9f, .9f, .9f);
                if (hover) fill = dark ? new Color(.29f, .29f, .29f) : new Color(.96f, .96f, .96f);
                GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, fill, 0, 4);
                GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0,
                    dark ? new Color(.38f, .38f, .38f) : new Color(.65f, .65f, .65f), 1, 4);
                float inset = (rect.height - 20) / 2;
                var thumbnailRect = new Rect(rect.x + inset, rect.y + inset, 20, 20);
                string status;
                var texture = thumbnails?.Get("", item.shopThumbnailUrl, state.consentVersion == BlmDatabaseReader.ConsentVersion, out status);
                if (texture != null) GUI.DrawTexture(thumbnailRect, texture, ScaleMode.ScaleAndCrop);
                else GUI.Label(thumbnailRect, "店", EditorStyles.centeredGreyMiniLabel);
                var labelRect = new Rect(thumbnailRect.xMax + 10, rect.center.y - 9, rect.width - inset * 2 - 60, 18);
                string name = item.shop;
                if (EditorStyles.label.CalcSize(new GUIContent(name)).x > labelRect.width)
                {
                    var elements = StringInfo.ParseCombiningCharacters(name);
                    int count = elements.Length;
                    while (count > 0 && EditorStyles.label.CalcSize(new GUIContent(name.Substring(0, elements[--count]) + "…")).x > labelRect.width) { }
                    name = name.Substring(0, elements[count]) + "…";
                }
                GUI.Label(labelRect, new GUIContent(name, tooltip), EditorStyles.label);
            }
            Rect star = new Rect(rect.xMax - 29, rect.y, 29, rect.height);
            if (Clickable.StableButton(star, 110090, new GUIContent("", "ショップをお気に入りに登録／解除"), GUIStyle.none)) ToggleFavorite("shops", item.shop);
            favoriteIcon.Draw(new Rect(star.center.x - 9, star.center.y - 9, 18, 18), Catalog.IsFavorite("shops", item.shop), 18);
            Rect linkRect = new Rect(rect.x, rect.y, rect.width - 29, rect.height);
            if (Clickable.StableButton(linkRect, 110091, new GUIContent("", tooltip), GUIStyle.none))
            {
                GUI.FocusControl(null); shop = item.shop; page = 1;
                search = tag = avatar = boothCategory = ""; ageFilter = BoothAgeFilter.AllAges; category = 0; favorites = freeOnly = excludeShared = excludeNoAvatar = false;
                ResetListPosition(); Show(); Focus(); RepaintViews();
            }
        }
        string AvatarDisplayName(string id)
        {
            string name = state.metadata.FirstOrDefault(meta => meta.id == id)?.mainName;
            return string.IsNullOrWhiteSpace(name) ? (Catalog.Find(id)?.title ?? id) : name;
        }
        List<DetailLink> DetailCompatibilityLinks(List<AvatarMatch> matches)
        {
            var parts = new List<DetailLink>();
            if (matches.Count == 0) { parts.Add(new DetailLink("対応アバターなし")); return parts; }
            var included = new HashSet<string>();
            void AddAvatar(AvatarMatch match)
            {
                string id = match.avatarId;
                parts.Add(new DetailLink(AvatarDisplayName(id), () => FilterFromDetail("", id), match.reason));
            }
            foreach (var direct in matches.Where(match => !match.indirect))
            {
                string name = AvatarDisplayName(direct.avatarId);
                var shared = new List<AvatarMatch>();
                var origins = new Queue<string>();
                origins.Enqueue(name);
                while (origins.Count > 0)
                {
                    string reason = "共通素体: " + origins.Dequeue();
                    foreach (var match in matches.Where(match => match.indirect && match.reason == reason))
                        if (included.Add(match.avatarId))
                        {
                            string sharedName = AvatarDisplayName(match.avatarId);
                            shared.Add(match); origins.Enqueue(sharedName);
                        }
                }
                if (parts.Count > 0) parts.Add(new DetailLink(" / "));
                AddAvatar(direct);
                if (shared.Count > 0)
                {
                    parts.Add(new DetailLink("（"));
                    for (int i = 0; i < shared.Count; i++)
                    {
                        if (i > 0) parts.Add(new DetailLink("・"));
                        AddAvatar(shared[i]);
                    }
                    parts.Add(new DetailLink("）"));
                }
            }
            // An excluded intermediate avatar can leave a shared-body result without a visible parent.
            foreach (var match in matches.Where(match => match.indirect && !included.Contains(match.avatarId)))
            {
                if (parts.Count > 0) parts.Add(new DetailLink(" / "));
                parts.Add(new DetailLink("（")); AddAvatar(match); parts.Add(new DetailLink("）"));
            }
            return parts;
        }
        sealed class DetailLink
        {
            internal string text, tooltip;
            internal Action click;
            internal DetailLink(string text, Action click = null, string tooltip = "")
            { this.text = text; this.click = click; this.tooltip = tooltip; }
        }
        void FilterFromDetail(string selectedTag, string selectedAvatar)
        {
            GUI.FocusControl(null); page = 1; search = shop = boothCategory = ""; ageFilter = BoothAgeFilter.AllAges; category = 0;
            tag = selectedTag; avatar = selectedAvatar; favorites = freeOnly = excludeShared = excludeNoAvatar = false;
            ResetListPosition(); Show(); Focus(); RepaintViews();
        }
        static string SingleLineEllipsis(string text, GUIStyle style, float width)
        {
            text = (text ?? "").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
            if (style.CalcSize(new GUIContent(text)).x <= width) return text;
            if (style.CalcSize(new GUIContent("…")).x > width) return "";
            int[] boundaries = StringInfo.ParseCombiningCharacters(text);
            int low = 0, high = boundaries.Length;
            while (low < high)
            {
                int mid = (low + high + 1) / 2;
                int end = mid == boundaries.Length ? text.Length : boundaries[mid];
                if (style.CalcSize(new GUIContent(text.Substring(0, end) + "…")).x <= width) low = mid; else high = mid - 1;
            }
            return text.Substring(0, low == boundaries.Length ? text.Length : boundaries[low]) + "…";
        }
        void DrawDetailHeading(string title, string suffix, bool booth, float width)
        {
            if (detailActionIcons == null) detailActionIcons = new DetailActionIcons();
            var titleStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleLeft, padding = new RectOffset(), margin = new RectOffset() };
            var suffixStyle = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleLeft, padding = new RectOffset(), margin = new RectOffset(), wordWrap = false };
            Rect row = GUILayoutUtility.GetRect(0, 24, GUILayout.ExpandWidth(true));
            var icon = new Rect(row.x, row.center.y - 8, 16, 16);
            if (booth) detailActionIcons.DrawTag(icon); else detailActionIcons.DrawAvatar(icon);
            float titleWidth = titleStyle.CalcSize(new GUIContent(title)).x;
            GUI.Label(new Rect(row.x + 22, row.y, titleWidth, row.height), title, titleStyle);
            var suffixRect = new Rect(row.x + 22 + titleWidth + 6, row.y, Mathf.Max(0, row.width - 28 - titleWidth), row.height);
            GUI.Label(suffixRect, new GUIContent(SingleLineEllipsis(suffix, suffixStyle, suffixRect.width), suffix), suffixStyle);
        }
        void DrawDetailLinks(List<DetailLink> links, float width)
        {
            if (detailLinkStyle == null) detailLinkStyle = new GUIStyle(EditorStyles.label) {
                padding = new RectOffset(), margin = new RectOffset(), alignment = TextAnchor.MiddleLeft, richText = false
            };
            const float lineHeight = 19;
            var pieces = new List<Tuple<Rect, string, DetailLink>>();
            float x = 0, y = 0;
            foreach (var link in links)
            {
                string remaining = link.text;
                float fullWidth = detailLinkStyle.CalcSize(new GUIContent(remaining)).x;
                if (x > 0 && x + fullWidth > width) { x = 0; y += lineHeight; }
                while (remaining.Length > 0)
                {
                    var elements = StringInfo.ParseCombiningCharacters(remaining);
                    int count = elements.Length;
                    string piece = remaining;
                    while (count > 1 && detailLinkStyle.CalcSize(new GUIContent(piece)).x > width - x)
                        piece = remaining.Substring(0, elements[--count]);
                    float size = detailLinkStyle.CalcSize(new GUIContent(piece)).x;
                    pieces.Add(Tuple.Create(new Rect(x, y, size, lineHeight), piece, link));
                    remaining = remaining.Substring(piece.Length); x += size;
                    if (remaining.Length > 0) { x = 0; y += lineHeight; }
                }
            }
            Rect area = GUILayoutUtility.GetRect(0, y + lineHeight, GUILayout.ExpandWidth(true));
            foreach (var piece in pieces)
            {
                Rect rect = piece.Item1; rect.position += area.position;
                var link = piece.Item3;
                GUI.Label(rect, new GUIContent(piece.Item2, link.tooltip), detailLinkStyle);
                if (link.click == null) continue;
                Clickable.Cursor(rect);
                if (Event.current.type == EventType.Repaint && rect.Contains(Event.current.mousePosition))
                    EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 2, rect.width, 1), detailLinkStyle.normal.textColor);
                if (Clickable.Button(rect, GUIContent.none, GUIStyle.none)) link.click();
            }
        }
        void DrawDetailTags(LibraryItem item)
        {
            if (detailTagStyle == null) detailTagStyle = new GUIStyle(EditorStyles.miniBoldLabel) {
                fontSize = 11, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(8, 8, 1, 1),
                margin = new RectOffset(), wordWrap = false, richText = false
            };
            Rect row = GUILayoutUtility.GetRect(0, 18, GUILayout.ExpandWidth(true));
            float x = row.x;
            var category = Catalog.Category(item);
            DrawDetailTag(ref x, row.y, Categories.Labels[(int)category], CategoryColor(category), Color.white);
            var boothStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleLeft, padding = new RectOffset(), wordWrap = false };
            float reserved = item.unpurchased ? detailTagStyle.CalcSize(new GUIContent("未購入")).x + 5 : 0;
            Rect boothRect = new Rect(x + 3, row.y, Mathf.Max(0, row.xMax - x - 3 - reserved), row.height);
            GUI.Label(boothRect, new GUIContent(SingleLineEllipsis(item.originalCategory, boothStyle, boothRect.width), item.originalCategory), boothStyle);
            if (item.unpurchased)
            {
                float chipX = row.xMax - reserved + 5;
                DrawDetailTag(ref chipX, row.y, "未購入", new Color32(201, 79, 120, 255), Color.white);
            }
        }
        void DrawDetailTag(ref float x, float y, string text, Color background, Color foreground, Color? border = null)
        {
            var content = new GUIContent(text);
            var rect = new Rect(x, y, detailTagStyle.CalcSize(content).x, 18);
            if (Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, background, 0, 9);
                if (border.HasValue) GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, border.Value, 1, 9);
                detailTagStyle.normal.textColor = foreground;
                GUI.Label(rect, content, detailTagStyle);
            }
            x += rect.width + 5;
        }
        void DrawDetailActions(LibraryItem item)
        {
            if (detailActionStyle == null) detailActionStyle = new GUIStyle(EditorStyles.label) {
                fontSize = 11, alignment = TextAnchor.MiddleCenter, wordWrap = false,
                padding = new RectOffset(), margin = new RectOffset()
            };
            if (detailActionIcons == null) detailActionIcons = new DetailActionIcons();
            if (favoriteIcon == null) favoriteIcon = new FavoriteIcon();
            const float gap = 5, height = 52;
            Rect row = GUILayoutUtility.GetRect(0, height, GUILayout.ExpandWidth(true));
            float width = (row.width - gap * 3) / 4;
            Rect Cell(int index) { return new Rect(row.x + index * (width + gap), row.y, width, height); }
            if (DrawDetailAction(Cell(0), "お気に入り", "お気に入り", 0, Catalog.IsFavorite(Catalog.Scope(item), item.id))) ToggleFavorite(Catalog.Scope(item), item.id);
            if (DrawDetailAction(Cell(1), "情報を編集", EditTitle(item), 2)) OpenEdit(item);
            using (new EditorGUI.DisabledScope(BlmDatabaseReader.BoothId(item.url).Length == 0))
                if (DrawDetailAction(Cell(2), "商品ページ", "商品ページ", 1)) Application.OpenURL(item.url);
            using (new EditorGUI.DisabledScope(item.unpurchased || string.IsNullOrEmpty(item.folder)))
                if (DrawDetailAction(Cell(3), "保存フォルダ", "保存フォルダを開く", 3)) OpenSavedFolder(item.folder);
        }
        void OpenSavedFolder(string folder)
        {
            try
            {
                if (!Directory.Exists(folder)) { message = "保存フォルダが見つかりません。"; return; }
                using (System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
                    FileName = Path.GetFullPath(folder), UseShellExecute = true
                })) { }
            }
            catch (Exception ex) { message = "保存フォルダを開けませんでした: " + ex.Message; }
        }
        bool DrawDetailAction(Rect rect, string label, string tooltip, int icon, bool selected = false)
        {
            bool clicked = Clickable.StableButton(rect, 110080 + icon, new GUIContent("", tooltip));
            var iconRect = new Rect(rect.center.x - 10, rect.y + 7, 20, 20);
            var before = GUI.color;
            try
            {
                if (!GUI.enabled) GUI.color *= new Color(1, 1, 1, .45f);
                if (icon == 0) favoriteIcon.Draw(iconRect, selected, 20);
                else if (icon == 1 || icon == 2) detailActionIcons.Draw(iconRect, icon == 2);
                else GUI.DrawTexture(iconRect, EditorGUIUtility.IconContent("Folder Icon").image, ScaleMode.ScaleToFit);
            }
            finally { GUI.color = before; }
            GUI.Label(new Rect(rect.x + 2, rect.y + 30, rect.width - 4, 16), new GUIContent(label, tooltip), detailActionStyle);
            return clicked;
        }
        internal string DrawEditWindow(string itemId)
        {
            if (state == null || state.consentVersion != BlmDatabaseReader.ConsentVersion)
            { EditorGUILayout.HelpBox("ライブラリ画面で読み込み・連携状態を確認してください。", MessageType.Info); return "情報を編集"; }
            var item = Catalog.Find(itemId);
            if (item == null) { EditorGUILayout.HelpBox("この商品は登録されていません。", MessageType.Info); return "情報を編集"; }
            GUILayout.Label(item.title, EditorStyles.wordWrappedLabel);
            GUILayout.Space(8);
            DrawEdit(item);
            return EditTitle(item);
        }
        void DrawEdit(LibraryItem item)
        {
            var meta = state.Meta(item.id);
            EditorGUI.BeginChangeCheck();
            if (item.source == "manual")
            {
                item.title = ManualEditTextField("表示名", item.title);
                if (!item.unpurchased) DrawManualItemFields(item);
            }
                        EnsureNavigationStyles();
            Rect categoryRow = EditorGUILayout.GetControlRect(false, NavigationHeight);
            var value = (AssetCategory)FilterPopup(categoryRow, "分類", (int)state.Category(item), Categories.Labels);
            if (value != state.Category(item)) state.SetCategory(item, value);
            if (value == AssetCategory.Avatar)
            {
                string mainName = EditorGUILayout.TextField("アバター名", meta.mainName);
                if (mainName != meta.mainName) { meta.mainName = mainName; meta.identifiersEdited = true; }
                DrawIdentifierInputs(meta);
                GUILayout.Space(8);
                DrawDetailHeading("共通素体（相互対応）", "", false, EditorGUIUtility.currentViewWidth - 44);
                foreach (var other in Catalog.Avatars.Where(x => x.id != item.id))
                {
                    var related = state.Meta(other.id);
                    bool has = meta.sharedBodies.Contains(other.id) || related.sharedBodies.Contains(item.id);
                    bool next = Clickable.ToggleLeft(EditAvatarName(other), has);
                    if (next == has) continue;
                    meta.sharedBodies.Remove(other.id); related.sharedBodies.Remove(item.id);
                    if (next) { meta.sharedBodies.Add(other.id); related.sharedBodies.Add(item.id); }
                }
                if (meta.identifiers.Any(x => !string.IsNullOrWhiteSpace(x) && !Matcher.ValidIdentifier(x))) EditorGUILayout.HelpBox("自動候補では除外される語があります。手動タグの内容が意図どおりか確認してください。", MessageType.Info);
            }
            else if (Categories.HasAvatarCompatibility(value))
            {
                GUILayout.Space(8);
                DrawDetailHeading("対応アバターの手動設定", "", false, EditorGUIUtility.currentViewWidth - 44);
                var currentMatches = Matches(item).ToDictionary(match => match.avatarId);
                Rect nameCell, settingCell, resultCell;
                EditAvatarTableRow(-1, out nameCell, out settingCell, out resultCell);
                GUI.Label(nameCell, "アバター名", EditorStyles.boldLabel);
                GUI.Label(settingCell, "設定", EditorStyles.boldLabel);
                GUI.Label(resultCell, "現在の判定", EditorStyles.boldLabel);
                int rowIndex = 0;
                foreach (var other in Catalog.Avatars)
                {
                    EditAvatarTableRow(rowIndex++, out nameCell, out settingCell, out resultCell);
                    string avatarName = EditAvatarName(other);
                    GUI.Label(nameCell, new GUIContent(avatarName, avatarName), EditorStyles.label);
                    int mode = meta.excludedAvatars.Contains(other.id) ? 2 : meta.compatibleAvatars.Contains(other.id) ? 1 : 0;
                    int next = EditorGUI.Popup(settingCell, mode, new[] { "自動", "対応", "除外" }, sortPopupStyle);
                    Clickable.Cursor(settingCell);
                    if (next != mode) { meta.compatibleAvatars.Remove(other.id); meta.excludedAvatars.Remove(other.id); if (next == 1) meta.compatibleAvatars.Add(other.id); if (next == 2) meta.excludedAvatars.Add(other.id); }
                    AvatarMatch match;
                    bool supported = currentMatches.TryGetValue(other.id, out match);
                    string reason = supported ? match.reason : mode == 2 ? "手動で除外されています。" : "現在の判定では対応アバターに含まれていません。";
                    GUI.Label(resultCell, new GUIContent(supported ? "対応" : "非対応", "現在の判定: " + reason), EditorStyles.label);
                }
            }
            if (EditorGUI.EndChangeCheck()) RefreshCatalog();
        }
        string EditAvatarName(LibraryItem item)
        {
            string name = state.metadata.FirstOrDefault(x => x.id == item.id)?.mainName;
            return string.IsNullOrWhiteSpace(name) ? item.title : name;
        }
        static Rect ManualEditField(string label)
        {
            Rect row = EditorGUILayout.GetControlRect(false, NavigationHeight);
            GUI.Label(new Rect(row.x, row.y, EditorGUIUtility.labelWidth, row.height), label,
                new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleLeft });
            return new Rect(row.x + EditorGUIUtility.labelWidth, row.y, Mathf.Max(0, row.width - EditorGUIUtility.labelWidth), row.height);
        }
        static string ManualEditTextField(string label, string value)
        {
            return EditorGUI.TextField(ManualEditField(label), value, new GUIStyle(EditorStyles.textField) { fixedHeight = NavigationHeight, alignment = TextAnchor.MiddleLeft });
        }
        void DrawManualItemFields(LibraryItem item)
        {
            EnsureNavigationStyles();
            Rect field = ManualEditField("登録済みショップ");
            if (Clickable.Button(field, new GUIContent(string.IsNullOrWhiteSpace(item.shop) ? "ショップを選択…" : item.shop, "登録済みのショップを検索して選択"), sortPopupStyle))
            {
                string id = item.id;
                PopupWindow.Show(field, new SearchableShopPopup(ManualItemEditing.Shops(state.items), item.shop, field.width, selected =>
                {
                    var current = state.items.Find(i => i.id == id);
                    if (current == null) return;
                    ManualItemEditing.SetShop(current, selected, state.items); Changed();
                }));
            }
            string editedShop = ManualEditTextField("ショップ（自由入力）", item.shop);
            if (editedShop != item.shop) ManualItemEditing.SetShop(item, editedShop, state.items);
            GUILayout.Space(6);
            GUILayout.Label("サムネイル", EditorStyles.boldLabel);
            if (!Directory.Exists(item.folder)) EditorGUILayout.HelpBox("保存フォルダが見つからないため画像を選択できません。", MessageType.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                Rect preview = GUILayoutUtility.GetRect(72, 72, GUILayout.Width(72));
                EditorGUI.DrawRect(preview, new Color(.18f, .18f, .18f));
                var texture = thumbnails.Get(item.thumbnailPath, "", true, out string status);
                if (texture != null) GUI.DrawTexture(preview, texture, ScaleMode.ScaleToFit);
                else GUI.Label(preview, string.IsNullOrEmpty(item.thumbnailPath) ? "未設定" : status, EditorStyles.centeredGreyMiniLabel);
                using (new EditorGUILayout.VerticalScope())
                {
                    using (new EditorGUI.DisabledScope(!Directory.Exists(item.folder)))
                        if (Clickable.LayoutButton("フォルダ内のテクスチャから選択…", GUILayout.Height(24))) ChooseManualThumbnail(item);
                    GUILayout.Label("PNG / JPEG・32MB・4096pxまで", EditorStyles.miniLabel);
                    using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(item.thumbnailPath)))
                        if (Clickable.LayoutButton("設定を解除", GUILayout.Width(90))) { item.thumbnailPath = ""; GUI.changed = true; }
                }
            }
            GUILayout.Space(8);
        }
        void ChooseManualThumbnail(LibraryItem item)
        {
            string id = item.id;
            ManualThumbnailPicker.Open(item.folder, path =>
            {
                var current = state.items.Find(i => i.id == id);
                if (current == null) return;
                current.thumbnailPath = path; thumbnails.Clear(); Changed();
            });
        }
        static void EditAvatarTableRow(int index, out Rect name, out Rect setting, out Rect result)
        {
            Rect row = GUILayoutUtility.GetRect(0, 32, GUILayout.ExpandWidth(true));
            float settingX = row.xMax - 188, resultX = row.xMax - 96;
            if (Event.current.type == EventType.Repaint)
            {
                bool dark = EditorGUIUtility.isProSkin;
                float shade = index < 0 ? (dark ? .19f : .73f) : index % 2 == 0 ? (dark ? .25f : .85f) : (dark ? .27f : .89f);
                EditorGUI.DrawRect(row, new Color(shade, shade, shade));
                Color line = dark ? new Color(1, 1, 1, .08f) : new Color(0, 0, 0, .1f);
                EditorGUI.DrawRect(new Rect(row.x, row.yMax - 1, row.width, 1), line);
                EditorGUI.DrawRect(new Rect(settingX, row.y, 1, row.height), line);
                EditorGUI.DrawRect(new Rect(resultX, row.y, 1, row.height), line);
            }
            float y = row.y + (row.height - EditorGUIUtility.singleLineHeight) / 2;
            name = new Rect(row.x + 8, y, Mathf.Max(0, settingX - row.x - 16), EditorGUIUtility.singleLineHeight);
            setting = new Rect(settingX + 8, row.center.y - 12, 76, 24);
            result = new Rect(resultX + 8, y, 80, EditorGUIUtility.singleLineHeight);
        }
        void DrawIdentifierInputs(ItemMetadata meta)
        {
            GUILayout.Space(8);
            DrawDetailHeading("識別タグ", "", true, EditorGUIUtility.currentViewWidth - 44);
            GUILayout.Label("識別タグの文字で対応アバターの自動判定を行います", EditorStyles.wordWrappedMiniLabel);
            GUILayout.Space(4);
            Rect nameRow = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            using (new EditorGUI.DisabledScope(true))
                EditorGUI.TextField(new Rect(nameRow.x, nameRow.y, nameRow.width - 76, nameRow.height), meta.mainName ?? "");
            GUI.Label(new Rect(nameRow.xMax - 72, nameRow.y, 72, nameRow.height), "アバター名",
                new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleLeft });
            // The matcher always includes mainName. Show it once as a fixed row,
            // preserving existing metadata and letting only additional aliases be edited.
            int removeIndex = -1;
            string main = Matcher.Normalize(meta.mainName);
            for (int i = 0; i < meta.identifiers.Count; i++)
            {
                if (main.Length > 0 && Matcher.Normalize(meta.identifiers[i]) == main) continue;
                using (new EditorGUILayout.HorizontalScope())
                {
                    string next = EditorGUILayout.TextField(meta.identifiers[i]);
                    if (next != meta.identifiers[i]) { meta.identifiers[i] = next; meta.identifiersEdited = true; }
                    if (Clickable.LayoutButton("削除", GUILayout.Width(72))) removeIndex = i;
                }
            }
            if (removeIndex >= 0)
            {
                meta.identifiers.RemoveAt(removeIndex);
                meta.identifiersEdited = true;
                GUI.changed = true;
                GUI.FocusControl(null);
            }
            if (Clickable.LayoutButton("＋ 識別タグを追加", GUILayout.Height(24)))
            {
                meta.identifiers.Add("");
                meta.identifiersEdited = true;
                GUI.changed = true;
            }
        }
        void ScanFiles(LibraryItem item)
        {
            string id = item.id, folder = item.folder;
            Run(token => FileCatalog.Scan(folder, token), result =>
            {
                Catalog.SetFilenames(id, result.files.Select(x => x.relative));
                if (detailWindow == null || selectedId != id) return;
                scan = result; scanItemId = id;
                message = string.Join("\n", result.warnings);
            }, "商品フォルダーを走査中…");
        }
        static string DetailFileKind(CatalogFile file)
        {
            return Array.IndexOf(FileTabs, file.kind) >= 0 ? file.kind : "その他ファイル";
        }
        void DrawFiles(LibraryItem item)
        {
            if (Event.current.type == EventType.Layout && pendingBatchSelecting.HasValue)
            { batchSelecting = pendingBatchSelecting.Value; pendingBatchSelecting = null; batchSelection.Clear(); }
            if (item.unpurchased || string.IsNullOrEmpty(item.folder))
            { GUILayout.Label("この商品の保存先は登録されていません。", EditorStyles.wordWrappedMiniLabel); return; }
            if (scan == null || scanItemId != item.id)
            { GUILayout.Label(work != null || scanAttemptId != item.id ? "ファイル一覧を取得中…" : "取得できませんでした。商品を選び直すと再取得します。", EditorStyles.wordWrappedMiniLabel); return; }
            string query = Matcher.Normalize(fileSearch);
            var groups = scan.files.Where(x => DetailFileKind(x) == FileTabs[fileTab] && Matcher.Normalize(x.relative).Contains(query))
                .GroupBy(DetailFileKind).ToList();
            if (FileTabs[fileTab] == "UnityPackage") DrawBatchHeading(item, groups.Sum(g => g.Count()));
            if (groups.Count == 0)
            {
                GUILayout.Space(8);
                Rect empty = GUILayoutUtility.GetRect(0, 54, GUILayout.ExpandWidth(true));
                GUI.Box(empty, GUIContent.none, EditorStyles.helpBox);
                detailActionIcons.DrawFileTab(new Rect(empty.x + 10, empty.center.y - 10, 20, 20), fileTab);
                GUI.Label(new Rect(empty.x + 40, empty.center.y - 10, Mathf.Max(0, empty.width - 50), 20),
                    "該当する" + FileTabs[fileTab] + "はありません。", EditorStyles.boldLabel);
            }
            foreach (var group in groups)
            {
                if (group.Key != "UnityPackage")
                {
                GUILayout.Space(8);
                Rect heading = GUILayoutUtility.GetRect(0, 24, GUILayout.ExpandWidth(true));
                GUI.Label(new Rect(heading.x, heading.center.y - 9, Mathf.Max(0, heading.width - 132), 18),
                    group.Key + "（" + group.Count() + "）", EditorStyles.boldLabel);
                if (group.Key != "UnityPackage")
                {
                    bool all = textureTreeKind == group.Key && textureTreeQuery == query && textureFolderCount > 0 && expandedTextureFolders.Count == textureFolderCount;
                    if (TextureFolderButton(new Rect(heading.xMax - 126, heading.y, 126, 24), all ? "すべて閉じる" : "すべて展開", all ? "CollapseAll" : "ExpandAll"))
                    { pendingFolderExpansion = !all; RepaintViews(); }
                }
                GUILayout.Space(4);
                }
                if (group.Key == "UnityPackage")
                    foreach (var card in scan.packages.Where(x => PackageMatches(x, query))) DrawPackageCard(item, card, query, 0);
                else DrawTextureTree(item, group, query);
            }
        }
        void DrawBatchHeading(LibraryItem item, int count)
        {
            GUILayout.Space(8);
            Rect row = GUILayoutUtility.GetRect(0, 24, GUILayout.ExpandWidth(true));
            GUI.Label(new Rect(row.x, row.y + 3, Mathf.Max(0, row.width - (batchSelecting ? 260 : 140)), 18), "UnityPackage（" + count + "）", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(ImportCoordinator.Busy))
            {
                if (!batchSelecting)
                {
                    using (new EditorGUI.DisabledScope(!scan.files.Any(f => DetailFileKind(f) == "UnityPackage")))
                        if (BatchImportActionButton(new Rect(row.xMax - 132, row.y, 132, 24), 93121, "一括インポート"))
                        { pendingBatchSelecting = true; RepaintViews(); }
                }
                else
                {
                    if (Clickable.StableButton(new Rect(row.xMax - 252, row.y, 90, 24), 93122, new GUIContent("キャンセル")))
                    { pendingBatchSelecting = false; RepaintViews(); }
                    using (new EditorGUI.DisabledScope(batchSelection.Count == 0))
                        if (BatchImportActionButton(new Rect(row.xMax - 156, row.y, 156, 24), 93123, batchSelection.Count + "個を一括インポート"))
                        {
                            try { ImportCoordinator.BeginBatch(batchSelection.ToArray(), item.id, state.Target(state.Category(item)), item.title, item.ageRestricted); pendingBatchSelecting = false; RepaintViews(); }
                            catch (Exception ex) { message = ex.Message; }
                        }
                }
            }
            GUILayout.Space(4);
        }
        [NonSerialized] GUIStyle batchActionLabelStyle;
        bool BatchImportActionButton(Rect rect, int hint, string label)
        {
            bool clicked = Clickable.StableButton(rect, hint, new GUIContent("", label));
            if (batchActionLabelStyle == null)
                batchActionLabelStyle = new GUIStyle(EditorStyles.label) {
                    fontSize = 11, fontStyle = FontStyle.Normal, alignment = TextAnchor.MiddleLeft,
                    padding = new RectOffset(), margin = new RectOffset(), wordWrap = false, clipping = TextClipping.Clip
                };
            // GUIStyle caches must not survive script reload as partially restored native styles.
            // Resolve the current theme explicitly instead of inheriting a stale label text color.
            batchActionLabelStyle.normal.textColor = EditorGUIUtility.isProSkin ? new Color(.8f, .8f, .8f) : new Color(.3f, .3f, .3f);
            float textWidth = batchActionLabelStyle.CalcSize(new GUIContent(label)).x;
            float x = Mathf.Round(rect.center.x - (16 + 6 + textWidth) / 2);
            var icon = new Rect(x, rect.center.y - 8, 16, 16);
            if (ImportCoordinator.CanCancelPreparation) DrawImportSpinner(icon);
            // GUI.Label applies Unity's disabled/content tint just like the adjacent text.
            else GUI.Label(icon, EditorGUIUtility.IconContent("Import"), GUIStyle.none);
            GUI.Label(new Rect(x + 22, rect.y, textWidth, rect.height), label, batchActionLabelStyle);
            return clicked;
        }
        static void DrawImportSpinner(Rect rect)
        {
            if (Event.current.type != EventType.Repaint) return;
            var matrix = GUI.matrix;
            var color = GUI.color;
            int frame = (int)(EditorApplication.timeSinceStartup * 12) % 12;
            try
            {
                for (int i = 0; i < 12; i++)
                {
                    GUI.matrix = matrix;
                    GUIUtility.RotateAroundPivot(i * 30, rect.center);
                    float alpha = .2f + .8f * (1 - (i - frame + 12) % 12 / 11f);
                    Color tint = EditorGUIUtility.isProSkin ? new Color(.85f, .85f, .85f, alpha) : new Color(.25f, .25f, .25f, alpha);
                    GUI.color = color * tint;
                    GUI.Label(new Rect(rect.center.x - 1, rect.y + 1, 2, 4), new GUIContent(Texture2D.whiteTexture), GUIStyle.none);
                }
            }
            finally { GUI.matrix = matrix; GUI.color = color; }
        }
        sealed class TextureFolder
        {
            internal string name, relative;
            internal int count;
            internal int columns = 4;
            internal readonly SortedDictionary<string, TextureFolder> children = new SortedDictionary<string, TextureFolder>(StringComparer.OrdinalIgnoreCase);
            internal readonly List<CatalogFile> files = new List<CatalogFile>();
        }
        void DrawTextureTree(LibraryItem item, IEnumerable<CatalogFile> files, string query)
        {
            if (textureTree == null || textureTreeScan != scan || textureTreeQuery != query || textureTreeKind != FileTabs[fileTab])
            {
                textureTree = new TextureFolder { name = "保存フォルダ直下", relative = "" };
                textureTreeScan = scan; textureTreeQuery = query; textureTreeKind = FileTabs[fileTab];
                textureFolderCount = 0;
                expandedTextureFolders.Clear();
                foreach (var file in files.OrderBy(x => x.relative, StringComparer.OrdinalIgnoreCase))
                {
                    var node = textureTree;
                    var parts = file.relative.Replace('\\', '/').Split('/');
                    node.count++;
                    for (int i = 0; i < parts.Length - 1; i++)
                    {
                        if (!node.children.TryGetValue(parts[i], out var child))
                        {
                            child = new TextureFolder { name = parts[i], relative = node.relative.Length == 0 ? parts[i] : node.relative + "/" + parts[i] };
                            node.children.Add(parts[i], child);
                            textureFolderCount++;
                        }
                        node = child; node.count++;
                        // Initially show the first level; searches expose all matching paths.
                        if (i == 0 || query.Length > 0) expandedTextureFolders.Add(node.relative);
                    }
                    node.files.Add(file);
                }
            }
            if (Event.current.type == EventType.Layout && pendingFolderExpansion.HasValue)
            {
                expandedTextureFolders.Clear();
                if (pendingFolderExpansion.Value) ExpandAllFolders(textureTree);
                pendingFolderExpansion = null; filesScroll.y = 0;
            }
            if (textureTree.files.Count > 0)
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    DrawTextureFolderHeader(item, textureTree, false);
                    DrawTextureGrid(textureTree);
                }
            foreach (var folder in textureTree.children.Values) DrawTextureFolder(item, folder);
        }
        void ExpandAllFolders(TextureFolder folder)
        {
            foreach (var child in folder.children.Values)
            { expandedTextureFolders.Add(child.relative); ExpandAllFolders(child); }
        }
        void DrawTextureFolder(LibraryItem item, TextureFolder folder)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                bool opened = expandedTextureFolders.Contains(folder.relative);
                bool next = DrawTextureFolderHeader(item, folder, true);
                if (next != opened)
                {
                    if (next) expandedTextureFolders.Add(folder.relative); else expandedTextureFolders.Remove(folder.relative);
                    RepaintViews();
                }
                // Keep this event's layout intact; apply the expansion on the next Layout.
                if (opened)
                {
                    foreach (var child in folder.children.Values) DrawTextureFolder(item, child);
                    DrawTextureGrid(folder);
                }
            }
            GUILayout.Space(2);
        }
        bool DrawTextureFolderHeader(LibraryItem item, TextureFolder folder, bool foldout)
        {
            Rect row = GUILayoutUtility.GetRect(0, 30, GUILayout.ExpandWidth(true));
            bool opened = expandedTextureFolders.Contains(folder.relative);
            bool canImportTextures = FileTabs[fileTab] == "テクスチャ";
            float buttonsX = row.xMax - (canImportTextures ? 214 : 84);
            bool next = foldout ? Clickable.Foldout(new Rect(row.x + 4, row.y + 2, Mathf.Max(16, buttonsX - row.x - 8), 26), opened, GUIContent.none, true) : true;
            GUI.DrawTexture(new Rect(row.x + 18, row.center.y - 8, 16, 16),
                EditorGUIUtility.IconContent(!foldout || opened ? "FolderOpened Icon" : "Folder Icon").image, ScaleMode.ScaleToFit);
            GUI.Label(new Rect(row.x + 39, row.center.y - 9, Mathf.Max(0, buttonsX - row.x - 43), 18), new GUIContent(folder.name + "（" + folder.count + "件）", folder.relative), EditorStyles.boldLabel);
            string path = Path.Combine(item.folder, folder.relative.Replace('/', Path.DirectorySeparatorChar));
            if (canImportTextures)
            using (new EditorGUI.DisabledScope(ImportCoordinator.Busy || item.unpurchased))
                if (TextureFolderButton(new Rect(buttonsX, row.center.y - 12, 124, 24), "インポート", "Import", Imported.ContainsSource(item.id, path, true), true))
                {
                    try { ImportCoordinator.BeginTextures(path, item.id, state.Target(AssetCategory.Texture), item.shop, item.title, item.ageRestricted); }
                    catch (Exception ex) { message = ex.Message; }
                }
            if (TextureFolderButton(new Rect(row.xMax - 84, row.center.y - 12, 84, 24), "開く", "Folder Icon")) OpenSavedFolder(path);
            return next;
        }
        GUIStyle textureFolderButtonStyle, textureFolderButtonLabelStyle;
        bool TextureFolderButton(Rect rect, string label, string iconName, bool imported = false, bool subduedChip = false)
        {
            if (textureFolderButtonStyle == null)
            {
                textureFolderButtonStyle = new GUIStyle(GUIStyle.none) { fixedHeight = 24, fontSize = 11, padding = new RectOffset() };
                textureFolderButtonLabelStyle = new GUIStyle(EditorStyles.label) { fontSize = 11, fontStyle = FontStyle.Normal, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(), margin = new RectOffset(), wordWrap = false };
                textureFolderButtonLabelStyle.normal.textColor = EditorGUIUtility.isProSkin ? new Color(.8f, .8f, .8f) : new Color(.3f, .3f, .3f);
            }
            bool clicked = Clickable.Button(rect, new GUIContent("", label), textureFolderButtonStyle);
            if (Event.current.type == EventType.Repaint)
            {
                bool hover = GUI.enabled && rect.Contains(Event.current.mousePosition);
                Color background = EditorGUIUtility.isProSkin ? (hover ? new Color32(68, 68, 68, 255) : new Color32(78, 78, 78, 255))
                    : (hover ? new Color32(185, 185, 185, 255) : new Color32(199, 199, 199, 255));
                GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, background, 0, 3);
                GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, new Color(0, 0, 0, .2f), 1, 3);
            }
            const float iconSize = 16, gap = 6;
            var content = new GUIContent(label);
            float textWidth = textureFolderButtonLabelStyle.CalcSize(content).x;
            float x = Mathf.Round(rect.center.x - (iconSize + gap + textWidth + (imported ? 28 : 0)) / 2);
            var iconRect = new Rect(x, rect.center.y - iconSize / 2, iconSize, iconSize);
            var previousColor = GUI.color;
            try
            {
                if (iconName == "Import" && ImportCoordinator.CanCancelPreparation) DrawImportSpinner(iconRect);
                else
                {
                    GUI.color *= new Color(1, 1, 1, GUI.enabled ? .82f : .35f);
                    if (iconName == "ExpandAll" || iconName == "CollapseAll") detailActionIcons.DrawExpand(iconRect, iconName == "ExpandAll");
                    else if (iconName == "Delete") detailActionIcons.DrawDelete(iconRect);
                    else GUI.DrawTexture(iconRect, EditorGUIUtility.IconContent(iconName).image, ScaleMode.ScaleToFit);
                }
            }
            finally { GUI.color = previousColor; }
            GUI.Label(new Rect(x + iconSize + gap, rect.y, textWidth, rect.height), content, textureFolderButtonLabelStyle);
            if (imported) DrawImportedChip(new Rect(x + iconSize + gap + textWidth + 4, rect.center.y - 9, 24, 18), "済", subduedChip);
            return clicked;
        }
        void DrawTextureGrid(TextureFolder folder)
        {
            if (folder.files.Count == 0) return;
            if (FileTabs[fileTab] == "3Dデータ" || FileTabs[fileTab] == "その他ファイル")
            {
                foreach (var file in folder.files) DrawFile(file);
                return;
            }
            const float cardWidth = 104, cardHeight = 140, gap = 6;
            int columns = folder.columns;
            int rows = (folder.files.Count + columns - 1) / columns;
            Rect area = GUILayoutUtility.GetRect(0, rows * (cardHeight + gap) - gap, GUILayout.ExpandWidth(true));
            GUILayout.Space(4);
            if (Event.current.type == EventType.Repaint)
            {
                int measuredColumns = Mathf.Max(1, Mathf.FloorToInt((area.width + gap) / (cardWidth + gap)));
                if (measuredColumns != columns)
                {
                    folder.columns = measuredColumns;
                    RepaintViews();
                    return; // The next Layout reserves the height for the measured column count.
                }
            }
            for (int i = 0; i < folder.files.Count; i++)
                DrawTextureFile(new Rect(area.x + (i % columns) * (cardWidth + gap), area.y + (i / columns) * (cardHeight + gap), cardWidth, cardHeight), folder.files[i]);
        }
        GUIStyle textureDateStyle;
        void DrawTextureFile(Rect row, CatalogFile file)
        {
            DrawOpenFileCard(row, file);
            Rect thumb = new Rect(row.x + 6, row.y + 6, 92, 92);
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(thumb, new Color32(36, 36, 36, 255));
                string extension = Path.GetExtension(file.path).ToLowerInvariant();
                bool editableImage = file.kind == "画像編集データ";
                bool supported = extension == ".png" || extension == ".jpg" || extension == ".jpeg" || extension == ".psd" || extension == ".psb";
                string status = supported ? "取得中" : extension.TrimStart('.').ToUpperInvariant();
                Texture2D texture = null;
                if (supported && row.yMax >= filesScroll.y && row.y <= filesScroll.y + detailWindow.position.height)
                {
                    if (textureThumbnails == null) textureThumbnails = new ThumbnailCache(RepaintViews, true);
                    texture = textureThumbnails.Get(file.path, "", false, out status);
                }
                if (texture != null)
                {
                    EditorGUI.DrawTextureTransparent(thumb, texture, ScaleMode.ScaleToFit);
                    if (thumb.Contains(Event.current.mousePosition) && EditorWindow.mouseOverWindow == detailWindow)
                    {
                        textureHoverImage = texture; textureHoverName = Path.GetFileName(file.path);
                        textureHoverScreenRect = new Rect(GUIUtility.GUIToScreenPoint(thumb.position), thumb.size);
                    }
                }
                else
                {
                    DrawFileTypeIcon(new Rect(thumb.center.x - 16, thumb.center.y - 30, 32, 32), file);
                    string fallback = editableImage ? extension.TrimStart('.').ToUpperInvariant() : status;
                    GUI.Label(new Rect(thumb.x, thumb.center.y + 3, thumb.width, 16),
                        new GUIContent(supported && status == ThumbnailCache.LoadingStatus ? "読込中" : editableImage ? fallback : supported ? "表示不可" : status,
                            editableImage ? "対応する埋め込みプレビューがあるPSD・PSBを表示します。" : status), EditorStyles.centeredGreyMiniLabel);
                }
            }
            string name = Path.GetFileName(file.path), label = name;
            var boundaries = StringInfo.ParseCombiningCharacters(name);
            int count = boundaries.Length;
            while (count > 0 && EditorStyles.miniLabel.CalcSize(new GUIContent(label)).x > thumb.width)
                label = name.Substring(0, boundaries[--count]) + "…";
            GUI.Label(new Rect(thumb.x, thumb.yMax + 4, thumb.width, 16), new GUIContent(label, file.relative), EditorStyles.miniLabel);
            var dateColor = EditorGUIUtility.isProSkin ? new Color(.65f, .65f, .65f) : new Color(.4f, .4f, .4f);
            if (textureDateStyle == null) textureDateStyle = new GUIStyle(EditorStyles.miniLabel);
            textureDateStyle.normal.textColor = dateColor;
            GUI.Label(new Rect(thumb.x, thumb.yMax + 21, thumb.width, 16), new GUIContent(file.modifiedUtc.ToLocalTime().ToString("yyyy/MM/dd"), "更新日時: " + file.modifiedUtc.ToLocalTime().ToString("yyyy/MM/dd HH:mm")), textureDateStyle);
        }
        void DrawTextureTooltip(float width, float height)
        {
            if (Event.current.type != EventType.Repaint || textureHoverImage == null || EditorWindow.mouseOverWindow != detailWindow) return;
            const float size = 256, padding = 8;
            Vector2 anchor = GUIUtility.ScreenToGUIPoint(textureHoverScreenRect.position);
            float boxWidth = size + padding * 2, boxHeight = size + padding * 2 + 24;
            float x = anchor.x + textureHoverScreenRect.width + 12;
            if (x + boxWidth > width - 8) x = anchor.x - boxWidth - 12;
            var box = new Rect(Mathf.Clamp(x, 8, Mathf.Max(8, width - boxWidth - 8)), Mathf.Clamp(anchor.y, 8, Mathf.Max(8, height - boxHeight - 8)), boxWidth, boxHeight);
            GUI.DrawTexture(box, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, new Color32(38, 39, 43, 255), 0, 6);
            GUI.DrawTexture(box, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, new Color32(115, 117, 125, 255), 1, 6);
            EditorGUI.DrawTextureTransparent(new Rect(box.x + padding, box.y + padding, size, size), textureHoverImage, ScaleMode.ScaleToFit);
            var before = GUI.contentColor;
            try
            {
                GUI.contentColor = Color.white;
                GUI.Label(new Rect(box.x + padding, box.yMax - 24, size, 18), textureHoverName, EditorStyles.miniLabel);
            }
            finally { GUI.contentColor = before; }
        }
        static bool PackageMatches(PackageFileCard card, string query)
        { return Matcher.Normalize(card.file.relative).Contains(query) || card.older.Any(x => PackageMatches(x, query)); }
        const float PackageCardBodyHeight = 100;
        [NonSerialized] GUIStyle packageSelectionStyle;
        static float PackageCardHeight(PackageFileCard card, string query)
        {
            var children = card.older.Where(child => PackageMatches(child, query)).ToList();
            return PackageCardBodyHeight + (children.Count == 0 ? 0 : 8 + children.Sum(child => PackageCardHeight(child, query) + 4));
        }
        void DrawPackageCard(LibraryItem item, PackageFileCard card, string query, int depth)
        {
            if (packageTitleStyle == null)
            {
                packageTitleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13, wordWrap = false, clipping = TextClipping.Clip, padding = new RectOffset() };
                packageMetaStyle = new GUIStyle(EditorStyles.miniLabel) { fontSize = 11, clipping = TextClipping.Clip, padding = new RectOffset() };
                packageDateStyle = new GUIStyle(packageMetaStyle);
                packageBadgeStyle = new GUIStyle(EditorStyles.miniBoldLabel) { fontSize = 10, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(7, 7, 2, 2) };
                packageBadgeStyle.normal.textColor = Color.white;
            }
            var outer = GUILayoutUtility.GetRect(0, PackageCardHeight(card, query) + 2, GUILayout.ExpandWidth(true));
            DrawPackageCardContents(item, card, query, new Rect(outer.x, outer.y, outer.width, outer.height - 2), depth);
        }
        void DrawPackageCardContents(LibraryItem item, PackageFileCard card, string query, Rect rect, int depth)
        {
            if (Event.current.type == EventType.Repaint)
            {
                GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
                if (batchSelecting && batchSelection.Contains(card.file.path))
                    EditorGUI.DrawRect(new Rect(rect.x + 1, rect.y + 1, rect.width - 2, PackageCardBodyHeight - 2), EditorGUIUtility.isProSkin ? new Color32(49, 76, 98, 255) : new Color32(174, 206, 230, 255));
            }
            float x = rect.x + 10, y = rect.y + 6, width = rect.width - 20;
            string name = Path.GetFileName(card.file.path);
            float contentWidth = Mathf.Max(0, width - (batchSelecting ? 92 : 136));
            float titleWidth = contentWidth;
            GUI.Label(new Rect(x, y + 2, titleWidth, 19), new GUIContent(PackageEllipsis(name, titleWidth), name + "\n" + card.file.relative), packageTitleStyle);
            if (batchSelecting)
            {
                using (new EditorGUI.DisabledScope(ImportCoordinator.Busy))
                {
                    Rect check = new Rect(rect.xMax - 90, rect.y + (PackageCardBodyHeight - 25) / 2, 80, 25);
                    bool selected = batchSelection.Contains(card.file.path);
                    if (packageSelectionStyle == null)
                        packageSelectionStyle = new GUIStyle(GUI.skin.toggle) { fontSize = 12, alignment = TextAnchor.MiddleLeft };
                    // Scale the native checkbox and its label together, including the hit area.
                    var matrix = GUI.matrix;
                    bool next;
                    try
                    {
                        GUIUtility.ScaleAroundPivot(new Vector2(1.25f, 1.25f), check.position);
                        next = GUI.Toggle(new Rect(check.x, check.y, check.width / 1.25f, check.height / 1.25f), selected, "選択", packageSelectionStyle);
                    }
                    finally { GUI.matrix = matrix; }
                    Clickable.Cursor(check);
                    if (next != selected) { if (next) batchSelection.Add(card.file.path); else batchSelection.Remove(card.file.path); RepaintViews(); }
                }
            }
            else
            {
                float actionX = rect.xMax - 134, actionY = rect.y + (PackageCardBodyHeight - 54) / 2;
                using (new EditorGUI.DisabledScope(ImportCoordinator.Busy))
                    if (TextureFolderButton(new Rect(actionX, actionY, 124, 24), "インポート", "Import", Imported.ContainsSource(item.id, card.file.path, false), true))
                    { try { ImportCoordinator.Begin(card.file.path, item.id, state.Target(state.Category(item)), item.title, item.ageRestricted); } catch (Exception ex) { message = ex.Message; } }
                if (TextureFolderButton(new Rect(actionX, actionY + 30, 124, 24), "開く", "Folder Icon"))
                    OpenSavedFolder(Path.GetDirectoryName(card.file.path));
            }
            y += 27;
            string directory = Path.GetDirectoryName(card.file.relative);
            var folderIcon = EditorGUIUtility.IconContent("Folder Icon").image;
            if (folderIcon != null) GUI.DrawTexture(new Rect(x, y, 16, 16), folderIcon, ScaleMode.ScaleToFit);
            string modified = card.file.modifiedUtc.ToLocalTime().ToString("yyyy/MM/dd HH:mm");
            GUI.Label(new Rect(x + 20, y, Mathf.Max(0, contentWidth - 20), 16), new GUIContent(string.IsNullOrEmpty(directory) ? "保存フォルダー直下" : directory, card.file.relative), packageMetaStyle);
            y += 19;
            var modifiedIcon = EditorGUIUtility.IconContent("Refresh").image;
            var dateColor = EditorGUIUtility.isProSkin ? new Color(.65f, .65f, .65f) : new Color(.4f, .4f, .4f);
            packageDateStyle.normal.textColor = dateColor;
            packageDateStyle.hover.textColor = packageDateStyle.active.textColor = packageDateStyle.focused.textColor = dateColor;
            packageDateStyle.onNormal.textColor = packageDateStyle.onHover.textColor = packageDateStyle.onActive.textColor = packageDateStyle.onFocused.textColor = dateColor;
            var previousColor = GUI.color;
            try
            {
                GUI.color *= dateColor;
                if (modifiedIcon != null) GUI.DrawTexture(new Rect(x, y, 16, 16), modifiedIcon, ScaleMode.ScaleToFit);
            }
            finally { GUI.color = previousColor; }
            GUI.Label(new Rect(x, y, 16, 16), new GUIContent("", "ファイル更新日時"), GUIStyle.none);
            GUI.Label(new Rect(x + 20, y, Mathf.Max(0, contentWidth - 20), 16), new GUIContent(modified, "ファイル更新日時: " + modified), packageDateStyle);
            y += 19;
            var badges = new List<GUIContent>();
            var colors = new List<Color>();
            foreach (string avatar in Catalog.PackageAvatarNames(name))
            {
                badges.Add(new GUIContent(avatar, "ファイル名のアバター名・別名から判定: " + avatar));
                colors.Add(card.oldVersion ? new Color32(113, 134, 154, 255) : new Color32(79, 126, 168, 255));
            }
            if (card.material) { badges.Add(new GUIContent("マテリアル", "ファイル名にmaterialを含むUnityPackage")); colors.Add(new Color32(109, 114, 168, 255)); }
            if (card.version.Length > 0) { badges.Add(new GUIContent(card.version, "ファイル名・保存フォルダー名から判定")); colors.Add(card.oldVersion ? new Color32(113, 134, 154, 255) : new Color32(79, 126, 168, 255)); }
            if (card.duplicateName) { badges.Add(new GUIContent("同一名あり", "この商品内に同じファイル名が複数あります。内容の一致を示すものではありません。")); colors.Add(new Color32(184, 92, 92, 255)); }
            if (card.oldVersion) { badges.Add(new GUIContent("旧バージョン", "新しいバージョン: " + card.newerPath)); colors.Add(new Color32(119, 124, 132, 255)); }
            float bx = x;
            for (int i = 0; i < badges.Count; i++)
            {
                float reserve = i + 1 < badges.Count ? packageBadgeStyle.CalcSize(new GUIContent("+" + (badges.Count - i - 1))).x + 5 : 0;
                if (bx + packageBadgeStyle.CalcSize(badges[i]).x + reserve > x + contentWidth)
                {
                    DrawPackageBadge(ref bx, y, "+" + (badges.Count - i), string.Join("\n", badges.Skip(i).Select(b => b.text + ": " + b.tooltip)), new Color32(119, 124, 132, 255));
                    break;
                }
                DrawPackageBadge(ref bx, y, badges[i].text, badges[i].tooltip, colors[i]);
            }
            float childY = rect.y + PackageCardBodyHeight + 4;
            foreach (var older in card.older.Where(child => PackageMatches(child, query)))
            {
                float childHeight = PackageCardHeight(older, query);
                DrawPackageCardContents(item, older, query, new Rect(rect.x + 10, childY, rect.width - 16, childHeight), depth + 1);
                childY += childHeight + 4;
            }
        }
        bool PackageImportButton(Rect rect)
        {
            return TextureFolderButton(rect, "インポート", "Import");
        }
        string PackageEllipsis(string text, float width)
        {
            if (packageTitleStyle.CalcSize(new GUIContent(text)).x <= width) return text;
            var elements = StringInfo.ParseCombiningCharacters(text);
            int low = 0, high = elements.Length;
            while (low < high)
            {
                int mid = (low + high + 1) / 2;
                string candidate = text.Substring(0, mid == elements.Length ? text.Length : elements[mid]) + "…";
                if (packageTitleStyle.CalcSize(new GUIContent(candidate)).x <= width) low = mid; else high = mid - 1;
            }
            return text.Substring(0, low == elements.Length ? text.Length : elements[low]) + "…";
        }
        void DrawPackageBadge(ref float x, float y, string text, string tooltip, Color color)
        {
            var content = new GUIContent(text, tooltip);
            float width = packageBadgeStyle.CalcSize(content).x;
            var rect = new Rect(x, y, width, 19);
            if (Event.current.type == EventType.Repaint) GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, color, 0, rect.height / 2);
            GUI.Label(rect, content, packageBadgeStyle); x += width + 5;
        }
        void DrawOpenFileCard(Rect rect, CatalogFile file)
        {
            Clickable.Cursor(rect);
            if (Clickable.Button(rect, new GUIContent("", "クリックして既定のアプリで開く\n" + file.relative), GUIStyle.none))
                OpenFileWithDefaultApp(file.path);
            if (Event.current.type != EventType.Repaint) return;
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
            if (rect.Contains(Event.current.mousePosition) && EditorWindow.mouseOverWindow == detailWindow)
            {
                EditorGUI.DrawRect(new Rect(rect.x + 1, rect.y + 1, rect.width - 2, rect.height - 2),
                    EditorGUIUtility.isProSkin ? new Color32(62, 62, 62, 255) : new Color32(184, 184, 184, 255));
                GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0,
                    new Color(0, 0, 0, .5f), 1, 3);
            }
        }
        void OpenFileWithDefaultApp(string path)
        {
            try
            {
                if (!File.Exists(path)) { message = "ファイルが見つかりません。"; RepaintViews(); return; }
                using (System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
                    FileName = Path.GetFullPath(path), UseShellExecute = true, Verb = "open"
                })) { }
            }
            catch (Exception ex)
            {
                message = "既定のアプリで開けませんでした。ファイルの関連付けを確認してください: " + ex.Message;
                RepaintViews();
            }
        }
        void DrawFile(CatalogFile file)
        {
            Rect rect = GUILayoutUtility.GetRect(0, 54, GUILayout.ExpandWidth(true));
            GUILayout.Space(4);
            DrawOpenFileCard(rect, file);
            DrawFileTypeIcon(new Rect(rect.x + 8, rect.center.y - 12, 24, 24), file);
            if (fileCardTitleStyle == null)
                fileCardTitleStyle = new GUIStyle(EditorStyles.boldLabel) { clipping = TextClipping.Clip, padding = new RectOffset(), alignment = TextAnchor.MiddleLeft };
            float x = rect.x + 40, width = Mathf.Max(0, rect.width - 50);
            string name = Path.GetFileName(file.path), label = name;
            var elements = StringInfo.ParseCombiningCharacters(name);
            int count = elements.Length;
            while (count > 0 && fileCardTitleStyle.CalcSize(new GUIContent(label)).x > width)
                label = name.Substring(0, elements[--count]) + "…";
            GUI.Label(new Rect(x, rect.y + 7, width, 19), new GUIContent(label, "クリックして既定のアプリで開く\n" + file.relative), fileCardTitleStyle);
            if (textureDateStyle == null) textureDateStyle = new GUIStyle(EditorStyles.miniLabel);
            textureDateStyle.normal.textColor = EditorGUIUtility.isProSkin ? new Color(.65f, .65f, .65f) : new Color(.4f, .4f, .4f);
            string extension = Path.GetExtension(file.path).TrimStart('.').ToUpperInvariant();
            GUI.Label(new Rect(x, rect.y + 29, width, 16), new GUIContent(
                (extension.Length == 0 ? "ファイル" : extension) + "  ·  " + file.modifiedUtc.ToLocalTime().ToString("yyyy/MM/dd HH:mm"),
                "更新日時: " + file.modifiedUtc.ToLocalTime().ToString("yyyy/MM/dd HH:mm")), textureDateStyle);
        }
        void DrawFileTypeIcon(Rect rect, CatalogFile file)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (fileTypeIcons == null) fileTypeIcons = new FileTypeIconCache(RepaintViews);
            var icon = fileTypeIcons.Get(file.path, file.kind);
            if (icon != null) GUI.DrawTexture(rect, icon, ScaleMode.ScaleToFit);
        }
        void EnsureSettingsDraft()
        {
            if (classificationDraft != null && targetDraft != null) return;
            classificationApplied = state.categoryRulesConfigured ? CategoryRules.Copy(state.categoryRules) : CategoryRules.Defaults();
            classificationDraft = CategoryRules.Copy(classificationApplied);
            targetDraft = Categories.DisplayOrder.ToDictionary(c => c, state.Target);
        }
        void DrawTargets()
        {
            EnsureSettingsDraft(); EnsureNavigationStyles();
            GUILayout.Label("インポート先・分類設定", EditorStyles.boldLabel);
            GUILayout.Space(6);
            GUILayout.Label("インポート先が空欄の場合はAssets直下にインポートされます。\n「サブカテゴリ」「タグを含む」「ショップを含む」は、いずれかに当てはまれば対象となります。「タグを含まない」「ショップを含まない」は、いずれかに当てはまればその分類から除外します。除外条件だけでは分類しません。\nタグとショップ名はテキスト完全一致です。* を入力すると部分一致にできます。変更は「保存して適用」で反映します。", EditorStyles.wordWrappedMiniLabel);
            GUILayout.Space(8);
            classificationDraftChanged = !CategoryRules.SameContents(classificationDraft, classificationApplied)
                || targetDraft.Any(pair => pair.Value != state.Target(pair.Key));
            if (classificationNoticeStyle == null) classificationNoticeStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleLeft };
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!classificationDraftChanged || work != null || ImportCoordinator.Busy))
                    if (Clickable.LayoutButton("保存して適用", GUILayout.Width(120), GUILayout.Height(24))) ApplySettingsDraft();
                if (Clickable.LayoutButton("初期条件に戻す", GUILayout.Width(120), GUILayout.Height(24)))
                { classificationDraft = CategoryRules.Defaults(); GUI.FocusControl(null); GUIUtility.ExitGUI(); }
                GUILayout.Label(classificationDraftChanged ? "未適用の変更があります" : "", classificationNoticeStyle, GUILayout.Height(24));
            }
            GUILayout.Space(8);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("分類（優先順）", EditorStyles.boldLabel, GUILayout.Width(106));
                    GUILayout.Label("インポート先（Assets内）", EditorStyles.boldLabel, GUILayout.Width(180));
                    GUILayout.Label("分類条件", EditorStyles.boldLabel);
                }
                foreach (var rule in classificationDraft)
                {
                    Rect line = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
                    EditorGUI.DrawRect(line, new Color(.5f, .5f, .5f, .3f));
                    using (var row = new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label(Categories.Labels[(int)rule.category], GUILayout.Width(106), GUILayout.Height(24));
                        using (new EditorGUILayout.VerticalScope(GUILayout.Width(180)))
                        {
                            using (new EditorGUI.DisabledScope(rule.category == AssetCategory.Avatar))
                            {
                                string value = rule.category == AssetCategory.Avatar ? "Assets直下（固定）" : targetDraft[rule.category];
                                string edited = EditorGUILayout.TextField(value, GUILayout.Width(180), GUILayout.Height(24));
                                if (rule.category != AssetCategory.Avatar) targetDraft[rule.category] = edited;
                            }
                            string relative = rule.category == AssetCategory.Avatar ? "" : targetDraft[rule.category];
                            string destination = "Assets" + (string.IsNullOrWhiteSpace(relative) ? "" : " > " + relative.Replace('\\', '/').Replace("/", " > "));
                            Rect hint = GUILayoutUtility.GetRect(180, 18, GUILayout.Width(180));
                            var hintStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleLeft };
                            hintStyle.normal.textColor = EditorGUIUtility.isProSkin ? new Color(.6f, .6f, .6f) : new Color(.45f, .45f, .45f);
                            GUI.Label(hint, new GUIContent(SingleLineEllipsis(destination, hintStyle, hint.width), destination), hintStyle);
                        }
                        using (new EditorGUILayout.VerticalScope())
                        {
                            for (int i = 0; i < rule.conditions.Count; i++)
                            {
                                var condition = rule.conditions[i];
                                using (new EditorGUILayout.HorizontalScope())
                                {
                                    condition.source = (CategoryConditionSource)Clickable.Popup((int)condition.source, ClassificationSources, sortPopupStyle, GUILayout.Width(144), GUILayout.Height(24));
                                    condition.value = EditorGUILayout.TextField(condition.value ?? "", GUILayout.Height(24));
                                    if (Clickable.LayoutButton("削除", GUILayout.Width(44), GUILayout.Height(24)))
                                    { rule.conditions.RemoveAt(i); GUI.FocusControl(null); GUIUtility.ExitGUI(); }
                                }
                            }
                            if (Clickable.LayoutButton("＋ 条件を追加", GUILayout.Width(116), GUILayout.Height(24)))
                            { rule.conditions.Add(new CategoryCondition()); GUIUtility.ExitGUI(); }
                            GUILayout.Space(6);
                        }
                        if (Event.current.type == EventType.Repaint)
                            foreach (float offset in new[] { 108f, 292f }) EditorGUI.DrawRect(new Rect(row.rect.x + offset, row.rect.y, 1, row.rect.height), new Color(.5f, .5f, .5f, .3f));
                    }
                }
            }
        }
        void ApplySettingsDraft()
        {
            try
            {
                foreach (var pair in targetDraft)
                    if (pair.Key != AssetCategory.Avatar && pair.Value.Length > 0) SafePaths.Relative(pair.Value);
                // Save a detached candidate first: validation or disk errors must not change live settings.
                var candidate = JsonUtility.FromJson<LibraryState>(JsonUtility.ToJson(state));
                candidate.targets = targetDraft.Select(pair => new ImportTarget { category = pair.Key, subfolder = pair.Value }).ToList();
                candidate.categoryRules = CategoryRules.Copy(classificationDraft);
                candidate.categoryRulesConfigured = true;
                candidate.ApplyCategoryRules(); AvatarCatalog.Synchronize(candidate);
                LocalStore.Write(LocalStore.StatePath, candidate);
                state = candidate; dirty = false; catalog = null; popupLabels.Clear();
                classificationApplied = CategoryRules.Copy(classificationDraft); classificationDraftChanged = false;
                message = "インポート先と分類条件を保存して適用しました。";
                RepaintViews();
            }
            catch (Exception ex) { message = "適用できませんでした: " + ex.Message; }
        }
        void DrawSettings()
        {
            GUILayout.Label("表示・連携設定", EditorStyles.boldLabel);
            GUILayout.Space(8);
            EditorGUI.BeginChangeCheck();
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label("表示設定", EditorStyles.boldLabel);
                state.hideR18 = Clickable.ToggleLeft("R18商品を一覧・履歴から非表示", state.hideR18);
            }
            if (EditorGUI.EndChangeCheck()) Changed(false);
            GUILayout.Space(12);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label("KaihenShelfデータ保存先", EditorStyles.boldLabel);
                float pathHeight = Mathf.Max(18, EditorStyles.wordWrappedMiniLabel.CalcHeight(new GUIContent(LocalStore.StatePath), Mathf.Max(200, position.width - 220)));
                EditorGUILayout.SelectableLabel(LocalStore.StatePath, EditorStyles.wordWrappedMiniLabel, GUILayout.Height(pathHeight));
                if (Clickable.LayoutButton("保存先を開く", GUILayout.Width(120), GUILayout.Height(24))) { Save(); if (File.Exists(LocalStore.StatePath)) EditorUtility.RevealInFinder(LocalStore.StatePath); }
            }
            GUILayout.Space(12);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label("BOOTH Library Manager連携", EditorStyles.boldLabel);
                GUILayout.Space(10);
                EditorGUI.BeginChangeCheck();
                GUILayout.Label("BOOTH Library Managerの保存場所（DB）", EditorStyles.boldLabel);
                using (new EditorGUI.DisabledScope(work != null))
                using (new EditorGUILayout.HorizontalScope())
                {
                    state.blmPath = EditorGUILayout.TextField(state.blmPath);
                    if (Clickable.LayoutButton("参照…", GUILayout.Width(76)))
                    {
                        string directory = "";
                        try { directory = Path.GetDirectoryName(state.blmPath); } catch (ArgumentException) { }
                        string chosen = EditorUtility.OpenFilePanel("BOOTH Library Managerの保存場所（DB）", directory, "db");
                        if (!string.IsNullOrEmpty(chosen)) { state.blmPath = chosen; GUI.changed = true; }
                    }
                }
                if (EditorGUI.EndChangeCheck()) Changed(false);
                GUILayout.Space(16);
                GUILayout.Label("連携停止", EditorStyles.boldLabel);
                GUILayout.Label("同意確認画面に戻り、一覧・詳細や新しい読み取り操作が使えなくなります。\n登録情報・設定・履歴・保存済み画像・インポート済みアセットは残ります。\n再度「同意して開始」を押すと利用を再開できます。", EditorStyles.wordWrappedLabel);
                GUILayout.Space(6);
                using (new EditorGUI.DisabledScope(work != null || ImportCoordinator.Busy)) if (Clickable.LayoutButton("同意を取り消して読み取りを停止", GUILayout.Width(240), GUILayout.Height(24))) { state.consentVersion = ""; state.consentUtc = ""; consentCheck = false; thumbnails?.Clear(); thumbnails?.SetRemoteEnabled(false); Changed(); Save(); }
            }
        }
        static readonly string[] ClassificationSources = { "サブカテゴリ", "タグを含む", "タグを含まない", "ショップを含む", "ショップを含まない" };

        void DrawHistory()
        {
            // Replace the snapshot only at Layout so all events use the same card/control count.
            if (Event.current.type == EventType.Layout || historyView == null) RefreshHistoryView();
            if (historyViewError.Length > 0) { EditorGUILayout.HelpBox(historyViewError, MessageType.Error); return; }
            var history = historyView;
            var visibleRecords = history.records.Where(record => Catalog.IsHistoryVisible(record.itemId, state.hideR18, record.ageRestricted)).ToList();
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("インポート履歴", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(history.records.Count == 0 || ImportCoordinator.Busy))
                    if (Clickable.LayoutButton("履歴をリセット", GUILayout.Width(120), GUILayout.Height(24)))
                    {
                        if (EditorUtility.DisplayDialog("インポート履歴をリセット", "このプロジェクトのインポート履歴をリセットします。インポート済みのアセットは削除されません。", "リセット", "キャンセル"))
                        {
                            try { ImportHistoryUtility.Reset(LocalStore.HistoryPath); historyView = null; historyPage = 0; historyScroll = Vector2.zero; }
                            catch (Exception ex) { message = ex.Message; }
                            Repaint(); GUIUtility.ExitGUI();
                            GUIUtility.ExitGUI();
                        }
                    }
            }
            GUILayout.Space(6);
            EnsureNavigationStyles();
            int pages = Math.Max(1, (visibleRecords.Count + HistoryPageSize - 1) / HistoryPageSize);
            historyPage = Mathf.Clamp(historyPage, 0, pages - 1);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(visibleRecords.Count + "件", navigationLabelStyle, GUILayout.Height(NavigationHeight));
                GUILayout.FlexibleSpace();
                GUILayout.Label(HistoryPageSize + "件 / ページ", EditorStyles.miniLabel, GUILayout.Height(NavigationHeight));
                GUILayout.Space(8);
                using (new EditorGUI.DisabledScope(historyPage == 0))
                    if (Clickable.LayoutButton("‹", GUILayout.Width(25), GUILayout.Height(NavigationHeight))) ChangeHistoryPage(historyPage - 1);
                GUILayout.Label((historyPage + 1) + " / " + pages, navigationLabelStyle, GUILayout.Height(NavigationHeight));
                using (new EditorGUI.DisabledScope(historyPage == pages - 1))
                    if (Clickable.LayoutButton("›", GUILayout.Width(25), GUILayout.Height(NavigationHeight))) ChangeHistoryPage(historyPage + 1);
            }
            GUILayout.Space(6);
            historyScroll = EditorGUILayout.BeginScrollView(historyScroll);
            foreach (var record in visibleRecords.Skip(historyPage * HistoryPageSize).Take(HistoryPageSize))
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    var item = Catalog.Find(record.itemId);
                    string destination = ImportHistoryUtility.Destination(record);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (DrawThumbnail(item?.thumbnailPath, item?.thumbnailUrl, 76, historyScroll.y, item != null, width: 76)) Select(item);
                        GUILayout.Space(8);
                        using (new EditorGUILayout.VerticalScope(GUILayout.MinWidth(0)))
                        {
                            GUILayout.Label(new GUIContent(ImportHistoryUtility.ProductName(record, item)), EditorStyles.wordWrappedLabel);
                            GUILayout.Label(new GUIContent(Path.GetFileName(record.source), record.source), EditorStyles.wordWrappedMiniLabel);
                            GUILayout.Space(4);
                            GUILayout.Label(ImportHistoryUtility.LocalTime(record.utc) + "    " + ImportHistoryUtility.CountLabel(record) + "    " + record.status, EditorStyles.miniLabel);
                            GUILayout.Label(new GUIContent(string.IsNullOrEmpty(destination) ? "インポート先が見つかりません" : destination), EditorStyles.wordWrappedMiniLabel);
                        }
                        GUILayout.Space(10);
                        using (new EditorGUILayout.VerticalScope(GUILayout.Width(150)))
                        {
                            using (new EditorGUI.DisabledScope(!AssetDatabase.IsValidFolder(destination)))
                                if (TextureFolderButton(GUILayoutUtility.GetRect(150, 24), "インポート先を開く", "FolderOpened Icon"))
                                {
                                    // Defer changing Editor windows until this IMGUI layout has finished.
                                    EditorApplication.delayCall += () => { try { ProjectFolderBrowser.Open(destination, ex => { message = ex.Message; Repaint(); }); } catch (Exception ex) { message = ex.GetBaseException().Message; Repaint(); } };
                                }
                            GUILayout.Space(4);
                            using (new EditorGUI.DisabledScope(item == null))
                                if (TextureFolderButton(GUILayoutUtility.GetRect(150, 24), "アイテムを見る", "ViewToolOrbit")) Select(item);
                        }
                    }
                    if (record.status == "要確認" && !string.IsNullOrEmpty(record.message))
                        EditorGUILayout.HelpBox(record.message, MessageType.Warning);
                }
                GUILayout.Space(3);
            }
            if (visibleRecords.Count == 0) GUILayout.Label(history.records.Count == 0 ? "まだ履歴はありません。" : "表示できる履歴はありません。");
            EditorGUILayout.EndScrollView();
        }
        void ChangeHistoryPage(int value)
        {
            historyPage = value; historyScroll = Vector2.zero;
            GUI.FocusControl(null); Repaint(); GUIUtility.ExitGUI();
        }
        void RefreshHistoryView()
        {
            try
            {
                var info = new FileInfo(LocalStore.HistoryPath);
                long stamp = info.Exists ? info.LastWriteTimeUtc.Ticks : 0, length = info.Exists ? info.Length : 0;
                if (historyView != null && stamp == historyViewStamp && length == historyViewLength) { historyViewError = ""; return; }
                var next = LocalStore.Read<ImportHistory>(LocalStore.HistoryPath) ?? new ImportHistory();
                bool added = historyView != null && next.records.Count > historyView.records.Count;
                int lastPage = Math.Max(0, (next.records.Count - 1) / HistoryPageSize);
                int nextPage = added ? 0 : Mathf.Clamp(historyPage, 0, lastPage);
                if (nextPage != historyPage || added || next.records.Count == 0) historyScroll = Vector2.zero;
                historyPage = nextPage; historyView = next;
                historyViewStamp = stamp; historyViewLength = length; historyViewError = "";
            }
            catch (Exception ex) { historyViewError = "履歴を読み取れません: " + ex.Message; }
        }
        void DrawHelp()
        {
            GUILayout.Label(Pages[7], EditorStyles.boldLabel);
            GUILayout.Space(10);
            DrawQuickStartCard("1", "商品の読み込みを待つ",
                "同意後、BOOTH Library Managerの商品を自動で読み込みます。商品が表示されたら、使い始められます。");
            DrawQuickStartCard("2", "商品を探す",
                "「アバター」や「アイテム・アセット」から商品を選び、「詳細」を開きます。");
            DrawQuickStartCard("3", "インポート先を確認する",
                "「インポート先・分類設定」で配置先を確認。変更したら「保存して適用」を押します。");
            DrawQuickStartCard("4", "インポートする",
                "商品詳細の「インポート」を押し、確認画面で「Import」を押します。");
            GUILayout.Space(4);
            if (Clickable.LayoutButton("詳しい使い方（説明サイト）を開く ↗", GUILayout.Width(300), GUILayout.Height(30)))
                Application.OpenURL("https://usa-mishin.github.io/KaihenShelf/guide/usage");
            GUILayout.Space(16);
            Rect separator = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(separator, EditorGUIUtility.isProSkin ? new Color(1, 1, 1, .18f) : new Color(0, 0, 0, .2f));
            GUILayout.Space(12);
            GUILayout.Label("ポリシー", EditorStyles.boldLabel);
            DrawPolicy();
        }
        void DrawQuickStartCard(string number, string heading, string body)
        {
            var titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
            var bodyStyle = new GUIStyle(EditorStyles.wordWrappedLabel) { fontSize = 12 };
            bool dark = EditorGUIUtility.isProSkin;
            float bodyHeight = bodyStyle.CalcHeight(new GUIContent(body), Mathf.Max(200, position.width - 274));
            Rect card = GUILayoutUtility.GetRect(0, Mathf.Max(64, bodyHeight + 44), GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(card, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0,
                    dark ? new Color(.34f, .34f, .34f) : new Color(.65f, .65f, .65f), 0, 4);
                GUI.DrawTexture(new Rect(card.x + 1, card.y + 1, card.width - 2, card.height - 2),
                    Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0,
                    dark ? new Color(.22f, .22f, .22f) : new Color(.92f, .92f, .92f), 0, 3);
            }
            Rect marker = new Rect(card.x + 12, card.center.y - 14, 28, 28);
            GUI.DrawTexture(marker, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0,
                dark ? new Color(.42f, .42f, .42f) : new Color(.78f, .78f, .78f), 0, 14);
            var numberStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13, alignment = TextAnchor.MiddleCenter };
            numberStyle.normal.textColor = dark ? Color.white : new Color(.18f, .18f, .18f);
            GUI.Label(marker, number, numberStyle);
            float textX = card.x + 52, textWidth = card.width - 66;
            GUI.Label(new Rect(textX, card.y + 9, textWidth, 20), heading, titleStyle);
            GUI.Label(new Rect(textX, card.y + 32, textWidth, card.height - 40), body, bodyStyle);
            GUILayout.Space(6);
        }
        static void DrawGuideSection(string heading, string text)
        {
            GUILayout.Space(10);
            GUILayout.Label(heading, EditorStyles.boldLabel);
            GUILayout.Space(3);
            GUILayout.Label(text, EditorStyles.wordWrappedLabel);
        }
    }

    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "VrcKaihenLibrary", "VrcKaihenLibrary.Editor", "UnpurchasedWindow")]
    public sealed class UnpurchasedWindow : EditorWindow
    {
        string url = "", displayName = "", thumbnail = "";
        Action<LibraryItem> accept;
        public static void Show(Action<LibraryItem> accept)
        { var window = CreateInstance<UnpurchasedWindow>(); window.titleContent = new GUIContent("未購入アバター"); window.accept = accept; window.minSize = new Vector2(420, 180); window.ShowUtility(); }
        void OnGUI()
        {
            url = EditorGUILayout.TextField("BOOTH商品URL", url); displayName = EditorGUILayout.TextField("表示名", displayName); thumbnail = EditorGUILayout.TextField("サムネイルURL", thumbnail);
            EditorGUILayout.HelpBox("登録後の商品詳細で識別タグ・共通素体を設定してください。サムネイルにはBOOTH画像配信サーバーのHTTPS画像URLを指定します。", MessageType.Info);
            using (new EditorGUI.DisabledScope(BlmDatabaseReader.BoothId(url).Length == 0 || string.IsNullOrWhiteSpace(displayName)))
                if (Clickable.LayoutButton("登録"))
                { accept?.Invoke(new LibraryItem { id = "manual:" + Guid.NewGuid().ToString("N"), boothId = BlmDatabaseReader.BoothId(url), title = displayName, url = url, thumbnailUrl = thumbnail, unpurchased = true, category = AssetCategory.Avatar, registeredUtc = DateTime.UtcNow.ToString("o") }); Close(); }
        }
    }
}
