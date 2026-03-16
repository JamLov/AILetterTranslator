<script setup lang="ts">
import { ref } from 'vue';
import { useRouter } from 'vue-router';
import { api } from '../api';

const router = useRouter();
const name = ref('');
const description = ref('');
const isSubmitting = ref(false);
const errorMessage = ref('');

const submitProject = async () => {
  if (!name.value.trim()) {
    errorMessage.value = 'Please provide a name for the project.';
    return;
  }
  if (name.value.length > 250) {
    errorMessage.value = 'Project name cannot exceed 250 characters.';
    return;
  }
  if (description.value.length > 1000) {
    errorMessage.value = 'Description cannot exceed 1000 characters.';
    return;
  }

  isSubmitting.value = true;
  errorMessage.value = '';

  try {
    const res = await api('/api/projects', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        name: name.value,
        description: description.value || null
      })
    });

    if (res.ok) {
      const project = await res.json();
      router.push({ name: 'project-detail', params: { projectId: project.projectId } });
    } else if (res.status !== 401) {
      const data = await res.json();
      errorMessage.value = data.message || `Failed to create project: ${res.statusText}`;
    }
  } catch (err: any) {
    errorMessage.value = `Error creating project: ${err.message}`;
  } finally {
    isSubmitting.value = false;
  }
};

const cancel = () => {
  router.push('/projects');
};
</script>

<template>
  <div class="page page-narrow">
    <div class="page-header">
      <h1>New Project</h1>
    </div>

    <div class="card form-card">
      <form @submit.prevent="submitProject">

        <div v-if="errorMessage" class="form-error">
          {{ errorMessage }}
        </div>

        <div class="form-group">
          <label for="projectName" class="form-label">Project Name</label>
          <input
            type="text"
            id="projectName"
            v-model="name"
            placeholder="e.g., Grandma's Letters"
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
            placeholder="What is this project about?"
            maxlength="1000"
            :disabled="isSubmitting"
            class="form-input"
          ></textarea>
          <span class="form-hint">{{ description.length }} / 1000</span>
        </div>

        <div class="form-actions">
          <button type="button" @click="cancel" class="btn btn-secondary" :disabled="isSubmitting">Cancel</button>
          <button type="submit" class="btn btn-primary" :disabled="isSubmitting || !name.trim()">
            {{ isSubmitting ? 'Creating...' : 'Create Project' }}
          </button>
        </div>
      </form>
    </div>
  </div>
</template>

<style scoped>
.page-header {
  padding: 32px 0 20px;
}
.page-header h1 {
  font-size: 22px;
}
.form-card { padding: 28px; }
.form-group { margin-bottom: 20px; }
.form-label {
  display: block;
  font-size: 13px;
  font-weight: 600;
  margin-bottom: 6px;
  color: var(--color-text);
}
.optional {
  font-weight: 400;
  color: var(--color-text-muted);
}
.form-input {
  width: 100%;
  padding: 8px 12px;
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  font-size: 14px;
  font-family: inherit;
  color: var(--color-text);
  background: var(--color-surface);
  transition: border-color 0.15s ease;
}
.form-input:focus {
  outline: none;
  border-color: var(--color-primary);
  box-shadow: 0 0 0 3px rgba(37, 99, 235, 0.1);
}
textarea.form-input { resize: vertical; }
.form-hint {
  font-size: 12px;
  color: var(--color-text-muted);
  margin-top: 4px;
  display: block;
}
.form-error {
  padding: 10px 14px;
  background: var(--color-danger-bg);
  color: var(--color-danger);
  border-radius: var(--radius-md);
  font-size: 13px;
  margin-bottom: 20px;
  border-left: 3px solid var(--color-danger);
}
.form-actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  padding-top: 16px;
  border-top: 1px solid var(--color-border-light);
  margin-top: 8px;
}
</style>
