import { useMemo, useState, useCallback } from 'react'
import { Link, useNavigate } from 'react-router'
import {
  ArrowRight,
  Trash2,
  Inbox as InboxIcon,
  Mail,
  MessageCircle,
  Globe,
  Merge,
  CheckSquare,
  Square,
  Search,
} from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from '@/components/ui/alert-dialog'
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
} from '@/components/ui/dialog'
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from '@/components/ui/tooltip'
import { useInbox, usePromoteNote, useMergeNote } from '@/hooks/use-inbox'
import { useDeleteNote } from '@/hooks/use-notes'
import { relativeDate, truncateContent } from '@/lib/format'
import { searchTitles } from '@/api/notes'
import { toast } from 'sonner'
import type { Note, TitleSearchResult } from '@/api/types'

function ageColor(createdAt: string): string {
  const now = new Date()
  const created = new Date(createdAt)
  const diffMs = now.getTime() - created.getTime()
  const diffDays = diffMs / (1000 * 60 * 60 * 24)

  if (diffDays < 1) return 'text-green-600 dark:text-green-400'
  if (diffDays < 7) return 'text-amber-600 dark:text-amber-400'
  return 'text-red-600 dark:text-red-400'
}

function ageDotColor(createdAt: string): string {
  const now = new Date()
  const created = new Date(createdAt)
  const diffMs = now.getTime() - created.getTime()
  const diffDays = diffMs / (1000 * 60 * 60 * 24)

  if (diffDays < 1) return 'bg-green-500'
  if (diffDays < 7) return 'bg-amber-500'
  return 'bg-red-500'
}

function sourceIcon(source: string | null) {
  switch (source) {
    case 'email':
      return <Mail className="size-3" />
    case 'telegram':
      return <MessageCircle className="size-3" />
    case 'web':
      return <Globe className="size-3" />
    default:
      return <Globe className="size-3" />
  }
}

function displayTitle(note: Note): string {
  if (note.title && note.title !== 'auto') return note.title
  return truncateContent(note.content, 60)
}

interface MergeDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  noteId: string
}

function MergeDialog({ open, onOpenChange, noteId }: MergeDialogProps) {
  const [query, setQuery] = useState('')
  const [results, setResults] = useState<TitleSearchResult[]>([])
  const [searching, setSearching] = useState(false)
  const merge = useMergeNote()

  const handleSearch = useCallback(async (q: string) => {
    setQuery(q)
    if (q.length < 2) {
      setResults([])
      return
    }
    setSearching(true)
    try {
      const data = await searchTitles(q)
      setResults(data)
    } catch {
      setResults([])
    } finally {
      setSearching(false)
    }
  }, [])

  const handleSelect = useCallback(
    (targetId: string) => {
      merge.mutate(
        { fleetingId: noteId, targetId },
        {
          onSuccess: () => {
            toast.success('Note merged successfully')
            onOpenChange(false)
          },
          onError: () => toast.error('Failed to merge note'),
        },
      )
    },
    [merge, noteId, onOpenChange],
  )

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Merge into existing note</DialogTitle>
          <DialogDescription>
            Search for a note to merge this fleeting note into.
          </DialogDescription>
        </DialogHeader>
        <div className="space-y-3">
          <div className="flex items-center gap-2 rounded-md border px-3 py-2">
            <Search className="size-4 text-muted-foreground" />
            <input
              type="text"
              className="flex-1 bg-transparent text-sm outline-none placeholder:text-muted-foreground"
              placeholder="Search notes by title..."
              value={query}
              onChange={(e) => handleSearch(e.target.value)}
              autoFocus
            />
          </div>
          <div className="max-h-60 overflow-y-auto">
            {searching && (
              <div className="space-y-2 p-2">
                <Skeleton className="h-8 w-full" />
                <Skeleton className="h-8 w-full" />
              </div>
            )}
            {!searching && results.length === 0 && query.length >= 2 && (
              <p className="p-4 text-center text-sm text-muted-foreground">
                No notes found
              </p>
            )}
            {!searching &&
              results.map((r) => (
                <button
                  key={r.noteId}
                  onClick={() => handleSelect(r.noteId)}
                  disabled={merge.isPending}
                  className="w-full rounded-md px-3 py-2 text-left text-sm transition-colors hover:bg-accent disabled:opacity-50"
                >
                  {r.title}
                </button>
              ))}
          </div>
        </div>
      </DialogContent>
    </Dialog>
  )
}

interface InboxItemProps {
  note: Note
  selected: boolean
  onToggleSelect: (id: string) => void
}

