<script setup>
import { computed, ref } from 'vue'
import { withBase } from 'vitepress'
const props = defineProps({ name: String, english: Boolean, annotated: { type: Boolean, default: true }, caption: String })
const source = computed(() => withBase(`/screenshots/${props.name}.png`))
const viewer = ref(null)
// Keep the captured pixels unchanged. Annotations are a separate page layer.
const regions = {
  items: [[178,72,819,113,163,84], [178,193,819,31,163,207], [318,510,43,25,337,493]],
  detail: [[358,28,596,39,343,48], [819,153,126,25,797,165], [821,102,133,24,797,114]],
  'import-settings': [[292,241,184,74,278,253], [481,241,500,74,467,253], [179,128,121,26,164,141]]
}
const boxes = computed(() => props.annotated ? (regions[props.name] || []) : [])
const width = computed(() => props.name === 'detail' ? 962 : 1002)
const label = computed(() => props.english ? 'Open full-size screenshot' : '画像を原寸で開く')
</script>
<template>
  <figure class="guide-image">
    <a :href="source" target="_blank" rel="noopener" :aria-label="label" :title="label" class="screenshot-link" @click.prevent="viewer.showModal()">
      <img :src="source" :alt="caption || (english ? 'KaihenShelf in the Unity test project' : 'Unityのtestプロジェクト上のKaihenShelf')" :width="width" height="623" loading="lazy">
      <svg v-if="boxes.length" :viewBox="`0 0 ${width} 623`" aria-hidden="true" class="image-annotations">
        <g v-for="(r, i) in boxes" :key="i"><rect :x="r[0]" :y="r[1]" :width="r[2]" :height="r[3]"/><circle :cx="r[4]" :cy="r[5]" r="13"/><text :x="r[4]" :y="r[5]" dy=".35em">{{ i + 1 }}</text></g>
      </svg>
    </a>
    <figcaption>{{ caption }} <span>{{ english ? 'Click to enlarge.' : 'クリックで原寸表示。' }}</span></figcaption>
    <dialog ref="viewer" class="screenshot-dialog" :aria-label="caption || label" @click="event => { if (event.target === viewer) viewer.close() }">
      <form method="dialog"><button autofocus :aria-label="english ? 'Close screenshot' : '画像を閉じる'">×</button></form>
      <div class="screenshot-scroll">
        <div class="screenshot-link enlarged-screenshot" :style="{ width: width + 'px' }">
          <img :src="source" :alt="caption" :width="width" height="623" loading="lazy">
          <svg v-if="boxes.length" :viewBox="`0 0 ${width} 623`" aria-hidden="true" class="image-annotations">
            <g v-for="(r, i) in boxes" :key="i"><rect :x="r[0]" :y="r[1]" :width="r[2]" :height="r[3]"/><circle :cx="r[4]" :cy="r[5]" r="13"/><text :x="r[4]" :y="r[5]" dy=".35em">{{ i + 1 }}</text></g>
          </svg>
        </div>
      </div>
    </dialog>
  </figure>
</template>
