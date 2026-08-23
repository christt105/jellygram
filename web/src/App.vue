<template>
  <div class="dashboard">
    <aside class="sidebar">
      <div class="logo">
        <img src="/logo-mark.png" alt="Cinegram" class="logo-mark" />
        <div class="logo-text">
          <h2>Cinegram</h2>
          <span class="logo-subtitle label-caps">Media Management</span>
        </div>
      </div>

      <nav class="nav-menu">
        <router-link to="/movies" class="nav-item" active-class="active">
          <Film :size="20" />
          <span>Movies</span>
        </router-link>
        <router-link to="/series" class="nav-item" active-class="active">
          <Tv :size="20" />
          <span>Series</span>
        </router-link>
        <router-link to="/telegram" class="nav-item" active-class="active">
          <Send :size="20" />
          <span>Telegram Library</span>
        </router-link>
        <router-link to="/queue" class="nav-item" active-class="active">
          <List :size="20" />
          <span>Transfers</span>
        </router-link>
        <router-link to="/downloads" class="nav-item" active-class="active">
          <FolderDown :size="20" />
          <span>Downloads</span>
        </router-link>
        <router-link to="/settings" class="nav-item" active-class="active">
          <Settings :size="20" />
          <span>Settings</span>
        </router-link>
      </nav>

      <div class="server-status">
        <div class="status-indicator"></div>
        <span>Connected to Jellyfin &amp; Backend</span>
      </div>

      <a
        class="sidebar-footer"
        href="https://github.com/christt105/cinegram"
        target="_blank"
        rel="noopener noreferrer"
      >
        <Github :size="16" />
        <span>View on GitHub</span>
      </a>
    </aside>

    <main class="main-content" ref="mainContent">
      <header class="header" v-if="isMediaRoute">
        <div class="search-bar">
          <Search :size="18" class="search-icon" />
          <input type="text" v-model="searchQuery" placeholder="Search for movies, series..." />
        </div>
      </header>

      <router-view
        :searchQuery="searchQuery"
        :jellyfinItems="jellyfinItems"
        :telegramMovies="telegramMovies"
        :telegramSeries="telegramSeries"
        :loading="loading"
        :jellyfinError="jellyfinError"
        :backendError="backendError"
        @refresh="fetchItems"
      />
    </main>

    <ScrollToTop :target="mainContent" />
  </div>
</template>

<script setup lang="ts">
import { Film, Tv, Send, Settings, Search, List, Github, FolderDown } from 'lucide-vue-next';
import { onMounted, ref, computed, nextTick } from 'vue';
import { useRouter, useRoute } from 'vue-router';
import { useJellyfin } from './api/jellyfin';
import { useBackend } from './api/backend';
import ScrollToTop from './components/ScrollToTop.vue';

const searchQuery = ref('');

// Restore scroll position of the main content area on back/forward navigation.
// The scrollable element is <main>, not the window, so vue-router's built-in
// scrollBehavior can't handle it.
const mainContent = ref<HTMLElement | null>(null);
const router = useRouter();
const route = useRoute();
const scrollPositions = new Map<string, number>();

const isMediaRoute = computed(() => ['movies', 'series', 'telegram'].includes(route.name as string));

router.beforeEach((to, from) => {
  if (mainContent.value) {
    scrollPositions.set(from.fullPath, mainContent.value.scrollTop);
  }
  return true;
});

router.afterEach((to) => {
  const saved = scrollPositions.get(to.fullPath) ?? 0;
  nextTick(() => {
    requestAnimationFrame(() => {
      if (mainContent.value) mainContent.value.scrollTop = saved;
    });
  });
});

const { items: jellyfinItems, loading: jellyfinLoading, error: jellyfinError, fetchItems: fetchJellyfin } = useJellyfin();
const { telegramMovies, telegramSeries, loading: backendLoading, error: backendError, fetchTelegramMovies, fetchTelegramSeries } = useBackend();

const loading = computed(() => jellyfinLoading.value || backendLoading.value);

const fetchItems = () => {
  fetchJellyfin();
  fetchTelegramMovies();
  fetchTelegramSeries();
};

onMounted(() => {
  fetchItems();
});
</script>

<style>
.dashboard {
  display: flex;
  width: 100%;
  height: 100vh;
}

/* Sidebar */
.sidebar {
  width: 264px;
  flex-shrink: 0;
  display: flex;
  flex-direction: column;
  padding: var(--sp-lg) var(--sp-md);
  background: rgba(32, 31, 31, 0.5);
  backdrop-filter: blur(var(--glass-blur));
  -webkit-backdrop-filter: blur(var(--glass-blur));
  border-right: 1px solid var(--glass-border);
}

.logo {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: var(--sp-lg);
  padding: 0 8px;
}

.logo-mark {
  width: 44px;
  height: 44px;
  object-fit: contain;
  filter: drop-shadow(0 4px 12px rgba(157, 80, 187, 0.4));
}

