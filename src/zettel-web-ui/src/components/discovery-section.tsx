import { useMemo, useState } from 'react'
import { Link } from 'react-router'
import { Lightbulb, X, Unlink, CalendarDays, Shuffle } from 'lucide-react'
import { useDiscover, useDiscoverByMode } from '@/hooks/use-discover'
import { getDismissedIds, dismissNote } from '@/lib/dismissed-notes'
import { Button } from '@/components/ui/button'
import { truncateContent, relativeDate } from '@/lib/format'
import type { DiscoverMode } from '@/api/discover'
import type { SearchResult, Note } from '@/api/types'

type Tab = 'suggested' | DiscoverMode

const tabs: Array<{ key: Tab; label: string; icon: typeof Lightbulb }> = [
  { key: 'suggested', label: 'Suggested', icon: Lightbulb },
  { key: 'random', label: 'Forgotten', icon: Shuffle },
  { key: 'orphans', label: 'Orphans', icon: Unlink },
  { key: 'today', label: 'Today', icon: CalendarDays },
]

const emptyMessages: Record<Tab, string> = {
  suggested: 'No suggestions right now. Keep writing!',
  random: 'No forgotten notes found.',
  orphans: 'No orphan notes. All your notes are connected!',
  today: 'No notes created today yet.',
}

function SuggestedContent() {
  const { data: suggestions, isLoading, isError } = useDiscover()
  const [dismissed, setDismissed] = useState(() => getDismissedIds())

  const visible = useMemo(
    () => (suggestions ?? []).filter((s) => !dismissed.has(s.noteId)),
    [suggestions, dismissed],
  )

  function handleDismiss(noteId: string) {
    dismissNote(noteId)
    setDismissed(getDismissedIds())
  }

  if (isLoading || isError) return null
  if (visible.length === 0) {
    return (
      <p className="py-6 text-center text-sm text-muted-foreground">
        {emptyMessages.suggested}
      </p>
    )
  }

  return (
    <div className="grid gap-2 sm:grid-cols-2">
      {visible.map((result: SearchResult) => (
        <div
          key={result.noteId}
          className="group relative rounded-lg border border-border/50 bg-card p-3 transition-colors hover:border-border"
        >
          <button
            onClick={() => handleDismiss(result.noteId)}
            className="absolute right-2 top-2 rounded p-0.5 text-muted-foreground opacity-0 transition-opacity hover:text-foreground group-hover:opacity-100 focus-visible:opacity-100"
            aria-label="Dismiss"
          >
            <X className="h-3.5 w-3.5" />
          </button>
          <Link to={`/notes/${result.noteId}`} className="block">
            <p className="truncate text-sm font-medium pr-5">{result.title}</p>
            <p className="mt-1 line-clamp-2 text-xs text-muted-foreground">
              {result.snippet}
            </p>
          </Link>
        </div>
      ))}
    </div>
  )
}

function ModeContent({ mode }: { mode: DiscoverMode }) {
  const { data: notes, isLoading, isError } = useDiscoverByMode(mode)

  if (isLoading) {
    return (
      <div className="grid gap-2 sm:grid-cols-2">
        {Array.from({ length: 4 }).map((_, i) => (
          <div key={i} className="h-20 animate-pulse rounded-lg border border-border/50 bg-muted/30" />
        ))}
      </div>
    )
  }

  if (isError) return null

  if (!notes || notes.length === 0) {
    return (
      <p className="py-6 text-center text-sm text-muted-foreground">
        {emptyMessages[mode]}
      </p>
    )
  }

  return (
    <div className="grid gap-2 sm:grid-cols-2">
      {notes.map((note: Note) => (
        <Link
          key={note.id}
          to={`/notes/${note.id}`}
          className="block rounded-lg border border-border/50 bg-card p-3 transition-colors hover:border-border"
        >
          <p className="truncate text-sm font-medium">{note.title}</p>
          <p className="mt-1 line-clamp-2 text-xs text-muted-foreground">
            {truncateContent(note.content, 100)}
          </p>
          <p className="mt-1 text-xs text-muted-foreground/60">
            {relativeDate(note.updatedAt)}
          </p>
        </Link>
      ))}
    </div>
  )
}

export function DiscoverySection() {
  const [activeTab, setActiveTab] = useState<Tab>('suggested')

  return (
    <div className="mb-6">
      <div className="mb-3 flex items-center gap-3">
        <h2 className="flex items-center gap-1.5 text-xs font-medium uppercase tracking-wide text-muted-foreground">
          <Lightbulb className="h-3.5 w-3.5" />
          Rediscover
        </h2>
        <div className="flex gap-1">
          {tabs.map((tab) => {
            const Icon = tab.icon
            return (
              <Button
                key={tab.key}
                variant={activeTab === tab.key ? 'secondary' : 'ghost'}
                size="xs"
                className="gap-1 text-xs"
                onClick={() => setActiveTab(tab.key)}
              >
                <Icon className="h-3 w-3" />
                {tab.label}
              </Button>
            )
          })}
        </div>
      </div>

      {activeTab === 'suggested' ? (
        <SuggestedContent />
      ) : (
        <ModeContent mode={activeTab} />
      )}
    </div>
  )
}
