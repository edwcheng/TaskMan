<script setup lang="ts">
defineProps<{
  title: string
  message?: string
}>()
</script>

<template>
  <div class="empty-state">
    <div class="empty-icon" aria-hidden="true">
      <slot name="icon"><i class="pi pi-inbox" /></slot>
    </div>
    <h2 class="empty-title">{{ title }}</h2>
    <p v-if="message" class="empty-message">{{ message }}</p>
    <div class="empty-actions"><slot /></div>
  </div>
</template>
