import { useState, useEffect, useCallback, useRef } from 'react'

interface Draft {
  title: string
  content: string
  tags: string[]
  savedAt: number
}

const DRAFT_PREFIX = 'zettel-draft-'
const AUTOSAVE_INTERVAL = 5000
const DRAFT_MAX_AGE = 24 * 60 * 60 * 1000 // 24 hours
const SAVED_INDICATOR_DURATION = 3000

function draftKey(noteId: string | undefined): string {
  return `${DRAFT_PREFIX}${noteId ?? 'new'}`
}

export function useAutosave(
  noteId: string | undefined,
  title: string,
  content: string,
  tags: string[],
) {
  const timerRef = useRef<ReturnType<typeof setInterval>>(null)
  const [draftSavedRecently, setDraftSavedRecently] = useState(false)
  const indicatorTimerRef = useRef<ReturnType<typeof setTimeout>>(null)

  // Skip autosave for existing notes (noteId === '__skip__')
  const shouldSkip = noteId === '__skip__'

  const saveDraft = useCallback(() => {
    if (shouldSkip) return
    if (!title && !content) return
    const draft: Draft = { title, content, tags, savedAt: Date.now() }
    localStorage.setItem(draftKey(noteId), JSON.stringify(draft))

    setDraftSavedRecently(true)
    if (indicatorTimerRef.current) clearTimeout(indicatorTimerRef.current)
    indicatorTimerRef.current = setTimeout(
      () => setDraftSavedRecently(false),
      SAVED_INDICATOR_DURATION,
    )
  }, [noteId, title, content, tags, shouldSkip])

  // Autosave on interval
  useEffect(() => {
    if (shouldSkip) return
    timerRef.current = setInterval(saveDraft, AUTOSAVE_INTERVAL)
    return () => {
      if (timerRef.current) clearInterval(timerRef.current)
    }
  }, [saveDraft, shouldSkip])

  // Save on beforeunload
  useEffect(() => {
    if (shouldSkip) return
    const handleBeforeUnload = (e: BeforeUnloadEvent) => {
      saveDraft()
      if (title || content) {
        e.preventDefault()
      }
    }
    window.addEventListener('beforeunload', handleBeforeUnload)
    return () => window.removeEventListener('beforeunload', handleBeforeUnload)
  }, [saveDraft, title, content, shouldSkip])

  // Clean up indicator timer on unmount
  useEffect(() => {
    return () => {
      if (indicatorTimerRef.current) clearTimeout(indicatorTimerRef.current)
    }
  }, [])

  return { saveDraft, draftSavedRecently }
}

export function loadDraft(noteId: string | undefined): Draft | null {
  const raw = localStorage.getItem(draftKey(noteId))
  if (!raw) return null
  try {
    const draft: Draft = JSON.parse(raw)
    if (Date.now() - draft.savedAt > DRAFT_MAX_AGE) {
      localStorage.removeItem(draftKey(noteId))
      return null
    }
    return draft
  } catch {
    return null
  }
}

export function clearDraft(noteId: string | undefined): void {
  localStorage.removeItem(draftKey(noteId))
}
