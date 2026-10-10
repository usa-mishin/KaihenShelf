<script setup>
import { computed } from 'vue'
import { withBase } from 'vitepress'
import RepoLink from './RepoLink.vue'
import GuideImage from './GuideImage.vue'
const props = defineProps({ english: Boolean })
const guide = computed(() => withBase(props.english ? '/en/guide/' : '/guide/'))
const usage = computed(() => withBase(props.english ? '/en/guide/usage' : '/guide/usage'))
</script>
<template>
  <main class="home">
    <section class="intro">
      <h1>KaihenShelf</h1>
      <p class="intro-summary">{{ english ? 'Browse your VRChat assets and import them into Unity.' : 'VRChatの改変アイテムを、Unityで探してインポート。' }}</p>
      <p>{{ english ? 'An editor extension for browsing products saved by BOOTH Library Manager and folders on your PC. Find an asset, check its files, and choose what to import.' : 'BOOTH Library Managerに保存された商品や、手元のフォルダを一覧で確認できるUnity Editor拡張です。対応アバターや分類で探して、必要なファイルを取り込めます。' }}</p>
      <div class="intro-actions"><a class="primary-link" :href="guide">{{ english ? 'Installation guide' : '導入する' }} <span aria-hidden="true">→</span></a><a class="text-link" :href="usage">{{ english ? 'How to use it' : '使い方を見る' }}</a></div>
      <p class="system-note">Windows 10 / 11 · Unity 2022.3 · VCC / ALCOM</p>
    </section>
    <GuideImage name="avatars" :english="english" :annotated="false" :caption="english ? 'The avatar list in Unity. The app interface is currently Japanese.' : 'Unity上のアバター一覧。商品画像を見ながら探せます。'" />
    <section class="overview">
      <h2>{{ english ? 'What you can do' : 'できること' }}</h2>
      <dl>
        <div><dt>{{ english ? 'Find an asset' : 'アイテムを探す' }}</dt><dd>{{ english ? 'Filter by compatible avatar, shop, BOOTH tag, or category. Keep frequently used products in favorites.' : '対応アバター、ショップ、BOOTHタグ、分類で絞り込み。よく使う商品はお気に入りに登録できます。' }}</dd></div>
        <div><dt>{{ english ? 'Check the files' : '中身を確認する' }}</dt><dd>{{ english ? 'View UnityPackages and texture previews in the product detail window.' : '商品詳細でUnityPackageやテクスチャを確認。画像はサムネイルで見比べられます。' }}</dd></div>
        <div><dt>{{ english ? 'Import into Unity' : 'Unityに取り込む' }}</dt><dd>{{ english ? 'Set destinations by category and select packages for batch import. Revisit imported products from history.' : '分類ごとにインポート先を指定。複数のUnityPackageを選んで一括インポートでき、導入後は履歴から確認できます。' }}</dd></div>
      </dl>
      <a class="text-link" :href="usage">{{ english ? 'See the illustrated guide →' : '画面で使い方を見る →' }}</a>
    </section>
    <section class="install-section">
      <div><h2>{{ english ? 'Install KaihenShelf' : 'KaihenShelfを導入する' }}</h2><p>{{ english ? 'Add the repository to VCC or ALCOM, then install KaihenShelf in your Unity project.' : 'VCCまたはALCOMに配布リポジトリを追加して、使いたいUnityプロジェクトに導入します。' }}</p><a class="text-link" :href="guide">{{ english ? 'Requirements and setup →' : '動作環境と導入手順 →' }}</a></div>
      <RepoLink :english="english" />
    </section>
    <p class="home-notice">{{ english ? 'An unofficial tool by Usatrot Works. Not affiliated with or endorsed by BOOTH, BOOTH Library Manager, pixiv, VRChat, or Unity. Download product files before importing. Product artwork in screenshots belongs to its respective creators.' : 'Usatrot Worksによる非公式ツールです。BOOTH、BOOTH Library Manager、ピクシブ株式会社、VRChat、Unityが提供・保証・承認する製品ではありません。商品ファイルはあらかじめダウンロードしてください。画面内の商品画像は各制作者に帰属します。' }}</p>
  </main>
</template>
