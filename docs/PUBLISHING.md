# KaihenShelfの配布・サイト更新

GitHub: `usa-mishin/KaihenShelf` / 作者: **Usatrot Works**

## ローカル確認

```powershell
npm ci
./scripts/Build-Package.ps1
npm run test:listing
node scripts/build-listing.mjs --local
npm run build
npm run preview
```

サイトは `site/`、公開する日本語ポリシーの正本は `docs/PRIVACY.md`。英語ポリシーは `site/en/policy.md` も同時更新します。表示画像は自作・公開許諾のあるものだけを追加してください。

## 初回公開

GitHub PagesのSourceを **GitHub Actions** に設定します。公開用ファイルだけをコミットし、バージョンタグをpushしてください。最初のタグの前は公開リリースがないため、Pagesジョブは空の配布一覧を作らず停止します。

## パッケージ更新

1. パッケージの `package.json` にあるversionとダウンロードURLのバージョンを同時更新。
2. サイトの日英更新履歴とパッケージ内のガイドを更新。
3. ビルド・自動テスト・Unityでのコンパイルと導入を確認。
4. コミット後、`v0.1.140` の形式で一致するタグをpush。

Actionsがテスト、ZIP作成、Release公開、全公開リリースからの配布一覧生成、サイト公開を順番に実行します。サイトとindex.jsonは同一のPages成果物として公開します。通常のmain更新はサイトのみ公開し、Releaseは作成しません。

公開済みタグ・Release・ZIPを上書き／削除しないでください。不具合修正には新しいバージョンを発行します。配布一覧は過去バージョンも含め、ZIPを取得してSHA-256を算出します。

## 公開対象

Packages内のKaihenShelf本体、Tests/CoreTests、Tests/Distribution、ビルドツール、必要なscripts、site、公開用docs、READMEとLICENSEを管理します。Library、UserSettings、商品データ、実機テストの出力、個人用引き継ぎ資料は公開しません。

## 手動導入からの移行

legacyFoldersは旧ツール本体2パスとそのルートGUIDのみを指定しています。設定・履歴・インポート済み商品は対象外です。ルートを移動した場合にもGUIDで追跡します。ユーザーの商品をツール本体フォルダ内に保存しないよう、導入ガイドで案内します。
