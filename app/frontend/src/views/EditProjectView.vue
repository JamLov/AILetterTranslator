<script setup lang="ts">
import { ref, onMounted } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { api } from '../api';

const route = useRoute();
const router = useRouter();
const projectId = route.params.projectId as string;

const name = ref('');
const description = ref('');
const isLoading = ref(true);
const isSubmitting = ref(false);
const errorMessage = ref('');
const loadError = ref<string | null>(null);

onMounted(async () => {
  try {
    const res = await api(`/api/projects/${projectId}`);
    if (res.ok) {
      const detail = await res.json();
      if (!detail.isOwner) {
        loadError.value = 'Only the project owner can edit this project.';
        return;
      }
      name.value = detail.metadata.name;
      description.value = detail.metadata.description || '';
    } else if (res.status === 404) {
      loadError.value = 'Project not found.';
    } else if (res.status !== 401) {
      throw new Error(`Server responded with ${res.status}`);
    }
  } catch (err) {
    loadError.value = 'Failed to load project.';
    console.error(err);
  } finally {
    isLoading.value = false;
  }
});

const submitUpdate = async () => {
  if (!name.value.trim()) {
    errorMessage.value = 'Project name is required.';
    return;
  }

  isSubmitting.value = true;
  errorMessage.value = '';

  try {
    const res = await api(`/api/projects/${projectId}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        name: name.value,
        description: description.value || null
      })
    });

    if (res.ok) {
      router.push({ name: 'project-detail', params: { projectId } });
    } else if (res.status !== 401) {
      const data = await res.json();
      errorMessage.value = data.message || `Failed to update project: ${res.statusText}`;
    }
  } catch (err: any) {
    errorMessage.value = `Error updating project: ${err.message}`;
  } finally {
    isSubmitting.value = false;
  }
};

const cancel = () => {
  router.push({ name: 'project-detail', params: { projectId } });
};
</script>

<template>
  <div class="page page-narrow">
    <div class="page-header">
      <h1>Edit Project</h1>
    </div>

    <div v-if="isLoading" class="state-box">Loading...</div>
    <div v-else-if="loadError" class="state-box state-error">{{ loadError }}</div>

    <div v-else class="card form-card">
      <form @submit.prevent="submitUpdate">

        <div v-if="errorMessage" class="form-error">
          {{ errorMessage }}
        </div>

        <div class="form-group">
          <label for="projectName" class="form-label">Project Name</label>
          <input
            type="text"
            id="projectName"
            v-model="name"
            required
            maxlength="250"
            :disabled="isSubmitting"
            class="form-input"
          />
          <span class="form-hint">{{ name.length }} / 250</span>
        </div>

        <div class="form-group">
          <label for="description" class="form-label">Description <span class="optional">(optional)</span></label>
          <textarea
            id="description"
            v-model="description"
            rows="3"
            maxlength="1000"
            :disabled="isSubmitting"
            class="form-input"
          ></textarea>
          <span class="form-hint">{{ description.length }} / 1000</span>
        </div>

        <div class="form-actions">
          <button type="button" @click="cancel" class="btn btn-secondary" :disabled="isSubmitting">Cancel</button>
          <button type="submit" class="btn btn-primary" :disabled="isSubmitting || !name.trim()">
            {{ isSubmitting ? 'Saving...' : 'Save Changes' }}
          </button>
        </div>
      </form>
    </div>
  </div>
</template>

<style scoped>
.page-header { padding: 32px 0 20px; }
.page-header h1 { font-size: 22px; }
.state-box {
  text-align: center; padding: 64px 32px; margin-top: 16px;
  background: var(--color-surface); border: 1px solid var(--color-border);
  border-radius: var(--radius-lg); color: var(--color-text-secondary);
}
.state-error { background: var(--color-danger-bg); color: var(--color-danger); border-color: #fecaca; }
.form-card { padding: 28px; }
.form-group { margin-bottom: 20px; }
.form-label {
  display: block; font-size: 13px; font-weight: 600;
  margin-bottom: 6px; color: var(--color-text);
}
.optional { font-weight: 400; color: var(--color-text-muted); }
.form-input {
  width: 100%; padding: 8px 12px;
  border: 1px solid var(--color-border); border-radius: var(--radius-md);
  font-size: 14px; font-family: inherit;
  color: var(--color-text); background: var(--color-surface);
  transition: border-color 0.15s ease;
}
.form-input:focus {
  outline: none; border-color: var(--color-primary);
  box-shadow: 0 0 0 3px rgba(37, 99, 235, 0.1);
}
textarea.form-input { resize: vertical; }
.form-hint { font-size: 12px; color: var(--color-text-muted); margin-top: 4px; display: block; }
.form-error {
  padding: 10px 14px; background: var(--color-danger-bg);
  color: var(--color-danger); border-radius: var(--radius-md);
  font-size: 13px; margin-bottom: 20px; border-left: 3px solid var(--color-danger);
}
.form-actions {
  display: flex; justify-content: flex-end; gap: 8px;
  padding-top: 16px; border-top: 1px solid var(--color-border-light); margin-top: 8px;
}
</style>
