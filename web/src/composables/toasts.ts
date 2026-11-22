import { useToast } from 'primevue/usetoast'

// Consistent toast handling for mutations (PLAN.md section 12). Server
// errors are surfaced through problemMessage by the caller.
export function useToasts() {
  const toast = useToast()

  return {
    success(message: string): void {
      toast.add({ severity: 'success', summary: 'Done', detail: message })
    },
    error(message: string): void {
      toast.add({ severity: 'error', summary: 'Error', detail: message })
    },
    info(message: string): void {
      toast.add({ severity: 'info', detail: message })
    },
  }
}
