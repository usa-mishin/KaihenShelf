import { defineConfig } from 'vitepress'
const repo = 'https://github.com/usa-mishin/KaihenShelf'
export default defineConfig({
  base: '/KaihenShelf/',
  title: 'KaihenShelf',
  description: 'VRChatの改変アイテムを、探すところからUnityへの導入まで。Usatrot WorksのUnity Editor拡張。',
  lang: 'ja',
  cleanUrls: true,
  appearance: false,
  head: [['link', { rel: 'icon', href: '/KaihenShelf/shelf.svg', type: 'image/svg+xml' }]],
  locales: {
    root: { label: '日本語', lang: 'ja', themeConfig: {
      nav: [{ text: 'はじめる', link: '/guide/' }, { text: '使い方', link: '/guide/usage' }, { text: '更新履歴', link: '/changelog' }, { text: 'BOOTH', link: 'https://usatrot-works.booth.pm/' }],
      sidebar: [{ text: 'KaihenShelf', items: [
        { text: '導入する', link: '/guide/' }, { text: '使い方', link: '/guide/usage' },
        { text: 'よくある質問', link: '/guide/faq' }, { text: 'データとポリシー', link: '/policy' },
        { text: '更新履歴', link: '/changelog' }
      ] }],
      outline: { label: 'このページの内容' }, docFooter: { prev: '前へ', next: '次へ' }
    } },
    en: { label: 'English', lang: 'en', description: 'Find your VRChat assets and bring them into Unity.', themeConfig: {
      nav: [{ text: 'Get started', link: '/en/guide/' }, { text: 'Guide', link: '/en/guide/usage' }, { text: 'Changelog', link: '/en/changelog' }, { text: 'BOOTH', link: 'https://usatrot-works.booth.pm/' }],
      sidebar: [{ text: 'KaihenShelf', items: [
        { text: 'Installation', link: '/en/guide/' }, { text: 'Using KaihenShelf', link: '/en/guide/usage' },
        { text: 'FAQ', link: '/en/guide/faq' }, { text: 'Data & policy', link: '/en/policy' }, { text: 'Changelog', link: '/en/changelog' }
      ] }]
    } }
  },
  themeConfig: {
    logo: '/shelf.svg',
    socialLinks: [{ icon: 'github', link: repo }],
    search: { provider: 'local' },
    footer: { message: 'An unofficial tool by Usatrot Works · <a href="https://github.com/usa-mishin/KaihenShelf/blob/main/LICENSE">MIT License</a>', copyright: 'KaihenShelf · Usatrot Works' }
  },
  sitemap: { hostname: 'https://usa-mishin.github.io/KaihenShelf/' }
})
