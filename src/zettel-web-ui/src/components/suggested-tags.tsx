import { Sparkles } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { useSuggestedTags } from '@/hooks/use-suggested-tags'

interface SuggestedTagsProps {
  noteId: string | undefined
  existingTags: string[]
  onAddTag: (tag: string) => void
}

export function SuggestedTags({ noteId, existingTags, onAddTag }: SuggestedTagsProps) {
  const { data: suggestions, isLoading } = useSuggestedTags(noteId)

  if (isLoading || !suggestions || suggestions.length === 0) return null

  const existingSet = new Set(existingTags.map((t) => t.toLowerCase()))
  const filtered = suggestions.filter((tag) => !existingSet.has(tag.toLowerCase()))

  if (filtered.length === 0) return null

  return (
    <div className="flex flex-wrap items-center gap-1.5">
      <span className="flex items-center gap-1 text-xs text-muted-foreground">
        <Sparkles className="h-3 w-3" />
        Suggested:
      </span>
      {filtered.map((tag) => (
        <Badge
          key={tag}
          variant="outline"
          className="cursor-pointer text-xs font-normal transition-colors hover:bg-accent"
          onClick={() => onAddTag(tag)}
        >
          +{tag}
        </Badge>
      ))}
    </div>
  )
}
