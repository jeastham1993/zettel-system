import { useParams, Link } from 'react-router'
import { NoteEditor } from '@/components/note-editor'
import { Skeleton } from '@/components/ui/skeleton'
import { Button } from '@/components/ui/button'
import { useNote } from '@/hooks/use-notes'
import { RefreshCw, Home } from 'lucide-react'

export function EditorPage() {
  const { id } = useParams<{ id: string }>()
  const { data: note, isLoading, error, refetch } = useNote(id)

  const isEdit = !!id

  const is404 =
    error instanceof Error &&
    (error.message.includes('404') || error.message.includes('not found'))

  return (
    <div className="mx-auto max-w-2xl px-4 py-8">
      {isEdit && isLoading && (
        <div className="space-y-4">
          <Skeleton className="h-10 w-2/3" />
          <Skeleton className="h-4 w-1/4" />
          <Skeleton className="mt-6 h-40 w-full" />
        </div>
      )}
      {isEdit && error && (
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
      {(!isEdit || note) && <NoteEditor note={note} />}
    </div>
  )
}
