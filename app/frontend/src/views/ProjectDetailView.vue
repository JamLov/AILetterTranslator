<script setup lang="ts">
import { ref, onMounted } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { api } from '../api';

interface Job {
  jobId: string;
  jobName: string;
  createdAt: string;
  status: string;
  originalFileCount: number;
  letterDate: string | null;
}

interface ProjectMetadata {
  projectId: string;
  name: string;
  description: string | null;
  createdAt: string;
}

interface ProjectDetail {
  metadata: ProjectMetadata;
  jobs: Job[];
  isOwner: boolean;
  memberEmails: string[];
  memberCount: number;
}

const route = useRoute();
const router = useRouter();
const project = ref<ProjectDetail | null>(null);
const isLoading = ref(true);
const error = ref<string | null>(null);
const isDeleting = ref(false);
const showAddMember = ref(false);
const newMemberEmail = ref('');
const addMemberError = ref('');
const isAddingMember = ref(false);
const removingEmail = ref<string | null>(null);

const projectId = route.params.projectId as string;

onMounted(async () => {
  try {
    const res = await api(`/api/projects/${projectId}`);
    if (res.ok) {
      project.value = await res.json();
    } else if (res.status === 404) {
      error.value = 'Project not found.';
    } else if (res.status !== 401) {
      throw new Error(`Server responded with ${res.status}`);
    }
  } catch (err) {
    error.value = 'An error occurred while fetching the project.';
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

const addMember = async () => {
  if (!newMemberEmail.value.trim()) return;
  isAddingMember.value = true;
  addMemberError.value = '';
  try {
    const res = await api(`/api/projects/${projectId}/members`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email: newMemberEmail.value.trim() })
    });
    if (res.ok) {
      // Reload project to get updated members
      const detailRes = await api(`/api/projects/${projectId}`);
      if (detailRes.ok) {
        project.value = await detailRes.json();
      }
      newMemberEmail.value = '';
      showAddMember.value = false;
    } else {
      const data = await res.json().catch(() => null);
      addMemberError.value = data?.message || `Failed to add member: ${res.statusText}`;
    }
  } catch (err: any) {
    addMemberError.value = `Error: ${err.message}`;
  } finally {
    isAddingMember.value = false;
  }
};

const removeMember = async (email: string) => {
  removingEmail.value = email;
  try {
    const res = await api(`/api/projects/${projectId}/members/remove`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email })
    });
    if (res.ok) {
      const detailRes = await api(`/api/projects/${projectId}`);
      if (detailRes.ok) {
        project.value = await detailRes.json();
      }
    } else {
      const data = await res.json().catch(() => null);
      error.value = data?.message || 'Failed to remove member.';
    }
  } catch (err: any) {
    error.value = `Error removing member: ${err.message}`;
  } finally {
    removingEmail.value = null;
  }
};

const deleteProject = async () => {
  if (!confirm('Are you sure you want to delete this project? It must be empty.')) return;
  isDeleting.value = true;
  try {
    const res = await api(`/api/projects/${projectId}`, { method: 'DELETE' });
    if (res.ok) {
      router.push('/projects');
    } else {
      const data = await res.json();
      error.value = data.message || 'Failed to delete project.';
    }
  } catch (err: any) {
    error.value = `Failed to delete project: ${err.message}`;
  } finally {
    isDeleting.value = false;
  }
};
</script>

