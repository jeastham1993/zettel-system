import { createMMKV } from 'react-native-mmkv'

const storage = createMMKV({ id: 'draft-store' })

export interface Draft {
  title: string
  content: string
  tags: string[]
  savedAt: string
}

function draftKey(noteId: string): string {
  return `draft-${noteId}`
}

export const draftStore = {
  getDraft(noteId: string): Draft | null {
    const raw = storage.getString(draftKey(noteId))
    if (!raw) return null
    try {
      return JSON.parse(raw) as Draft
    } catch {
      return null
    }
  },

  saveDraft(noteId: string, draft: Draft): void {
    storage.set(draftKey(noteId), JSON.stringify(draft))
  },

  clearDraft(noteId: string): void {
    storage.remove(draftKey(noteId))
  },
}
