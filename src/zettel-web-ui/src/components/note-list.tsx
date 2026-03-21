import { useState, useMemo, useRef, useEffect } from 'react'
import { ArrowUpDown, ChevronLeft, ChevronRight, Tag, X } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { NoteListItem } from './note-list-item'
import { NoteEmpty } from './note-empty'
import { Skeleton } from '@/components/ui/skeleton'
import { useNotes } from '@/hooks/use-notes'
import type { Note, NoteType } from '@/api/types'

type SortOption = 'updated' | 'created' | 'title'

const PAGE_SIZE = 50

const sortLabels: Record<SortOption, string> = {
  updated: 'Recently updated',
  created: 'Recently created',
  title: 'Title',
}

type TypeFilter = NoteType | undefined

const typeFilters: { label: string; value: TypeFilter }[] = [
  { label: 'All', value: undefined },
  { label: 'Regular', value: 'Regular' },
  { label: 'Structure', value: 'Structure' },
  { label: 'Source', value: 'Source' },
]

function sortNotes(notes: Note[], sort: SortOption): Note[] {
  return [...notes].sort((a, b) => {
    switch (sort) {
      case 'updated':
        return new Date(b.updatedAt).getTime() - new Date(a.updatedAt).getTime()
      case 'created':
        return new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime()
      case 'title':
        return a.title.localeCompare(b.title)
    }
  })
}

function VirtualizedNoteList({ notes, onTagClick, itemHeight, maxHeight }: {
  notes: Note[]
  onTagClick: (tag: string) => void
  itemHeight: number
  maxHeight: number
}) {
  const containerRef = useRef<HTMLDivElement>(null)
  const [visibleRange, setVisibleRange] = useState({ start: 0, end: Math.min(10, notes.length) })
  
  useEffect(() => {
    const container = containerRef.current
    if (!container) return
    
    const handleScroll = () => {
      if (!container) return
      const scrollTop = container.scrollTop
      const startIndex = Math.floor(scrollTop / itemHeight)
      const endIndex = Math.min(
        startIndex + Math.ceil(maxHeight / itemHeight) + 1,
        notes.length
      )
      setVisibleRange({ start: startIndex, end: endIndex })
    }
    
    container.addEventListener('scroll', handleScroll)
    handleScroll() // Initialize
    
    return () => container.removeEventListener('scroll', handleScroll)
  }, [notes.length, itemHeight, maxHeight])
  
  const visibleNotes = notes.slice(visibleRange.start, visibleRange.end)
  const paddingTop = visibleRange.start * itemHeight
  const paddingBottom = Math.max(0, (notes.length - visibleRange.end) * itemHeight)
  
  return (
    <div
      ref={containerRef}
      style={{ height: `${Math.min(maxHeight, notes.length * itemHeight)}px`, overflowY: 'auto', width: '100%' }}
    >
      <div style={{ paddingTop: `${paddingTop}px`, paddingBottom: `${paddingBottom}px` }}>
        {visibleNotes.map((note) => (
          <NoteListItem
            key={note.id}
            note={note}
            onTagClick={onTagClick}
          />
        ))}
      </div>
    </div>
  )
}

