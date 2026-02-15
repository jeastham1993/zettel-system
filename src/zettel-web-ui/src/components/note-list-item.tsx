import { Link } from 'react-router'
import { Network, BookOpen } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { relativeDate, truncateContent } from '@/lib/format'
import type { Note } from '@/api/types'

interface NoteListItemProps {
  note: Note
  onTagClick?: (tag: string) => void
}

export function NoteListItem({ note, onTagClick }: NoteListItemProps) {
  return (
    <Link
      to={`/notes/${note.id}`}
      className="group block rounded-lg px-3 py-4 transition-colors duration-150 hover:bg-muted/50"
    >
      <div className="flex items-start justify-between gap-4">
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2">
            {note.noteType === 'Structure' && (
              <Network className="h-4 w-4 shrink-0 text-blue-500" />
            )}
            {note.noteType === 'Source' && (
              <BookOpen className="h-4 w-4 shrink-0 text-emerald-500" />
            )}
            <h2 className="font-serif text-lg font-medium tracking-tight group-hover:text-accent">
              {note.title}
            </h2>
          </div>
          {note.noteType === 'Source' && note.sourceAuthor && (
            <p className="mt-0.5 text-xs text-muted-foreground">
              {note.sourceAuthor}
              {note.sourceYear ? ` (${note.sourceYear})` : ''}
              {note.sourceType ? ` \u00b7 ${note.sourceType}` : ''}
            </p>
          )}
          {note.content && (
            <p className="mt-1 text-sm leading-relaxed text-muted-foreground">
              {truncateContent(note.content)}
            </p>
          )}
          {note.tags.length > 0 && (
            <div className="mt-2 flex flex-wrap gap-1.5">
              {note.tags.map((t) => (
                <Badge
                  key={t.tag}
                  variant="secondary"
                  className={
                    onTagClick
                      ? 'cursor-pointer text-xs font-normal hover:bg-secondary/80'
                      : 'text-xs font-normal'
                  }
                  onClick={
                    onTagClick
                      ? (e) => {
                          e.preventDefault()
                          e.stopPropagation()
                          onTagClick(t.tag)
                        }
                      : undefined
                  }
                >
                  #{t.tag}
                </Badge>
              ))}
            </div>
          )}
        </div>
        <time className="shrink-0 text-xs text-muted-foreground">
          {relativeDate(note.updatedAt)}
        </time>
      </div>
    </Link>
  )
}
