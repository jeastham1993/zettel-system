import { useState, useCallback, useEffect, useMemo, useRef } from 'react'
import { useNavigate, useBlocker } from 'react-router'
import { useEditor, EditorContent } from '@tiptap/react'
import StarterKit from '@tiptap/starter-kit'
import Link from '@tiptap/extension-link'
import Placeholder from '@tiptap/extension-placeholder'
import Typography from '@tiptap/extension-typography'
import TaskList from '@tiptap/extension-task-list'
import TaskItem from '@tiptap/extension-task-item'
import { WikiLinkSuggestion } from './extensions/wiki-link-suggestion'
import { suggestionRenderer } from './extensions/suggestion-renderer'
import { ArrowLeft, Save, ChevronDown } from 'lucide-react'
import { Link as RouterLink } from 'react-router'
import { Button } from '@/components/ui/button'
import { Separator } from '@/components/ui/separator'
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { EditorToolbar } from './editor-toolbar'
import { TagInput } from './tag-input'
import { useCreateNote, useUpdateNote } from '@/hooks/use-notes'
import { useKeyboardShortcuts } from '@/hooks/use-keyboard-shortcuts'
import { useAutosave, loadDraft, clearDraft } from '@/hooks/use-autosave'
import { toast } from 'sonner'
import type { Note, NoteType, SourceType } from '@/api/types'

const isMac = navigator.userAgent.includes('Mac')
const mod = isMac ? '\u2318' : 'Ctrl'

const noteTypeLabels: Record<NoteType, string> = {
  Regular: 'Regular',
  Structure: 'Structure',
  Source: 'Source',
}

const sourceTypes: SourceType[] = ['book', 'article', 'web', 'podcast', 'other']

interface NoteEditorProps {
  note?: Note
}