<template>
  <div class="page">
    <div v-if="isLoading" class="state-box">Loading project...</div>
    <div v-else-if="error" class="state-box state-error">{{ error }}</div>
    <div v-else-if="project">

      <div class="detail-header">
        <button @click="router.push('/projects')" class="back-link">&larr; Back to Projects</button>
        <div class="header-row">
          <div>
            <h1 class="detail-title">{{ project.metadata.name }}</h1>
            <p v-if="project.metadata.description" class="detail-desc">{{ project.metadata.description }}</p>
            <div class="detail-meta">
              <span>Created {{ formatDate(project.metadata.createdAt) }}</span>
              <span :class="project.isOwner ? 'pill pill-finished' : 'pill pill-not-started'">
                {{ project.isOwner ? 'Owner' : 'Member' }}
              </span>
              <span v-if="project.memberCount > 0" class="members-count">
                {{ project.memberCount }} member(s)
              </span>
            </div>
          </div>
          <div v-if="project.isOwner" class="header-actions">
            <router-link :to="{ name: 'edit-project', params: { projectId } }" class="btn btn-secondary btn-sm">Edit Project</router-link>
            <router-link :to="{ name: 'new-project-job', params: { projectId } }" class="btn btn-primary btn-sm">+ Add Job</router-link>
          </div>
        </div>
      </div>

      <!-- Members Section (owner only) -->
      <div v-if="project.isOwner" class="card members-section">
        <div class="members-header">
          <h3 class="members-title">Members</h3>
          <button class="btn btn-secondary btn-sm" @click="showAddMember = true">+ Add Member</button>
        </div>
        <div v-if="project.memberEmails.length === 0" class="members-empty">
          No members yet. Add members by email to give them read-only access.
        </div>
        <ul v-else class="member-list">
          <li v-for="email in project.memberEmails" :key="email" class="member-item">
            <span class="member-email">{{ email }}</span>
            <button
              class="btn-remove"
              :disabled="removingEmail === email"
              @click="removeMember(email)"
              title="Remove member"
            >{{ removingEmail === email ? '...' : '&times;' }}</button>
          </li>
        </ul>
      </div>

      <!-- Add Member Modal -->
      <div v-if="showAddMember" class="modal-overlay" @click.self="showAddMember = false">
        <div class="modal-box">
          <h3 class="modal-title">Add Member</h3>
          <p class="modal-desc">Enter the email address of a registered user to give them read-only access to this project.</p>
          <div v-if="addMemberError" class="form-error">{{ addMemberError }}</div>
          <input
            type="email"
            v-model="newMemberEmail"
            placeholder="user@example.com"
            class="form-input"
            :disabled="isAddingMember"
            @keyup.enter="addMember"
          />
          <div class="modal-actions">
            <button class="btn btn-secondary btn-sm" @click="showAddMember = false; addMemberError = ''; newMemberEmail = '';" :disabled="isAddingMember">Cancel</button>
            <button class="btn btn-primary btn-sm" @click="addMember" :disabled="isAddingMember || !newMemberEmail.trim()">
              {{ isAddingMember ? 'Adding...' : 'Add Member' }}
            </button>
          </div>
        </div>
      </div>

      <div v-if="project.jobs.length === 0" class="state-box">
        <h3>No jobs yet</h3>
        <p class="state-sub" v-if="project.isOwner">Click <strong>+ Add Job</strong> to start translating letters in this project.</p>
        <p class="state-sub" v-else>The project owner hasn't added any jobs yet.</p>
        <div v-if="project.isOwner && project.jobs.length === 0" class="empty-actions">
          <button class="btn btn-danger btn-sm" :disabled="isDeleting" @click="deleteProject">
            {{ isDeleting ? 'Deleting...' : 'Delete Empty Project' }}
          </button>
        </div>
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
              v-for="job in project.jobs"
              :key="job.jobId"
              class="job-row"
              @click="router.push({ name: 'project-job-detail', params: { projectId, jobId: job.jobId } })"
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
.state-box h3 { font-size: 16px; margin-bottom: 6px; color: var(--color-text); }
.state-sub { font-size: 13px; color: var(--color-text-muted); }
.state-error { background: var(--color-danger-bg); color: var(--color-danger); border-color: #fecaca; }

.detail-header { padding: 24px 0 20px; }
.back-link {
  background: none; border: none; padding: 0;
  font-size: 13px; color: var(--color-text-muted);
  cursor: pointer; margin-bottom: 8px; display: inline-block; font-family: inherit;
}
.back-link:hover { color: var(--color-primary); }
.header-row {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 16px;
}
.header-actions { display: flex; gap: 8px; flex-shrink: 0; }
.detail-title { font-size: 24px; font-weight: 700; letter-spacing: -0.3px; margin-bottom: 4px; }
.detail-desc { font-size: 14px; color: var(--color-text-secondary); margin-bottom: 8px; }
.detail-meta {
  display: flex; align-items: center; gap: 12px;
  font-size: 13px; color: var(--color-text-secondary);
}
.members-count { color: var(--color-text-muted); }
.empty-actions { margin-top: 20px; }

.jobs-table { width: 100%; border-collapse: collapse; font-size: 13px; }
.jobs-table th {
  text-align: left; padding: 10px 16px; font-size: 11px; font-weight: 600;
  text-transform: uppercase; letter-spacing: 0.5px;
  color: var(--color-text-muted); border-bottom: 1px solid var(--color-border);
  background: var(--color-row-hover);
}
.jobs-table td { padding: 12px 16px; border-bottom: 1px solid var(--color-border-light); vertical-align: middle; }
.jobs-table tbody tr:last-child td { border-bottom: none; }
.job-row { cursor: pointer; transition: background 0.1s ease; }
.job-row:hover { background: var(--color-row-hover); }
.job-name { font-weight: 500; color: var(--color-text); }
.col-name { width: 38%; }
.col-letter-date { width: 15%; color: var(--color-text-secondary); white-space: nowrap; }
.col-date { width: 22%; color: var(--color-text-secondary); white-space: nowrap; }
.col-files { width: 8%; text-align: center; color: var(--color-text-secondary); }
.col-status { width: 17%; }

/* Members Section */
.members-section { padding: 20px; margin-bottom: 20px; }
.members-header {
  display: flex; justify-content: space-between; align-items: center;
  margin-bottom: 12px;
}
.members-title {
  font-size: 14px; font-weight: 600; color: var(--color-text);
}
.members-empty {
  font-size: 13px; color: var(--color-text-muted); padding: 8px 0;
}
.member-list { list-style: none; padding: 0; margin: 0; }
.member-item {
  display: flex; align-items: center; justify-content: space-between;
  padding: 8px 0;
  border-bottom: 1px solid var(--color-border-light);
  font-size: 13px;
}
.member-item:last-child { border-bottom: none; }
.member-email { color: var(--color-text-secondary); }
.btn-remove {
  background: none; border: none; cursor: pointer;
  font-size: 18px; line-height: 1; color: var(--color-text-muted);
  padding: 2px 6px; border-radius: var(--radius-md);
  transition: all 0.15s ease;
}
.btn-remove:hover { color: var(--color-danger); background: var(--color-danger-bg); }

/* Add Member Modal */
.modal-overlay {
  position: fixed; inset: 0; z-index: 200;
  background: rgba(0, 0, 0, 0.4);
  display: flex; align-items: center; justify-content: center;
}
.modal-box {
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-lg);
  padding: 24px;
  width: 100%; max-width: 420px;
  box-shadow: 0 8px 32px rgba(0, 0, 0, 0.12);
}
.modal-title { font-size: 16px; font-weight: 600; margin-bottom: 6px; }
.modal-desc { font-size: 13px; color: var(--color-text-muted); margin-bottom: 16px; line-height: 1.5; }
.modal-actions { display: flex; justify-content: flex-end; gap: 8px; margin-top: 16px; }
.form-input {
  width: 100%; padding: 8px 12px;
  border: 1px solid var(--color-border); border-radius: var(--radius-md);
  font-size: 14px; font-family: inherit;
  color: var(--color-text); background: var(--color-surface);
}
.form-input:focus {
  outline: none; border-color: var(--color-primary);
  box-shadow: 0 0 0 3px rgba(37, 99, 235, 0.1);
}
.form-error {
  padding: 8px 12px; background: var(--color-danger-bg);
  color: var(--color-danger); border-radius: var(--radius-md);
  font-size: 13px; margin-bottom: 12px; border-left: 3px solid var(--color-danger);
}
</style>
