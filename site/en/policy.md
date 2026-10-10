# Data & policy

Updated October 10, 2026 · KaihenShelf 0.1.140

KaihenShelf is an unofficial integration. It is not provided, endorsed, or guaranteed by BOOTH, BOOTH Library Manager, pixiv, VRChat, or Unity.

## What the app reads

After consent, the app reads product names, descriptions, shops, categories, tags, URLs, image URLs, local paths, dates, age restrictions, purchased variations, and update notices from the local BOOTH Library Manager database. The database is opened read-only.

Opening product details scans the selected product folder for file names, dates, sizes, supported images, and supported embedded PSD/PSB previews. Import checks read the selected files, destination paths, GUIDs, and existing project files.

## Where data is saved

| Data | Location |
| --- | --- |
| Product cache, settings, favorites, avatar information, consent | `%LOCALAPPDATA%/KaihenShelf/Unity/state.json` |
| Import history | `<project>/UserSettings/KaihenShelf/history.json` |
| Folder image assignments and reduced images | `<project>/UserSettings/KaihenShelf/FolderThumbnails` |
| Temporary extraction and recovery data | `<project>/Library/KaihenShelf` |
| Imported product assets | The location shown in the import confirmation, inside Assets |

Common settings are shared between Unity projects on the same PC. History and folder images belong to each project. Previous JSON data is retained in `.bak` files. Legacy data locations are migrated; conflicting old and new data is not overwritten automatically. Edit shared settings in one Unity Editor at a time.

## Original files and imports

The app does not modify the BOOTH Library Manager database, original product files, or the Windows app's library database. Imports create or update files in the current project's Assets after user action and a confirmation screen. This confirmation screen belongs to KaihenShelf, although it resembles Unity's.

Temporary extraction data is normally removed on completion or cancellation. Failed writes trigger a recovery attempt; if recovery fails, backups are retained and their location is reported. Forced termination can leave temporary files. Resetting history or withdrawing consent does not delete imported assets or saved images.

## Network and external apps

Product images prefer local files. Where unavailable, and for shop images, the app may fetch stored HTTPS image URLs on `booth.pximg.net`, `booth.pm`, and its subdomains. It does not follow redirects or send login cookies. Image hosts receive normal request information including the image URL and IP address.

Regular thumbnails and previews use bounded memory caches. Product images assigned to project folders are saved as reduced images, at most 128 pixels, outside Assets. Product page buttons open BOOTH in your default browser. File cards pass the original file path to your default application; that application's network access and edits are its own behavior.

## What is not collected, and how to stop

The extension does not read BOOTH passwords, browser cookies, or VRChat accounts. It has no advertising, telemetry, automatic crash reporting, or product-data uploads to a separate server.

Use **同意を取り消して読み取りを停止** under **表示・連携設定** to withdraw consent and stop use. Existing settings, cached product information, history, folder images, and imported assets remain. Consent again to resume.

## Automatic detection and asset behavior

Classification, avatar compatibility, purchase type, old-version detection, and duplicate-name detection are aids based on available information. Review and correct errors manually. Import checks do not guarantee code safety, fixed-path compatibility, complete shader/SDK dependencies, or correct operation of every asset. Follow each product's license and installation instructions.

## This website

The site is hosted on GitHub Pages. It does not read your local library. GitHub may record connection information such as IP addresses for security: [GitHub Pages documentation](https://docs.github.com/en/pages/getting-started-with-github-pages/what-is-github-pages).
