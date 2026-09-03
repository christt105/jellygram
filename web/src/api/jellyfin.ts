import { Jellyfin } from '@jellyfin/sdk';
import { getItemsApi } from '@jellyfin/sdk/lib/utils/api/items-api';
import { getSystemApi } from '@jellyfin/sdk/lib/utils/api/system-api';
import { getUserApi } from '@jellyfin/sdk/lib/utils/api/user-api';
import { BaseItemKind, ItemFields } from '@jellyfin/sdk/lib/generated-client/models';
import { ref } from 'vue';
import { jellyfinUrl as serverUrl, jellyfinToken as token } from '../config';

const jellyfin = new Jellyfin({
    clientInfo: { name: 'Cinegram', version: '2.0.0' },
    deviceInfo: { name: 'Browser', id: 'cinegram-web' }
});

export const api = jellyfin.createApi(serverUrl);
if (token) {
    api.accessToken = token;
}

export const itemsApi = getItemsApi(api);
export const systemApi = getSystemApi(api);
export const userApi = getUserApi(api);

export type MediaItem = {
    Id: string;
    Name: string;
    Type: string;
    DateCreated?: string;
    ProductionYear?: number;
    Overview?: string;
    ImageTags?: { Primary?: string };
};

export const getImageUrl = (itemId: string, tag?: string, maxWidth: number = 400) => {
    if (!tag) return '';
    return `${serverUrl}/Items/${itemId}/Images/Primary?tag=${tag}&maxWidth=${maxWidth}`;
};

// Jellyfin fills in metadata for every episode/movie a provider knows about, even when
// no video file was ever downloaded. Those placeholders have LocationType 'Virtual'
// instead of 'FileSystem', which is how we tell a real, uploadable file apart from one.
export const hasVideoFile = (item: any): boolean => item?.LocationType !== 'Virtual';

export interface Media {
  id: string;
  title: string;
  year: number;
  type: 'movie' | 'series';
  synopsis: string;
  coverUrl: string;
  resolutions: string[];
  tags: string[];
  tmdbId?: number | null;
  path?: string;
  dateCreated?: string;
  rating?: number;
}

export function useJellyfin() {
    const items = ref<Media[]>([]);
    const loading = ref(false);
    const error = ref<string | null>(null);

    const fetchItems = async () => {
        if (!token) {
            error.value = 'No API token configured in .env';
            return;
        }

        loading.value = true;
        error.value = null;

        try {
            const usersRes = await userApi.getUsers();
            if (!usersRes.data || usersRes.data.length === 0) {
                throw new Error("No users found.");
            }

            const adminUser = usersRes.data.find(u => u.Policy?.IsAdministrator) || usersRes.data[0];
            const currentUserId = adminUser.Id as string;

            const res = await itemsApi.getItems({
                userId: currentUserId,
                recursive: true,
                includeItemTypes: [BaseItemKind.Movie, BaseItemKind.Series] as BaseItemKind[],
                fields: [ItemFields.Overview, ItemFields.Tags, ItemFields.MediaSources, ItemFields.ProviderIds, ItemFields.Path, ItemFields.DateCreated] as ItemFields[]
            });

            const jellyfinItems = (res.data.Items || []).filter(hasVideoFile) as any[];
            
            items.value = jellyfinItems.map(item => {
                // Determine resolutions if available
                const resolutions: string[] = [];
                if (item.MediaSources && item.MediaSources.length > 0) {
                    item.MediaSources.forEach((ms: any) => {
                        if (ms.MediaStreams) {
                            const videoStream = ms.MediaStreams.find((s: any) => s.Type === 'Video');
                            if (videoStream) {
                                const w = videoStream.Width || 0;
                                const h = videoStream.Height || 0;
                                // Use width primarily because widescreen movies have smaller heights (e.g. 1920x800 for 1080p)
                                if (w >= 3800 || h >= 2100) resolutions.push('4K');
                                else if (w >= 1900 || h >= 1000) resolutions.push('1080p');
                                else if (w >= 1200 || h >= 700) resolutions.push('720p');
                                else resolutions.push('SD');
                            }
                        }
                    });
                }
                
                // Fallback resolution if none found
                if (resolutions.length === 0) resolutions.push('1080p');

                return {
                    id: item.Id as string,
                    title: item.Name as string,
                    year: item.ProductionYear || 0,
                    type: item.Type?.toLowerCase() === 'movie' ? 'movie' : 'series',
                    synopsis: item.Overview || 'No synopsis available.',
                    coverUrl: getImageUrl(item.Id, item.ImageTags?.Primary),
                    resolutions: [...new Set(resolutions)], // Unique resolutions
                    tags: item.Tags || [],
                    tmdbId: item.ProviderIds?.Tmdb ? parseInt(item.ProviderIds.Tmdb) : null,
                    path: item.Path as string,
                    dateCreated: item.DateCreated as string,
                    rating: item.CommunityRating || 0
                };
            });
        } catch (e: any) {
            console.error(e);
            error.value = e.message || 'Failed to fetch items from Jellyfin';
        } finally {
            loading.value = false;
        }
    };

    return {
        items,
        loading,
        error,
        fetchItems
    };
}

