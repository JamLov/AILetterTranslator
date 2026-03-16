import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import JobDetailView from '../src/views/JobDetailView.vue'
import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '../src/stores/auth'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', component: { template: '<div>Home</div>' } },
    { path: '/job/:jobId', name: 'job-detail', component: JobDetailView, meta: { requiresAuth: true } },
    { path: '/login', component: { template: '<div>Login</div>' } },
    { path: '/dashboard', component: { template: '<div>Dashboard</div>' } }
  ]
})

global.fetch = vi.fn()

function createFetchResponse(data: any, ok = true, status = 200) {
  return { ok, status, json: () => new Promise((resolve) => resolve(data)) }
}

const mockJobDetail = {
  metadata: {
    jobId: '123-abc',
    jobName: 'My Test Job',
    createdAt: new Date().toISOString(),
    status: 'Finished'
  },
  notes: 'These are test notes.',
  originalFileNames: ['page1.jpg', 'page2.jpg'],
  transcribedHtml: '<h1>Transcription</h1><p>Some transcribed text.</p>',
  translatedHtml: '<h1>Translation</h1><p>Some translated text.</p>',
  translatedWithNotesHtml: '<h1>Translation with Notes</h1><p>Some contextual text.</p>',
};

describe('JobDetailView', () => {
  beforeEach(async () => {
    vi.resetAllMocks()
    localStorage.clear()
    setActivePinia(createPinia())
    await router.push('/')
    await router.isReady()
  })

  it('shows loading state initially', async () => {
    const authStore = useAuthStore()
    authStore.setAuth('fake-token', 'fake-user')
    ;(global.fetch as any).mockResolvedValue(createFetchResponse(mockJobDetail))

    await router.push('/job/123-abc')
    await router.isReady()

    const wrapper = mount(JobDetailView, {
      global: {
        plugins: [router]
      }
    })

    expect(wrapper.text()).toContain('Loading job details...')
  })

  it('renders job details when fetch is successful', async () => {
    const authStore = useAuthStore()
    authStore.setAuth('fake-token', 'fake-user')
    ;(global.fetch as any).mockResolvedValue(createFetchResponse(mockJobDetail))

    await router.push('/job/123-abc')
    await router.isReady()

    const wrapper = mount(JobDetailView, {
      global: {
        plugins: [router]
      }
    })

    await new Promise(resolve => setTimeout(resolve, 0))

    expect(wrapper.text()).toContain('My Test Job')
    expect(wrapper.text()).toContain('Finished')
    expect(wrapper.text()).toContain('These are test notes.')
    expect(wrapper.text()).toContain('page1.jpg')
    expect(wrapper.text()).toContain('page2.jpg')
    // Default active tab is 'translated'
    expect(wrapper.find('.markdown-content').html()).toContain('<h1>Translation</h1>')
  })

  it('shows not found message on 404 response', async () => {
    const authStore = useAuthStore()
    authStore.setAuth('fake-token', 'fake-user')
    ;(global.fetch as any).mockResolvedValue(createFetchResponse(null, false, 404))

    await router.push('/job/not-found-id')
    await router.isReady()

    const wrapper = mount(JobDetailView, {
      global: {
        plugins: [router]
      }
    })

    await new Promise(resolve => setTimeout(resolve, 0))

    expect(wrapper.find('.state-error').exists()).toBe(true)
    expect(wrapper.text()).toContain('The job you are looking for could not be found.')
  })
})
