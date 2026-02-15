import { Link } from 'react-router'
import { Sparkles, ArrowLeft } from 'lucide-react'
import { Skeleton } from '@/components/ui/skeleton'
import { useRelatedNotes } from '@/hooks/use-related-notes'
import { useBacklinks } from '@/hooks/use-backlinks'

interface RelatedNotesSidebarProps {
  noteId: string
}

export function RelatedNotesSidebar({ noteId }: RelatedNotesSidebarProps) {
  const { data: related, isLoading: relatedLoading } = useRelatedNotes(noteId)
  const { data: backlinks, isLoading: backlinksLoading } = useBacklinks(noteId)

  const hasRelated = related && related.length > 0
  const hasBacklinks = backlinks && backlinks.length > 0
  const isLoading = relatedLoading || backlinksLoading

  if (isLoading) {
    return (
      <div className="space-y-6">
        <div className="space-y-3">
          <h3 className="flex items-center gap-1.5 text-xs font-medium uppercase tracking-wide text-muted-foreground">
            <ArrowLeft className="h-3 w-3" />
            Backlinks
          </h3>
          <Skeleton className="h-8 w-full" />
          <Skeleton className="h-8 w-full" />
        </div>
        <div className="space-y-3">
          <h3 className="flex items-center gap-1.5 text-xs font-medium uppercase tracking-wide text-muted-foreground">
            <Sparkles className="h-3 w-3" />
            Related
          </h3>
          <Skeleton className="h-12 w-full" />
          <Skeleton className="h-12 w-full" />
          <Skeleton className="h-12 w-full" />
        </div>
      </div>
    )
  }

  if (!hasRelated && !hasBacklinks) {
    return null
  }

  return (
    <div className="space-y-6">
      {hasBacklinks && (
        <div className="space-y-3">
          <h3 className="flex items-center gap-1.5 text-xs font-medium uppercase tracking-wide text-muted-foreground">
            <ArrowLeft className="h-3 w-3" />
            Backlinks ({backlinks.length})
          </h3>
          <div className="space-y-1">
            {backlinks.map((bl) => (
              <Link
                key={bl.id}
                to={`/notes/${bl.id}`}
                className="block rounded-md px-2 py-1.5 transition-colors hover:bg-accent"
              >
                <p className="truncate text-sm font-medium">{bl.title}</p>
              </Link>
            ))}
          </div>
        </div>
      )}

      {hasRelated && (
        <div className="space-y-3">
          <h3 className="flex items-center gap-1.5 text-xs font-medium uppercase tracking-wide text-muted-foreground">
            <Sparkles className="h-3 w-3" />
            Related
          </h3>
          <div className="space-y-1">
            {related.map((result) => (
              <Link
                key={result.noteId}
                to={`/notes/${result.noteId}`}
                className="block rounded-md px-2 py-1.5 transition-colors hover:bg-accent"
              >
                <p className="truncate text-sm font-medium">{result.title}</p>
                <p className="text-xs tabular-nums text-muted-foreground">
                  {Math.round(result.rank * 100)}% similar
                </p>
              </Link>
            ))}
          </div>
        </div>
      )}
    </div>
  )
}
