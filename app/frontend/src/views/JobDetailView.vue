<script setup lang="ts">
import { ref, computed, onMounted } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { api } from '../api';

interface JobMetadata {
  jobId: string;
  jobName: string;
  createdAt: string;
  status: string;
  letterDate: string | null;
}

interface JobDetail {
  metadata: JobMetadata;
  notes: string | null;
  originalFileNames: string[];
  transcribedHtml: string | null;
  translatedHtml: string | null;
  translatedWithNotesHtml: string | null;
}

interface ProjectSummary {
  projectId: string;
  name: string;
  isOwner: boolean;
}

const route = useRoute();
const router = useRouter();
const projectId = computed(() => route.params.projectId as string | undefined);
const job = ref<JobDetail | null>(null);
const isLoading = ref(true);
const error = ref<string | null>(null);
const activeTab = ref<'transcribed' | 'translated' | 'contextual'>('transcribed');
const isProjectOwner = ref(true); // true for standalone jobs (user always owns them)
const ownedProjects = ref<ProjectSummary[]>([]); // for move-to-project picker
const selectedMoveProjectId = ref('');
const isMoving = ref(false);
const showMetadataDialog = ref(false);
const editLetterDate = ref('');
const isSavingMetadata = ref(false);

const openMetadataDialog = () => {
  editLetterDate.value = job.value?.metadata.letterDate || '';
  showMetadataDialog.value = true;
};

const saveMetadata = async () => {
  if (!job.value) return;
  isSavingMetadata.value = true;
  const metadataUrl = projectId.value
    ? `/api/projects/${projectId.value}/jobs/${job.value.metadata.jobId}/metadata`
    : `/api/jobs/${job.value.metadata.jobId}/metadata`;
  try {
    const res = await api(metadataUrl, {
      method: 'PATCH',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ letterDate: editLetterDate.value || null })
    });
    if (res.ok) {
      job.value.metadata.letterDate = editLetterDate.value || null;
      showMetadataDialog.value = false;
    } else {
      error.value = `Failed to update metadata: ${res.status} ${res.statusText}`;
    }
  } catch (err: any) {
    error.value = `Failed to update metadata: ${err.message}`;
  } finally {
    isSavingMetadata.value = false;
  }
};

const formatLetterDate = (dateString: string) => {
  return new Date(dateString + 'T00:00:00').toLocaleDateString(undefined, {
    year: 'numeric', month: 'long', day: 'numeric'
  });
};

