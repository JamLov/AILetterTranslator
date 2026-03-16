import { useAuthStore } from './stores/auth'
import router from './router'

export async function api(path: string, options: RequestInit = {}): Promise<Response> {
  const authStore = useAuthStore()

  const headers = new Headers(options.headers)
  if (authStore.token) {
    headers.set('Authorization', `Bearer ${authStore.token}`)
  }

  const res = await fetch(`${import.meta.env.VITE_API_BASE_URL ?? ''}${path}`, {
    ...options,
    headers
  })

  if (res.status === 401) {
    authStore.clearAuth()
    router.push('/login')
  }

  return res
}
