<template>
  <div class="downloads-view">
    <div class="content-header">
      <div class="content-heading">
        <h1>Downloads</h1>
        <p class="content-subtitle">Files detected in the downloads folder, guessed against TMDB</p>
      </div>
      <button class="glass-button primary" @click="fetchFiles">
        <RefreshCw :size="16" :class="{ spinning: isLoading }" />
        Refresh
      </button>
    </div>

    <div class="stats-bento">
      <div class="stat-card glass-panel">
        <span class="stat-label label-caps">Total</span>
        <span class="stat-value" style="color: var(--primary);">{{ files.length }}</span>
      </div>
      <div class="stat-card glass-panel">
        <span class="stat-label label-caps">Awaiting Decision</span>
        <span class="stat-value" style="color: var(--secondary);">{{ awaitingCount }}</span>
      </div>
      <div class="stat-card glass-panel">
        <span class="stat-label label-caps">Moved</span>
        <span class="stat-value" style="color: var(--success);">{{ movedCount }}</span>
      </div>
      <div class="stat-card glass-panel">
        <span class="stat-label label-caps">Errors</span>
        <span class="stat-value" style="color: var(--error);">{{ errorCount }}</span>
      </div>
    </div>

    <div class="filters">
      <button
        v-for="opt in statusOptions"
        :key="opt.value"
        class="glass-button"
        :class="{ active: statusFilter === opt.value }"
        @click="statusFilter = opt.value; fetchFiles()"
      >
        {{ opt.label }}
      </button>
    </div>

    <div class="glass-panel batch-bar">
      <div class="batch-bar-selection">
        <button class="glass-button" @click="selectAllPending">Select all pending</button>
        <button class="glass-button" :disabled="selectedIds.size === 0" @click="clearSelection">Clear selection</button>
        <span class="selection-count">{{ selectedIds.size }} selected</span>
      </div>
      <div class="batch-bar-actions">
        <button
          class="glass-button success"
          :disabled="selectedIds.size === 0 || isBatchActing"
          @click="confirmSelected"
        >
          <Check :size="16" />
          Confirm selected
        </button>
        <button
          class="glass-button"
          :disabled="selectedIds.size === 0 || isBatchActing"
          @click="openBatchCorrect"
        >
          <Edit3 :size="16" />
          Correct selected
        </button>
      </div>
    </div>

    <div v-if="isLoading && files.length === 0" class="empty-state">Loading&hellip;</div>
    <div v-else-if="files.length === 0" class="empty-state">No watched files for this filter.</div>
    <div v-else class="task-list">
      <div
        v-for="row in files"
        :key="row.id"
        class="glass-panel task-card"
        :class="{ dimmed: row.status === 'removed' }"
      >
        <div class="task-info">
          <div class="task-selectblock">
            <input
              type="checkbox"
              :disabled="row.status === 'removed' || row.status === 'moved'"
              :checked="selectedIds.has(row.id)"
              @change="toggleSelect(row.id)"
            />
            <div class="task-titleblock">
              <h4>{{ row.filename }}</h4>
              <span class="task-direction label-caps">{{ guessLabel(row) }}</span>
            </div>
          </div>
          <div class="task-badges">
            <span class="badge confidence" :class="confidenceClass(row.confidence)">
              {{ Math.round(row.confidence * 100) }}%
            </span>
            <span class="badge" :class="row.status">{{ row.status }}</span>
          </div>
        </div>

        <div class="task-row-actions">
          <button
            class="glass-button success btn-sm"
            :disabled="!row.guess_tmdb_id || isBatchActing"
            title="Confirm using this row's current guess"
            @click="confirmRow(row)"
          >
            <Check :size="14" />
            Confirm
          </button>
          <button class="glass-button btn-sm" :disabled="isBatchActing" @click="openSingleCorrect(row)">
            <Edit3 :size="14" />
            Correct
          </button>
        </div>

        <div class="task-meta">
          <span>{{ row.path }}</span>
          <span>{{ formatSize(row.filesize) }}</span>
        </div>

        <div v-if="row.status === 'removed'" class="removed-msg">
          File was removed from the downloads folder before it was actioned.
        </div>
        <div v-if="row.status === 'moved' && row.moved_path" class="moved-msg">
          Moved to {{ row.moved_path }}
        </div>
        <div v-if="row.status === 'error' && row.error_message" class="error-msg">
          {{ row.error_message }}
        </div>
      </div>
    </div>

    <!-- TMDB correction modal, shared for single-row and batch correct -->
    <div v-if="correctModal.open" class="modal-overlay">
      <div class="glass-panel modal-panel">
        <div class="modal-header">
          <div>
            <h3>Correct identity</h3>
            <p class="modal-subtitle">
              {{ correctModal.mode === 'batch'
                ? `Applying to ${selectedIds.size} selected file(s)`
                : correctModal.targetRow?.filename }}
            </p>
          </div>
          <button class="glass-button icon-only" @click="closeCorrectModal">✕</button>
        </div>

        <template v-if="correctModal.step === 'search'">
          <div class="search-row">
            <input
              v-model="searchQueryTMDB"
              type="text"
              placeholder="Name or TMDB ID..."
              class="search-input"
              @keyup.enter="searchTMDB"
            />
            <button class="glass-button primary" @click="searchTMDB">Search</button>
          </div>

          <div class="search-results">
            <div v-if="isSearchingTMDB" class="search-placeholder">Searching TMDB...</div>
            <div v-else-if="searchResultsTMDB.length === 0 && searchQueryTMDB" class="search-placeholder">
              No results found.
            </div>
            <div v-else-if="searchResultsTMDB.length === 0" class="search-placeholder">
              Type a title above and press Search.
            </div>

            <div v-else v-for="result in searchResultsTMDB" :key="result.id" class="result-card">
              <img v-if="result.poster_path" :src="'https://image.tmdb.org/t/p/w92' + result.poster_path" class="result-poster" />
              <div v-else class="result-poster result-poster-placeholder">🎬</div>

              <div class="result-info">
                <div class="result-title-row">
                  <strong>{{ result.title }}</strong>
                  <span class="result-year">{{ result.year }}</span>
                  <span class="result-type">{{ result.media_type === 'movie' ? 'Movie' : 'Series' }}</span>
                </div>
                <p class="result-overview">{{ result.overview }}</p>
              </div>

              <button class="glass-button btn-sm" @click="selectResult(result)">Select</button>
            </div>
          </div>
        </template>

        <template v-else-if="correctModal.step === 'episode-select'">
          <div class="episode-select">
            <p class="modal-subtitle">
              Linking to series: <strong>{{ correctModal.pendingTitle }}</strong>
            </p>
            <p class="episode-hint">Use season 0 for specials and OVAs.</p>
            <div class="episode-fields">
              <div class="field">
                <label>Season</label>
                <input type="number" v-model.number="correctModal.season" min="0" />
              </div>
              <div class="field">
                <label>Episode <span class="field-hint">(empty = season pack)</span></label>
                <input type="number" v-model.number="correctModal.episode" min="1" placeholder="—" />
              </div>
            </div>
          </div>
          <div class="modal-footer">
            <button class="glass-button" @click="correctModal.step = 'search'">&larr; Back</button>
            <button class="glass-button success" :disabled="correctModal.saving" @click="applyCorrection">
              {{ correctModal.saving ? 'Saving…' : 'Confirm' }}
            </button>
          </div>
        </template>

        <div v-if="correctModal.step === 'search'" class="modal-footer">
          <button class="glass-button" @click="closeCorrectModal">Close</button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue';
