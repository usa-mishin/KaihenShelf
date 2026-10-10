# Using KaihenShelf

## Find an asset

Browse avatars or items and assets. Filter by name, shop, BOOTH tag, or compatible avatar. Save favorites and use **リセット** to reset the filters. Automatic classification and avatar detection can be corrected in **情報を編集** (Edit information).

## Check the files

Open the product detail window and switch between UnityPackages, textures, image source files, 3D data, and other files. Supported images show previews; source formats need a supported embedded preview. File cards can open files in your PC's default application.

## Choose a destination

In **インポート先・分類設定**, configure import destinations and classification rules, then click **保存して適用** (Save & apply). An empty destination retains the package's original structure under Assets.

Subcategory and included-tag rules match with OR. Any excluded tag excludes the category. Tags match exactly unless you use `*`, for example `*material*`. Categories are checked from top to bottom; manual product classifications take priority.

## Import

Click **インポート**, review the destination and files, then click **Import**. To combine several UnityPackages, use **一括インポート**, select the required packages, and review the combined file list. If materials and prefabs come separately, select both.

KaihenShelf places the combined files with their GUIDs preserved before Unity reads them. It cannot detect every dependency or fixed-path requirement. Follow the asset creator's instructions and install required shaders and SDKs first.

Texture folders can be imported with their hierarchy preserved; only supported texture formats are included.

## After import

Use **インポート履歴** (Import history) to revisit a product or its destination. Resetting history does not delete imported assets. **商品サムネイル設定** in the confirmation window can show product images on Project folders.
