<script setup lang="ts">
import { ref, onMounted } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { api } from '../api';

interface JobMetadata {
  jobId: string;
  jobName: string;
  createdAt: string;
  status: string;
}

interface JobDetail {
  metadata: JobMetadata;
  notes: string | null;
  originalFileNames: string[];
  transcribedHtml: string | null;
  translatedHtml: string | null;
  translatedWithNotesHtml: string | null;
}

const route = useRoute();
const router = useRouter();
const job = ref<JobDetail | null>(null);
const isLoading = ref(true);
const error = ref<string | null>(null);
const activeTab = ref<'transcribed' | 'translated' | 'contextual'>('transcribed');

onMounted(async () => {
  const jobId = route.params.jobId;

  try {
    const res = await api(`/api/jobs/${jobId}`);

    if (res.ok) {
      job.value = await res.json();
    } else if (res.status === 401) {
      return;
    } else if (res.status === 404) {
      error.value = "The job you are looking for could not be found.";
    } else {
      throw new Error(`Server responded with ${res.status}`);
    }
  } catch (err) {
    error.value = 'An error occurred while fetching the job details.';
    console.error(err);
  } finally {
    isLoading.value = false;
  }
});

const formatDate = (dateString: string) => {
  return new Date(dateString).toLocaleDateString(undefined, {
    year: 'numeric', month: 'long', day: 'numeric',
    hour: '2-digit', minute: '2-digit'
  });
};

const isResetting = ref(false);

const resetJob = async () => {
  if (!job.value) return;
  isResetting.value = true;
  try {
    const res = await api(`/api/jobs/${job.value.metadata.jobId}/reset`, {
      method: 'POST'
    });
    if (res.ok) {
      job.value.metadata.status = 'Not Started';
      job.value.transcribedHtml = null;
      job.value.translatedHtml = null;
      job.value.translatedWithNotesHtml = null;
    } else {
      error.value = `Failed to reset job: ${res.status} ${res.statusText}`;
    }
  } catch (err: any) {
    error.value = `Failed to reset job: ${err.message}`;
  } finally {
    isResetting.value = false;
  }
};

const statusClass = (status: string) => {
  return `pill pill-${status.toLowerCase().replace(' ', '-')}`;
};

const activeHtml = () => {
  if (!job.value) return null;
  switch (activeTab.value) {
    case 'transcribed': return job.value.transcribedHtml;
    case 'translated': return job.value.translatedHtml;
    case 'contextual': return job.value.translatedWithNotesHtml;
  }
};
</script>

<template>
  <div class="page">
    <div v-if="isLoading" class="state-box">Loading job details...</div>
    <div v-else-if="error" class="state-box state-error">{{ error }}</div>
    <div v-else-if="job" class="detail-layout">

      <!-- Header -->
      <div class="detail-header">
        <div class="detail-header-left">
          <button @click="router.push('/dashboard')" class="back-link">&larr; Back to Jobs</button>
          <h1 class="detail-title">{{ job.metadata.jobName }}</h1>
          <div class="detail-meta">
            <span>{{ formatDate(job.metadata.createdAt) }}</span>
            <span :class="statusClass(job.metadata.status)">{{ job.metadata.status }}</span>
            <button
              v-if="job.metadata.status !== 'Not Started' && job.metadata.status !== 'In Progress'"
              class="btn btn-secondary btn-sm"
              :disabled="isResetting"
              @click="resetJob"
            >{{ isResetting ? 'Resetting...' : 'Reset Job' }}</button>
          </div>
        </div>
      </div>

      <div class="detail-body">

        <!-- Sidebar -->
        <aside class="detail-sidebar">
          <div class="sidebar-block card">
            <h3 class="sidebar-heading">Files</h3>
            <ul class="file-list">
              <li v-for="name in job.originalFileNames" :key="name">{{ name }}</li>
            </ul>
          </div>
          <div v-if="job.notes" class="sidebar-block card">
            <h3 class="sidebar-heading">Notes</h3>
            <p class="sidebar-text">{{ job.notes }}</p>
          </div>
        </aside>

        <!-- Main content with tabs -->
        <main class="detail-main">
          <div class="card">
            <div class="tab-bar">
              <button
                class="tab"
                :class="{ 'tab-active': activeTab === 'transcribed' }"
                @click="activeTab = 'transcribed'"
              >Transcription</button>
              <button
                class="tab"
                :class="{ 'tab-active': activeTab === 'translated' }"
                @click="activeTab = 'translated'"
              >Translation</button>
              <button
                class="tab"
                :class="{ 'tab-active': activeTab === 'contextual' }"
                @click="activeTab = 'contextual'"
              >Translation + Context</button>
            </div>
            <div class="tab-content">
              <div v-if="activeHtml()" v-html="activeHtml()" class="markdown-content"></div>
              <p v-else class="not-available">Not yet available. The job may still be processing.</p>
            </div>
          </div>
        </main>
      </div>
    </div>
  </div>
