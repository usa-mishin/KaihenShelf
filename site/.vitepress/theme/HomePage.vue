<script setup>
import { computed } from 'vue'
import { withBase } from 'vitepress'
import RepoLink from './RepoLink.vue'
const props = defineProps({ english: Boolean })
const guide = computed(() => withBase(props.english ? '/en/guide/' : '/guide/'))
const features = computed(() => props.english ? [
  ['01', 'Find the right piece.', 'Browse by avatar, shop, tags, or category. Keep favorites close and adjust automatic matches when needed.'],
  ['02', 'See what is inside.', 'UnityPackages, textures, and source files, together in one detail window. Preview images before you import.'],
  ['03', 'Bring it into Unity.', 'Choose a destination and review the files. Select multiple UnityPackages to import their contents together.']
] : [
  ['01', 'あのアイテムが、見つかる。', '対応アバター、ショップ、タグ、分類で絞り込み。お気に入りも、自動判定の手直しも、いつものUnityから。'],
  ['02', '中身を見て、選べる。', 'UnityPackageもテクスチャも、商品詳細にまとめて表示。画像を確かめて、必要なファイルを選べます。'],
  ['03', 'そのまま、改変へ。', '配置先とファイルを確認してインポート。分かれたUnityPackageも、選択した内容をまとめて取り込めます。']
])
</script>
<template>
  <main class="home">
    <section class="hero">
      <div class="hero-copy">
        <p class="eyebrow">FOR YOUR NEXT AVATAR.</p>
        <h1>{{ english ? 'A home for your' : '改変アイテムを、' }}<br><span>{{ english ? 'VRChat assets.' : 'ひとつの棚に。' }}</span></h1>
        <p class="hero-lead">{{ english ? 'Find what you love. Bring it into Unity.' : '探す時間を、つくる時間に。' }}<br>{{ english ? 'KaihenShelf connects your local library to your creative workflow.' : 'KaihenShelfは、手元のアイテムを探すところからUnityへの導入までをつなぐライブラリです。' }}</p>
        <a class="primary-link" :href="guide">{{ english ? 'Get started' : 'KaihenShelfをはじめる' }} <span aria-hidden="true">→</span></a>
        <p class="hero-meta">Windows 10 / 11 · Unity 2022.3 · {{ english ? 'Japanese app interface' : 'Unity Editor拡張' }}</p>
      </div>
      <div class="shelf-demo" :aria-label="english ? 'Illustration of browsing and importing assets' : 'アイテムの検索と導入を表すイメージ'">
        <div class="demo-bar"><img :src="withBase('/shelf.svg')" alt="" width="25" height="25"><b>KaihenShelf</b><span>YOUR COLLECTION</span></div>
        <div class="demo-body"><div class="demo-menu"><span>Avatars</span><strong>Items & assets</strong><span>Shops</span><span>BOOTH tags</span></div>
          <div class="demo-main"><div class="demo-search">⌕ <span>{{ english ? 'Something for your next look…' : '次の改変に、何を合わせよう。' }}</span></div>
            <div class="demo-tabs"><b>All</b><span>Outfits</span><span>Hair</span><span>Textures</span></div>
            <div class="demo-cards"><div v-for="(item, i) in ['Weekend outfit', 'Soft hair', 'Eye colors']" :key="item" class="demo-card"><div :class="['sample-art', 'art-' + i]"><svg viewBox="0 0 100 100" aria-hidden="true"><path v-if="i === 0" d="M32 23 13 36l12 19 10-7v33h31V48l10 7 12-19-20-13-10 10h-15z"/><path v-else-if="i === 1" d="M25 77V43c0-32 51-32 51 0v34l-13-8V42c-8 7-13 6-20 0v27z"/><g v-else><path d="M12 50c19-29 56-29 76 0-20 28-56 28-76 0z"/><circle cx="50" cy="50" r="12"/></g></svg></div><b>{{ item }}</b><span>Sample collection</span></div></div>
            <div class="demo-import"><span>{{ english ? 'Choose together. Import together.' : '必要なファイルを、まとめて。' }}</span><b>↓ Import</b></div>
          </div>
        </div>
        <p class="demo-caption">{{ english ? 'Concept illustration · sample assets, not an app screenshot' : '機能を紹介するイメージ図です。商品・画面はサンプルです。' }}</p>
      </div>
    </section>
    <section class="features" :aria-label="english ? 'Features' : 'できること'"><article v-for="f in features" :key="f[0]"><span class="feature-number">{{ f[0] }}</span><h2>{{ f[1] }}</h2><p>{{ f[2] }}</p></article></section>
    <section class="connection"><p class="eyebrow">YOUR FILES, YOUR WORKFLOW.</p><h2>{{ english ? 'Your library, inside Unity.' : 'いつものライブラリを、Unityのそばに。' }}</h2><p>{{ english ? 'Read product information saved by BOOTH Library Manager, or register a local folder. Your original product files stay where they are.' : 'BOOTH Library Managerに保存された商品情報を読み込んで表示。フォルダからの追加登録にも対応します。元の商品ファイルは、そのままの場所に。' }}</p><div class="flow"><span>BOOTH Library Manager<br><small>{{ english ? 'or a local folder' : 'または手元のフォルダ' }}</small></span><b aria-hidden="true">→</b><strong>KaihenShelf</strong><b aria-hidden="true">→</b><span>Unity<br><small>{{ english ? 'Review & import' : '確認してインポート' }}</small></span></div></section>
    <section class="start"><div><p class="eyebrow">MAKE ROOM FOR YOUR NEXT IDEA.</p><h2>{{ english ? 'Ready for your next edit?' : '次の改変を、ここから。' }}</h2><p>{{ english ? 'Add the repository, then install KaihenShelf in your project. See the guide for the full setup.' : '配布リポジトリを追加し、使いたいプロジェクトにKaihenShelfを導入します。詳しい手順は導入ガイドへ。' }}</p><a :href="guide" class="text-link">{{ english ? 'Read the installation guide →' : '導入ガイドを見る →' }}</a></div><RepoLink :english="english" /></section>
    <p class="home-notice">{{ english ? 'Unofficial integration. Not affiliated with or endorsed by BOOTH, BOOTH Library Manager, pixiv, VRChat, or Unity. Download products with BOOTH Library Manager before importing them. The app currently uses Japanese UI.' : 'BOOTH Library Manager連携・非公式。BOOTH、BOOTH Library Manager、ピクシブ株式会社、VRChat、Unityが提供・保証・承認する製品ではありません。商品ファイルは、あらかじめBOOTH Library Managerなどでダウンロードしてください。' }}</p>
  </main>
</template>
