# Using KaihenShelf

Follow the numbered callouts in these screenshots from a Unity test project. The app currently uses Japanese UI; explanations below each image are in English.

## Find an asset

Browse avatars or items and assets. Filter by name, shop, BOOTH tag, or compatible avatar. Save favorites and use **リセット** to reset the filters. Automatic classification and avatar detection can be corrected in **情報を編集** (Edit information).

<GuideImage name="items" english caption="Finding an asset in the item list" />

1. **Search filters**: narrow the list by product name, shop, tag, or compatible avatar.
2. **Category tabs**: choose outfits, hair, textures, or another category.
3. **Details (詳細)**: open the product's files in a separate window.

## Check the files

Open the product detail window and switch between UnityPackages, textures, image source files, 3D data, and other files. Supported images show previews; source formats need a supported embedded preview. File cards can open files in your PC's default application.

<GuideImage name="detail" english caption="Product files and import controls" />

1. **File type tabs**: switch between UnityPackages, textures, and other files.
2. **Import (インポート)**: open the confirmation window for this UnityPackage.
3. **Batch import (一括インポート)**: enter selection mode to choose several UnityPackages.

## Choose a destination

In **インポート先・分類設定**, configure import destinations and classification rules, then click **保存して適用** (Save & apply). An empty destination retains the package's original structure under Assets.

<GuideImage name="import-settings" english caption="Import destinations and classification rules" />

1. **Destination**: set a folder under Assets. The resulting path appears below the input.
2. **Rules**: edit the subcategory and tag conditions for this category.
3. **Save & apply (保存して適用)**: apply your changes. This button stays disabled until you make a change.

Subcategory and included-tag rules match with OR. Any excluded tag excludes the category. Tags match exactly unless you use `*`, for example `*material*`. Categories are checked from top to bottom; manual product classifications take priority.

## Import

Click **インポート**, review the destination and files, then click **Import**. To combine several UnityPackages, use **一括インポート**, select the required packages, and review the combined file list. If materials and prefabs come separately, select both.

KaihenShelf places the combined files with their GUIDs preserved before Unity reads them. It cannot detect every dependency or fixed-path requirement. Follow the asset creator's instructions and install required shaders and SDKs first.

If shared files have different contents, choose which package to use for each file. The chosen settings apply to every asset referencing that file. After making all choices, click **インポート内容を確認** (Review import contents) to proceed to the regular import confirmation.

Texture folders can be imported with their hierarchy preserved; only supported texture formats are included.

## After import

Use **インポート履歴** (Import history) to revisit a product or its destination. Resetting history does not delete imported assets. **商品サムネイル設定** in the confirmation window can show product images on Project folders.
