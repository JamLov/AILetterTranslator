<script setup lang="ts">
import { ref } from 'vue';
import { useRouter } from 'vue-router';
import { useAuthStore } from '../stores/auth';

const router = useRouter();
const authStore = useAuthStore();
const jobName = ref('');
const notes = ref('');
const selectedFiles = ref<File[]>([]);
const isDragging = ref(false);
const isSubmitting = ref(false);
const errorMessage = ref('');

const MAX_FILE_SIZE_MB = 4;
const MAX_FILE_SIZE_BYTES = MAX_FILE_SIZE_MB * 1024 * 1024;
const ALLOWED_TYPES = ['image/jpeg', 'image/png'];

const handleFileSelect = (event: Event) => {
  const input = event.target as HTMLInputElement;
  if (input.files) {
    addFiles(Array.from(input.files));
  }
};

const handleDrop = (event: DragEvent) => {
  isDragging.value = false;
  if (event.dataTransfer?.files) {
    addFiles(Array.from(event.dataTransfer.files));
  }
};

const addFiles = (files: File[]) => {
  errorMessage.value = "";

  const validFiles = files.filter(file => {
    if (!ALLOWED_TYPES.includes(file.type)) {
      errorMessage.value = "Only JPG and PNG files are allowed.";
      return false;
    }
    if (file.size > MAX_FILE_SIZE_BYTES) {
      errorMessage.value = `File ${file.name} exceeds the ${MAX_FILE_SIZE_MB}MB limit.`;
      return false;
    }
    return true;
  });

  selectedFiles.value.push(...validFiles);
};

const removeFile = (index: number) => {
  selectedFiles.value.splice(index, 1);
};

const submitJob = async () => {
  if (!jobName.value.trim()) {
    errorMessage.value = "Please provide a title for the job.";
    return;
  }
  if (jobName.value.length > 250) {
    errorMessage.value = "Job title cannot exceed 250 characters.";
    return;
  }
  if (notes.value.length > 1000) {
    errorMessage.value = "Notes cannot exceed 1000 characters.";
    return;
  }
  if (selectedFiles.value.length === 0) {
    errorMessage.value = "Please select at least one valid file to translate.";
    return;
  }

  isSubmitting.value = true;
  errorMessage.value = "";

  const formData = new FormData();
  formData.append('jobName', jobName.value);
  formData.append('notes', notes.value);

  selectedFiles.value.forEach(file => {
    formData.append('files', file);
  });

  try {
    const res = await fetch(`${import.meta.env.VITE_API_BASE_URL ?? ''}/api/jobs`, {
      method: 'POST',
      headers: {
        'Authorization': `Bearer ${authStore.token}`
      },
      body: formData
    });

    if (res.ok) {
      router.push('/dashboard');
    } else {
      const data = await res.json();
      errorMessage.value = data.message || `Failed to create job: ${res.statusText}`;
    }
  } catch (error: any) {
    errorMessage.value = `Error submitting job: ${error.message}`;
  } finally {
    isSubmitting.value = false;
  }
};

const cancel = () => {
  router.push('/dashboard');
};
</script>

<template>
  <div class="page page-narrow">
    <div class="page-header">
      <h1>New Translation Job</h1>
    </div>

    <div class="card form-card">
      <form @submit.prevent="submitJob">

        <div v-if="errorMessage" class="form-error">
          {{ errorMessage }}
        </div>

        <div class="form-group">
          <label for="jobName" class="form-label">Job Title</label>
          <input
            type="text"
            id="jobName"
            v-model="jobName"
            placeholder="e.g., Letter from Grandfather, 1946"
            required
            maxlength="250"
            :disabled="isSubmitting"
            class="form-input"
          />
          <span class="form-hint">{{ jobName.length }} / 250</span>
        </div>

        <div class="form-group">
          <label for="notes" class="form-label">Notes / Context <span class="optional">(optional)</span></label>
          <textarea
            id="notes"
            v-model="notes"
            rows="3"
            placeholder="Any context to help the AI: places, names, dialects, time period..."
            maxlength="1000"
            :disabled="isSubmitting"
            class="form-input"
          ></textarea>
          <span class="form-hint">{{ notes.length }} / 1000</span>
        </div>

        <div class="form-group">
          <label class="form-label">Upload Images</label>
          <div
            class="drop-zone"
            :class="{ 'drop-zone-active': isDragging }"
            @dragover.prevent="isDragging = true"
            @dragleave.prevent="isDragging = false"
            @drop.prevent="handleDrop"
            @click="($refs.fileInput as HTMLInputElement).click()"
          >
            <input
              type="file"
              ref="fileInput"
              multiple
              accept="image/jpeg, image/png"
              style="display: none;"
              @change="handleFileSelect"
              :disabled="isSubmitting"
            />
            <div class="drop-zone-content">
              <p v-if="!isDragging" class="drop-zone-text">Drop images here or <span class="drop-zone-link">browse</span></p>
              <p v-else class="drop-zone-text drop-zone-active-text">Release to upload</p>
              <span class="form-hint">JPG or PNG, max 4 MB each</span>
            </div>
          </div>

          <div v-if="selectedFiles.length > 0" class="file-list">
            <div v-for="(file, index) in selectedFiles" :key="index" class="file-item">
              <span class="file-name">{{ file.name }}</span>
              <span class="file-size">{{ (file.size / 1024 / 1024).toFixed(1) }} MB</span>
              <button type="button" @click.prevent="removeFile(index)" class="btn btn-danger" :disabled="isSubmitting">Remove</button>
            </div>
          </div>
        </div>

        <div class="form-actions">
          <button type="button" @click="cancel" class="btn btn-secondary" :disabled="isSubmitting">Cancel</button>
          <button type="submit" class="btn btn-primary" :disabled="isSubmitting || selectedFiles.length === 0 || !jobName.trim()">
            {{ isSubmitting ? 'Creating...' : 'Create Job' }}
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

.form-card {
  padding: 28px;
}

.form-group {
  margin-bottom: 20px;
}

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
textarea.form-input {
  resize: vertical;
}

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

.drop-zone {
  border: 2px dashed var(--color-border);
  border-radius: var(--radius-lg);
  padding: 32px 16px;
  text-align: center;
  cursor: pointer;
  transition: all 0.15s ease;
  background: var(--color-bg);
}
.drop-zone:hover, .drop-zone-active {
  border-color: var(--color-primary);
  background: rgba(37, 99, 235, 0.03);
}
.drop-zone-content {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 4px;
}
.drop-zone-text {
  font-size: 13px;
  color: var(--color-text-secondary);
}
.drop-zone-link {
  color: var(--color-primary);
  font-weight: 500;
}
.drop-zone-active-text {
  color: var(--color-primary);
  font-weight: 500;
}

.file-list {
  margin-top: 12px;
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.file-item {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 8px 12px;
  background: var(--color-bg);
  border-radius: var(--radius-md);
  font-size: 13px;
}
.file-name {
  flex: 1;
  font-weight: 500;
  color: var(--color-text);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.file-size {
  color: var(--color-text-muted);
  white-space: nowrap;
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
