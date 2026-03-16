import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import DashboardView from '../src/views/DashboardView.vue'
import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '../src/stores/auth'

const NewJobView = { template: '<div>New Job</div>' }
const JobDetailView = { template: '<div>Job Detail</div>' }

const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', component: { template: '<div>Home</div>' } },
    { path: '/dashboard', component: DashboardView, meta: { requiresAuth: true } },
    { path: '/login', name: 'login', component: { template: '<div>Login</div>' } },
    { path: '/new-job', component: NewJobView, meta: { requiresAuth: true } },
    { path: '/job/:jobId', name: 'job-detail', component: JobDetailView, meta: { requiresAuth: true } }
  ]
})

global.fetch = vi.fn()

function createFetchResponse(data: any, ok = true, status = 200) {
  return { ok, status, json: () => new Promise((resolve) => resolve(data)) }
}

describe('DashboardView', () => {
  beforeEach(async () => {
    vi.resetAllMocks()
    localStorage.clear()
    setActivePinia(createPinia())
    await router.push('/')
    await router.isReady()
  })

  it('redirects to login if no token is found', async () => {
    const pushSpy = vi.spyOn(router, 'push')

    await router.replace('/dashboard')
    await router.isReady()

    mount(DashboardView, {
      global: {
        plugins: [router]
      }
    })

    expect(pushSpy).toHaveBeenCalledWith('/login')
  })

  it('shows loading state initially', async () => {
    const authStore = useAuthStore()
    authStore.setAuth('fake-token', 'fake-user')
    ;(global.fetch as any).mockResolvedValue(createFetchResponse([]))

    await router.push('/dashboard')
    await router.isReady()

    const wrapper = mount(DashboardView, {
      global: {
        plugins: [router]
      }
    })

    expect(wrapper.text()).toContain('Loading your jobs...')
  })

  it('shows empty state when no jobs are fetched', async () => {
    const authStore = useAuthStore()
    authStore.setAuth('fake-token', 'fake-user')
    ;(global.fetch as any).mockResolvedValue(createFetchResponse([]))

    await router.push('/dashboard')
    await router.isReady()

    const wrapper = mount(DashboardView, {
      global: {
        plugins: [router]
      }
    })

    await new Promise(resolve => setTimeout(resolve, 0))

    expect(wrapper.find('.state-box').exists()).toBe(true)
    expect(wrapper.text()).toContain('No jobs yet')
  })

  it('renders a table of jobs when jobs are fetched', async () => {
    const authStore = useAuthStore()
    authStore.setAuth('fake-token', 'fake-user')
    const mockJobs = [
      { jobId: '1', jobName: 'Job 1', createdAt: new Date().toISOString(), status: 'Finished', originalFileCount: 2 },
      { jobId: '2', jobName: 'Job 2', createdAt: new Date().toISOString(), status: 'In Progress', originalFileCount: 1 },
    ]
    ;(global.fetch as any).mockResolvedValue(createFetchResponse(mockJobs))

    await router.push('/dashboard')
    await router.isReady()

    const wrapper = mount(DashboardView, {
      global: {
        plugins: [router]
      }
    })

    await new Promise(resolve => setTimeout(resolve, 0))

    expect(wrapper.findAll('.job-row').length).toBe(2)
    expect(wrapper.text()).toContain('Job 1')
    expect(wrapper.text()).toContain('Job 2')
    expect(wrapper.text()).toContain('Finished')
    expect(wrapper.text()).toContain('In Progress')
  })

  it('shows an error message when fetch fails', async () => {
    const authStore = useAuthStore()
    authStore.setAuth('fake-token', 'fake-user')
    ;(global.fetch as any).mockRejectedValue(new Error('API Down'))

    await router.push('/dashboard')
    await router.isReady()

    const wrapper = mount(DashboardView, {
      global: {
        plugins: [router]
      }
    })

    await new Promise(resolve => setTimeout(resolve, 0))

    expect(wrapper.find('.state-error').exists()).toBe(true)
    expect(wrapper.text()).toContain('An error occurred while fetching your jobs.')
  })
})
