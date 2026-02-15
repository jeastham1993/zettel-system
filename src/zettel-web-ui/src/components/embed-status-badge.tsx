import type { EmbedStatus } from '@/api/types'

const statusConfig: Record<EmbedStatus, { color: string; label: string } | null> = {
  Completed: null,
  Pending: { color: 'bg-amber-500', label: 'Embedding pending' },
  Processing: { color: 'bg-blue-500', label: 'Embedding...' },
  Failed: { color: 'bg-red-500', label: 'Embedding failed' },
  Stale: { color: 'bg-orange-500', label: 'Embedding stale' },
}

interface EmbedStatusBadgeProps {
  status: EmbedStatus
}

export function EmbedStatusBadge({ status }: EmbedStatusBadgeProps) {
  const config = statusConfig[status]
  if (!config) return null

  return (
    <span className="inline-flex items-center gap-1.5 text-xs text-muted-foreground">
      <span className={`h-1.5 w-1.5 rounded-full ${config.color}`} />
      {config.label}
    </span>
  )
}