</template>

<style scoped>
.state-box {
  text-align: center;
  padding: 64px 32px;
  margin-top: 32px;
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-lg);
  color: var(--color-text-secondary);
}
.state-error {
  background: var(--color-danger-bg);
  color: var(--color-danger);
  border-color: #fecaca;
}

.detail-header {
  padding: 24px 0 20px;
}

.back-link {
  background: none;
  border: none;
  padding: 0;
  font-size: 13px;
  color: var(--color-text-muted);
  cursor: pointer;
  margin-bottom: 8px;
  display: inline-block;
  font-family: inherit;
}
.back-link:hover {
  color: var(--color-primary);
}

.detail-title {
  font-size: 24px;
  font-weight: 700;
  letter-spacing: -0.3px;
  margin-bottom: 8px;
}

.detail-meta {
  display: flex;
  align-items: center;
  gap: 12px;
  font-size: 13px;
  color: var(--color-text-secondary);
}

.detail-body {
  display: flex;
  gap: 24px;
  align-items: flex-start;
}

.detail-sidebar {
  width: 260px;
  flex-shrink: 0;
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.sidebar-block {
  padding: 16px;
}

.sidebar-heading {
  font-size: 11px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  color: var(--color-text-muted);
  margin-bottom: 10px;
  padding-bottom: 8px;
  border-bottom: 1px solid var(--color-border-light);
}

.file-list {
  list-style: none;
  padding: 0;
  margin: 0;
  font-size: 13px;
  color: var(--color-text-secondary);
}
.file-list li {
  padding: 3px 0;
}

.sidebar-text {
  font-size: 13px;
  color: var(--color-text-secondary);
  line-height: 1.6;
}

.detail-main {
  flex: 1;
  min-width: 0;
}

/* Tabs */
.tab-bar {
  display: flex;
  border-bottom: 1px solid var(--color-border);
  padding: 0 8px;
}
.tab {
  background: none;
  border: none;
  border-bottom: 2px solid transparent;
  padding: 12px 16px;
  font-size: 13px;
  font-weight: 500;
  color: var(--color-text-muted);
  cursor: pointer;
  font-family: inherit;
  transition: all 0.15s ease;
}
.tab:hover {
  color: var(--color-text);
}
.tab-active {
  color: var(--color-primary);
  border-bottom-color: var(--color-primary);
}

.tab-content {
  padding: 24px;
  min-height: 200px;
}

.markdown-content {
  font-size: 14px;
  line-height: 1.7;
  color: var(--color-text);
}
.markdown-content :deep(h1) {
  font-size: 20px;
  margin-bottom: 12px;
}
.markdown-content :deep(h2) {
  font-size: 17px;
  margin-bottom: 10px;
}
.markdown-content :deep(p) {
  margin-bottom: 12px;
}

.not-available {
  font-size: 13px;
  font-style: italic;
  color: var(--color-text-muted);
  padding: 32px 0;
  text-align: center;
}
</style>
