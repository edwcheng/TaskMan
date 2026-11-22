import { ref } from 'vue'
import { defineStore } from 'pinia'
import * as forumApi from '../api/forums'
import type { Forum } from '../api/types'

export const useForumsStore = defineStore('forums', () => {
  const forums = ref<Forum[]>([])
  const loaded = ref(false)

  // The sidebar and pickers share one cached list; admin screens call
  // refresh() after mutating forums.
  async function load(force = false): Promise<void> {
    if (loaded.value && !force) return

    forums.value = await forumApi.list()
    loaded.value = true
  }

  function refresh(): Promise<void> {
    return load(true)
  }

  return { forums, loaded, load, refresh }
})
