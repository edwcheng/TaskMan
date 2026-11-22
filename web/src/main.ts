import { createApp } from 'vue'
import { createPinia } from 'pinia'
import 'primeicons/primeicons.css'
import PrimeVue from 'primevue/config'
import ConfirmationService from 'primevue/confirmationservice'
import ToastService from 'primevue/toastservice'
import App from './App.vue'
import router from './router'
import { taskmanPreset } from './theme'
import './style.css'

const app = createApp(App)

app.use(createPinia())
app.use(router)
app.use(PrimeVue, {
  theme: {
    preset: taskmanPreset,
    // Class-based dark mode: the ui store toggles .p-dark on <html>.
    options: { darkModeSelector: '.p-dark' },
  },
})
app.use(ToastService)
app.use(ConfirmationService)

app.mount('#app')
