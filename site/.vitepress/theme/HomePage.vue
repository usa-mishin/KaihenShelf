<script setup>
import { computed } from 'vue'
import { withBase } from 'vitepress'
import RepoLink from './RepoLink.vue'
const props = defineProps({ english: Boolean })
const guide = computed(() => withBase(props.english ? '/en/guide/' : '/guide/'))
const usage = computed(() => withBase(props.english ? '/en/guide/usage' : '/guide/usage'))
</script>
<template>
  <main class="home">
    <section class="intro">
      <h1>KaihenShelf</h1>
      <p class="intro-summary">{{ english ? 'Your VRChat asset library in Unity.' : 'Unityで使う、VRChatアイテム管理ツール。' }}</p>
      <p>{{ english ? 'Browse products saved by BOOTH Library Manager and folders on your PC, then import the files you need.' : 'BOOTH Library Managerに保存された商品や手元のフォルダから、改変アイテムを探してインポートできます。' }}</p>
      <RepoLink :english="english" />
      <nav class="intro-actions" :aria-label="english ? 'Documentation and shop' : 'ガイドとショップ'">
        <a class="text-link" :href="guide">{{ english ? 'Installation' : '導入手順' }}</a>
        <a class="text-link" :href="usage">{{ english ? 'Documentation' : '使い方' }}</a>
        <a class="text-link" href="https://usatrot-works.booth.pm/">BOOTH ↗</a>
      </nav>
      <p class="system-note">Windows 10 / 11 · Unity 2022.3 · VCC / ALCOM</p>
    </section>
    <section class="overview" :aria-label="english ? 'Features' : '主な機能'">
      <dl>
        <div><dt>{{ english ? 'Find assets' : '探す' }}</dt><dd>{{ english ? 'Filter by compatible avatar, shop, BOOTH tag, or category. Save frequently used products in favorites.' : '対応アバター・ショップ・BOOTHタグ・分類で絞り込み。よく使う商品はお気に入りに登録できます。' }}</dd></div>
        <div><dt>{{ english ? 'Check files' : '確認する' }}</dt><dd>{{ english ? 'Check UnityPackages and texture previews in a single product detail window.' : 'UnityPackageやテクスチャを商品詳細にまとめて表示。必要なファイルを確認できます。' }}</dd></div>
        <div><dt>{{ english ? 'Import' : '取り込む' }}</dt><dd>{{ english ? 'Set destinations by category, import multiple UnityPackages together, and revisit products from history.' : '分類ごとに配置先を指定し、複数のUnityPackageを一括インポート。取り込んだ商品は履歴から確認できます。' }}</dd></div>
      </dl>
    </section>
    <p class="home-notice">{{ english ? 'An unofficial tool by Usatrot Works. Not affiliated with or endorsed by BOOTH, BOOTH Library Manager, pixiv, VRChat, or Unity. Download product files before importing.' : 'Usatrot Worksによる非公式ツールです。BOOTH、BOOTH Library Manager、ピクシブ株式会社、VRChat、Unityが提供・保証・承認する製品ではありません。商品ファイルはあらかじめダウンロードしてください。' }}</p>
  </main>
</template>
