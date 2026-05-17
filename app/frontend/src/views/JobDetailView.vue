<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { api } from '../api';
import FileListItem from '../components/FileListItem.vue'
import ImagePreviewModal from '../components/ImagePreviewModal.vue'

interface JobMetadata {
  jobId: string;
  jobName: string;
  createdAt: string;
  status: string;
  errorMessage: string | null;
  letterDate: string | null;
  latestVersionNumber: number | null;
  pendingProcessingMode: string | null;
  basedOnVersionNumber: number | null;
}

interface VersionSummary {
  versionNumber: number;
  createdAt: string;
  createdByUserId: string | null;
  processingMode: string;
  basedOnVersionNumber: number | null;
  letterDate: string | null;
  isCurrent: boolean;
}

interface JobView {
  metadata: JobMetadata;
  version?: VersionSummary;        // present when viewing a historical version
  notes: string | null;
  originalFileNames: string[];
  transcribedHtml: string | null;
  translatedHtml: string | null;
  translatedWithNotesHtml: string | null;
  transcribedWithNotesHtml: string | null;
}

interface ProjectSummary {
  projectId: string;
  name: string;
  isOwner: boolean;
}

const route = useRoute();
const router = useRouter();
const projectId = computed(() => route.params.projectId as string | undefined);
const jobIdParam = computed(() => route.params.jobId as string);

const job = ref<JobView | null>(null);
const versions = ref<VersionSummary[]>([]);
const isLoading = ref(true);
const error = ref<string | null>(null);
const activeTab = ref<'transcribed' | 'translated' | 'contextual' | 'transcribed-contextual'>('transcribed');
const isProjectOwner = ref(true);
const ownedProjects = ref<ProjectSummary[]>([]);
const selectedMoveProjectId = ref('');
const isMoving = ref(false);
const showMetadataDialog = ref(false);
const editLetterDate = ref('');
const isSavingMetadata = ref(false);
const isResetting = ref(false);
const isReverting = ref(false);

// Create-version modal state
const showCreateModal = ref(false);
const editMode = ref<'TranscriptionEdit' | 'TranslationEdit' | null>(null);
const editedMarkdown = ref('');
const editedNotes = ref('');
const isCreatingVersion = ref(false);
const isLoadingSource = ref(false);

const previewIndex = ref<number | null>(null)

function openPreview(fileName: string, _imageUrl: string) {
  const idx = job.value?.originalFileNames.indexOf(fileName) ?? -1
  if (idx >= 0) previewIndex.value = idx
}

function closePreview() {
  previewIndex.value = null
}
const createVersionError = ref<string | null>(null);

// Derived state
const selectedVersionNumber = computed(() => {
  const v = route.query.version;
  if (typeof v === 'string') {
    const n = parseInt(v, 10);
    if (!Number.isNaN(n) && n > 0) return n;
  }
  return null;
});

const latestVersionNumber = computed(() => {
  if (versions.value.length > 0) return versions.value[0].versionNumber;
  return job.value?.metadata.latestVersionNumber ?? 1;
});

const isViewingHistoricalVersion = computed(() =>
  selectedVersionNumber.value != null && selectedVersionNumber.value !== latestVersionNumber.value
);

const canEdit = computed(() =>
  !isViewingHistoricalVersion.value
  && isProjectOwner.value
  && job.value?.metadata.status === 'Finished'
);

const isFailedEdit = computed(() =>
  job.value?.metadata.status === 'Failed'
  && !!job.value?.metadata.pendingProcessingMode
  && job.value.metadata.pendingProcessingMode !== 'Initial'
  && !isViewingHistoricalVersion.value
);

// URL builders
const baseUrl = computed(() => projectId.value
  ? `/api/projects/${projectId.value}/jobs/${jobIdParam.value}`
  : `/api/jobs/${jobIdParam.value}`);

const loadJob = async () => {
  isLoading.value = true;
  error.value = null;
  try {
    const versionToLoad = selectedVersionNumber.value;
    const url = versionToLoad != null
      ? `${baseUrl.value}/versions/${versionToLoad}`
      : baseUrl.value;

    const res = await api(url);
    if (res.ok) {
      job.value = await res.json();
    } else if (res.status === 401) {
      return;
    } else if (res.status === 404) {
      error.value = versionToLoad != null
        ? `Version ${versionToLoad} not found.`
        : 'The job you are looking for could not be found.';
    } else {
      throw new Error(`Server responded with ${res.status}`);
    }
  } catch (err) {
    error.value = 'An error occurred while fetching the job details.';
    console.error(err);
  } finally {
    isLoading.value = false;
  }
};

