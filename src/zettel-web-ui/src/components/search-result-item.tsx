import { FileText } from 'lucide-react'
import { CommandItem } from '@/components/ui/command'
import type { SearchResult } from '@/api/types'

interface SearchResultItemProps {
  result: SearchResult
  onSelect: (noteId: string) => void
}

export function SearchResultItem({ result, onSelect }: SearchResultItemProps) {
  return (
    <CommandItem
      value={result.noteId}
      onSelect={() => onSelect(result.noteId)}
      className="gap-2"
    >
      <FileText className="h-4 w-4 shrink-0 text-muted-foreground" />
      <div className="min-w-0 flex-1">
        <p className="truncate text-sm font-medium">{result.title}</p>
        {result.snippet && (
          <p className="truncate text-xs text-muted-foreground">{result.snippet}</p>
        )}
      </div>
      {result.rank > 0 && (
        <span className="ml-auto shrink-0 text-xs tabular-nums text-muted-foreground">
          {Math.round(result.rank * 100)}%
        </span>
      )}
    </CommandItem>
  )
}
