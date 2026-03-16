import { defineStore } from 'pinia'
import { ref, watch } from 'vue'

export const useThemeStore = defineStore('theme', () => {
  const stored = localStorage.getItem('theme')
  const prefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches
  const isDark = ref(stored ? stored === 'dark' : prefersDark)

  function apply() {
    document.documentElement.setAttribute('data-theme', isDark.value ? 'dark' : 'light')
  }

  function toggle() {
    isDark.value = !isDark.value
  }

  watch(isDark, (val) => {
    localStorage.setItem('theme', val ? 'dark' : 'light')
    apply()
  })

  // Apply on creation
  apply()

  return { isDark, toggle }
})
