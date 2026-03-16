<script setup lang="ts">
import { ref, onMounted } from 'vue';
import { useRouter } from 'vue-router';
import { useAuthStore } from '../stores/auth';
import { api } from '../api';

interface Job {
  jobId: string;
  jobName: string;
  createdAt: string;
  status: string;
  originalFileCount: number;
  letterDate: string | null;
}

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
const jobs = ref<Job[]>([]);
const projects = ref<ProjectSummary[]>([]);
const isLoading = ref(true);
const error = ref<string | null>(null);

onMounted(async () => {
  if (!authStore.isAuthenticated) {
    router.push('/login');
    return;
  }

  try {
    const [jobsRes, projectsRes] = await Promise.all([
      api('/api/jobs'),
      api('/api/projects')
    ]);

    if (jobsRes.ok) {
      jobs.value = await jobsRes.json();
    } else if (jobsRes.status !== 401) {
      throw new Error(`Failed to fetch jobs. Server responded with ${jobsRes.status}`);
    }

    if (projectsRes.ok) {
      projects.value = await projectsRes.json();
    }
  } catch (err) {
    error.value = 'An error occurred while loading your data. Please try again later.';
    console.error(err);
  } finally {
    isLoading.value = false;
  }
});

const formatDate = (dateString: string) => {
  return new Date(dateString).toLocaleDateString(undefined, {
    year: 'numeric', month: 'short', day: 'numeric',
    hour: '2-digit', minute: '2-digit'
  });
};

const formatLetterDate = (dateString: string) => {
  return new Date(dateString + 'T00:00:00').toLocaleDateString(undefined, {
    year: 'numeric', month: 'short', day: 'numeric'
  });
};

const statusClass = (status: string) => {
  return `pill pill-${status.toLowerCase().replace(' ', '-')}`;
};
</script>

<template>
  <div class="page">
    <div v-if="isLoading" class="state-box" style="margin-top: 32px;">
      <p>Loading...</p>
    </div>

    <div v-else-if="error" class="state-box state-error" style="margin-top: 32px;">
      <p>{{ error }}</p>
    </div>

    <template v-else>
      <!-- Projects Section -->
      <div v-if="projects.length > 0" class="section">
        <div class="section-header">
          <h2>Projects</h2>
          <router-link to="/projects/new" class="btn btn-primary btn-sm">+ New Project</router-link>
        </div>
        <div class="project-grid">
          <div
            v-for="project in projects"
            :key="project.projectId"
            class="project-tile card"
            @click="router.push({ name: 'project-detail', params: { projectId: project.projectId } })"
          >
            <div class="tile-header">
              <h3 class="tile-name">{{ project.name }}</h3>
              <span :class="project.isOwner ? 'pill pill-finished' : 'pill pill-not-started'">
                {{ project.isOwner ? 'Owner' : 'Member' }}
              </span>
            </div>
            <p v-if="project.description" class="tile-desc">{{ project.description }}</p>
            <div class="tile-footer">
              <span class="tile-stat">{{ project.jobCount }} job{{ project.jobCount !== 1 ? 's' : '' }}</span>
              <span class="tile-date">{{ formatDate(project.createdAt) }}</span>
            </div>
          </div>
        </div>
      </div>

      <!-- My Jobs Section -->
      <div class="section">
        <div class="section-header">
          <h2>My Jobs</h2>
        </div>

        <div v-if="jobs.length === 0" class="state-box">
          <h3>No standalone jobs</h3>
          <p class="state-sub">Click <strong>+ New Job</strong> above to translate a letter, or create a <router-link to="/projects/new">project</router-link> to organise related jobs.</p>
        </div>

        <div v-else class="card">
          <table class="jobs-table">
            <thead>
              <tr>
                <th class="col-name">Name</th>
                <th class="col-letter-date">Letter Date</th>
                <th class="col-date">Created</th>
                <th class="col-files">Files</th>
                <th class="col-status">Status</th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="job in jobs"
                :key="job.jobId"
                class="job-row"
                @click="router.push({ name: 'job-detail', params: { jobId: job.jobId } })"
              >
                <td class="col-name">
                  <span class="job-name">{{ job.jobName }}</span>
                </td>
                <td class="col-letter-date">{{ job.letterDate ? formatLetterDate(job.letterDate) : '—' }}</td>
                <td class="col-date">{{ formatDate(job.createdAt) }}</td>
                <td class="col-files">{{ job.originalFileCount }}</td>
                <td class="col-status">
                  <span :class="statusClass(job.status)">{{ job.status }}</span>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </template>
  </div>
</template>

<style scoped>
.section {
  margin-bottom: 32px;
}
.section-header {
  padding: 32px 0 16px;
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.section-header h2 {
  font-size: 20px;
  font-weight: 600;
}

/* Project Tiles */
.project-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 16px;
}
.project-tile {
  padding: 20px;
  cursor: pointer;
  transition: border-color 0.15s ease, box-shadow 0.15s ease;
  display: flex;
  flex-direction: column;
  min-height: 140px;
}
.project-tile:hover {
  border-color: var(--color-primary);
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.06);
}
.tile-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 8px;
  margin-bottom: 8px;
}
.tile-name {
  font-size: 15px;
  font-weight: 600;
  color: var(--color-text);
  line-height: 1.3;
}
.tile-desc {
  font-size: 13px;
  color: var(--color-text-secondary);
  line-height: 1.5;
  flex: 1;
  overflow: hidden;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
}
.tile-footer {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-top: auto;
  padding-top: 12px;
  border-top: 1px solid var(--color-border-light);
  font-size: 12px;
  color: var(--color-text-muted);
}
.tile-stat {
  font-weight: 500;
}

@media (max-width: 900px) {
  .project-grid {
    grid-template-columns: repeat(2, 1fr);
  }
}
@media (max-width: 600px) {
  .project-grid {
    grid-template-columns: 1fr;
  }
}

/* State boxes */
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
.state-sub a {
  color: var(--color-primary);
}
.state-error {
  background: var(--color-danger-bg);
  color: var(--color-danger);
  border-color: #fecaca;
}

/* Table */
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
.col-name { width: 38%; }
.col-letter-date { width: 15%; color: var(--color-text-secondary); white-space: nowrap; }
.col-date { width: 22%; color: var(--color-text-secondary); white-space: nowrap; }
.col-files { width: 8%; text-align: center; color: var(--color-text-secondary); }
.col-status { width: 17%; }
</style>
