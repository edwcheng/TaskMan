<script setup lang="ts">
import { useRouter } from 'vue-router'
import Button from 'primevue/button'
import EmptyState from '../components/EmptyState.vue'

const router = useRouter()
</script>

<template>
  <EmptyState title="Page not found" message="The address does not match any TaskMan page.">
    <template #icon>
      <i class="pi pi-exclamation-triangle" />
    </template>
    <Button label="Back to tasks" icon="pi pi-arrow-left" @click="router.push('/tasks')" />
  </EmptyState>
</template>
