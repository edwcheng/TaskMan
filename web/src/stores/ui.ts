import { computed, ref } from 'vue'
import { defineStore } from 'pinia'

export type ThemeMode = 'light' | 'dark'

const themeKey = 'taskman.theme'
const sidebarKey = 'taskman.sidebarCollapsed'

function readStoredTheme(): ThemeMode {
  return localStorage.getItem(themeKey) === 'dark' ? 'dark' : 'light'
}

function readStoredSidebar(): boolean {
  return localStorage.getItem(sidebarKey) === 'true'
}

function applyThemeToDocument(theme: ThemeMode): void {
  document.documentElement.classList.toggle('p-dark', theme === 'dark')
}

export const useUiStore = defineStore('ui', () => {
  const theme = ref<ThemeMode>(readStoredTheme())
  const sidebarCollapsed = ref(readStoredSidebar())

  // Keep the root element in sync as soon as the store exists, so the first
  // paint already reflects the stored preference.
  applyThemeToDocument(theme.value)

  const isDark = computed(() => theme.value === 'dark')

  function setTheme(next: ThemeMode): void {
    theme.value = next
    localStorage.setItem(themeKey, next)
    applyThemeToDocument(next)
  }

  function toggleTheme(): void {
    setTheme(isDark.value ? 'light' : 'dark')
  }

  function toggleSidebar(): void {
    sidebarCollapsed.value = !sidebarCollapsed.value
    localStorage.setItem(sidebarKey, String(sidebarCollapsed.value))
  }

  return { theme, sidebarCollapsed, isDark, setTheme, toggleTheme, toggleSidebar }
})
