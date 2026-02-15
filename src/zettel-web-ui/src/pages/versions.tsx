import { useState } from 'react'
import { useParams, Link } from 'react-router'
import { ArrowLeft, Clock, FileText } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { useVersions, useVersion } from '@/hooks/use-versions'
import { fullDate } from '@/lib/format'

export function VersionsPage() {
  const { id } = useParams<{ id: string }>()
  const { data: versions, isLoading, isError } = useVersions(id)
  const [selectedVersionId, setSelectedVersionId] = useState<number | undefined>(undefined)
  const { data: selectedVersion, isLoading: versionLoading } = useVersion(id, selectedVersionId)

  if (isLoading) {
    return (
      <div className="mx-auto max-w-4xl px-4 py-8">
        <Skeleton className="mb-6 h-8 w-64" />
        <div className="space-y-3">
          {Array.from({ length: 5 }).map((_, i) => (
            <Skeleton key={i} className="h-12 w-full" />
          ))}
        </div>
      </div>
    )
  }

  if (isError || !id) {
    return (
      <div className="mx-auto max-w-4xl px-4 py-8">
        <p className="text-sm text-muted-foreground">Failed to load version history.</p>
        <Button variant="outline" size="sm" className="mt-4" asChild>
          <Link to={`/notes/${id ?? ''}`}>Back to note</Link>
        </Button>
      </div>
    )
  }

  return (
    <div className="mx-auto max-w-4xl px-4 py-8">
      <div className="mb-6 flex items-center gap-3">
        <Button variant="ghost" size="sm" asChild>
          <Link to={`/notes/${id}`}>
            <ArrowLeft className="h-4 w-4" />
          </Link>
        </Button>
        <h1 className="font-serif text-2xl font-bold tracking-tight">Version History</h1>
      </div>

      {!versions || versions.length === 0 ? (
        <div className="flex flex-col items-center justify-center py-20 text-center">
          <Clock className="size-12 text-muted-foreground/30" />
          <h2 className="mt-4 font-serif text-lg font-medium">No version history</h2>
          <p className="mt-1 text-sm text-muted-foreground">
            Versions will appear here as the note is updated.
          </p>
        </div>
      ) : (
        <div className="grid gap-6 lg:grid-cols-[300px_1fr]">
          <div className="space-y-1">
            {versions.map((v) => (
              <button
                key={v.id}
                onClick={() => setSelectedVersionId(v.id)}
                className={`w-full rounded-md px-3 py-2 text-left transition-colors ${
                  selectedVersionId === v.id
                    ? 'bg-accent text-accent-foreground'
                    : 'hover:bg-muted/50'
                }`}
              >
                <p className="truncate text-sm font-medium">{v.title}</p>
                <p className="text-xs text-muted-foreground">
                  {fullDate(v.savedAt)}
                </p>
              </button>
            ))}
          </div>

          <div className="min-h-[300px] rounded-lg border p-6">
            {selectedVersionId === undefined ? (
              <div className="flex h-full items-center justify-center text-sm text-muted-foreground">
                <div className="text-center">
                  <FileText className="mx-auto h-8 w-8 text-muted-foreground/30" />
                  <p className="mt-2">Select a version to view its content</p>
                </div>
              </div>
            ) : versionLoading ? (
              <div className="space-y-3">
                <Skeleton className="h-6 w-2/3" />
                <Skeleton className="h-4 w-full" />
                <Skeleton className="h-4 w-full" />
                <Skeleton className="h-4 w-3/4" />
              </div>
            ) : selectedVersion ? (
              <div>
                <h2 className="mb-4 font-serif text-xl font-bold">{selectedVersion.title}</h2>
                <div
                  className="prose prose-stone dark:prose-invert max-w-none text-sm"
                  dangerouslySetInnerHTML={{ __html: selectedVersion.content }}
                />
              </div>
            ) : (
              <p className="text-sm text-muted-foreground">Failed to load version.</p>
            )}
          </div>
        </div>
      )}
    </div>
  )
}