function InboxItem({ note, selected, onToggleSelect }: InboxItemProps) {
  const navigate = useNavigate()
  const promote = usePromoteNote()
  const deleteNote = useDeleteNote()
  const [mergeOpen, setMergeOpen] = useState(false)

  const handlePromote = () => {
    promote.mutate(note.id, {
      onSuccess: () => toast.success('Note promoted to permanent'),
      onError: () => toast.error('Failed to promote note'),
    })
  }

  const handleDelete = () => {
    deleteNote.mutate(note.id, {
      onSuccess: () => toast.success('Note discarded'),
      onError: () => toast.error('Failed to delete note'),
    })
  }

  return (
    <div className="group rounded-lg px-3 py-4 transition-colors duration-150 hover:bg-muted/50">
      <div className="flex items-start gap-3">
        <button
          onClick={() => onToggleSelect(note.id)}
          className="mt-1 shrink-0 text-muted-foreground hover:text-foreground"
          aria-label={selected ? 'Deselect' : 'Select'}
        >
          {selected ? (
            <CheckSquare className="size-4" />
          ) : (
            <Square className="size-4" />
          )}
        </button>
        <div className={`mt-1.5 size-2 shrink-0 rounded-full ${ageDotColor(note.createdAt)}`} />
        <div className="min-w-0 flex-1">
          <div className="flex items-start justify-between gap-4">
            <div className="min-w-0 flex-1">
              <p className="text-sm font-medium truncate">{displayTitle(note)}</p>
              {note.content && (
                <p className="mt-1 text-sm leading-relaxed text-muted-foreground">
                  {truncateContent(note.content, 200)}
                </p>
              )}
              <div className="mt-2 flex flex-wrap items-center gap-1.5">
                {note.tags.length > 0 &&
                  note.tags.map((t) => (
                    <Badge
                      key={t.tag}
                      variant="secondary"
                      className="text-xs font-normal"
                    >
                      #{t.tag}
                    </Badge>
                  ))}
                <Badge variant="outline" className="gap-1 text-xs font-normal">
                  {sourceIcon(note.source)}
                  {note.source ?? 'web'}
                </Badge>
              </div>
            </div>
            <time className={`shrink-0 text-xs ${ageColor(note.createdAt)}`}>
              {relativeDate(note.createdAt)}
            </time>
          </div>

          <div className="mt-3 flex items-center gap-1.5">
            <Tooltip>
              <TooltipTrigger asChild>
                <Button
                  variant="ghost"
                  size="xs"
                  onClick={() => navigate(`/notes/${note.id}/edit`)}
                >
                  Process
                </Button>
              </TooltipTrigger>
              <TooltipContent>Open in editor</TooltipContent>
            </Tooltip>

            <Tooltip>
              <TooltipTrigger asChild>
                <Button
                  variant="ghost"
                  size="xs"
                  onClick={handlePromote}
                  disabled={promote.isPending}
                >
                  <ArrowRight className="size-3" />
                  Promote
                </Button>
              </TooltipTrigger>
              <TooltipContent>Make permanent without editing</TooltipContent>
            </Tooltip>

            <Tooltip>
              <TooltipTrigger asChild>
                <Button
                  variant="ghost"
                  size="xs"
                  onClick={() => setMergeOpen(true)}
                >
                  <Merge className="size-3" />
                  Merge
                </Button>
              </TooltipTrigger>
              <TooltipContent>Merge into an existing note</TooltipContent>
            </Tooltip>

            <AlertDialog>
              <Tooltip>
                <TooltipTrigger asChild>
                  <AlertDialogTrigger asChild>
                    <Button variant="ghost" size="xs" className="text-muted-foreground">
                      <Trash2 className="size-3" />
                      Discard
                    </Button>
                  </AlertDialogTrigger>
                </TooltipTrigger>
                <TooltipContent>Delete this note</TooltipContent>
              </Tooltip>
              <AlertDialogContent size="sm">
                <AlertDialogHeader>
                  <AlertDialogTitle>Discard note?</AlertDialogTitle>
                  <AlertDialogDescription>
                    This will permanently delete this fleeting note.
                  </AlertDialogDescription>
                </AlertDialogHeader>
                <AlertDialogFooter>
                  <AlertDialogCancel>Cancel</AlertDialogCancel>
                  <AlertDialogAction variant="destructive" onClick={handleDelete}>
                    Discard
                  </AlertDialogAction>
                </AlertDialogFooter>
              </AlertDialogContent>
            </AlertDialog>
          </div>
        </div>
      </div>

      <MergeDialog open={mergeOpen} onOpenChange={setMergeOpen} noteId={note.id} />
    </div>
  )
}

