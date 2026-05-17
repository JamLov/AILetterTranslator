<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted, watch } from 'vue'
import { api } from '../api'

const props = defineProps<{
  fileNames: string[]
  currentIndex: number
  jobId: string
  projectId?: string
}>()

const emit = defineEmits<{
  close: []
  navigate: [index: number]
}>()

const imageCache = new Map<string, string>()
const currentUrl = ref<string | null>(null)
const isLoading = ref(false)

const currentFileName = computed(() => props.fileNames[props.currentIndex])
const hasPrev = computed(() => props.currentIndex > 0)
const hasNext = computed(() => props.currentIndex < props.fileNames.length - 1)
const counter = computed(() => `${props.currentIndex + 1} / ${props.fileNames.length}`)

async function loadImage(fileName: string) {
  if (imageCache.has(fileName)) {
    currentUrl.value = imageCache.get(fileName)!
    return
  }
  isLoading.value = true
  try {
    const basePath = props.projectId
      ? `/api/projects/${props.projectId}/jobs/${props.jobId}/files/${encodeURIComponent(fileName)}`
      : `/api/jobs/${props.jobId}/files/${encodeURIComponent(fileName)}`
    const res = await api(basePath)
    if (res.ok) {
      const blob = await res.blob()
      const url = URL.createObjectURL(blob)
      imageCache.set(fileName, url)
      currentUrl.value = url
    }
  } finally {
    isLoading.value = false
  }
}

function prev() {
  if (hasPrev.value) emit('navigate', props.currentIndex - 1)
}

function next() {
  if (hasNext.value) emit('navigate', props.currentIndex + 1)
}

function download() {
  if (!currentUrl.value) return
  const a = document.createElement('a')
  a.href = currentUrl.value
  a.download = currentFileName.value
  document.body.appendChild(a)
  a.click()
  document.body.removeChild(a)
}

function onKeydown(e: KeyboardEvent) {
  if (e.key === 'Escape') emit('close')
  else if (e.key === 'ArrowLeft') prev()
  else if (e.key === 'ArrowRight') next()
}

watch(() => props.currentIndex, () => {
  loadImage(currentFileName.value)
}, { immediate: true })

onMounted(() => {
  document.addEventListener('keydown', onKeydown)
})

onUnmounted(() => {
  document.removeEventListener('keydown', onKeydown)
  for (const url of imageCache.values()) {
    URL.revokeObjectURL(url)
  }
})
</script>

<template>
  <div class="dialog-overlay" @click.self="emit('close')">
    <div class="image-preview-dialog" role="dialog" aria-modal="true" aria-labelledby="preview-title">
      <button v-if="hasPrev" class="nav-btn nav-btn-prev" @click="prev" title="Previous (←)">‹</button>
      <div class="image-preview-header">
        <span id="preview-title" class="image-preview-filename">{{ currentFileName }}</span>
        <span class="image-preview-counter">{{ counter }}</span>
        <div class="image-preview-actions">
          <button class="btn btn-sm btn-secondary" @click="download" title="Download">
            ⬇ Download
          </button>
          <button class="btn btn-sm btn-secondary" @click="emit('close')" title="Close">
            ✕
          </button>
        </div>
      </div>
      <div class="image-preview-body">
        <span v-if="isLoading" class="image-preview-loading">Loading...</span>
        <img v-else-if="currentUrl" :src="currentUrl" :alt="currentFileName" />
      </div>
      <button v-if="hasNext" class="nav-btn nav-btn-next" @click="next" title="Next (→)">›</button>
    </div>
  </div>
</template>

<style scoped>
.dialog-overlay {
  position: fixed;
  inset: 0;
  background: rgba(255, 255, 255, 0.75);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
}
[data-theme="dark"] .dialog-overlay {
  background: rgba(0, 0, 0, 0.8);
}
.image-preview-dialog {
  position: relative;
  background: var(--color-surface);
  border-radius: var(--radius-lg, 12px);
  box-shadow: 0 8px 32px rgba(0, 0, 0, 0.3);
  max-width: 92vw;
  max-height: 92vh;
  display: flex;
  flex-direction: column;
  overflow: visible;
}
.image-preview-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 12px 16px;
  border-bottom: 1px solid var(--color-border);
  gap: 12px;
  border-radius: var(--radius-lg, 12px) var(--radius-lg, 12px) 0 0;
  background: var(--color-surface);
}
.image-preview-filename {
  font-weight: 500;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  min-width: 0;
}
.image-preview-counter {
  color: var(--color-text-secondary);
  font-size: 0.85em;
  flex-shrink: 0;
}
.image-preview-actions {
  display: flex;
  gap: 8px;
  flex-shrink: 0;
}
.image-preview-body {
  padding: 16px;
  overflow: auto;
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 200px;
  border-radius: 0 0 var(--radius-lg, 12px) var(--radius-lg, 12px);
  background: var(--color-surface);
}
.image-preview-body img {
  max-width: 88vw;
  max-height: 78vh;
  object-fit: contain;
}
.image-preview-loading {
  color: var(--color-text-secondary);
  font-size: 0.9em;
}
.nav-btn {
  position: absolute;
  top: 50%;
  transform: translateY(-50%);
  background: rgba(0, 0, 0, 0.5);
  color: white;
  border: none;
  border-radius: 50%;
  width: 40px;
  height: 40px;
  font-size: 24px;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1001;
  transition: background 0.2s;
}
.nav-btn:hover {
  background: rgba(0, 0, 0, 0.8);
}
.nav-btn-prev {
  left: -20px;
}
.nav-btn-next {
  right: -20px;
}
</style>
