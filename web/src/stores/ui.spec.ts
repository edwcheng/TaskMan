import { beforeEach, describe, expect, it } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useUiStore } from './ui'

beforeEach(() => {
  localStorage.clear()
  document.documentElement.classList.remove('p-dark')
  setActivePinia(createPinia())
})

describe('useUiStore', () => {
  it('defaults to the light theme without the p-dark class', () => {
    const ui = useUiStore()

    expect(ui.theme).toBe('light')
    expect(ui.isDark).toBe(false)
    expect(document.documentElement.classList.contains('p-dark')).toBe(false)
  })

  it('toggles the p-dark class and persists the choice', () => {
    const ui = useUiStore()

    ui.toggleTheme()

    expect(ui.theme).toBe('dark')
    expect(ui.isDark).toBe(true)
    expect(document.documentElement.classList.contains('p-dark')).toBe(true)
    expect(localStorage.getItem('taskman.theme')).toBe('dark')
  })

  it('toggling twice returns to light and removes the class', () => {
    const ui = useUiStore()

    ui.toggleTheme()
    ui.toggleTheme()

    expect(ui.theme).toBe('light')
    expect(document.documentElement.classList.contains('p-dark')).toBe(false)
    expect(localStorage.getItem('taskman.theme')).toBe('light')
  })

  it('restores a stored theme on store creation', () => {
    localStorage.setItem('taskman.theme', 'dark')

    const ui = useUiStore()

    expect(ui.theme).toBe('dark')
    expect(document.documentElement.classList.contains('p-dark')).toBe(true)
  })

  it('persists the collapsed sidebar state', () => {
    const ui = useUiStore()

    expect(ui.sidebarCollapsed).toBe(false)

    ui.toggleSidebar()
    expect(ui.sidebarCollapsed).toBe(true)
    expect(localStorage.getItem('taskman.sidebarCollapsed')).toBe('true')

    ui.toggleSidebar()
    expect(ui.sidebarCollapsed).toBe(false)
    expect(localStorage.getItem('taskman.sidebarCollapsed')).toBe('false')
  })

  it('restores a stored collapsed sidebar', () => {
    localStorage.setItem('taskman.sidebarCollapsed', 'true')

    const ui = useUiStore()

    expect(ui.sidebarCollapsed).toBe(true)
  })
})