export function NoteEditor({ note }: NoteEditorProps) {
  const navigate = useNavigate()
  const createNote = useCreateNote()
  const updateNote = useUpdateNote()
  const savedRef = useRef(false)

  // Load draft if available (for new notes only -- we don't save drafts
  // for existing notes, so there's nothing to restore for them)
  const draft = !note ? loadDraft(undefined) : null

  const initialTitle = draft?.title ?? note?.title ?? ''
  const initialContent = draft?.content ?? note?.content ?? ''
  const initialTags = draft?.tags ?? note?.tags.map((t) => t.tag) ?? []
  const initialNoteType: NoteType = note?.noteType ?? 'Regular'

  const [title, setTitle] = useState(initialTitle)
  const [tags, setTags] = useState<string[]>(initialTags)
  const [noteType, setNoteType] = useState<NoteType>(initialNoteType)
  const [sourceAuthor, setSourceAuthor] = useState(note?.sourceAuthor ?? '')
  const [sourceTitle, setSourceTitle] = useState(note?.sourceTitle ?? '')
  const [sourceUrl, setSourceUrl] = useState(note?.sourceUrl ?? '')
  const [sourceYear, setSourceYear] = useState(note?.sourceYear?.toString() ?? '')
  const [sourceType, setSourceType] = useState<SourceType | ''>(note?.sourceType ?? '')

  // Notify user if a draft was restored
  const draftNotifiedRef = useRef(false)
  useEffect(() => {
    if (draft && !draftNotifiedRef.current) {
      draftNotifiedRef.current = true
      toast.info('Draft restored from autosave')
    }
  }, [draft])

  const editor = useEditor({
    extensions: [
      StarterKit,
      Link.configure({ openOnClick: false }),
      Placeholder.configure({ placeholder: 'Start writing...' }),
      Typography,
      TaskList,
      TaskItem.configure({ nested: true }),
      WikiLinkSuggestion.configure({
        suggestion: {
          render: suggestionRenderer,
        },
      }),
    ],
    content: initialContent,
  })

  const editorContent = editor?.getHTML() ?? ''

  // Only autosave for new notes (not existing notes, to keep it simple)
  const { draftSavedRecently } = useAutosave(
    note ? '__skip__' : undefined,
    note ? '' : title,
    note ? '' : editorContent,
    note ? [] : tags,
  )

  const [editorDirty, setEditorDirty] = useState(false)

  useEffect(() => {
    if (!editor) return
    const handler = () => setEditorDirty(true)
    editor.on('update', handler)
    return () => { editor.off('update', handler) }
  }, [editor])

  const isDirty =
    !savedRef.current &&
    (title !== initialTitle ||
      editorDirty ||
      noteType !== initialNoteType ||
      JSON.stringify(tags) !== JSON.stringify(initialTags))

  const blocker = useBlocker(isDirty)

  const handleSave = useCallback(() => {
    if (!editor) return
    const content = editor.getHTML()

    if (!title.trim()) {
      toast.error('Title is required')
      return
    }

    savedRef.current = true
    setEditorDirty(false)

    const parsedYear = sourceYear ? parseInt(sourceYear, 10) : undefined
    const yearToSend = parsedYear && !isNaN(parsedYear) ? parsedYear : undefined

    if (note) {
      updateNote.mutate(
        {
          id: note.id,
          title: title.trim(),
          content,
          tags,
          noteType,
          sourceAuthor: noteType === 'Source' && sourceAuthor ? sourceAuthor : undefined,
          sourceTitle: noteType === 'Source' && sourceTitle ? sourceTitle : undefined,
          sourceUrl: noteType === 'Source' && sourceUrl ? sourceUrl : undefined,
          sourceYear: noteType === 'Source' ? yearToSend : undefined,
          sourceType: noteType === 'Source' && sourceType ? sourceType as SourceType : undefined,
        },
        {
          onSuccess: (updated) => {
            clearDraft(note.id)
            toast.success('Note saved')
            navigate(`/notes/${updated.id}`)
          },
          onError: () => {
            savedRef.current = false
            toast.error('Failed to save note')
          },
        },
      )
    } else {
      createNote.mutate(
        {
          title: title.trim(),
          content,
          tags,
          noteType,
          sourceAuthor: noteType === 'Source' && sourceAuthor ? sourceAuthor : undefined,
          sourceTitle: noteType === 'Source' && sourceTitle ? sourceTitle : undefined,
          sourceUrl: noteType === 'Source' && sourceUrl ? sourceUrl : undefined,
          sourceYear: noteType === 'Source' ? yearToSend : undefined,
          sourceType: noteType === 'Source' && sourceType ? sourceType as SourceType : undefined,
        },
        {
          onSuccess: (created) => {
            clearDraft(undefined)
            toast.success('Note created')
            navigate(`/notes/${created.id}`)
          },
          onError: () => {
            savedRef.current = false
            toast.error('Failed to create note')
          },
        },
      )
    }
  }, [editor, title, tags, noteType, sourceAuthor, sourceTitle, sourceUrl, sourceYear, sourceType, note, createNote, updateNote, navigate])

  const isSaving = createNote.isPending || updateNote.isPending

  const shortcutHandlers = useMemo(() => ({ onSave: handleSave }), [handleSave])
  useKeyboardShortcuts(shortcutHandlers)

  return (
    <div>
      <div className="mb-6 flex items-center justify-between">
        <Button variant="ghost" size="sm" asChild>
          <RouterLink
            to={note ? `/notes/${note.id}` : '/'}
            className="gap-1.5 text-muted-foreground"
          >
            <ArrowLeft className="h-4 w-4" />
            {note ? 'Back to note' : 'Back'}
          </RouterLink>
        </Button>
        <div className="flex items-center gap-2">
          {draftSavedRecently && !note && (
            <span className="text-xs text-muted-foreground">Draft saved</span>
          )}
          <Button
            size="sm"
            onClick={handleSave}
            disabled={isSaving}
            className="gap-1.5"
          >
            <Save className="h-3.5 w-3.5" />
            {isSaving ? 'Saving...' : 'Save'}
            <kbd className="pointer-events-none hidden select-none rounded border border-primary-foreground/20 bg-primary-foreground/10 px-1 py-0.5 font-mono text-[10px] sm:inline">
              {mod}S
            </kbd>
          </Button>
        </div>
      </div>

      <input
        type="text"
        value={title}
        onChange={(e) => setTitle(e.target.value)}
        placeholder="Untitled"
        aria-label="Note title"
        className="w-full bg-transparent font-serif text-3xl font-semibold tracking-tight outline-none placeholder:text-muted-foreground/40"
        autoFocus={!note}
      />

      <div className="mt-3 flex items-center gap-3">
        <TagInput tags={tags} onChange={setTags} />

        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <Button variant="outline" size="sm" className="gap-1 text-xs shrink-0">
              {noteTypeLabels[noteType]}
              <ChevronDown className="h-3 w-3" />
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end">
            <DropdownMenuRadioGroup
              value={noteType}
              onValueChange={(v) => setNoteType(v as NoteType)}
            >
              <DropdownMenuRadioItem value="Regular">Regular</DropdownMenuRadioItem>
              <DropdownMenuRadioItem value="Structure">Structure</DropdownMenuRadioItem>
              <DropdownMenuRadioItem value="Source">Source</DropdownMenuRadioItem>
            </DropdownMenuRadioGroup>
          </DropdownMenuContent>
        </DropdownMenu>
      </div>

      {noteType === 'Source' && (
        <div className="mt-3 rounded-lg border bg-muted/30 p-4 space-y-3">
          <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
            Source metadata
          </p>
          <div className="grid gap-3 sm:grid-cols-2">
            <input
              type="text"
              value={sourceAuthor}
              onChange={(e) => setSourceAuthor(e.target.value)}
              placeholder="Author"
              aria-label="Source author"
              className="rounded-md border bg-background px-3 py-1.5 text-sm outline-none focus:ring-1 focus:ring-ring"
            />
            <input
              type="text"
              value={sourceTitle}
              onChange={(e) => setSourceTitle(e.target.value)}
              placeholder="Source title"
              aria-label="Source title"
              className="rounded-md border bg-background px-3 py-1.5 text-sm outline-none focus:ring-1 focus:ring-ring"
            />
            <input
              type="text"
              value={sourceUrl}
              onChange={(e) => setSourceUrl(e.target.value)}
              placeholder="URL"
              aria-label="Source URL"
              className="rounded-md border bg-background px-3 py-1.5 text-sm outline-none focus:ring-1 focus:ring-ring"
            />
            <input
              type="text"
              value={sourceYear}
              onChange={(e) => setSourceYear(e.target.value)}
              placeholder="Year"
              aria-label="Source year"
              inputMode="numeric"
              className="rounded-md border bg-background px-3 py-1.5 text-sm outline-none focus:ring-1 focus:ring-ring"
            />
            <select
              value={sourceType}
              onChange={(e) => setSourceType(e.target.value as SourceType | '')}
              aria-label="Source type"
              className="rounded-md border bg-background px-3 py-1.5 text-sm outline-none focus:ring-1 focus:ring-ring"
            >
              <option value="">Source type...</option>
              {sourceTypes.map((st) => (
                <option key={st} value={st}>
                  {st.charAt(0).toUpperCase() + st.slice(1)}
                </option>
              ))}
            </select>
          </div>
        </div>
      )}

      <Separator className="my-4" />

      {editor && <EditorToolbar editor={editor} />}

      <div className="prose prose-stone mt-4 max-w-none">
        <EditorContent editor={editor} />
      </div>

      <AlertDialog open={blocker.state === 'blocked'}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Unsaved changes</AlertDialogTitle>
            <AlertDialogDescription>
              You have unsaved changes. Are you sure you want to leave?
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel onClick={() => blocker.reset?.()}>
              Stay
            </AlertDialogCancel>
            <AlertDialogAction onClick={() => blocker.proceed?.()}>
              Leave
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  )
}