.logo-text {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.logo h2 {
  font-weight: 800;
  font-size: 1.5rem;
  letter-spacing: -0.02em;
  color: var(--primary);
  line-height: 1;
}

.logo-subtitle {
  color: var(--on-surface-variant);
  opacity: 0.7;
}

.nav-menu {
  display: flex;
  flex-direction: column;
  gap: 8px;
  flex-grow: 1;
}

.nav-item {
  display: flex;
  align-items: center;
  gap: 14px;
  padding: 12px 16px;
  border-radius: var(--r-lg);
  color: var(--on-surface-variant);
  text-decoration: none;
  font-weight: 600;
  transition: all 0.2s ease;
}

.nav-item:hover {
  background: var(--surface-container-high);
  color: var(--on-surface);
}

.nav-item:active {
  transform: scale(0.97);
}

.nav-item.active {
  background: rgba(42, 42, 42, 0.5);
  color: var(--primary);
  border-right: 2px solid var(--primary);
  font-weight: 700;
}

.server-status {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 14px 16px;
  background: var(--surface-container-lowest);
  border-radius: var(--r-lg);
  border: 1px solid var(--glass-border);
  font-size: 0.8rem;
  color: var(--on-surface-variant);
}

.status-indicator {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  flex-shrink: 0;
  background: var(--success);
  box-shadow: 0 0 10px var(--success);
}

.sidebar-footer {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  margin-top: 10px;
  padding: 10px 16px;
  border-radius: var(--r-lg);
  color: var(--on-surface-variant);
  text-decoration: none;
  font-size: 0.8rem;
  font-weight: 600;
  transition: all 0.2s ease;
}

.sidebar-footer:hover {
  color: var(--on-surface);
  background: var(--surface-container-high);
}

/* Main Content */
.main-content {
  flex-grow: 1;
  padding: var(--sp-md) var(--sp-lg);
  display: flex;
  flex-direction: column;
  overflow-y: auto;
  overflow-x: hidden;
  min-width: 0;
}

.header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: var(--sp-lg);
  gap: var(--sp-md);
}

.search-bar {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 10px 20px;
  flex-grow: 1;
  max-width: 560px;
  border-radius: var(--r-full);
  background: var(--surface-container-lowest);
  border: 1px solid var(--outline-variant);
  transition: all 0.2s ease;
}

.search-bar:focus-within {
  border-color: var(--primary);
  box-shadow: 0 0 0 3px rgba(237, 177, 255, 0.15);
}

.search-icon {
  color: var(--on-surface-variant);
}

.search-bar input {
  background: transparent;
  border: none;
  color: var(--on-surface);
  font-family: inherit;
  font-size: 0.95rem;
  width: 100%;
  outline: none;
}

.search-bar input::placeholder {
  color: var(--on-surface-variant);
}

.content-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-end;
  gap: var(--sp-md);
  margin-bottom: var(--sp-md);
}

.content-header h1 {
  font-size: 2rem;
  font-weight: 700;
  letter-spacing: -0.01em;
}

.content-subtitle {
  color: var(--on-surface-variant);
  font-size: 0.9rem;
  margin-top: 4px;
}

.filters {
  display: flex;
  gap: 12px;
  align-items: center;
}

.media-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(210px, 1fr));
  gap: var(--sp-md);
  padding-bottom: var(--sp-lg);
}

.state-message {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  height: 240px;
  gap: 16px;
  color: var(--on-surface-variant);
}

.state-message.error {
  color: var(--error);
}

.local-warning {
  background: rgba(147, 0, 10, 0.15);
  border: 1px solid rgba(255, 180, 171, 0.3);
  padding: 12px 16px;
  border-radius: var(--r-lg);
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: var(--sp-md);
  color: var(--error);
  font-size: 0.9rem;
}

/* Settings styling */
.settings-container {
  padding: 8px;
  display: flex;
  flex-direction: column;
  gap: var(--sp-md);
}

.settings-container h1 {
  font-size: 2rem;
  font-weight: 700;
  letter-spacing: -0.01em;
}

.settings-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(320px, 1fr));
  gap: var(--sp-md);
}

.settings-card {
  padding: var(--sp-md);
  border-radius: var(--r-xl);
}

.settings-card h3 {
  margin-bottom: 20px;
  font-size: 1.15rem;
  font-weight: 700;
  border-bottom: 1px solid var(--glass-border);
  padding-bottom: 12px;
}

.status-item {
  display: flex;
  justify-content: space-between;
  padding: 10px 0;
  border-bottom: 1px solid rgba(255, 255, 255, 0.05);
}

.status-item:last-child {
  border-bottom: none;
}

.status-item .label {
  color: var(--on-surface-variant);
  font-weight: 500;
}

.status-item .value {
  color: var(--on-surface);
  font-weight: 600;
}

.success-text {
  color: var(--success);
}

.error-text {
  color: var(--error);
}

/* Mobile Responsive Layout */
@media (max-width: 768px) {
  .dashboard {
    flex-direction: column;
  }

  .sidebar {
    position: fixed;
    bottom: 0;
    left: 0;
    width: 100%;
    height: 72px;
    padding: 0 12px;
    flex-direction: row;
    justify-content: space-around;
    align-items: center;
    border-radius: var(--r-2xl) var(--r-2xl) 0 0;
    border-right: none;
    border-top: 1px solid var(--glass-border);
    z-index: 100;
    background: var(--glass-bg-strong);
    box-shadow: 0 -4px 20px rgba(0, 0, 0, 0.5);
  }

  .logo, .server-status, .sidebar-footer {
    display: none;
  }

  .nav-menu {
    flex-direction: row;
    justify-content: space-around;
    align-items: center;
    width: 100%;
  }

  .nav-item {
    flex-direction: column;
    gap: 4px;
    padding: 8px;
  }

  .nav-item span {
    font-size: 0.62rem;
    display: block;
  }

  .nav-item.active {
    background: transparent;
    border-right: none;
    color: var(--primary);
  }

  .main-content {
    padding: var(--sp-sm);
    padding-bottom: 90px;
  }

  .header {
    flex-direction: row;
    margin-bottom: var(--sp-md);
  }

  .search-bar {
    max-width: 100%;
  }

  .content-header {
    flex-direction: column;
    align-items: flex-start;
    gap: 16px;
  }

  .filters {
    width: 100%;
    overflow-x: auto;
    padding-bottom: 8px;
    white-space: nowrap;
    gap: 12px;
  }

  .media-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
    gap: 12px;
  }
}
</style>