import { RefreshCw, Check, Edit3 } from 'lucide-vue-next';
import { backendUrl } from '../config';
import {
  listWatchedFiles,
  confirmWatchedFile,
  correctWatchedFile,
  type WatchedFile,
  type WatchedFileStatus
} from '../api/backend';

interface TMDBSearchResult {
  id: number;
  title: string;
  year?: number;
  media_type: 'movie' | 'tv';
  poster_path?: string;
  overview?: string;
}

const files = ref<WatchedFile[]>([]);
const isLoading = ref(false);
const statusFilter = ref<WatchedFileStatus | ''>('');
const selectedIds = ref<Set<number>>(new Set());
const isBatchActing = ref(false);
let pollInterval: ReturnType<typeof setInterval> | null = null;

const statusOptions: { value: WatchedFileStatus | ''; label: string }[] = [
  { value: '', label: 'All' },
  { value: 'pending', label: 'Pending' },
  { value: 'notified', label: 'Notified' },
  { value: 'confirmed', label: 'Confirmed' },
  { value: 'corrected', label: 'Corrected' },
  { value: 'moved', label: 'Moved' },
  { value: 'removed', label: 'Removed' },
  { value: 'error', label: 'Error' }
];

const awaitingCount = computed(
  () => files.value.filter(f => f.status === 'pending' || f.status === 'notified').length
);
const movedCount = computed(() => files.value.filter(f => f.status === 'moved').length);
const errorCount = computed(() => files.value.filter(f => f.status === 'error').length);

