<script setup lang="ts">
import { ref, onUnmounted, nextTick } from 'vue'
import { api } from '../api'

const props = defineProps<{
  fileName: string
  jobId: string
  projectId?: string
}>()

const emit = defineEmits<{
  preview: [fileName: string, imageUrl: string]
}>()

const imageUrl = ref<string | null>(null)
const showTooltip = ref(false)
const isLoading = ref(false)
const tooltipStyle = ref<Record<string, string>>({})
let hoverTimeout: number | null = null

async function fetchImage() {
  if (imageUrl.value || isLoading.value) return
  isLoading.value = true
  try {
    const basePath = props.projectId
      ? `/api/projects/${props.projectId}/jobs/${props.jobId}/files/${encodeURIComponent(props.fileName)}`
      : `/api/jobs/${props.jobId}/files/${encodeURIComponent(props.fileName)}`
    const res = await api(basePath)
    if (res.ok) {
      const blob = await res.blob()
      imageUrl.value = URL.createObjectURL(blob)
    }
  } finally {
    isLoading.value = false
  }
}

function positionTooltip(el: HTMLElement) {
  const rect = el.getBoundingClientRect()
  const margin = 12
  const vw = window.innerWidth
  const vh = window.innerHeight

  // Position to the right of the item, vertically centered
  let left = rect.right + margin
  let top = rect.top

  // If it would overflow right, position to the left of the item instead
  const tooltipMaxW = vw * 0.6
  if (left + tooltipMaxW > vw - margin) {
    left = Math.max(margin, rect.left - tooltipMaxW - margin)
  }

  // Ensure it doesn't go below viewport
  const tooltipMaxH = vh * 0.6
  if (top + tooltipMaxH > vh - margin) {
    top = Math.max(margin, vh - tooltipMaxH - margin)
  }

  tooltipStyle.value = {
    left: `${left}px`,
    top: `${top}px`
  }
}

function onMouseEnter(e: MouseEvent) {
  const target = e.currentTarget as HTMLElement
  hoverTimeout = window.setTimeout(async () => {
    showTooltip.value = true
    positionTooltip(target)
    await fetchImage()
    await nextTick()
    positionTooltip(target)
  }, 300)
}

function onMouseLeave() {
  if (hoverTimeout) {
    clearTimeout(hoverTimeout)
    hoverTimeout = null
  }
  showTooltip.value = false
}

async function onClick() {
  await fetchImage()
  if (imageUrl.value) {
    emit('preview', props.fileName, imageUrl.value)
  }
}

onUnmounted(() => {
  if (imageUrl.value) {
    URL.revokeObjectURL(imageUrl.value)
  }
})
</script>

<template>
  <li class="file-item" @mouseenter="onMouseEnter" @mouseleave="onMouseLeave" @click="onClick" :title="fileName">
    <span class="file-item-name">{{ fileName }}</span>
    <Teleport to="body">
      <div v-if="showTooltip && imageUrl" ref="tooltipRef" class="file-preview-tooltip" :style="tooltipStyle">
        <div class="file-preview-filename">{{ fileName }}</div>
        <img :src="imageUrl" :alt="fileName" />
      </div>
      <div v-if="showTooltip && isLoading" class="file-preview-tooltip" :style="tooltipStyle">
        <span class="file-preview-loading">Loading...</span>
      </div>
    </Teleport>
  </li>
</template>

<style scoped>
.file-item {
  cursor: pointer;
  padding: 4px 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.file-item:hover {
  color: var(--color-primary);
}
.file-item-name {
  display: inline;
}
</style>

<style>
.file-preview-tooltip {
  position: fixed;
  z-index: 500;
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md, 8px);
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
  padding: 8px;
  max-width: 60vw;
  max-height: 60vh;
  pointer-events: none;
}
.file-preview-tooltip img {
  max-width: calc(60vw - 16px);
  max-height: calc(60vh - 16px);
  object-fit: contain;
  border-radius: var(--radius-sm, 4px);
}
.file-preview-loading {
  display: block;
  padding: 16px;
  text-align: center;
  color: var(--color-text-secondary);
  font-size: 0.85em;
}
.file-preview-filename {
  padding: 2px 4px 6px;
  font-size: 0.8em;
  color: var(--color-text-secondary);
  word-break: break-all;
  text-align: center;
}
</style>
