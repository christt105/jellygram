import { createRouter, createWebHistory } from 'vue-router'
import MediaView from '../views/MediaView.vue'
import SettingsView from '../views/SettingsView.vue'
import QueueView from '../views/QueueView.vue'

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    {
      path: '/',
      redirect: '/movies'
    },
    {
      path: '/movies',
      name: 'movies',
      component: MediaView,
      props: { type: 'movies' }
    },
    {
      path: '/series',
      name: 'series',
      component: MediaView,
      props: { type: 'series' }
    },
    {
      path: '/telegram',
      name: 'telegram',
      component: MediaView,
      props: { type: 'telegram' }
    },
    {
      path: '/item/:type/:id',
      name: 'item-detail',
      component: () => import('../views/ItemDetailView.vue'),
      props: true
    },
    {
      path: '/jellyfin/:type/:id',
      name: 'jellyfin-detail',
      component: () => import('../views/JellyfinItemDetailView.vue'),
      props: true
    },
    {
      path: '/queue',
      name: 'queue',
      component: QueueView
    },
    {
      path: '/downloads',
      name: 'downloads',
      component: () => import('../views/DownloadsView.vue')
    },
    {
      path: '/settings',
      name: 'settings',
      component: SettingsView
    }
  ]
})

export default router
