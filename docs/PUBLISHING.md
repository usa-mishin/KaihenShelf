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

GitHub PagesのSourceを **GitHub Actions** に設定します。公開用ファイルだけをコミットし、バージョンタグをpushしてください。公開リリースがない場合は配布一覧の生成が停止します。空の配布一覧は公開しません。

## パッケージ更新

1. パッケージの `package.json` にあるversionとダウンロードURLのバージョンを同時更新。
2. サイトの日英更新履歴とパッケージ内のガイドを更新。
3. ビルド・自動テスト・Unityでのコンパイルと導入を確認。
4. コミット後、`v0.1.140` の形式で一致するタグをpush。

Build packagesがテスト、ZIP作成、Release公開を実行します。成功後にPublish websiteがmainのコードから全公開リリースの配布一覧とサイトを生成し、同一のPages成果物として公開します。Pagesはmainからのみ公開する既定の環境保護を維持します。通常のmain更新でもサイトを公開でき、Releaseは作成しません。

公開済みタグ・Release・ZIPを上書き／削除しないでください。不具合修正には新しいバージョンを発行します。配布一覧は過去バージョンも含め、ZIPを取得してSHA-256を算出します。

## 公開対象

Packages内のKaihenShelf本体、Tests/CoreTests、Tests/Distribution、ビルドツール、必要なscripts、site、公開用docs、READMEとLICENSEを管理します。Library、UserSettings、商品データ、実機テストの出力、個人用引き継ぎ資料は公開しません。

## 手動導入からの移行

legacyFoldersは旧ツール本体2パスとそのルートGUIDのみを指定しています。設定・履歴・インポート済み商品は対象外です。ルートを移動した場合にもGUIDで追跡します。ユーザーの商品をツール本体フォルダ内に保存しないよう、導入ガイドで案内します。

## 0.1.140のローカル検証

- Coreテスト185件、配布一覧テスト3件。
- Unity 2022.3.22f1のAPIコンパイル。
- vrc-get 1.9.2で新規導入、旧名称フォルダ、現在の名称フォルダ、移動済みフォルダの置き換え。
- インポート済み商品と履歴の保持、削除・再導入。
- インストールされたVPMパッケージからのUnityアセンブリ解決・既定アバターデータの読み込み。
- サイト日英ビルド、ブラウザーでガイド遷移・言語切替・URLコピー・ライト／ダーク表示を確認。

Unity検証スクリプトは公式vrc-get CLIを `artifacts/vrc-get.exe` に配置してから実行します。専用の一時プロジェクトと一時User Package登録を使用し、登録は終了時に解除します。既存プロジェクトを変更しません。

VitePressは安定版1.6.4を使用し、開発用Viteの既知問題に対応するため6.4.4に固定しています。依存更新時はビルドとプレビューを確認してください。
