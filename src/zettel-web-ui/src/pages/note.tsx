import { useState } from 'react'
import { useParams, Link } from 'react-router'
import { NoteView } from '@/components/note-view'
import { RelatedNotesSidebar } from '@/components/related-notes-sidebar'
import { Skeleton } from '@/components/ui/skeleton'
import { Button } from '@/components/ui/button'
import { useNote } from '@/hooks/use-notes'
import { RefreshCw, Home, ChevronDown, ChevronUp } from 'lucide-react'

export function NotePage() {
  const { id } = useParams<{ id: string }>()
  const { data: note, isLoading, error, refetch } = useNote(id)
  const [relatedOpen, setRelatedOpen] = useState(false)

  const is404 =
    error instanceof Error &&
    (error.message.includes('404') || error.message.includes('not found'))

  return (
    <div className="mx-auto flex max-w-4xl gap-8 px-4 py-8">
      <div className="min-w-0 max-w-2xl flex-1">
        {isLoading && (
          <div className="space-y-4">
            <Skeleton className="h-8 w-2/3" />
            <Skeleton className="h-4 w-1/3" />
            <Skeleton className="mt-6 h-40 w-full" />
          </div>
        )}
        {error && (
          <div className="flex flex-col items-center gap-4 py-16 text-center">
            <p className="text-sm text-destructive">
              {is404
                ? 'This note could not be found. It may have been deleted.'
                : 'Failed to load note. Please check your connection and try again.'}
            </p>
            <div className="flex gap-2">
              {!is404 && (
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => refetch()}
                  className="gap-1.5"
                >
                  <RefreshCw className="h-3.5 w-3.5" />
                  Retry
                </Button>
              )}
              <Button variant="ghost" size="sm" asChild>
                <Link to="/" className="gap-1.5">
                  <Home className="h-3.5 w-3.5" />
                  Go home
                </Link>
              </Button>
            </div>
          </div>
        )}
        {note && <NoteView note={note} />}

        {/* Mobile related notes - collapsible section below content */}
        {note && (
          <div className="mt-8 border-t border-border/50 pt-4 lg:hidden">
            <button
              onClick={() => setRelatedOpen((prev) => !prev)}
              className="flex w-full items-center justify-between text-sm font-medium text-muted-foreground"
            >
              <span>Related Notes</span>
              {relatedOpen ? (
                <ChevronUp className="h-4 w-4" />
              ) : (
                <ChevronDown className="h-4 w-4" />
              )}
            </button>
            {relatedOpen && (
              <div className="mt-3">
                <RelatedNotesSidebar noteId={note.id} />
              </div>
            )}
          </div>
        )}
      </div>

      {/* Desktop related notes - sidebar */}
      {note && (
        <aside className="hidden w-56 shrink-0 lg:block">
          <div className="sticky top-8">
            <RelatedNotesSidebar noteId={note.id} />
          </div>
        </aside>
      )}
    </div>
  )
}