onMounted(async () => {
  const jobId = route.params.jobId;
  const apiUrl = projectId.value
    ? `/api/projects/${projectId.value}/jobs/${jobId}`
    : `/api/jobs/${jobId}`;

  try {
    const res = await api(apiUrl);

    if (res.ok) {
      job.value = await res.json();
    } else if (res.status === 401) {
      return;
    } else if (res.status === 404) {
      error.value = "The job you are looking for could not be found.";
    } else {
      throw new Error(`Server responded with ${res.status}`);
    }

    if (projectId.value) {
      // For project jobs, check if the user is the owner
      const projectRes = await api(`/api/projects/${projectId.value}`);
      if (projectRes.ok) {
        const projectDetail = await projectRes.json();
        isProjectOwner.value = projectDetail.isOwner;
      }
    } else {
      // For standalone jobs, fetch projects the user owns (for move picker)
      const projectsRes = await api('/api/projects');
      if (projectsRes.ok) {
        const all = await projectsRes.json() as ProjectSummary[];
        ownedProjects.value = all.filter(p => p.isOwner);
      }
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
  const resetUrl = projectId.value
    ? `/api/projects/${projectId.value}/jobs/${job.value.metadata.jobId}/reset`
    : `/api/jobs/${job.value.metadata.jobId}/reset`;
  try {
    const res = await api(resetUrl, {
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

const moveToProject = async () => {
  if (!job.value || !selectedMoveProjectId.value) return;
  isMoving.value = true;
  try {
    const res = await api(`/api/jobs/${job.value.metadata.jobId}/move-to-project/${selectedMoveProjectId.value}`, {
      method: 'POST'
    });
    if (res.ok) {
      router.push({ name: 'project-job-detail', params: { projectId: selectedMoveProjectId.value, jobId: job.value.metadata.jobId } });
    } else {
      const data = await res.json().catch(() => null);
      error.value = data?.message || `Failed to move job: ${res.status} ${res.statusText}`;
    }
  } catch (err: any) {
    error.value = `Failed to move job: ${err.message}`;
  } finally {
    isMoving.value = false;
  }
};

const moveToStandalone = async () => {
  if (!job.value || !projectId.value) return;
  isMoving.value = true;
  try {
    const res = await api(`/api/projects/${projectId.value}/jobs/${job.value.metadata.jobId}/move-to-standalone`, {
      method: 'POST'
    });
    if (res.ok) {
      router.push({ name: 'job-detail', params: { jobId: job.value.metadata.jobId } });
    } else {
      const data = await res.json().catch(() => null);
      error.value = data?.message || `Failed to move job: ${res.status} ${res.statusText}`;
    }
  } catch (err: any) {
    error.value = `Failed to move job: ${err.message}`;
  } finally {
    isMoving.value = false;
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
          <button @click="router.push(projectId ? { name: 'project-detail', params: { projectId } } : '/dashboard')" class="back-link">
            &larr; {{ projectId ? 'Back to Project' : 'Back to Jobs' }}
          </button>
          <h1 class="detail-title">{{ job.metadata.jobName }}</h1>
          <div class="detail-meta">
            <span>{{ formatDate(job.metadata.createdAt) }}</span>
            <span :class="statusClass(job.metadata.status)">{{ job.metadata.status }}</span>
            <button
              v-if="isProjectOwner && job.metadata.status !== 'Not Started' && job.metadata.status !== 'In Progress'"
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
            <div class="sidebar-heading-row">
              <h3 class="sidebar-heading">Metadata</h3>
              <button v-if="isProjectOwner" class="edit-icon-btn" @click="openMetadataDialog" title="Edit metadata">
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                  <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/>
                  <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/>
                </svg>
              </button>
            </div>
            <div class="metadata-row">
              <span class="metadata-label">Letter Date</span>
              <span class="metadata-value">{{ job.metadata.letterDate ? formatLetterDate(job.metadata.letterDate) : 'Unknown' }}</span>
            </div>
          </div>

          <!-- Metadata Edit Dialog -->
          <div v-if="showMetadataDialog" class="dialog-overlay" @click.self="showMetadataDialog = false">
            <div class="dialog">
              <h3 class="dialog-title">Edit Metadata</h3>
              <div class="dialog-field">
                <label class="dialog-label" for="letterDate">Letter Date</label>
                <input
                  id="letterDate"
                  type="date"
                  v-model="editLetterDate"
                  class="dialog-input"
                  :disabled="isSavingMetadata"
                />
              </div>
              <div class="dialog-actions">
                <button class="btn btn-secondary btn-sm" @click="showMetadataDialog = false" :disabled="isSavingMetadata">Cancel</button>
                <button class="btn btn-primary btn-sm" @click="saveMetadata" :disabled="isSavingMetadata">
                  {{ isSavingMetadata ? 'Saving...' : 'Save' }}
                </button>
              </div>
            </div>
          </div>

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

          <!-- Move to Project (standalone jobs only) -->
          <div v-if="!projectId && ownedProjects.length > 0" class="sidebar-block card">
            <h3 class="sidebar-heading">Move to Project</h3>
            <select v-model="selectedMoveProjectId" class="move-select" :disabled="isMoving">
              <option value="" disabled>Select a project...</option>
              <option v-for="p in ownedProjects" :key="p.projectId" :value="p.projectId">{{ p.name }}</option>
            </select>
            <button
              class="btn btn-secondary btn-sm move-btn"
              :disabled="!selectedMoveProjectId || isMoving"
              @click="moveToProject"
            >{{ isMoving ? 'Moving...' : 'Move' }}</button>
          </div>

          <!-- Move to Standalone (project jobs, owner only) -->
          <div v-if="projectId && isProjectOwner" class="sidebar-block card">
            <h3 class="sidebar-heading">Move Job</h3>
            <button
              class="btn btn-secondary btn-sm"
              :disabled="isMoving"
              @click="moveToStandalone"
            >{{ isMoving ? 'Moving...' : 'Move to My Jobs' }}</button>
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

.sidebar-heading-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 10px;
  padding-bottom: 8px;
  border-bottom: 1px solid var(--color-border-light);
}
.sidebar-heading-row .sidebar-heading {
  margin-bottom: 0;
  padding-bottom: 0;
  border-bottom: none;
}
.edit-icon-btn {
  background: none;
  border: none;
  padding: 4px;
  cursor: pointer;
  color: var(--color-text-muted);
  border-radius: var(--radius-sm);
  display: flex;
  align-items: center;
  justify-content: center;
}
.edit-icon-btn:hover {
  color: var(--color-primary);
  background: var(--color-surface-hover, rgba(0,0,0,0.05));
}
.metadata-row {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  font-size: 13px;
  padding: 2px 0;
}
.metadata-label {
  color: var(--color-text-muted);
}
.metadata-value {
  color: var(--color-text-secondary);
  font-weight: 500;
}

.dialog-overlay {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background: rgba(0, 0, 0, 0.4);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
}
.dialog {
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-lg);
  padding: 24px;
  width: 360px;
  max-width: 90vw;
  box-shadow: 0 8px 32px rgba(0, 0, 0, 0.15);
}
.dialog-title {
  font-size: 16px;
  font-weight: 600;
  margin-bottom: 20px;
}
.dialog-field {
  margin-bottom: 20px;
}
.dialog-label {
  display: block;
  font-size: 12px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  color: var(--color-text-muted);
  margin-bottom: 6px;
}
.dialog-input {
  width: 100%;
  padding: 8px 10px;
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  font-size: 14px;
  font-family: inherit;
  color: var(--color-text);
  background: var(--color-surface);
  box-sizing: border-box;
}
.dialog-input:focus {
  outline: none;
  border-color: var(--color-primary);
}
.dialog-actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}

.move-select {
  width: 100%;
  padding: 6px 10px;
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  font-size: 13px;
  font-family: inherit;
  color: var(--color-text);
  background: var(--color-surface);
  margin-bottom: 8px;
}
.move-btn {
  width: 100%;
}
</style>