const pad = (n: number) => n.toString().padStart(2, '0');

const guessLabel = (row: WatchedFile) => {
  const title = row.guess_title || 'Unrecognized';
  if (row.guess_season == null) return title;
  if (row.guess_episode == null) return `${title} S${pad(row.guess_season)} (season pack)`;
  return `${title} S${pad(row.guess_season)}E${pad(row.guess_episode)}`;
};

const confidenceClass = (confidence: number) => {
  if (confidence >= 0.8) return 'high';
  if (confidence >= 0.5) return 'medium';
  return 'low';
};

const formatSize = (bytes: number) => {
  if (bytes < 1024) return `${bytes} B`;
  const units = ['KB', 'MB', 'GB', 'TB'];
  let value = bytes;
  let unitIndex = -1;
  do {
    value /= 1024;
    unitIndex++;
  } while (value >= 1024 && unitIndex < units.length - 1);
  return `${value.toFixed(1)} ${units[unitIndex]}`;
};

const fetchFiles = async () => {
  isLoading.value = true;
  try {
    files.value = await listWatchedFiles(statusFilter.value || undefined);
  } catch (error) {
    console.error('Error fetching watched files:', error);
  } finally {
    isLoading.value = false;
  }
};

const toggleSelect = (id: number) => {
  if (selectedIds.value.has(id)) {
    selectedIds.value.delete(id);
  } else {
    selectedIds.value.add(id);
  }
  selectedIds.value = new Set(selectedIds.value);
};

const selectAllPending = () => {
  const pending = files.value.filter(f => f.status === 'pending' || f.status === 'notified');
  selectedIds.value = new Set(pending.map(f => f.id));
};

const clearSelection = () => {
  selectedIds.value = new Set();
};

const confirmRow = async (row: WatchedFile) => {
  if (!row.guess_tmdb_id) return;
  try {
    await confirmWatchedFile(row.id, row.guess_tmdb_id, row.guess_season, row.guess_episode);
    await fetchFiles();
  } catch (err) {
    console.error(err);
    alert(`Failed to confirm ${row.filename}.`);
  }
};

const confirmSelected = async () => {
  isBatchActing.value = true;
  const targets = files.value.filter(f => selectedIds.value.has(f.id));
  const skipped: string[] = [];
  const failed: string[] = [];

  try {
    for (const row of targets) {
      if (!row.guess_tmdb_id) {
        skipped.push(row.filename);
        continue;
      }
      try {
        await confirmWatchedFile(row.id, row.guess_tmdb_id, row.guess_season, row.guess_episode);
      } catch (err) {
        console.error(err);
        failed.push(row.filename);
      }
    }
  } finally {
    isBatchActing.value = false;
    clearSelection();
    await fetchFiles();
  }

  if (skipped.length || failed.length) {
    const parts: string[] = [];
    if (skipped.length) parts.push(`No TMDB guess, skipped: ${skipped.join(', ')}`);
    if (failed.length) parts.push(`Failed to confirm: ${failed.join(', ')}`);
    alert(parts.join('\n'));
  }
};

interface CorrectModalState {
  open: boolean;
  mode: 'single' | 'batch';
  step: 'search' | 'episode-select';
  targetRow: WatchedFile | null;
  pendingTmdbId: number;
  pendingTitle: string;
  season: number;
  episode: number | null;
  saving: boolean;
}

const correctModal = ref<CorrectModalState>({
  open: false,
  mode: 'single',
  step: 'search',
  targetRow: null,
  pendingTmdbId: 0,
  pendingTitle: '',
  season: 1,
  episode: null,
  saving: false
});