export const fetchItemDetails = async (id: string, type: 'movie' | 'series') => {
    if (!token) throw new Error('No API token configured');
    const usersRes = await userApi.getUsers();
    if (!usersRes.data || usersRes.data.length === 0) throw new Error("No users found.");
    const adminUser = usersRes.data.find(u => u.Policy?.IsAdministrator) || usersRes.data[0];
    const currentUserId = adminUser.Id as string;

    const res = await fetch(`${serverUrl}/Users/${currentUserId}/Items/${id}?Fields=Path,Overview,ProviderIds,MediaSources`, {
        headers: { 'Authorization': `MediaBrowser Token="${token}"` }
    });
    const item = await res.json();

    if (type === 'series') {
        const seasonsRes = await fetch(`${serverUrl}/Shows/${id}/Seasons?userId=${currentUserId}`, {
            headers: { 'Authorization': `MediaBrowser Token="${token}"` }
        });
        const seasonsData = await seasonsRes.json();
        item.seasons = seasonsData.Items || [];

        for (const s of item.seasons) {
            const epsRes = await fetch(`${serverUrl}/Shows/${id}/Episodes?seasonId=${s.Id}&userId=${currentUserId}&Fields=Path,Overview,MediaSources`, {
                headers: { 'Authorization': `MediaBrowser Token="${token}"` }
            });
            const epsData = await epsRes.json();
            s.episodes = (epsData.Items || []).filter(hasVideoFile);
        }
        // Seasons left with no real episode file are metadata-only placeholders.
        item.seasons = item.seasons.filter((s: any) => s.episodes.length > 0);
    }

    return item;
};

export const findJellyfinItemByTmdbId = async (tmdbId: number, type: 'movie' | 'series') => {
    if (!token) return null;
    try {
        const usersRes = await userApi.getUsers();
        if (!usersRes.data || usersRes.data.length === 0) return null;
        const adminUser = usersRes.data.find(u => u.Policy?.IsAdministrator) || usersRes.data[0];
        const currentUserId = adminUser.Id as string;

        const itemType = type === 'movie' ? BaseItemKind.Movie : BaseItemKind.Series;
        
        const res = await itemsApi.getItems({
            userId: currentUserId,
            recursive: true,
            includeItemTypes: [itemType] as BaseItemKind[],
            fields: [ItemFields.ProviderIds] as ItemFields[]
        });
        
        const matching = (res.data.Items || []).find((item: any) => {
            const itemTmdb = item.ProviderIds?.Tmdb ? parseInt(item.ProviderIds.Tmdb) : null;
            return itemTmdb === tmdbId;
        });
        
        return matching ? (matching.Id as string) : null;
    } catch (e) {
        console.error("Error finding Jellyfin item by TMDB ID:", e);
        return null;
    }
};
