<script setup lang="ts">
import { AxiosError } from 'axios'
import { computed, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Message from 'primevue/message'
import Password from 'primevue/password'
import { problemMessage, retryAfterSeconds } from '../api/client'
import { useAuthStore } from '../stores/auth'
import { safeReturnTo } from '../utils/returnUrl'

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()

const userName = ref('')
const password = ref('')
const busy = ref(false)
const error = ref<string | null>(null)

const destination = computed(() => safeReturnTo(route.query.return) ?? '/')

async function submit() {
  if (busy.value) return

  busy.value = true
  error.value = null

  try {
    await auth.login(userName.value.trim(), password.value)
    router.push(destination.value)
  } catch (err) {
    if (err instanceof AxiosError && err.response?.status === 429) {
      const wait = retryAfterSeconds(err)
      error.value = wait
        ? `Too many sign-in attempts. Try again in ${wait} seconds.`
        : 'Too many sign-in attempts. Try again shortly.'
    } else {
      error.value = problemMessage(err, 'Sign in failed. Please try again.')
    }
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div class="login-page">
    <form class="login-card" @submit.prevent="submit">
      <p class="login-eyebrow">TaskMan</p>
      <h1 class="login-title">Sign in</h1>
      <p class="login-lede">Use your account to manage tasks and forums.</p>

      <Message v-if="error" severity="error" class="login-error" :show-icon="false">{{
        error
      }}</Message>

      <div class="login-field">
        <label for="login-username">Username</label>
        <InputText
          id="login-username"
          v-model="userName"
          autocomplete="username"
          required
          autofocus
        />
      </div>

      <div class="login-field">
        <label for="login-password">Password</label>
        <Password
          v-model="password"
          :feedback="false"
          toggle-mask
          input-mode="password"
          input-id="login-password"
          autocomplete="current-password"
          required
        />
      </div>

      <Button type="submit" label="Sign in" :loading="busy" class="login-submit" />
    </form>
  </div>
</template>