const searchQueryTMDB = ref('');
const searchResultsTMDB = ref<TMDBSearchResult[]>([]);
const isSearchingTMDB = ref(false);

const openSingleCorrect = (row: WatchedFile) => {
  correctModal.value = {
    open: true,
    mode: 'single',
    step: 'search',
    targetRow: row,
    pendingTmdbId: 0,
    pendingTitle: '',
    season: row.guess_season ?? 1,
    episode: row.guess_episode ?? null,
    saving: false
  };
  searchQueryTMDB.value = row.guess_title || row.filename;
  searchResultsTMDB.value = [];
};

const openBatchCorrect = () => {
  correctModal.value = {
    open: true,
    mode: 'batch',
    step: 'search',
    targetRow: null,
    pendingTmdbId: 0,
    pendingTitle: '',
    season: 1,
    episode: null,
    saving: false
  };
  searchQueryTMDB.value = '';
  searchResultsTMDB.value = [];
};

const closeCorrectModal = () => {
  correctModal.value.open = false;
};

const searchTMDB = async () => {
  if (!searchQueryTMDB.value.trim()) return;
  isSearchingTMDB.value = true;
  searchResultsTMDB.value = [];
  try {
    const res = await fetch(
      `${backendUrl}/tmdb/search?query=${encodeURIComponent(searchQueryTMDB.value.trim())}&media_type=multi`
    );
    if (res.ok) {
      searchResultsTMDB.value = await res.json();
    }
  } catch (err) {
    console.error(err);
  } finally {
    isSearchingTMDB.value = false;
  }
};

const selectResult = async (result: TMDBSearchResult) => {
  if (correctModal.value.mode === 'batch') {
    await applyBatchCorrection(result);
    return;
  }

  if (result.media_type === 'tv') {
    correctModal.value.step = 'episode-select';
    correctModal.value.pendingTmdbId = result.id;
    correctModal.value.pendingTitle = result.title;
  } else {
    correctModal.value.pendingTmdbId = result.id;
    correctModal.value.pendingTitle = result.title;
    await applyCorrection();
  }
};

const applyCorrection = async () => {
  const row = correctModal.value.targetRow;
  if (!row) return;
  correctModal.value.saving = true;
  try {
    await correctWatchedFile(row.id, correctModal.value.pendingTmdbId, correctModal.value.season, correctModal.value.episode);
    correctModal.value.open = false;
    await fetchFiles();
  } catch (err) {
    console.error(err);
    alert(`Failed to correct ${row.filename}.`);
  } finally {
    correctModal.value.saving = false;
  }
};

const applyBatchCorrection = async (result: TMDBSearchResult) => {
  isBatchActing.value = true;
  correctModal.value.open = false;
  const targets = files.value.filter(f => selectedIds.value.has(f.id));
  const failed: string[] = [];

  try {
    for (const row of targets) {
      try {
        await correctWatchedFile(row.id, result.id, row.guess_season, row.guess_episode);
      } catch (err) {
        console.error(err);
        failed.push(row.filename);
      }
    }
  } finally {
    isBatchActing.value = false;
    clearSelection();
    await fetchFiles();
  }

  if (failed.length) {
    alert(`Failed to correct: ${failed.join(', ')}`);
  }
};

onMounted(() => {
  fetchFiles();
  pollInterval = setInterval(fetchFiles, 5000);
});

onUnmounted(() => {
  if (pollInterval) clearInterval(pollInterval);
});
</script>

<style scoped>
.downloads-view {
  display: flex;
  flex-direction: column;
}

.stats-bento {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
  gap: var(--gutter);
  margin-bottom: var(--sp-md);
}

.stat-card {
  padding: var(--sp-md);
  border-radius: var(--r-xl);
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.stat-label {
  color: var(--on-surface-variant);
  opacity: 0.7;
}

.stat-value {
  font-size: 2.25rem;
  font-weight: 800;
  line-height: 1;
}

.filters {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
  margin-bottom: var(--sp-sm);
}

.batch-bar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  flex-wrap: wrap;
  gap: 12px;
  padding: 14px 18px;
  margin-bottom: var(--sp-md);
}

.batch-bar-selection,
.batch-bar-actions {
  display: flex;
  align-items: center;
  gap: 10px;
  flex-wrap: wrap;
}

