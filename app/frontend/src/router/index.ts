import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '../stores/auth'
import LoginView from '../views/LoginView.vue'
import DashboardView from '../views/DashboardView.vue'
import NewJobView from '../views/NewJobView.vue'

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    {
      path: '/',
      redirect: '/dashboard'
    },
    {
      path: '/login',
      name: 'login',
      component: LoginView
    },
    {
      path: '/dashboard',
      name: 'dashboard',
      component: DashboardView,
      meta: { requiresAuth: true }
    },
    {
      path: '/new-job',
      name: 'new-job',
      component: NewJobView,
      meta: { requiresAuth: true }
    },
    {
      path: '/job/:jobId',
      name: 'job-detail',
      component: () => import('../views/JobDetailView.vue'),
      meta: { requiresAuth: true }
    },
    {
      path: '/projects',
      name: 'projects',
      component: () => import('../views/ProjectsView.vue'),
      meta: { requiresAuth: true }
    },
    {
      path: '/projects/new',
      name: 'new-project',
      component: () => import('../views/NewProjectView.vue'),
      meta: { requiresAuth: true }
    },
    {
      path: '/projects/:projectId',
      name: 'project-detail',
      component: () => import('../views/ProjectDetailView.vue'),
      meta: { requiresAuth: true }
    },
    {
      path: '/projects/:projectId/edit',
      name: 'edit-project',
      component: () => import('../views/EditProjectView.vue'),
      meta: { requiresAuth: true }
    },
    {
      path: '/projects/:projectId/new-job',
      name: 'new-project-job',
      component: () => import('../views/NewJobView.vue'),
      meta: { requiresAuth: true }
    },
    {
      path: '/projects/:projectId/jobs/:jobId',
      name: 'project-job-detail',
      component: () => import('../views/JobDetailView.vue'),
      meta: { requiresAuth: true }
    }
  ]
})

// Navigation guard to check authentication
router.beforeEach((to, _from, next) => {
  const authStore = useAuthStore()

  if (to.meta.requiresAuth && !authStore.isAuthenticated) {
    next('/login')
  } else if (to.name === 'login' && authStore.isAuthenticated) {
    next('/dashboard')
  } else {
    next()
  }
})

export default router
