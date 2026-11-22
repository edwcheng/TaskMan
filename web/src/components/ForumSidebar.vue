<script setup lang="ts">
import { onMounted } from 'vue'
import { useToasts } from '../composables/toasts'
import { useAuthStore } from '../stores/auth'
import { useForumsStore } from '../stores/forums'

defineProps<{ collapsed: boolean }>()

const auth = useAuthStore()
const forums = useForumsStore()
const toasts = useToasts()

onMounted(() => {
  forums.load().catch(() => toasts.error('Could not load forums.'))
})
</script>

<template>
  <nav class="sidebar" :class="{ collapsed }" aria-label="Main navigation">
    <ul class="sidebar-list">
      <li>
        <router-link class="sidebar-item" to="/tasks">All tasks</router-link>
      </li>
      <li>
        <router-link class="sidebar-item" to="/my-tasks">My tasks</router-link>
      </li>

      <li class="sidebar-heading">Forums</li>
      <li v-for="forum in forums.forums" :key="forum.id">
        <router-link class="sidebar-item" :to="`/forums/${forum.slug}`">
          {{ forum.name }}
          <span v-if="forum.isArchived" class="sidebar-flag">archived</span>
        </router-link>
      </li>

      <template v-if="auth.isAdmin">
        <li class="sidebar-heading">Admin</li>
        <li>
          <router-link class="sidebar-item" to="/admin/users">Users</router-link>
        </li>
        <li>
          <router-link class="sidebar-item" to="/admin/forums">Forums</router-link>
        </li>
      </template>
    </ul>
  </nav>
</template>
