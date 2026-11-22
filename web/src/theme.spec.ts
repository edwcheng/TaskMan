import { createApp, defineComponent } from 'vue'
import { afterEach, describe, expect, it } from 'vitest'
import PrimeVue from 'primevue/config'
import Button from 'primevue/button'
import { taskmanPreset } from './theme'

function collectStyles(): string {
  return Array.from(document.querySelectorAll('style'))
    .map((style) => style.textContent ?? '')
    .join('\n')
}

async function waitForStyle(
  predicate: (css: string) => boolean,
  timeoutMs = 3000
): Promise<string> {
  const start = Date.now()

  while (Date.now() - start < timeoutMs) {
    const css = collectStyles()

    if (predicate(css)) return css

    await new Promise((resolve) => setTimeout(resolve, 50))
  }

  throw new Error('Theme styles were not injected within the timeout')
}

function mountThemedApp() {
  const app = createApp(
    defineComponent({
      // Aliased to avoid registering PrimeVue's single-word Button name.
      components: { PrimeButton: Button },
      template: '<PrimeButton label="ok" />',
    })
  )

  app.use(PrimeVue, {
    theme: {
      preset: taskmanPreset,
      options: { darkModeSelector: '.p-dark' },
    },
  })

  const el = document.createElement('div')
  document.body.appendChild(el)
  app.mount(el)

  return app
}

afterEach(() => {
  document.body.innerHTML = ''
})

describe('taskmanPreset', () => {
  it('applies the indigo primary palette', async () => {
    const app = mountThemedApp()

    const css = await waitForStyle((value) => value.includes('#4f46e5'))

    expect(css).toContain('#4f46e5')
    app.unmount()
  })

  it('scopes dark mode to the .p-dark selector', async () => {
    const app = mountThemedApp()

    const css = await waitForStyle((value) => value.includes('.p-dark'))

    expect(css).toContain('.p-dark')
    app.unmount()
  })
})
