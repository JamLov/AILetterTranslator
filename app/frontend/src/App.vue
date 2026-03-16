<script setup lang="ts">
import { useRouter } from 'vue-router';
import { useAuthStore } from './stores/auth';
import { useThemeStore } from './stores/theme';

const router = useRouter();
const authStore = useAuthStore();
const themeStore = useThemeStore();

const logout = () => {
  authStore.clearAuth();
  router.push('/login');
};
</script>

<template>
  <div class="app-shell">
    <nav class="topbar" v-if="authStore.isAuthenticated">
      <div class="topbar-inner">
        <router-link to="/dashboard" class="topbar-brand">Letter Translator</router-link>
        <div class="topbar-actions">
          <router-link to="/dashboard" class="nav-link">My Jobs</router-link>
          <router-link to="/projects" class="nav-link">Projects</router-link>
          <router-link to="/new-job" class="btn btn-primary btn-sm">+ New Job</router-link>
          <button @click="logout" class="btn btn-secondary btn-sm">Log Out</button>
          <button @click="themeStore.toggle()" class="theme-toggle" title="Toggle dark mode">
            <span v-if="themeStore.isDark" class="theme-icon">&#9788;</span>
            <span v-else class="theme-icon">&#9790;</span>
          </button>
        </div>
      </div>
    </nav>
    <router-view />
  </div>
</template>

<style>
.app-shell {
  min-height: 100vh;
}

.topbar {
  background: var(--color-surface);
  border-bottom: 1px solid var(--color-border);
  position: sticky;
  top: 0;
  z-index: 100;
}

.topbar-inner {
  max-width: 1400px;
  margin: 0 auto;
  padding: 0 32px;
  height: 52px;
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.topbar-brand {
  font-size: 15px;
  font-weight: 700;
  color: var(--color-text);
  text-decoration: none;
  letter-spacing: -0.3px;
}
.topbar-brand:hover {
  text-decoration: none;
  color: var(--color-primary);
}

.topbar-actions {
  display: flex;
  align-items: center;
  gap: 8px;
}

.nav-link {
  font-size: 13px;
  font-weight: 500;
  color: var(--color-text-secondary);
  text-decoration: none;
  padding: 4px 8px;
  border-radius: var(--radius-md);
  transition: color 0.15s ease;
}
.nav-link:hover {
  color: var(--color-text);
}
.nav-link.router-link-active {
  color: var(--color-primary);
}

.btn-sm {
  padding: 5px 12px;
  font-size: 12px;
}

.theme-toggle {
  background: none;
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md);
  width: 32px;
  height: 32px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
  transition: all 0.15s ease;
  padding: 0;
}
.theme-toggle:hover {
  background: var(--color-row-hover);
  border-color: var(--color-text-muted);
}
.theme-icon {
  font-size: 16px;
  line-height: 1;
  color: var(--color-text-secondary);
}
</style>
