const STORAGE_KEY = 'zettel-dismissed-notes'
const EXPIRY_DAYS = 30

interface DismissedEntry {
  noteId: string
  dismissedAt: number
}

function loadEntries(): DismissedEntry[] {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (!raw) return []
    const entries: DismissedEntry[] = JSON.parse(raw)
    const cutoff = Date.now() - EXPIRY_DAYS * 24 * 60 * 60 * 1000
    return entries.filter((e) => e.dismissedAt > cutoff)
  } catch {
    return []
  }
}

function saveEntries(entries: DismissedEntry[]) {
  localStorage.setItem(STORAGE_KEY, JSON.stringify(entries))
}

export function isDismissed(noteId: string): boolean {
  return loadEntries().some((e) => e.noteId === noteId)
}

export function dismissNote(noteId: string) {
  const entries = loadEntries().filter((e) => e.noteId !== noteId)
  entries.push({ noteId, dismissedAt: Date.now() })
  saveEntries(entries)
}

export function getDismissedIds(): Set<string> {
  return new Set(loadEntries().map((e) => e.noteId))
}
