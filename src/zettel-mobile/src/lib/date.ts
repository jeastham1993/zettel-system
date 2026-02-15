/**
 * Relative date formatting without external dependencies.
 * Keeps the bundle lean -- no date-fns needed on mobile.
 */

const MINUTE = 60 * 1000
const HOUR = 60 * MINUTE
const DAY = 24 * HOUR

export function relativeDate(dateString: string): string {
  const date = new Date(dateString)
  if (isNaN(date.getTime())) return 'Unknown date'

  const now = new Date()
  const diff = now.getTime() - date.getTime()

  if (diff < MINUTE) return 'just now'
  if (diff < HOUR) {
    const mins = Math.floor(diff / MINUTE)
    return `${mins}m ago`
  }
  if (diff < DAY) {
    const hours = Math.floor(diff / HOUR)
    return `${hours}h ago`
  }
  if (diff < 7 * DAY) {
    const days = Math.floor(diff / DAY)
    return `${days}d ago`
  }

  return date.toLocaleDateString(undefined, {
    month: 'short',
    day: 'numeric',
    year: date.getFullYear() !== now.getFullYear() ? 'numeric' : undefined,
  })
}

export function fullDate(dateString: string): string {
  const date = new Date(dateString)
  if (isNaN(date.getTime())) return 'Unknown date'

  return date.toLocaleDateString(undefined, {
    year: 'numeric',
    month: 'long',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
  })
}

// Re-export truncateContent from its new home in markdown.ts
// so existing imports from date.ts continue to work.
export { truncateContent } from './markdown'
