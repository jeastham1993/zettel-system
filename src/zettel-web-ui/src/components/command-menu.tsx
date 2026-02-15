import { useState, useEffect } from 'react'
import { useNavigate } from 'react-router'
import { Plus, Settings, RefreshCw, GitBranch, Inbox, Loader2 } from 'lucide-react'
import {
  CommandDialog,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
  CommandSeparator,
} from '@/components/ui/command'
import { SearchResultItem } from './search-result-item'
import { useSearch } from '@/hooks/use-search'
import { useNotes, useReEmbed } from '@/hooks/use-notes'
import { toast } from 'sonner'

interface CommandMenuProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  initialQuery?: string
}

export function CommandMenu({ open, onOpenChange, initialQuery }: CommandMenuProps) {
  const navigate = useNavigate()
  const [query, setQuery] = useState('')
  const { data: searchResults, isLoading: isSearching } = useSearch(query)
  const { data: notesData } = useNotes()
  const reEmbed = useReEmbed()

  // Sync initialQuery when command menu opens with a tag search
  useEffect(() => {
    if (open && initialQuery) {
      setQuery(initialQuery)
    }
  }, [open, initialQuery])

  const recentNotes = notesData?.items
    ?.slice()
    .sort((a, b) => new Date(b.updatedAt).getTime() - new Date(a.updatedAt).getTime())
    .slice(0, 5)

  const go = (path: string) => {
    onOpenChange(false)
    setQuery('')
    navigate(path)
  }

  return (
    <CommandDialog
      open={open}
      onOpenChange={(value) => {
        if (!value) setQuery('')
        onOpenChange(value)
      }}
      className="max-w-[calc(100vw-2rem)] sm:max-w-lg"
    >
      <CommandInput
        placeholder="Search notes..."
        value={query}
        onValueChange={setQuery}
      />
      <CommandList className="max-h-[60vh] sm:max-h-[300px]">
        {query && isSearching && (
          <div className="flex items-center justify-center gap-2 py-6 text-sm text-muted-foreground">
            <Loader2 className="h-4 w-4 animate-spin" />
            Searching...
          </div>
        )}

        {query && !isSearching && (!searchResults || searchResults.length === 0) && (
          <CommandEmpty>No results found.</CommandEmpty>
        )}

        {!query && (
          <CommandEmpty>Type to search your notes.</CommandEmpty>
        )}

        {query && searchResults && searchResults.length > 0 && (
          <CommandGroup heading="Results">
            {searchResults.map((result) => (
              <SearchResultItem
                key={result.noteId}
                result={result}
                onSelect={(id) => go(`/notes/${id}`)}
              />
            ))}
          </CommandGroup>
        )}

        {!query && recentNotes && recentNotes.length > 0 && (
          <CommandGroup heading="Recent">
            {recentNotes.map((note) => (
              <SearchResultItem
                key={note.id}
                result={{
                  noteId: note.id,
                  title: note.title,
                  snippet: '',
                  rank: 0,
                }}
                onSelect={(id) => go(`/notes/${id}`)}
              />
            ))}
          </CommandGroup>
        )}

        <CommandSeparator />

        <CommandGroup heading="Actions">
          <CommandItem onSelect={() => go('/new')} className="gap-2">
            <Plus className="h-4 w-4" />
            New note
          </CommandItem>
          <CommandItem onSelect={() => go('/inbox')} className="gap-2">
            <Inbox className="h-4 w-4" />
            Inbox
          </CommandItem>
          <CommandItem onSelect={() => go('/graph')} className="gap-2">
            <GitBranch className="h-4 w-4" />
            Knowledge graph
          </CommandItem>
          <CommandItem onSelect={() => go('/settings')} className="gap-2">
            <Settings className="h-4 w-4" />
            Settings
          </CommandItem>
          <CommandItem
            onSelect={() => {
              onOpenChange(false)
              setQuery('')
              reEmbed.mutate(undefined, {
                onSuccess: (result) =>
                  toast.success(`Queued ${result.queued} notes for re-embedding`),
                onError: () => toast.error('Failed to re-embed notes'),
              })
            }}
            className="gap-2"
          >
            <RefreshCw className="h-4 w-4" />
            Re-embed all notes
          </CommandItem>
        </CommandGroup>
      </CommandList>
    </CommandDialog>
  )
}
