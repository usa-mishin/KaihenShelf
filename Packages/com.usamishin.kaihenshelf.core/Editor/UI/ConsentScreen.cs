using System;
using UnityEditor;
using UnityEngine;

namespace KaihenShelf
{
    // Keep the decision controls outside the scrollable disclosure, including in short windows.
    public static class ConsentScreen
    {
        public static bool Draw(ref Vector2 scroll, ref bool confirmed, ref bool details, bool updated, Action drawDetails)
        {
            using (new EditorGUILayout.VerticalScope(new GUIStyle { padding = new RectOffset(16, 16, 12, 12) }))
            {
                GUILayout.Label("KaihenShelfへようこそ", new GUIStyle(EditorStyles.boldLabel) { fontSize = 16 });
                GUILayout.Space(4);
                GUILayout.Label("利用前に、読み取り・保存・通信の範囲をご確認ください。", EditorStyles.wordWrappedLabel);
                GUILayout.Space(8);
                scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
                if (updated) EditorGUILayout.HelpBox("確認事項が更新されています。内容を確認して、再度同意してください。", MessageType.Info);
                GUILayout.Label("この拡張機能は非公式です。BOOTH、BOOTH Library Manager、ピクシブ株式会社、VRChat、Unityが提供・保証・承認する製品ではありません。", EditorStyles.wordWrappedLabel);
                GUILayout.Space(12);
                Section("読み取り", "BOOTH Library Managerの商品・購入情報と、選択した商品フォルダのファイル情報・画像を読み取ります。元の商品ファイルとデータベースは変更しません。");
                Section("保存・インポート", "商品情報・設定・縮小画像をコンピューター内に保存し、設定は同じコンピューターのUnityプロジェクトで共有します。確認後のインポートはプロジェクト内のファイルを追加・更新し、履歴と作業用データも保存します。");
                Section("通信", "画像表示のためBOOTHの画像配信先へ接続し、画像のアドレスと接続元のネットワークアドレスが伝わります。パスワードやログイン情報は取得・送信せず、利用状況や商品情報を独自サーバーへ送信しません。");
                Section("外部アプリ", "商品ページやファイルを開くと、アドレスや保存場所を既定のブラウザー・アプリに渡します。以後の通信・編集は、そのアプリの動作に従います。");
                Section("利用条件", "自動判定やインポート後の正常動作は保証されません。商品の利用規約・ライセンス・導入手順に従ってください。");
                Section("停止方法", "「表示・連携設定」で同意を取り消せます。保存済みのデータやインポート済みアセットは残ります。");
                GUILayout.Space(8);
                if (CompactPillButton.Draw(details ? "閉じる" : "詳しいポリシーを表示", 110071))
                { details = !details; GUIUtility.ExitGUI(); }
                if (details) { GUILayout.Space(8); drawDetails?.Invoke(); }
                EditorGUILayout.EndScrollView();
                GUILayout.Space(8);
                Rect line = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(line, new Color(.5f, .5f, .5f, .4f));
                GUILayout.Space(8);
                confirmed = ConfirmationToggle(confirmed);
                GUILayout.Space(4);
                using (new EditorGUI.DisabledScope(!confirmed))
                    return Clickable.LayoutButton("同意して開始", GUILayout.Height(34));
            }
        }
        static void Section(string heading, string text)
        {
            var headingStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleLeft, padding = new RectOffset(), margin = new RectOffset() };
            var bodyStyle = new GUIStyle(EditorStyles.wordWrappedLabel) { alignment = TextAnchor.MiddleLeft, padding = new RectOffset(), margin = new RectOffset() };
            float bodyWidth = Mathf.Max(200, EditorGUIUtility.currentViewWidth - 220);
            float height = Mathf.Max(36, bodyStyle.CalcHeight(new GUIContent(text), bodyWidth) + 12);
            Rect row = GUILayoutUtility.GetRect(0, height, GUILayout.ExpandWidth(true));
            const float divider = 138;
            if (Event.current.type == EventType.Repaint)
            {
                bool dark = EditorGUIUtility.isProSkin;
                EditorGUI.DrawRect(row, dark ? new Color(.23f, .23f, .23f) : new Color(.88f, .88f, .88f));
                EditorGUI.DrawRect(new Rect(row.x, row.y, divider, row.height), dark ? new Color(.20f, .20f, .20f) : new Color(.81f, .81f, .81f));
                Color line = dark ? new Color(.36f, .36f, .36f) : new Color(.65f, .65f, .65f);
                EditorGUI.DrawRect(new Rect(row.x, row.y, row.width, 1), line);
                EditorGUI.DrawRect(new Rect(row.x, row.yMax - 1, row.width, 1), line);
                foreach (float x in new[] { row.x, row.x + divider, row.xMax - 1 })
                    EditorGUI.DrawRect(new Rect(x, row.y, 1, row.height), line);
            }
            GUI.Label(new Rect(row.x + 10, row.y, divider - 20, row.height), heading, headingStyle);
            GUI.Label(new Rect(row.x + divider + 10, row.y + 6, Mathf.Max(0, row.width - divider - 20), row.height - 12), text, bodyStyle);
        }
        static bool ConfirmationToggle(bool value)
        {
            Rect row = GUILayoutUtility.GetRect(0, 34, GUILayout.ExpandWidth(true));
            const string text = "読み取り・保存・通信と利用条件を確認し、同意します";
            var style = new GUIStyle(EditorStyles.label) { fontSize = 15, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(), margin = new RectOffset() };
            var content = new GUIContent(text);
            Rect hit = new Rect(row.x, row.y, Mathf.Min(row.width, 30 + style.CalcSize(content).x), row.height);
            // Scale only the native checkbox; draw the larger label on the same vertical center.
            var matrix = GUI.matrix;
            bool next;
            Rect check = new Rect(row.x, row.center.y - 11.25f, 18, 18);
            try
            {
                GUIUtility.ScaleAroundPivot(new Vector2(1.25f, 1.25f), check.position);
                next = GUI.Toggle(check, value, GUIContent.none, EditorStyles.toggle);
            }
            finally { GUI.matrix = matrix; }
            var label = new Rect(row.x + 30, row.y, Mathf.Max(0, hit.width - 30), row.height);
            if (Clickable.StableButton(label, 110072, content, style)) next = !next;
            Clickable.Cursor(hit);
            return next;
        }
    }
}
