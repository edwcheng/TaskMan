import Aura from '@primevue/themes/aura'
import { definePreset } from '@primevue/themes'

// TaskMan identity: indigo primary on Aura's slate color scheme. The slate
// scheme is already Aura's default surface palette, so only the primary
// palette is swapped; the {indigo.N} references resolve against the
// primitive palettes bundled with the Aura preset.
export const taskmanPreset = definePreset(Aura, {
  semantic: {
    primary: {
      50: '{indigo.50}',
      100: '{indigo.100}',
      200: '{indigo.200}',
      300: '{indigo.300}',
      400: '{indigo.400}',
      500: '{indigo.500}',
      600: '{indigo.600}',
      700: '{indigo.700}',
      800: '{indigo.800}',
      900: '{indigo.900}',
      950: '{indigo.950}',
    },
  },
})
