import { formatDistanceToNow, format } from 'date-fns'

export function relativeDate(dateString: string): string {
  const date = new Date(dateString)
  const now = new Date()
  const diffDays = Math.floor((now.getTime() - date.getTime()) / (1000 * 60 * 60 * 24))

  if (diffDays < 7) {
    return formatDistanceToNow(date, { addSuffix: true })
  }
  return format(date, 'MMM d, yyyy')
}

export function fullDate(dateString: string): string {
  return format(new Date(dateString), 'MMMM d, yyyy \'at\' h:mm a')
}

export function truncateContent(content: string, maxLength = 120): string {
  const text = content.replace(/<[^>]*>/g, '')
  if (text.length <= maxLength) return text
  const truncated = text.slice(0, maxLength)
  const lastSpace = truncated.lastIndexOf(' ')
  return (lastSpace > maxLength * 0.6 ? truncated.slice(0, lastSpace) : truncated) + '...'
}
