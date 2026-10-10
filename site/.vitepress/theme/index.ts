import DefaultTheme from 'vitepress/theme'
import HomePage from './HomePage.vue'
import RepoLink from './RepoLink.vue'
import GuideImage from './GuideImage.vue'
import './style.css'
export default {
  extends: DefaultTheme,
  enhanceApp({ app }) { app.component('HomePage', HomePage); app.component('RepoLink', RepoLink); app.component('GuideImage', GuideImage) }
}
