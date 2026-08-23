import { ref } from 'vue';
import { backendUrl } from '../config';

export interface TelegramFile {
  id: number;
  message_id: number;
  filename: string;
  filesize: number;
  mime_type: string;
  created_at: string;
}

export interface TelegramCollection {
  id: number;
  name: string;
  quality: string;
  technical_metadata?: string | null;
  local_path?: string | null;
  files: TelegramFile[];
}

export interface BackendMovie {
  id: number;
  tmdb_id: number;
  title: string;
  poster_path: string;
  release_year: number;
  overview: string;
  collections: TelegramCollection[];
}

export interface BackendEpisode {
  id: number;
  episode_number: number;
  title: string;
  collections: TelegramCollection[];
}

export interface BackendSeason {
  id: number;
  season_number: number;
  episodes: BackendEpisode[];
  collections: TelegramCollection[];
}

export interface BackendSeries {
  id: number;
  tmdb_id: number;
  manual_title: string;
  poster_path?: string;
  overview?: string;
  release_year?: number;
  seasons: BackendSeason[];
}

export type WatchedFileStatus =
  | 'pending'
  | 'notified'
  | 'confirmed'
  | 'corrected'
  | 'moved'
  | 'removed'
  | 'error';

export interface WatchedFile {
  id: number;
  path: string;
  filename: string;
  filesize: number;
  first_seen_at: string;
  guess_media_type?: 'movie' | 'tv' | null;
  guess_tmdb_id?: number | null;
  guess_title?: string | null;
  guess_year?: number | null;
  guess_season?: number | null;
  guess_episode?: number | null;
  confidence: number;
  guess_source?: string | null;
  status: WatchedFileStatus;
  notified_at?: string | null;
  moved_path?: string | null;
  error_message?: string | null;
}

export interface WatchedFileResolution {
  id: number;
  path: string;
  filename: string;
  tmdb_id: number;
  media_type: string;
  title: string;
  year?: number | null;
  season?: number | null;
  episode?: number | null;
  status: string;
}

export async function listWatchedFiles(status?: WatchedFileStatus): Promise<WatchedFile[]> {
  const url = status ? `${backendUrl}/watch?status=${status}` : `${backendUrl}/watch`;
  const res = await fetch(url);
  if (!res.ok) {
    throw new Error(`Failed to list watched files: ${res.statusText}`);
  }
  return res.json();
}

export async function confirmWatchedFile(
  id: number,
  tmdbId: number,
  season?: number | null,
  episode?: number | null
): Promise<WatchedFileResolution> {
  const res = await fetch(`${backendUrl}/watch/${id}/confirm`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ tmdb_id: tmdbId, season, episode })
  });
  if (!res.ok) {
    throw new Error(`Failed to confirm watched file ${id}: ${res.statusText}`);
  }
  return res.json();
}

export async function reidentifyWatchedFiles(): Promise<WatchedFile[]> {
  const res = await fetch(`${backendUrl}/watch/reidentify`, { method: 'POST' });
  if (!res.ok) {
    throw new Error(`Failed to re-identify watched files: ${res.statusText}`);
  }
  return res.json();
}

export async function correctWatchedFile(
  id: number,
  tmdbId: number,
  season?: number | null,
  episode?: number | null
): Promise<WatchedFileResolution> {
  const res = await fetch(`${backendUrl}/watch/${id}/correct`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ tmdb_id: tmdbId, season, episode })
  });
  if (!res.ok) {
    throw new Error(`Failed to correct watched file ${id}: ${res.statusText}`);
  }
  return res.json();
}

export function useBackend() {
  const telegramMovies = ref<BackendMovie[]>([]);
  const telegramSeries = ref<BackendSeries[]>([]);
  const loading = ref(false);
  const error = ref<string | null>(null);

  const fetchTelegramMovies = async () => {
    loading.value = true;
    error.value = null;
    try {
      const res = await fetch(`${backendUrl}/movies`);
      if (!res.ok) {
        throw new Error(`Error: ${res.statusText}`);
      }
      telegramMovies.value = await res.json();
    } catch (e: any) {
      console.error(e);
      error.value = e.message || 'Failed to fetch movies from backend';
    } finally {
      loading.value = false;
    }
  };

  const fetchTelegramSeries = async () => {
    loading.value = true;
    error.value = null;
    try {
      const res = await fetch(`${backendUrl}/series`);
      if (!res.ok) {
        throw new Error(`Error: ${res.statusText}`);
      }
      telegramSeries.value = await res.json();
    } catch (e: any) {
      console.error(e);
      error.value = e.message || 'Failed to fetch series from backend';
    } finally {
      loading.value = false;
    }
  };

  return {
    telegramMovies,
    telegramSeries,
    loading,
    error,
    fetchTelegramMovies,
    fetchTelegramSeries
  };
}
