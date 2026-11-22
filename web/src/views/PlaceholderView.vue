<script setup lang="ts">
import EmptyState from '../components/EmptyState.vue'

defineProps<{
  title: string
  note?: string
}>()
</script>

<template>
  <EmptyState :title="title" :message="note ?? 'This screen arrives in the next delivery phase.'" />
</template>
