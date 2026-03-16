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
}

const router = useRouter();
const authStore = useAuthStore();
const jobs = ref<Job[]>([]);
const isLoading = ref(true);
const error = ref<string | null>(null);

onMounted(async () => {
  if (!authStore.isAuthenticated) {
    router.push('/login');
    return;
  }

  try {
    const res = await api('/api/jobs');

    if (res.ok) {
      jobs.value = await res.json();
    } else if (res.status !== 401) {
      throw new Error(`Failed to fetch jobs. Server responded with ${res.status}`);
    }
  } catch (err) {
    error.value = 'An error occurred while fetching your jobs. Please try again later.';
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

const statusClass = (status: string) => {
  return `pill pill-${status.toLowerCase().replace(' ', '-')}`;
};
</script>

<template>
  <div class="page">
    <div class="page-header">
      <h1>My Jobs</h1>
    </div>

    <div v-if="isLoading" class="state-box">
      <p>Loading your jobs...</p>
    </div>

    <div v-else-if="error" class="state-box state-error">
      <p>{{ error }}</p>
    </div>

    <div v-else-if="jobs.length === 0" class="state-box">
      <h3>No jobs yet</h3>
      <p class="state-sub">You haven't translated any letters yet. Click <strong>+ New Job</strong> above to get started.</p>
    </div>

    <div v-else class="card">
      <table class="jobs-table">
        <thead>
          <tr>
            <th class="col-name">Name</th>
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

<style scoped>
.page-header {
  padding: 32px 0 20px;
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
.col-name {
  width: 50%;
}
.col-date {
  width: 25%;
  color: var(--color-text-secondary);
  white-space: nowrap;
}
.col-files {
  width: 8%;
  text-align: center;
  color: var(--color-text-secondary);
}
.col-status {
  width: 17%;
}
</style>
