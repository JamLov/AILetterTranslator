import { defineStore } from 'pinia'
import { ref, computed } from 'vue'

export const useAuthStore = defineStore('auth', () => {
  const token = ref<string | null>(localStorage.getItem('userToken'))
  const userId = ref<string | null>(localStorage.getItem('userId'))

  const isAuthenticated = computed(() => !!token.value)

  function setAuth(newToken: string, newUserId: string) {
    token.value = newToken
    userId.value = newUserId
    localStorage.setItem('userToken', newToken)
    localStorage.setItem('userId', newUserId)
  }

  function clearAuth() {
    token.value = null
    userId.value = null
    localStorage.removeItem('userToken')
    localStorage.removeItem('userId')
  }

  return { token, userId, isAuthenticated, setAuth, clearAuth }
})