.selection-count {
  font-size: 0.85rem;
  color: var(--on-surface-variant);
}

.task-list {
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding-bottom: 24px;
}

.task-card {
  padding: 16px;
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.task-card.dimmed {
  opacity: 0.5;
}

.task-info {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 12px;
}

.task-selectblock {
  display: flex;
  align-items: flex-start;
  gap: 12px;
  min-width: 0;
}

.task-selectblock input[type="checkbox"] {
  margin-top: 4px;
  width: 16px;
  height: 16px;
  flex-shrink: 0;
  accent-color: var(--primary);
}

.task-titleblock {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.task-titleblock h4 {
  margin: 0;
  font-size: 1rem;
  color: var(--text-primary);
  word-break: break-all;
}

.task-direction {
  color: var(--on-surface-variant);
  opacity: 0.7;
  font-size: 11px;
}

.task-badges {
  display: flex;
  gap: 8px;
  flex-shrink: 0;
}

.task-row-actions {
  display: flex;
  gap: 8px;
}

.glass-button.btn-sm {
  padding: 5px 10px;
  font-size: 0.78rem;
}

.glass-button.success {
  background: rgba(16, 185, 129, 0.15);
  border-color: rgba(16, 185, 129, 0.35);
  color: #a7f3d0;
}

.glass-button.success:hover:not(:disabled) {
  filter: brightness(1.15);
}

.glass-button:disabled {
  opacity: 0.45;
  cursor: not-allowed;
  transform: none !important;
}

.glass-button.icon-only {
  padding: 0;
  border-radius: 50%;
  width: 28px;
  height: 28px;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 14px;
  flex-shrink: 0;
}

.task-meta {
  display: flex;
  justify-content: space-between;
  gap: 12px;
  font-size: 0.75rem;
  color: var(--text-secondary);
}

.task-meta span:first-child {
  word-break: break-all;
}

.badge {
  padding: 4px 10px;
  border-radius: var(--r-full);
  font-family: 'Geist', 'Inter', sans-serif;
  font-size: 0.7rem;
  font-weight: 600;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  white-space: nowrap;
}

.badge.pending { background: rgba(245, 191, 0, 0.15); color: var(--warning); border: 1px solid rgba(245, 191, 0, 0.25); }
.badge.notified { background: rgba(214, 186, 255, 0.15); color: var(--secondary); border: 1px solid rgba(214, 186, 255, 0.25); }
.badge.confirmed, .badge.corrected { background: rgba(34, 197, 94, 0.12); color: var(--success); border: 1px solid rgba(34, 197, 94, 0.25); }
.badge.moved { background: rgba(34, 197, 94, 0.2); color: var(--success); border: 1px solid rgba(34, 197, 94, 0.35); }
.badge.removed { background: rgba(255, 255, 255, 0.06); color: var(--on-surface-variant); border: 1px solid var(--glass-border); }
.badge.error { background: rgba(255, 180, 171, 0.12); color: var(--error); border: 1px solid rgba(255, 180, 171, 0.25); }

.badge.confidence.high { background: rgba(34, 197, 94, 0.12); color: var(--success); border: 1px solid rgba(34, 197, 94, 0.25); }
.badge.confidence.medium { background: rgba(245, 191, 0, 0.15); color: var(--warning); border: 1px solid rgba(245, 191, 0, 0.25); }
.badge.confidence.low { background: rgba(255, 180, 171, 0.12); color: var(--error); border: 1px solid rgba(255, 180, 171, 0.25); }

.removed-msg {
  font-size: 0.8rem;
  color: var(--on-surface-variant);
  background: rgba(255, 255, 255, 0.03);
  padding: 8px;
  border-radius: 4px;
}

.moved-msg {
  font-size: 0.8rem;
  color: var(--success);
  background: rgba(34, 197, 94, 0.08);
  padding: 8px;
  border-radius: 4px;
}

.error-msg {
  font-size: 0.8rem;
  color: var(--error);
  background: rgba(239, 68, 68, 0.1);
  padding: 8px;
  border-radius: 4px;
}

.empty-state {
  padding: 32px;
  text-align: center;
  color: var(--text-secondary);
  background: rgba(0, 0, 0, 0.2);
  border-radius: 12px;
  border: 1px dashed var(--glass-border);
}

.spinning {
  animation: spin 1s linear infinite;
}

.modal-overlay {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background: rgba(0, 0, 0, 0.8);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
  backdrop-filter: blur(8px);
  padding: 1rem;
}

.modal-panel {
  width: 100%;
  max-width: 600px;
  max-height: 85vh;
  display: flex;
  flex-direction: column;
  gap: 1rem;
  padding: 1.5rem;
  background: rgba(15, 23, 42, 0.95);
  border: 1px solid rgba(214, 186, 255, 0.2);
  overflow: hidden;
  box-shadow: 0 20px 25px -5px rgba(0, 0, 0, 0.5);
}

.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  border-bottom: 1px solid rgba(255, 255, 255, 0.08);
  padding-bottom: 0.75rem;
}

.modal-header h3 {
  margin: 0 0 0.25rem 0;
  font-size: 1.2rem;
  color: #fff;
}

.modal-subtitle {
  margin: 0;
  font-size: 0.85rem;
  color: var(--secondary);
}

.search-row {
  display: flex;
  gap: 0.5rem;
}

.search-input {
  flex-grow: 1;
  padding: 10px 14px;
  border-radius: 8px;
  background: rgba(255, 255, 255, 0.05);
  border: 1px solid rgba(255, 255, 255, 0.12);
  color: #fff;
  font-size: 0.95rem;
}

.search-results {
  flex-grow: 1;
  overflow-y: auto;
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  padding-right: 0.25rem;
  min-height: 200px;
}

.search-placeholder {
  text-align: center;
  padding: 2rem;
  color: #a1a1aa;
}

.result-card {
  display: flex;
  gap: 1rem;
  padding: 0.75rem;
  background: rgba(255, 255, 255, 0.03);
  border: 1px solid rgba(255, 255, 255, 0.05);
  border-radius: 10px;
  transition: background 0.2s;
}

.result-poster {
  width: 50px;
  height: 75px;
  object-fit: cover;
  border-radius: 6px;
  flex-shrink: 0;
}

.result-poster-placeholder {
  background: rgba(255, 255, 255, 0.05);
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 1.5rem;
  color: #4b5563;
}

.result-info {
  flex-grow: 1;
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  min-width: 0;
}

.result-title-row {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex-wrap: wrap;
}

.result-title-row strong {
  color: #fff;
  text-overflow: ellipsis;
  overflow: hidden;
  white-space: nowrap;
  max-width: 250px;
}

.result-year {
  font-size: 0.75rem;
  background: rgba(255, 255, 255, 0.08);
  padding: 2px 6px;
  border-radius: 4px;
  color: #d1d5db;
}

.result-type {
  font-size: 0.7rem;
  background: rgba(214, 186, 255, 0.14);
  color: var(--secondary);
  padding: 2px 6px;
  border-radius: 4px;
  font-weight: 600;
  text-transform: uppercase;
}

.result-overview {
  margin: 0;
  font-size: 0.8rem;
  color: #a1a1aa;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
}

.episode-select {
  display: flex;
  flex-direction: column;
  gap: 1rem;
  flex-grow: 1;
}

.episode-hint {
  margin: 0;
  font-size: 0.8rem;
  color: #a1a1aa;
}

.episode-fields {
  display: flex;
  gap: 1rem;
  flex-wrap: wrap;
}

.field {
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
  flex: 1;
  min-width: 120px;
}

.field label {
  font-size: 0.8rem;
  color: #a1a1aa;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

.field-hint {
  font-weight: 400;
  text-transform: none;
}

.field input {
  padding: 10px 14px;
  border-radius: 8px;
  background: rgba(255, 255, 255, 0.05);
  border: 1px solid rgba(255, 255, 255, 0.12);
  color: #fff;
  font-size: 1rem;
  width: 100%;
  box-sizing: border-box;
}

.modal-footer {
  display: flex;
  justify-content: flex-end;
  gap: 0.5rem;
  border-top: 1px solid rgba(255, 255, 255, 0.08);
  padding-top: 0.75rem;
}

@media (max-width: 768px) {
  .task-info {
    flex-direction: column;
  }
}
</style>