export function NoteList() {
  const [skip, setSkip] = useState(0)
  const [sort, setSort] = useState<SortOption>('updated')
  const [tagFilter, setTagFilter] = useState<string | undefined>(undefined)
  const [typeFilter, setTypeFilter] = useState<TypeFilter>(undefined)

  const { data, isLoading } = useNotes(skip, PAGE_SIZE, tagFilter, typeFilter)

  const notes = data?.items
  const totalCount = data?.totalCount ?? 0

  const sorted = useMemo(
    () => (notes ? sortNotes(notes, sort) : []),
    [notes, sort],
  )

  const hasPrevious = skip > 0
  const hasNext = skip + PAGE_SIZE < totalCount

  function handleTagClick(tag: string) {
    setTagFilter(tag)
    setSkip(0)
  }

  function clearTagFilter() {
    setTagFilter(undefined)
    setSkip(0)
  }

  function handleTypeFilter(value: TypeFilter) {
    setTypeFilter(value)
    setSkip(0)
  }

  if (isLoading) {
    return (
      <div className="space-y-4">
        {Array.from({ length: 5 }).map((_, i) => (
          <div key={i} className="space-y-2 px-3 py-4">
            <Skeleton className="h-5 w-2/3" />
            <Skeleton className="h-4 w-full" />
          </div>
        ))}
      </div>
    )
  }

  if (!notes || (notes.length === 0 && !tagFilter && !typeFilter)) {
    return <NoteEmpty />
  }

  const hasActiveFilter = !!tagFilter || !!typeFilter

  return (
    <div>
      <div className="mb-3 flex flex-wrap items-center gap-1">
        {typeFilters.map((f) => (
          <Button
            key={f.label}
            variant={typeFilter === f.value ? 'secondary' : 'ghost'}
            size="xs"
            onClick={() => handleTypeFilter(f.value)}
          >
            {f.label}
          </Button>
        ))}
      </div>

      <div className="mb-2 flex items-center justify-between">
        <div className="flex items-center gap-2">
          <p className="text-sm text-muted-foreground">
            {totalCount} {totalCount === 1 ? 'note' : 'notes'}
          </p>
          {tagFilter && (
            <Badge
              variant="outline"
              className="gap-1 text-xs font-normal cursor-pointer"
              onClick={clearTagFilter}
            >
              <Tag className="h-3 w-3" />
              #{tagFilter}
              <X className="h-3 w-3" />
            </Badge>
          )}
        </div>
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <Button variant="ghost" size="sm" className="gap-1.5 text-xs text-muted-foreground">
              <ArrowUpDown className="h-3 w-3" />
              {sortLabels[sort]}
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end">
            <DropdownMenuRadioGroup value={sort} onValueChange={(v) => setSort(v as SortOption)}>
              {(Object.keys(sortLabels) as SortOption[]).map((key) => (
                <DropdownMenuRadioItem key={key} value={key}>
                  {sortLabels[key]}
                </DropdownMenuRadioItem>
              ))}
            </DropdownMenuRadioGroup>
          </DropdownMenuContent>
        </DropdownMenu>
      </div>

      {notes.length === 0 && hasActiveFilter ? (
        <div className="flex flex-col items-center justify-center py-16 text-center">
          <Tag className="h-8 w-8 text-muted-foreground/40" />
          <p className="mt-3 text-sm text-muted-foreground">
            {tagFilter
              ? <>No notes tagged with &ldquo;{tagFilter}&rdquo;</>
              : <>No {typeFilter?.toLowerCase()} notes found</>
            }
          </p>
          <Button
            variant="outline"
            size="sm"
            className="mt-4"
            onClick={() => {
              clearTagFilter()
              setTypeFilter(undefined)
            }}
          >
            Clear filters
          </Button>
        </div>
      ) : (
        <>
          <div className="divide-y divide-border/50">
            <VirtualizedNoteList
              notes={sorted}
              onTagClick={handleTagClick}
              itemHeight={80}
              maxHeight={600}
            />
          </div>
          {(hasPrevious || hasNext) && (
            <div className="mt-4 flex items-center justify-between">
              <Button
                variant="outline"
                size="sm"
                disabled={!hasPrevious}
                onClick={() => setSkip((prev) => Math.max(0, prev - PAGE_SIZE))}
                className="gap-1"
              >
                <ChevronLeft className="h-4 w-4" />
                Previous
              </Button>
              <p className="text-xs text-muted-foreground">
                {skip + 1}&ndash;{Math.min(skip + PAGE_SIZE, totalCount)} of {totalCount}
              </p>
              <Button
                variant="outline"
                size="sm"
                disabled={!hasNext}
                onClick={() => setSkip((prev) => prev + PAGE_SIZE)}
                className="gap-1"
              >
                Next
                <ChevronRight className="h-4 w-4" />
              </Button>
            </div>
          )}
        </>
      )}
    </div>
  )
}
