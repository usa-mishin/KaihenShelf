# Get started

KaihenShelf is a **Windows** Unity Editor extension by Usatrot Works. It has been tested with **Unity 2022.3.22f1**. The app interface is currently Japanese; this site includes an English guide.

## 1. Add to your project

Install VCC or ALCOM, add the repository below, then install **KaihenShelf** from your project's package management screen.

<RepoLink english />

The button opens the compatible application registered on your PC. If it does not work, copy the repository URL and paste it into your package manager's Add Repository screen.

## 2. Open in Unity

Choose **KaihenShelf → KaihenShelf** from the Unity menu. Review the data access, storage, and network policy, then consent to start.

## 3. Load your library

In **表示・連携設定** (Display & integration), check the BOOTH Library Manager database location, then click **データ再取得** (Reload data). You can browse to the database if it is stored elsewhere.

Download product files with BOOTH Library Manager or another appropriate method first. KaihenShelf does not sign in to BOOTH or download purchased files. You can also use **フォルダから追加登録** to register a local folder.

## Migrating from a UnityPackage installation

Back up your project and close Unity before installing through VCC or ALCOM. The legacy tool folder is replaced during migration. Do not store product assets inside the tool's own folder.

Settings, import history, and folder thumbnails are stored outside the package and are retained during normal updates. Old data locations are migrated on first launch. If a data conflict is reported, inspect the indicated locations instead of overwriting them.

## Manual installation

You can also import the UnityPackage from [GitHub Releases](https://github.com/usa-mishin/KaihenShelf/releases) using **Assets → Import Package → Custom Package**. Do not install both variants together. VCC or ALCOM is recommended for updates.

KaihenShelf itself does not require the VRChat SDK. Install shaders and SDKs required by your assets according to their creators' instructions.
