<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import Avatar from 'primevue/avatar'
import Button from 'primevue/button'
import ConfirmDialog from 'primevue/confirmdialog'
import InputText from 'primevue/inputtext'
import Menu from 'primevue/menu'
import Toast from 'primevue/toast'
import { useAuthStore } from '../stores/auth'
import { useUiStore } from '../stores/ui'
import ForumSidebar from './ForumSidebar.vue'

const auth = useAuthStore()
const ui = useUiStore()
const router = useRouter()

const search = ref('')

const initials = computed(() => {
  const name = auth.user?.displayName ?? auth.user?.userName ?? '?'

  return name
    .split(/\s+/)
    .map((part) => part[0])
    .join('')
    .slice(0, 2)
    .toUpperCase()
})

const userMenu = computed(() => [
  {
    items: [
      { label: 'Profile', icon: 'pi pi-user', command: () => router.push('/profile') },
      ...(auth.isAdmin
        ? [
            {
              label: 'User management',
              icon: 'pi pi-users',
              command: () => router.push('/admin/users'),
            },
            {
              label: 'Forum management',
              icon: 'pi pi-inbox',
              command: () => router.push('/admin/forums'),
            },
          ]
        : []),
      { separator: true },
      { label: 'Sign out', icon: 'pi pi-sign-out', command: signOut },
    ],
  },
])

async function signOut() {
  await auth.logout()
  router.push({ path: '/login' })
}

function submitSearch() {
  const q = search.value.trim()

  router.push(q ? { path: '/tasks', query: { q } } : { path: '/tasks' })
}
</script>

<template>
  <div class="shell">
    <header class="topbar">
      <Button
        class="topbar-toggle"
        icon="pi pi-bars"
        severity="secondary"
        text
        rounded
        aria-label="Toggle navigation"
        @click="ui.toggleSidebar()"
      />

      <router-link class="brand" to="/tasks">TaskMan</router-link>

      <form class="topbar-search" role="search" @submit.prevent="submitSearch">
        <InputText v-model="search" placeholder="Search tasks…" aria-label="Search tasks" />
      </form>

      <div class="topbar-actions">
        <Button
          class="theme-toggle"
          :icon="ui.isDark ? 'pi pi-sun' : 'pi pi-moon'"
          severity="secondary"
          text
          rounded
          :aria-label="`Switch to ${ui.isDark ? 'light' : 'dark'} theme`"
          @click="ui.toggleTheme()"
        />

        <Menu class="user-menu" :model="userMenu">
          <template #start>
            <span class="user-menu-start">
              <Avatar :label="initials" shape="circle" />
              <span class="user-menu-name">{{ auth.user?.displayName }}</span>
            </span>
          </template>
        </Menu>
      </div>
    </header>

    <div class="shell-body">
      <ForumSidebar :collapsed="ui.sidebarCollapsed" />

      <main class="content">
        <router-view />
      </main>
    </div>

    <Toast position="top-right" />
    <ConfirmDialog />
  </div>
</template>
