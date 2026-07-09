import { useState } from 'react'
import { Link } from 'react-router'
import { BookOpen, ChevronDown, ChevronUp } from 'lucide-react'
import { Skeleton } from '@/components/ui/skeleton'
import { useQuery } from '@tanstack/react-query'
import * as notesApi from '@/api/notes'

interface SourceReferencesProps {
  sourceId: string
}

export function SourceReferences({ sourceId }: SourceReferencesProps) {
  const [open, setOpen] = useState(false)
  
  const { data: references, isLoading, error } = useQuery({
    queryKey: ['source-references', sourceId],
    queryFn: () => notesApi.getSourceReferences(sourceId),
    enabled: open, // Only fetch when the section is open
  })

  const hasReferences = references && references.length > 0

  return (
    <div className="mt-6 border-t border-border/50 pt-4">
      <button
        onClick={() => setOpen((prev) => !prev)}
        className="flex w-full items-center justify-between text-sm font-medium text-muted-foreground"
      >
        <div className="flex items-center gap-2">
          <BookOpen className="h-4 w-4" />
          <span>Source References</span>
        </div>
        {open ? (
          <ChevronUp className="h-4 w-4" />
        ) : (
          <ChevronDown className="h-4 w-4" />
        )}
      </button>

      {open && (
        <div className="mt-3 space-y-3">
          {isLoading && (
            <div className="space-y-2">
              <Skeleton className="h-4 w-full" />
              <Skeleton className="h-4 w-2/3" />
            </div>
          )}

          {error && (
            <p className="text-sm text-destructive">
              Failed to load source references
            </p>
          )}

          {hasReferences ? (
            <ul className="space-y-2">
              {references.map((note) => (
                <li key={note.id} className="text-sm">
                  <Link
                    to={`/notes/${note.id}`}
                    className="block rounded-md p-2 hover:bg-accent hover:text-accent-foreground"
                  >
                    <div className="font-medium">{note.title}</div>
                    <div className="text-xs text-muted-foreground">
                      {note.noteType === 'Source' && 'Source • '}
                      {note.tags.length > 0 && (
                        <span>
                          {note.tags.map((t) => `#${t.tag}`).join(' ')}
                        </span>
                      )}
                    </div>
                  </Link>
                </li>
              ))}
            </ul>
          ) : (
            !isLoading && (
              <p className="text-sm text-muted-foreground">
                No notes reference this source yet
              </p>
            )
          )}
        </div>
      )}
    </div>
  )
}
