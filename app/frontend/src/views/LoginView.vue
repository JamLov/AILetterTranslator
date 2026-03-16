<script setup lang="ts">
import { ref } from 'vue';
import { useRouter } from 'vue-router';
import { useAuthStore } from '../stores/auth';
import { useThemeStore } from '../stores/theme';

const router = useRouter();
const authStore = useAuthStore();
const themeStore = useThemeStore();
const message = ref<string>('');
const isLoggingIn = ref(false);

const callback = async (response: any) => {
  const credential = response.credential;
  isLoggingIn.value = true;
  message.value = 'Signing in...';

  try {
    const res = await fetch(`${import.meta.env.VITE_API_BASE_URL ?? ''}/api/login`, {
      method: 'POST',
      headers: {
        'Authorization': `Bearer ${credential}`,
        'Content-Type': 'application/json'
      }
    });

    if (res.ok) {
      const data = await res.json();
      authStore.setAuth(credential, data.userId);
      router.push('/dashboard');
    } else {
      message.value = `Access Denied: ${res.status} ${res.statusText}`;
    }
  } catch (error: any) {
    message.value = `Error: ${error.message}`;
  } finally {
    isLoggingIn.value = false;
  }
};
</script>

<template>
  <div class="login-page">
    <button @click="themeStore.toggle()" class="login-theme-toggle theme-toggle" title="Toggle dark mode">
      <span v-if="themeStore.isDark" class="theme-icon">&#9788;</span>
      <span v-else class="theme-icon">&#9790;</span>
    </button>
    <div class="login-card card">
      <h1 class="login-title">Letter Translator</h1>
      <p class="login-subtitle">Translate handwritten letters and historical documents using AI.</p>

      <div class="login-divider"></div>

      <p v-if="!isLoggingIn" class="login-prompt">Sign in with your Google account to continue.</p>

      <div class="login-action">
        <GoogleLogin :callback="callback" v-if="!isLoggingIn" />
        <p v-if="isLoggingIn" class="login-loading">Signing in...</p>
      </div>

      <div v-if="message && !isLoggingIn" class="login-error">
        {{ message }}
      </div>
    </div>
  </div>
</template>

<style scoped>
.login-page {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 100vh;
  padding: 32px;
  position: relative;
}

.login-theme-toggle {
  position: absolute;
  top: 20px;
  right: 24px;
}

.login-card {
  width: 100%;
  max-width: 400px;
  padding: 40px 36px;
  text-align: center;
}

.login-title {
  font-size: 24px;
  font-weight: 700;
  letter-spacing: -0.5px;
  margin-bottom: 8px;
}

.login-subtitle {
  font-size: 14px;
  color: var(--color-text-secondary);
  line-height: 1.5;
}

.login-divider {
  height: 1px;
  background: var(--color-border);
  margin: 24px 0;
}

.login-prompt {
  font-size: 13px;
  color: var(--color-text-muted);
  margin-bottom: 20px;
}

.login-action {
  display: flex;
  justify-content: center;
}

.login-loading {
  font-size: 13px;
  color: var(--color-text-secondary);
}

.login-error {
  margin-top: 16px;
  padding: 10px 14px;
  background: var(--color-danger-bg);
  color: var(--color-danger);
  border-radius: var(--radius-md);
  font-size: 13px;
}
</style>