const loadVersions = async () => {
  try {
    const res = await api(`${baseUrl.value}/versions`);
    if (res.ok) {
      const data = await res.json();
      if (Array.isArray(data)) versions.value = data;
    }
  } catch (err) {
    console.error('Could not load version list', err);
  }
};

const openMetadataDialog = () => {
  editLetterDate.value = job.value?.metadata.letterDate || '';
  showMetadataDialog.value = true;
};

const saveMetadata = async () => {
  if (!job.value) return;
  isSavingMetadata.value = true;
  try {
    const res = await api(`${baseUrl.value}/metadata`, {
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

const formatLetterDate = (dateString: string) =>
  new Date(dateString + 'T00:00:00').toLocaleDateString(undefined, {
    year: 'numeric', month: 'long', day: 'numeric'
  });

const formatDate = (dateString: string) =>
  new Date(dateString).toLocaleDateString(undefined, {
    year: 'numeric', month: 'short', day: 'numeric'
  });

const formatDateTime = (dateString: string) =>
  new Date(dateString).toLocaleDateString(undefined, {
    year: 'numeric', month: 'long', day: 'numeric',
    hour: '2-digit', minute: '2-digit'
  });

const modeLabel = (mode: string): string => {
  switch (mode) {
    case 'Initial': return 'Initial';
    case 'TranscriptionEdit': return 'Transcription edit';
    case 'TranslationEdit': return 'Translation edit';
    default: return mode;
  }
};

const resetJob = async () => {
  if (!job.value) return;
  isResetting.value = true;
  try {
    const res = await api(`${baseUrl.value}/reset`, { method: 'POST' });
    if (res.ok) {
      // For a Failed edit, Retry just re-queues without clearing pending fields,
      // so the worker re-runs the same edit mode. For a normal reset on a
      // Finished initial job, this falls back to Initial reprocessing.
      await loadJob();
      await loadVersions();
    } else {
      error.value = `Failed to reset job: ${res.status} ${res.statusText}`;
    }
  } catch (err: any) {
    error.value = `Failed to reset job: ${err.message}`;
  } finally {
    isResetting.value = false;
  }
};

const revertVersion = async () => {
  if (!job.value) return;
  const target = latestVersionNumber.value - 1;
  if (target < 1) return;

  if (!confirm(`Revert to v${target}? Your unsaved edit will be discarded and the job will return to its prior state.`)) return;

  isReverting.value = true;
  try {
    const res = await api(`${baseUrl.value}/versions/revert`, { method: 'POST' });
    if (res.ok) {
      // Clear ?version=N if it pointed at the soon-to-be-deleted snapshot.
      if (selectedVersionNumber.value != null) {
        await router.replace({ query: {} });
      }
      await loadJob();
      await loadVersions();
    } else {
      error.value = `Failed to revert: ${res.status} ${res.statusText}`;
    }
  } catch (err: any) {
    error.value = `Failed to revert: ${err.message}`;
  } finally {
    isReverting.value = false;
  }
};

const openCreateModal = async (mode: 'TranscriptionEdit' | 'TranslationEdit') => {
  if (!job.value) return;
  editMode.value = mode;
  editedMarkdown.value = '';
  editedNotes.value = job.value.notes || '';
  createVersionError.value = null;
  showCreateModal.value = true;

  // Fetch raw markdown for the editor
  isLoadingSource.value = true;
  try {
    const source = mode === 'TranscriptionEdit' ? 'transcribed' : 'translated';
    const res = await api(`${baseUrl.value}/source/${source}`);
    if (res.ok) {
      const data = await res.json();
      editedMarkdown.value = data.content || '';
    } else {
      createVersionError.value = `Could not load existing ${source} content (status ${res.status}).`;
    }
  } catch (err: any) {
    createVersionError.value = `Could not load existing content: ${err.message}`;
  } finally {
    isLoadingSource.value = false;
  }
};

const resetCreateModalState = () => {
  showCreateModal.value = false;
  editMode.value = null;
  editedMarkdown.value = '';
  editedNotes.value = '';
  createVersionError.value = null;
};

const closeCreateModal = () => {
  // Guards the user-initiated close paths (Cancel button, backdrop click) while a
  // submit is in flight. The success path uses resetCreateModalState() directly.
  if (isCreatingVersion.value) return;
  resetCreateModalState();
};

const submitCreateVersion = async () => {
  if (!editMode.value || !editedMarkdown.value.trim()) {
    createVersionError.value = 'Please enter the corrected content.';
    return;
  }

  isCreatingVersion.value = true;
  createVersionError.value = null;
  try {
    const res = await api(`${baseUrl.value}/versions`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        mode: editMode.value,
        editedMarkdown: editedMarkdown.value,
        notes: editedNotes.value || null
      })
    });
    if (res.status === 202) {
      resetCreateModalState();
      await loadJob();
      await loadVersions();
    } else if (res.status === 409) {
      createVersionError.value = 'Cannot create a new version while the job is currently processing. Please wait and try again.';
    } else {
      const data = await res.json().catch(() => null);
      createVersionError.value = data?.message || `Failed: ${res.status} ${res.statusText}`;
    }
  } catch (err: any) {
    createVersionError.value = `Failed to create version: ${err.message}`;
  } finally {
    isCreatingVersion.value = false;
  }
};

const moveToProject = async () => {
  if (!job.value || !selectedMoveProjectId.value) return;
  isMoving.value = true;
  try {
    const res = await api(`/api/jobs/${jobIdParam.value}/move-to-project/${selectedMoveProjectId.value}`, {
      method: 'POST'
    });
    if (res.ok) {
      router.push({ name: 'project-job-detail', params: { projectId: selectedMoveProjectId.value, jobId: jobIdParam.value } });
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
    const res = await api(`/api/projects/${projectId.value}/jobs/${jobIdParam.value}/move-to-standalone`, {
      method: 'POST'
    });
    if (res.ok) {
      router.push({ name: 'job-detail', params: { jobId: jobIdParam.value } });
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

const selectVersion = (versionNumber: number) => {
  if (versionNumber === latestVersionNumber.value) {
    router.replace({ query: { ...route.query, version: undefined } });
  } else {
    router.replace({ query: { ...route.query, version: String(versionNumber) } });
  }
};

const returnToCurrent = () => {
  const { version: _, ...rest } = route.query;
  router.replace({ query: rest });
};

const statusClass = (status: string) => `pill pill-${status.toLowerCase().replace(' ', '-')}`;

const activeHtml = () => {
  if (!job.value) return null;
  switch (activeTab.value) {
    case 'transcribed': return job.value.transcribedHtml;
    case 'translated': return job.value.translatedHtml;
    case 'contextual': return job.value.translatedWithNotesHtml;
    case 'transcribed-contextual': return job.value.transcribedWithNotesHtml;
  }
};

watch(() => route.query.version, () => loadJob());

onMounted(async () => {
  await Promise.all([loadJob(), loadVersions()]);

  if (projectId.value) {
    const projectRes = await api(`/api/projects/${projectId.value}`);
    if (projectRes.ok) {
      const projectDetail = await projectRes.json();
      isProjectOwner.value = projectDetail.isOwner;
    }
  } else {
    const projectsRes = await api('/api/projects');
    if (projectsRes.ok) {
      const all = await projectsRes.json();
      if (Array.isArray(all)) {
        ownedProjects.value = (all as ProjectSummary[]).filter(p => p.isOwner);
      }
    }
  }
});
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
            <span>{{ formatDateTime(job.metadata.createdAt) }}</span>
            <span :class="statusClass(job.metadata.status)">{{ job.metadata.status }}</span>
            <button
              v-if="!isViewingHistoricalVersion && !isFailedEdit && isProjectOwner && job.metadata.status !== 'Not Started' && job.metadata.status !== 'In Progress'"
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
          <!-- Versions browser -->
          <div class="sidebar-block card">
            <h3 class="sidebar-heading">Versions</h3>
            <ul class="version-list">
              <li
                v-for="v in versions"
                :key="v.versionNumber"
                class="version-row"
                :class="{
                  'version-active': (selectedVersionNumber ?? latestVersionNumber) === v.versionNumber,
                  'version-clickable': true
                }"
                @click="selectVersion(v.versionNumber)"
              >
                <div class="version-row-main">
                  <span class="version-number">v{{ v.versionNumber }}</span>
                  <span class="version-mode">{{ v.isCurrent ? '(current)' : modeLabel(v.processingMode) }}</span>
                </div>
                <div class="version-row-meta">{{ formatDate(v.createdAt) }}</div>
              </li>
            </ul>
          </div>

          <div class="sidebar-block card">
            <div class="sidebar-heading-row">
              <h3 class="sidebar-heading">Metadata</h3>
              <button v-if="!isViewingHistoricalVersion && isProjectOwner" class="edit-icon-btn" @click="openMetadataDialog" title="Edit metadata">
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
              <FileListItem
                v-for="name in job.originalFileNames"
                :key="name"
                :file-name="name"
                :job-id="job.metadata.jobId"
                :project-id="projectId ?? undefined"
                @preview="openPreview"
              />
            </ul>
          </div>
          <div v-if="job.notes" class="sidebar-block card">
            <h3 class="sidebar-heading">Notes</h3>
            <p class="sidebar-text">{{ job.notes }}</p>
          </div>

          <!-- Move to Project (standalone jobs only) -->
          <div v-if="!isViewingHistoricalVersion && !projectId && ownedProjects.length > 0" class="sidebar-block card">
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
          <div v-if="!isViewingHistoricalVersion && projectId && isProjectOwner" class="sidebar-block card">
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
          <!-- Historical-version banner -->
          <div v-if="isViewingHistoricalVersion" class="version-banner">
            Viewing version {{ selectedVersionNumber }} (read-only).
            <button class="link-btn" @click="returnToCurrent">Return to current</button>
          </div>

          <!-- Failed-edit recovery banner -->
          <div v-if="isFailedEdit" class="failed-banner">
            <div class="failed-banner-msg">
              <strong>{{ modeLabel(job.metadata.pendingProcessingMode || '') }} failed.</strong>
              <span v-if="job.metadata.errorMessage"> {{ job.metadata.errorMessage }}</span>
            </div>
            <div class="failed-banner-actions">
              <button class="btn btn-secondary btn-sm" :disabled="isResetting" @click="resetJob">
                {{ isResetting ? 'Retrying...' : 'Retry' }}
              </button>
              <button class="btn btn-secondary btn-sm" :disabled="isReverting" @click="revertVersion">
                {{ isReverting ? 'Reverting...' : `Revert to v${latestVersionNumber - 1}` }}
              </button>
            </div>
          </div>

          <div class="card">
            <div class="tab-bar">
              <button
                class="tab"
                :class="{ 'tab-active': activeTab === 'transcribed' }"
                @click="activeTab = 'transcribed'"
              >Transcription</button>
              <button
                class="tab"
                :class="{ 'tab-active': activeTab === 'transcribed-contextual' }"
                @click="activeTab = 'transcribed-contextual'"
              >Transcription + Context</button>
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
              <div class="tab-actions" v-if="canEdit">
                <button
                  v-if="activeTab === 'transcribed'"
                  class="btn btn-secondary btn-sm"
                  @click="openCreateModal('TranscriptionEdit')"
                >Edit transcription</button>
                <button
                  v-else-if="activeTab === 'translated'"
                  class="btn btn-secondary btn-sm"
                  @click="openCreateModal('TranslationEdit')"
                >Edit translation</button>
              </div>
            </div>
            <div class="tab-content">
              <div v-if="activeHtml()" v-html="activeHtml()" class="markdown-content"></div>
              <p v-else class="not-available">Not yet available. The job may still be processing.</p>
            </div>
          </div>
        </main>
      </div>

      <!-- Create-version modal -->
      <div v-if="showCreateModal" class="dialog-overlay" @click.self="closeCreateModal">
        <div class="dialog dialog-wide">
          <h3 class="dialog-title">
            {{ editMode === 'TranscriptionEdit' ? 'Edit transcription' : 'Edit translation' }}
          </h3>
          <p class="dialog-help">
            Make corrections to the
            {{ editMode === 'TranscriptionEdit' ? 'transcription' : 'translation' }}
            below. On submit, Gemini will
            {{ editMode === 'TranscriptionEdit'
              ? 're-translate and re-add contextual notes, preserving prior context where text is unchanged.'
              : 're-add contextual notes against your corrected translation, preserving prior context where text is unchanged.' }}
          </p>

          <div class="dialog-field">
            <label class="dialog-label">{{ editMode === 'TranscriptionEdit' ? 'Transcription' : 'Translation' }} (Markdown)</label>
            <textarea
              v-model="editedMarkdown"
              class="dialog-textarea"
              :disabled="isCreatingVersion || isLoadingSource"
              :placeholder="isLoadingSource ? 'Loading existing content...' : 'Edit the markdown content here...'"
              rows="18"
            ></textarea>
          </div>

          <div class="dialog-field">
            <label class="dialog-label">Notes (optional, max 1000 chars)</label>
            <textarea
              v-model="editedNotes"
              class="dialog-textarea"
              :disabled="isCreatingVersion"
              placeholder="Optional contextual notes for this version..."
              maxlength="1000"
              rows="4"
            ></textarea>
          </div>

          <p v-if="createVersionError" class="dialog-error">{{ createVersionError }}</p>

          <div class="dialog-actions">
            <button class="btn btn-secondary btn-sm" @click="closeCreateModal" :disabled="isCreatingVersion">Cancel</button>
            <button class="btn btn-primary btn-sm" @click="submitCreateVersion" :disabled="isCreatingVersion || isLoadingSource || !editedMarkdown.trim()">
              {{ isCreatingVersion ? 'Submitting...' : 'Create new version' }}
            </button>
          </div>
        </div>
      </div>
    </div>
    <ImagePreviewModal
      v-if="previewIndex != null && job"
      :file-names="job.originalFileNames"
      :current-index="previewIndex"
      :job-id="job.metadata.jobId"
      :project-id="projectId ?? undefined"
      @close="closePreview"
      @navigate="(idx: number) => previewIndex = idx"
    />
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

/* Version list */
.version-list {
  list-style: none;
  padding: 0;
  margin: 0;
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.version-row {
  padding: 8px 10px;
  border-radius: var(--radius-md);
  cursor: pointer;
  transition: background-color 0.1s ease;
}
.version-clickable:hover {
  background: var(--color-surface-hover, rgba(0,0,0,0.05));
}
.version-active {
  background: var(--color-primary-bg, rgba(99, 102, 241, 0.1));
}
.version-row-main {
  display: flex;
  align-items: baseline;
  gap: 8px;
  font-size: 13px;
}
.version-number {
  font-weight: 600;
  color: var(--color-text);
}
.version-mode {
  color: var(--color-text-muted);
  font-size: 12px;
}
.version-row-meta {
  font-size: 11px;
  color: var(--color-text-muted);
  margin-top: 2px;
}

.version-banner {
  background: var(--color-warning-bg, #fff7ed);
  border: 1px solid var(--color-warning-border, #fdba74);
  color: var(--color-warning, #9a3412);
  padding: 10px 14px;
  border-radius: var(--radius-md);
  font-size: 13px;
  margin-bottom: 12px;
  display: flex;
  align-items: center;
  gap: 12px;
}
.link-btn {
  background: none;
  border: none;
  color: var(--color-primary);
  cursor: pointer;
  font-family: inherit;
  font-size: 13px;
  padding: 0;
  text-decoration: underline;
}

.failed-banner {
  background: var(--color-danger-bg, #fef2f2);
  border: 1px solid #fecaca;
  color: var(--color-danger, #991b1b);
  padding: 12px 14px;
  border-radius: var(--radius-md);
  font-size: 13px;
  margin-bottom: 12px;
}
.failed-banner-msg {
  margin-bottom: 8px;
}
.failed-banner-actions {
  display: flex;
  gap: 8px;
}

/* Tabs */
.tab-bar {
  display: flex;
  align-items: center;
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
.tab-actions {
  margin-left: auto;
  padding: 6px 8px;
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
  max-height: 90vh;
  overflow-y: auto;
  box-shadow: 0 8px 32px rgba(0, 0, 0, 0.15);
}
.dialog-wide {
  width: 720px;
}
.dialog-title {
  font-size: 16px;
  font-weight: 600;
  margin-bottom: 12px;
}
.dialog-help {
  font-size: 13px;
  color: var(--color-text-muted);
  line-height: 1.5;
  margin-bottom: 16px;
}
.dialog-field {
  margin-bottom: 16px;
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
.dialog-textarea {
  width: 100%;
  padding: 10px;
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  font-size: 13px;
  font-family: 'Menlo', 'Consolas', 'Courier New', monospace;
  line-height: 1.5;
  color: var(--color-text);
  background: var(--color-surface);
  box-sizing: border-box;
  resize: vertical;
}
.dialog-textarea:focus {
  outline: none;
  border-color: var(--color-primary);
}
.dialog-error {
  background: var(--color-danger-bg, #fef2f2);
  color: var(--color-danger, #991b1b);
  padding: 10px 12px;
  border-radius: var(--radius-md);
  font-size: 13px;
  margin-bottom: 12px;
  border: 1px solid #fecaca;
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
