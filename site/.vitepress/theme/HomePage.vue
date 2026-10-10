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
      <div class="intro-copy">
        <h1>KaihenShelf</h1>
        <p class="intro-summary">{{ english ? 'Your VRChat asset library in Unity.' : 'Unityで使う、VRChatアイテム管理ツール。' }}</p>
        <p class="intro-description">{{ english ? 'Browse products saved by BOOTH Library Manager and folders on your PC, then import the files you need.' : 'BOOTH Library Managerに保存された商品や手元のフォルダから、改変アイテムを探してインポートできます。' }}</p>
        <p class="system-note">Windows 10 / 11 · Unity 2022.3</p>
      </div>
      <section class="home-install" :aria-label="english ? 'Install KaihenShelf' : 'KaihenShelfを導入'">
        <h2>{{ english ? 'Add to your Unity project' : 'Unityプロジェクトに追加' }}</h2>
        <p>{{ english ? 'Start by adding the package repository.' : 'まずは配布リポジトリを追加してください。' }}</p>
        <RepoLink :english="english" />
      </section>
    </section>
    <nav class="home-navigation" :aria-label="english ? 'Documentation and shop' : 'ガイドとショップ'">
      <a :href="guide">
        <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 3v12m-5-5 5 5 5-5M4 16v5h16v-5"/></svg>
        <span><strong>{{ english ? 'Installation guide' : '導入手順を見る' }}</strong><small>{{ english ? 'Set up KaihenShelf' : 'はじめて使う方はこちら' }}</small></span><b aria-hidden="true">→</b>
      </a>
      <a :href="usage">
        <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 5v16M12 5C8 2 4 3 2 4v15c4-2 7-1 10 2 3-3 6-4 10-2V4c-2-1-6-2-10 1Z"/></svg>
        <span><strong>{{ english ? 'User guide' : '使い方を見る' }}</strong><small>{{ english ? 'Steps with screenshots' : '画面付きで操作を確認' }}</small></span><b aria-hidden="true">→</b>
      </a>
      <a href="https://usatrot-works.booth.pm/">
        <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M3 9h18l-2-6H5L3 9Zm1 0v12h16V9M9 21v-7h6v7M3 9v2c0 3 5 3 5 0 0 3 8 3 8 0 0 3 5 3 5 0V9"/></svg>
        <span><strong>{{ english ? 'Visit BOOTH' : 'BOOTHを開く' }}</strong><small>Usatrot Works</small></span><b aria-hidden="true">↗</b>
      </a>
    </nav>
    <section class="overview">
      <h2>{{ english ? 'What you can do' : 'KaihenShelfでできること' }}</h2>
      <dl>
        <div><dt>{{ english ? 'Find assets' : '探す' }}</dt><dd>{{ english ? 'Filter by compatible avatar, shop, BOOTH tag, or category. Save frequently used products in favorites.' : '対応アバター・ショップ・BOOTHタグ・分類で絞り込み。よく使う商品はお気に入りに登録できます。' }}</dd></div>
        <div><dt>{{ english ? 'Check files' : '確認する' }}</dt><dd>{{ english ? 'Check UnityPackages and texture previews in a single product detail window.' : 'UnityPackageやテクスチャを商品詳細にまとめて表示。必要なファイルを確認できます。' }}</dd></div>
        <div><dt>{{ english ? 'Import' : '取り込む' }}</dt><dd>{{ english ? 'Set destinations by category, import multiple UnityPackages together, and revisit products from history.' : '分類ごとに配置先を指定し、複数のUnityPackageを一括インポート。取り込んだ商品は履歴から確認できます。' }}</dd></div>
      </dl>
    </section>
    <p class="home-notice">{{ english ? 'An unofficial tool by Usatrot Works. Not affiliated with or endorsed by BOOTH, BOOTH Library Manager, pixiv, VRChat, or Unity. Download product files before importing.' : 'Usatrot Worksによる非公式ツールです。BOOTH、BOOTH Library Manager、ピクシブ株式会社、VRChat、Unityが提供・保証・承認する製品ではありません。商品ファイルはあらかじめダウンロードしてください。' }}</p>
  </main>
</template>
<style scoped>
.home { max-width: 1080px; margin: auto; padding: 0 32px; }
.home .intro { display: grid; grid-template-columns: 1.25fr 1fr; gap: 44px; align-items: center; padding: 52px 0 32px; }
h1 { font-size: 42px; font-weight: 700; letter-spacing: -.035em; line-height: 1.2; margin: 0 0 20px; }
h1::after { content: ''; display: block; width: 44px; height: 4px; background: var(--vp-c-brand-1); margin-top: 18px; border-radius: 2px; }
.intro-summary { font-size: 19px; line-height: 1.7; font-weight: 600; margin-bottom: 12px; }
.intro-description { font-size: 14px; color: var(--vp-c-text-2); line-height: 1.9; }
.system-note { font-size: 12px; color: var(--vp-c-text-2); margin-top: 18px; }
.home-install { padding: 24px; background: #f5f7f9; border: 1px solid #e0e5e9; border-radius: 8px; }
.home-install h2 { font-size: 17px; font-weight: 650; margin: 0 0 8px; line-height: 1.5; }
.home-install>p { color: var(--vp-c-text-2); font-size: 13px; line-height: 1.7; margin-bottom: 18px; }
.home-install :deep(.install-link) { min-height: 46px; }
.home-install :deep(.copy-link) { background: #fff; }
.home-navigation { display: grid; grid-template-columns: repeat(3, 1fr); gap: 14px; padding-bottom: 34px; }
.home-navigation a { display: flex; align-items: center; gap: 14px; padding: 19px 18px; border: 1px solid #b9cbd9; border-radius: 6px; color: var(--vp-c-brand-1); transition: background .15s, border-color .15s; }
.home-navigation a:hover { background: #eef4f8; border-color: var(--vp-c-brand-1); }
.home-navigation a:hover strong { text-decoration: underline; text-underline-offset: 4px; }
.home-navigation svg { width: 24px; height: 24px; flex-shrink: 0; fill: none; stroke: currentColor; stroke-width: 1.6; stroke-linecap: round; stroke-linejoin: round; }
.home-navigation strong { display: block; font-size: 15px; font-weight: 650; line-height: 1.5; }
.home-navigation small { display: block; font-size: 12px; color: var(--vp-c-text-2); margin-top: 5px; line-height: 1.5; }
.home-navigation b { margin-left: auto; font-size: 19px; }
.overview { border-top: 1px solid var(--vp-c-divider); padding: 26px 0 30px; }
.overview h2 { font-size: 19px; font-weight: 650; margin-bottom: 22px; }
.overview dl { display: grid; grid-template-columns: repeat(3, 1fr); gap: 30px; margin: 0; }
.overview dt { font-size: 16px; font-weight: 650; margin-bottom: 9px; }
.overview dd { font-size: 14px; line-height: 1.9; color: var(--vp-c-text-2); margin: 0; }
.home-notice { border-top: 1px solid var(--vp-c-divider); font-size: 12px; color: var(--vp-c-text-2); line-height: 1.9; padding: 22px 0 30px; }
@media(max-width:850px) { .home .intro { grid-template-columns: 1fr; gap: 24px; padding-top: 34px; } .home-install { max-width: 520px; } .home-navigation { grid-template-columns: 1fr; gap: 10px; } .home-navigation a { padding: 15px 18px; } .overview dl { grid-template-columns: 1fr; gap: 22px; } }
@media(max-width:480px) { .home { padding: 0 20px; } h1 { font-size: 35px; } .intro-summary { font-size: 18px; } .home-install { padding: 20px; } }
</style>
