<script setup lang="ts">
import { onMounted, ref } from 'vue'

type HealthState = 'checking' | 'online' | 'offline'

const health = ref<HealthState>('checking')

onMounted(async () => {
  try {
    const response = await fetch('/health', { headers: { Accept: 'application/json' } })
    health.value = response.ok ? 'online' : 'offline'
  } catch {
    health.value = 'offline'
  }
})
</script>

<template>
  <main class="shell">
    <section class="card">
      <p class="eyebrow">TaskMan</p>
      <h1>Forum-style task management</h1>
      <p class="lede">
        Phase 0 scaffold is running. The API health check below is proxied through Vite.
      </p>
      <p class="status" :data-state="health">
        <span class="dot" aria-hidden="true"></span>
        API {{ health }}
      </p>
      <p class="hint">Next: data foundation, authentication, and the task board.</p>
    </section>
  </main>
</template>
