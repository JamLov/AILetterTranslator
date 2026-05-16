<script setup lang="ts">
import { ref, onUnmounted } from 'vue';
import { useRouter } from 'vue-router';
import { useAuthStore } from '../stores/auth';
import { useThemeStore } from '../stores/theme';

const router = useRouter();
const authStore = useAuthStore();
const themeStore = useThemeStore();
const message = ref<string>('');
const isLoggingIn = ref(false);
const isWakingUp = ref(false);
const starChar = ref('*');

const starFrames = ['*', '\u2729', '\u2727', '\u2726', '\u2728', '\u2735', '\u2731', '\u273A', '\u2749', '\u2743'];
let wakeTimeout: ReturnType<typeof setTimeout> | null = null;
let starInterval: ReturnType<typeof setInterval> | null = null;
let starIndex = 0;

function startWakeAnimation() {
  isWakingUp.value = true;
  starInterval = setInterval(() => {
    starIndex = (starIndex + 1) % starFrames.length;
    starChar.value = starFrames[starIndex];
  }, 150);
}

function stopWakeAnimation() {
  if (wakeTimeout) { clearTimeout(wakeTimeout); wakeTimeout = null; }
  if (starInterval) { clearInterval(starInterval); starInterval = null; }
  isWakingUp.value = false;
}

onUnmounted(stopWakeAnimation);

const callback = async (response: any) => {
  const credential = response.credential;
  isLoggingIn.value = true;
  message.value = '';

  wakeTimeout = setTimeout(startWakeAnimation, 10000);

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
    stopWakeAnimation();
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
        <div v-if="isLoggingIn" class="login-status">
          <p class="login-status-text">
            <span v-if="!isWakingUp">signing in...</span>
            <span v-else><span class="wake-star">{{ starChar }}</span> waking up from sleep... <span class="wake-star">{{ starChar }}</span></span>
          </p>
          <p v-if="isWakingUp" class="login-status-sub">hang tight, the server is stretching</p>
        </div>
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

.login-status {
  padding: 4px 0;
}

.login-status-text {
  font-family: 'Courier New', Courier, monospace;
  font-size: 13px;
  color: var(--color-text-secondary);
  letter-spacing: 0.5px;
}

.wake-star {
  display: inline-block;
  font-size: 14px;
}

.login-status-sub {
  font-family: 'Courier New', Courier, monospace;
  font-size: 11px;
  color: var(--color-text-muted);
  margin-top: 6px;
  letter-spacing: 0.3px;
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
