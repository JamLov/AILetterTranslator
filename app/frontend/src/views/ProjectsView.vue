<script setup lang="ts">
import { ref, onMounted } from 'vue';
import { useRouter } from 'vue-router';
import { useAuthStore } from '../stores/auth';
import { api } from '../api';

interface ProjectSummary {
  projectId: string;
  name: string;
  description: string | null;
  isOwner: boolean;
  jobCount: number;
  createdAt: string;
}

const router = useRouter();
const authStore = useAuthStore();
const projects = ref<ProjectSummary[]>([]);
const isLoading = ref(true);
const error = ref<string | null>(null);

onMounted(async () => {
  if (!authStore.isAuthenticated) {
    router.push('/login');
    return;
  }

  try {
    const res = await api('/api/projects');
    if (res.ok) {
      projects.value = await res.json();
    } else if (res.status !== 401) {
      throw new Error(`Failed to fetch projects. Server responded with ${res.status}`);
    }
  } catch (err) {
    error.value = 'An error occurred while fetching your projects. Please try again later.';
    console.error(err);
  } finally {
    isLoading.value = false;
  }
});

const formatDate = (dateString: string) => {
  return new Date(dateString).toLocaleDateString(undefined, {
    year: 'numeric', month: 'short', day: 'numeric'
  });
};
</script>

<template>
  <div class="page">
    <div class="page-header">
      <h1>My Projects</h1>
      <router-link to="/projects/new" class="btn btn-primary btn-sm">+ New Project</router-link>
    </div>

    <div v-if="isLoading" class="state-box">
      <p>Loading your projects...</p>
    </div>

    <div v-else-if="error" class="state-box state-error">
      <p>{{ error }}</p>
    </div>

    <div v-else-if="projects.length === 0" class="state-box">
      <h3>No projects yet</h3>
      <p class="state-sub">Create a project to organise related jobs and share them with others.</p>
    </div>

    <div v-else class="card">
      <table class="jobs-table">
        <thead>
          <tr>
            <th class="col-name">Name</th>
            <th class="col-desc">Description</th>
            <th class="col-role">Role</th>
            <th class="col-files">Jobs</th>
            <th class="col-date">Created</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="project in projects"
            :key="project.projectId"
            class="job-row"
            @click="router.push({ name: 'project-detail', params: { projectId: project.projectId } })"
          >
            <td class="col-name">
              <span class="job-name">{{ project.name }}</span>
            </td>
            <td class="col-desc">
              <span class="desc-text">{{ project.description || '-' }}</span>
            </td>
            <td class="col-role">
              <span :class="project.isOwner ? 'pill pill-finished' : 'pill pill-not-started'">
                {{ project.isOwner ? 'Owner' : 'Member' }}
              </span>
            </td>
            <td class="col-files">{{ project.jobCount }}</td>
            <td class="col-date">{{ formatDate(project.createdAt) }}</td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>

<style scoped>
.page-header {
  padding: 32px 0 20px;
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.page-header h1 {
  font-size: 22px;
  font-weight: 600;
}

.state-box {
  text-align: center;
  padding: 64px 32px;
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-lg);
  color: var(--color-text-secondary);
}
.state-box h3 {
  font-size: 16px;
  margin-bottom: 6px;
  color: var(--color-text);
}
.state-sub {
  font-size: 13px;
  color: var(--color-text-muted);
}
.state-error {
  background: var(--color-danger-bg);
  color: var(--color-danger);
  border-color: #fecaca;
}

.jobs-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 13px;
}
.jobs-table th {
  text-align: left;
  padding: 10px 16px;
  font-size: 11px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  color: var(--color-text-muted);
  border-bottom: 1px solid var(--color-border);
  background: var(--color-row-hover);
}
.jobs-table td {
  padding: 12px 16px;
  border-bottom: 1px solid var(--color-border-light);
  vertical-align: middle;
}
.jobs-table tbody tr:last-child td {
  border-bottom: none;
}
.job-row {
  cursor: pointer;
  transition: background 0.1s ease;
}
.job-row:hover {
  background: var(--color-row-hover);
}
.job-name {
  font-weight: 500;
  color: var(--color-text);
}
.col-name { width: 30%; }
.col-desc { width: 30%; }
.col-role { width: 12%; }
.col-files { width: 8%; text-align: center; color: var(--color-text-secondary); }
.col-date { width: 20%; color: var(--color-text-secondary); white-space: nowrap; }
.desc-text {
  color: var(--color-text-secondary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  display: block;
  max-width: 300px;
}
</style>
