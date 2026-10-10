<script setup>
import { ref } from 'vue'
defineProps({ english: Boolean })
const copied = ref(false)
const failed = ref(false)
const url = 'https://usa-mishin.github.io/KaihenShelf/index.json'
async function copy() {
  try { await navigator.clipboard.writeText(url); copied.value = true; failed.value = false }
  catch { failed.value = true }
}
</script>
<template>
  <div class="repo-link">
    <a class="install-link" :href="'vcc://vpm/addRepo?url=' + encodeURIComponent(url)">{{ english ? 'Add to VCC / ALCOM' : 'VCC / ALCOMに追加' }} <span aria-hidden="true">↗</span></a>
    <button class="copy-link" @click="copy">{{ copied ? (english ? 'Copied' : 'コピーしました') : (english ? 'Copy repository URL' : '配布URLをコピー') }}</button>
    <p v-if="copied || failed" role="status">{{ failed ? (english ? 'Copy the URL below manually.' : '下のURLを選択してコピーしてください。') : (english ? 'Paste this URL in your package manager’s repository settings.' : 'パッケージ管理アプリのリポジトリ追加に貼り付けてください。') }}</p>
    <code class="repo-url">{{ url }}</code>
  </div>
</template>
