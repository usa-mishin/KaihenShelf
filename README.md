# KaihenShelf

**VRChatの改変アイテムを、ひとつの棚に。**

Usatrot WorksによるWindows用Unity Editor拡張です。BOOTH Library Managerに保存された商品情報と手元のファイルを整理し、Unityへの導入を支援します。BOOTH Library Manager連携・非公式です。

- [紹介サイト](https://usa-mishin.github.io/KaihenShelf/)
- [導入ガイド](https://usa-mishin.github.io/KaihenShelf/guide/)
- [English guide](https://usa-mishin.github.io/KaihenShelf/en/guide/)
- [BOOTH](https://usatrot-works.booth.pm/)
- [更新履歴](https://usa-mishin.github.io/KaihenShelf/changelog)

## 導入

VCC／ALCOMに以下の配布リポジトリを追加し、対象プロジェクトに **KaihenShelf** を追加してください。

`https://usa-mishin.github.io/KaihenShelf/index.json`

Unity上部の **KaihenShelf → KaihenShelf** から起動します。Windows 10/11・Unity 2022.3向け（検証: 2022.3.22f1）。アプリのUIは現在日本語です。

手動導入用UnityPackageは [Releases](https://github.com/usa-mishin/KaihenShelf/releases) にあります。VCC版と二重導入しないでください。旧UnityPackage版から切り替える前にプロジェクトをバックアップしてください。

## できること

- アバター・ショップ・タグ・分類でアイテムを検索
- 商品詳細でUnityPackageやテクスチャなどを確認
- 分類ごとのインポート先と自動分類条件を設定
- 複数のUnityPackageをまとめて確認・インポート
- 商品サムネイルによるフォルダ表示とインポート履歴
- 手元のフォルダから追加登録

BOOTHへのログインや商品ファイルのダウンロードは行いません。商品データベースは読み取り専用です。依存関係や固定パスの検査はすべてを検出できるものではありません。商品の導入手順・利用規約を優先してください。

## 開発

ソースは `Packages/com.usamishin.kaihenshelf.core/`。Windows、PowerShell、.NET 8 SDKでパッケージを生成できます。

```powershell
./scripts/Build-Package.ps1
./scripts/Test-UnityCompile.ps1
```

サイトはNode.js 22以降で開発します。

```powershell
npm ci
npm run test:listing
node scripts/build-listing.mjs --local
npm run build
npm run preview
```

[配布・サイト更新](docs/PUBLISHING.md) / [データ取扱い](docs/PRIVACY.md) / [詳細ガイド](Packages/com.usamishin.kaihenshelf.core/Documentation/USER-GUIDE.md)

## お問い合わせ・ライセンス

[GitHub Issues](https://github.com/usa-mishin/KaihenShelf/issues) または usamishin.art@gmail.com。

[MIT License](LICENSE)。同梱の第三者コードについてはパッケージの [THIRD PARTY NOTICES.md](Packages/com.usamishin.kaihenshelf.core/THIRD%20PARTY%20NOTICES.md) を参照してください。

BOOTH、BOOTH Library Manager、ピクシブ株式会社、VRChat、Unityが提供・保証・承認する製品ではありません。