export function InboxPage() {
  const { data: notes, isLoading } = useInbox()
  const deleteNote = useDeleteNote()
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set())

  const staleCount = useMemo(() => {
    if (!notes) return 0
    const weekAgo = Date.now() - 7 * 24 * 60 * 60 * 1000
    return notes.filter((n) => new Date(n.createdAt).getTime() < weekAgo).length
  }, [notes])

  const toggleSelect = useCallback((id: string) => {
    setSelectedIds((prev) => {
      const next = new Set(prev)
      if (next.has(id)) {
        next.delete(id)
      } else {
        next.add(id)
      }
      return next
    })
  }, [])

  const toggleSelectAll = useCallback(() => {
    if (!notes) return
    if (selectedIds.size === notes.length) {
      setSelectedIds(new Set())
    } else {
      setSelectedIds(new Set(notes.map((n) => n.id)))
    }
  }, [notes, selectedIds.size])

  const handleBulkDiscard = useCallback(() => {
    const ids = Array.from(selectedIds)
    let completed = 0
    for (const id of ids) {
      deleteNote.mutate(id, {
        onSuccess: () => {
          completed++
          if (completed === ids.length) {
            toast.success(`Discarded ${ids.length} notes`)
            setSelectedIds(new Set())
          }
        },
        onError: () => {
          completed++
          if (completed === ids.length) {
            toast.error('Some notes failed to discard')
          }
        },
      })
    }
  }, [selectedIds, deleteNote])

  const allSelected = notes ? notes.length > 0 && selectedIds.size === notes.length : false

  if (isLoading) {
    return (
      <div className="mx-auto max-w-2xl px-4 py-8">
        <Skeleton className="mb-6 h-8 w-48" />
        <div className="space-y-4">
          {Array.from({ length: 4 }).map((_, i) => (
            <div key={i} className="space-y-2 px-3 py-4">
              <Skeleton className="h-4 w-full" />
              <Skeleton className="h-4 w-2/3" />
              <Skeleton className="h-3 w-24" />
            </div>
          ))}
        </div>
      </div>
    )
  }

  return (
    <div className="mx-auto max-w-2xl px-4 py-8">
      <div className="mb-6">
        <h1 className="font-serif text-2xl font-bold tracking-tight">Inbox</h1>
        {notes && notes.length > 0 && (
          <p className="mt-1 text-sm text-muted-foreground">
            {notes.length} {notes.length === 1 ? 'note' : 'notes'}
            {staleCount > 0 && (
              <span className="text-red-600 dark:text-red-400">
                , {staleCount} older than a week
              </span>
            )}
          </p>
        )}
      </div>

      {!notes || notes.length === 0 ? (
        <div className="flex flex-col items-center justify-center py-20 text-center">
          <InboxIcon className="size-12 text-muted-foreground/30" />
          <h2 className="mt-4 font-serif text-lg font-medium">Inbox zero</h2>
          <p className="mt-1 text-sm text-muted-foreground">
            No fleeting notes to process. Use the capture button to jot down ideas.
          </p>
          <Button variant="outline" size="sm" className="mt-4" asChild>
            <Link to="/">Back to notes</Link>
          </Button>
        </div>
      ) : (
        <>
          <div className="mb-3 flex items-center gap-3">
            <button
              onClick={toggleSelectAll}
              className="flex items-center gap-2 text-sm text-muted-foreground hover:text-foreground"
            >
              {allSelected ? (
                <CheckSquare className="size-4" />
              ) : (
                <Square className="size-4" />
              )}
              Select all
            </button>
            {selectedIds.size > 0 && (
              <AlertDialog>
                <AlertDialogTrigger asChild>
                  <Button variant="outline" size="sm" className="gap-1.5 text-destructive">
                    <Trash2 className="size-3" />
                    Discard {selectedIds.size} selected
                  </Button>
                </AlertDialogTrigger>
                <AlertDialogContent size="sm">
                  <AlertDialogHeader>
                    <AlertDialogTitle>Discard {selectedIds.size} notes?</AlertDialogTitle>
                    <AlertDialogDescription>
                      This will permanently delete the selected fleeting notes.
                    </AlertDialogDescription>
                  </AlertDialogHeader>
                  <AlertDialogFooter>
                    <AlertDialogCancel>Cancel</AlertDialogCancel>
                    <AlertDialogAction variant="destructive" onClick={handleBulkDiscard}>
                      Discard all
                    </AlertDialogAction>
                  </AlertDialogFooter>
                </AlertDialogContent>
              </AlertDialog>
            )}
          </div>
          <div className="divide-y divide-border/50">
            {notes.map((note) => (
              <InboxItem
                key={note.id}
                note={note}
                selected={selectedIds.has(note.id)}
                onToggleSelect={toggleSelect}
              />
            ))}
          </div>
        </>
      )}
    </div>
  )
}
