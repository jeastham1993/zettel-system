import { useCallback } from 'react'
import { useEditor, EditorContent } from '@tiptap/react'
import StarterKit from '@tiptap/starter-kit'
import Link from '@tiptap/extension-link'
import { Link as RouterLink, useNavigate } from 'react-router'
import { Pencil, Trash2, ArrowLeft, Network, BookOpen, ExternalLink } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Separator } from '@/components/ui/separator'
import { EmbedStatusBadge } from './embed-status-badge'
import { ConfirmDialog } from './confirm-dialog'
import { fullDate } from '@/lib/format'
import { useDeleteNote } from '@/hooks/use-notes'
import { toast } from 'sonner'
import type { Note } from '@/api/types'

interface NoteViewProps {
  note: Note
}

export function NoteView({ note }: NoteViewProps) {
  const navigate = useNavigate()
  const deleteNote = useDeleteNote()

  const editor = useEditor({
    extensions: [
      StarterKit,
      Link.configure({ openOnClick: true }),
    ],
    content: note.content,
    editable: false,
  })

  const handleDelete = () => {
    deleteNote.mutate(note.id, {
      onSuccess: () => {
        toast.success('Note deleted')
        navigate('/')
      },
    })
  }

  const handleTagClick = useCallback((tag: string) => {
    window.dispatchEvent(
      new CustomEvent('zettel:search-tag', { detail: tag }),
    )
  }, [])

  const hasSourceMeta = note.noteType === 'Source' && (
    note.sourceAuthor || note.sourceTitle || note.sourceUrl || note.sourceYear || note.sourceType
  )

  return (
    <article>
      <div className="mb-6 flex items-center gap-2">
        <Button variant="ghost" size="sm" asChild>
          <RouterLink to="/" className="gap-1.5 text-muted-foreground">
            <ArrowLeft className="h-4 w-4" />
            Back
          </RouterLink>
        </Button>
      </div>

      <div className="flex items-center gap-2">
        {note.noteType === 'Structure' && (
          <Network className="h-5 w-5 shrink-0 text-blue-500" />
        )}
        {note.noteType === 'Source' && (
          <BookOpen className="h-5 w-5 shrink-0 text-emerald-500" />
        )}
        <h1 className="font-serif text-3xl font-semibold leading-tight tracking-tight">
          {note.title}
        </h1>
      </div>

      <div className="mt-3 flex flex-wrap items-center gap-3 text-sm text-muted-foreground">
        <time>{fullDate(note.updatedAt)}</time>
        <EmbedStatusBadge status={note.embedStatus} />
        {note.noteType !== 'Regular' && (
          <Badge variant="outline" className="text-xs font-normal">
            {note.noteType}
          </Badge>
        )}
      </div>

      {hasSourceMeta && (
        <div className="mt-4 rounded-lg border bg-muted/30 p-4">
          <div className="flex items-center gap-2 text-xs font-medium uppercase tracking-wide text-muted-foreground">
            <BookOpen className="h-3.5 w-3.5" />
            Source
          </div>
          <div className="mt-2 space-y-1 text-sm">
            {note.sourceTitle && (
              <p className="font-medium">{note.sourceTitle}</p>
            )}
            {note.sourceAuthor && (
              <p className="text-muted-foreground">{note.sourceAuthor}</p>
            )}
            <div className="flex flex-wrap items-center gap-2 text-muted-foreground">
              {note.sourceYear && <span>{note.sourceYear}</span>}
              {note.sourceType && (
                <Badge variant="secondary" className="text-xs font-normal capitalize">
                  {note.sourceType}
                </Badge>
              )}
            </div>
            {note.sourceUrl && (
              <a
                href={note.sourceUrl}
                target="_blank"
                rel="noopener noreferrer"
                className="inline-flex items-center gap-1 text-xs text-accent hover:underline"
              >
                <ExternalLink className="h-3 w-3" />
                {note.sourceUrl}
              </a>
            )}
          </div>
        </div>
      )}

      {note.tags.length > 0 && (
        <div className="mt-3 flex flex-wrap gap-1.5">
          {note.tags.map((t) => (
            <Badge
              key={t.tag}
              variant="secondary"
              className="cursor-pointer text-xs font-normal transition-colors hover:bg-accent hover:text-accent-foreground"
              onClick={() => handleTagClick(t.tag)}
            >
              #{t.tag}
            </Badge>
          ))}
        </div>
      )}

      <Separator className="my-6" />

      <div className="prose prose-stone max-w-none">
        <EditorContent editor={editor} />
      </div>

      <Separator className="my-8" />

      <div className="flex items-center gap-2">
        <Button variant="outline" size="sm" asChild>
          <RouterLink to={`/notes/${note.id}/edit`} className="gap-1.5">
            <Pencil className="h-3.5 w-3.5" />
            Edit
          </RouterLink>
        </Button>
        <ConfirmDialog
          trigger={
            <Button variant="ghost" size="sm" className="gap-1.5 text-destructive">
              <Trash2 className="h-3.5 w-3.5" />
              Delete
            </Button>
          }
          title="Delete note"
          description="This action cannot be undone. This note will be permanently deleted."
          confirmLabel="Delete"
          onConfirm={handleDelete}
        />
      </div>
    </article>
  )
}
