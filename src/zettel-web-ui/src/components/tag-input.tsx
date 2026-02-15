import { useState, useRef } from 'react'
import { X } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from '@/components/ui/popover'
import { useTags } from '@/hooks/use-tags'

interface TagInputProps {
  tags: string[]
  onChange: (tags: string[]) => void
}

export function TagInput({ tags, onChange }: TagInputProps) {
  const [input, setInput] = useState('')
  const [open, setOpen] = useState(false)
  const inputRef = useRef<HTMLInputElement>(null)
  const { data: suggestions } = useTags(input)

  const addTag = (tag: string) => {
    const cleaned = tag.trim().toLowerCase()
    if (cleaned && !tags.includes(cleaned)) {
      onChange([...tags, cleaned])
    }
    setInput('')
    setOpen(false)
    inputRef.current?.focus()
  }

  const removeTag = (tag: string) => {
    onChange(tags.filter((t) => t !== tag))
  }

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter' || e.key === ',') {
      e.preventDefault()
      if (input.trim()) addTag(input)
    }
    if (e.key === 'Backspace' && !input && tags.length > 0) {
      removeTag(tags[tags.length - 1])
    }
  }

  const filteredSuggestions = suggestions?.filter((s) => !tags.includes(s)) ?? []

  return (
    <div className="flex flex-wrap items-center gap-1.5">
      {tags.map((tag) => (
        <Badge key={tag} variant="secondary" className="gap-1 text-xs font-normal">
          #{tag}
          <button
            type="button"
            aria-label={`Remove tag ${tag}`}
            onClick={() => removeTag(tag)}
            className="ml-0.5 rounded-full hover:bg-muted-foreground/20"
          >
            <X className="h-3 w-3" />
          </button>
        </Badge>
      ))}
      <Popover open={open && filteredSuggestions.length > 0} onOpenChange={setOpen}>
        <PopoverTrigger asChild>
          <input
            ref={inputRef}
            type="text"
            value={input}
            onChange={(e) => {
              setInput(e.target.value)
              setOpen(e.target.value.length > 0)
            }}
            onKeyDown={handleKeyDown}
            onFocus={() => input.length > 0 && setOpen(true)}
            placeholder={tags.length === 0 ? 'Add tags...' : ''}
            className="min-w-[80px] flex-1 bg-transparent text-sm outline-none placeholder:text-muted-foreground"
          />
        </PopoverTrigger>
        <PopoverContent
          className="w-48 p-1"
          align="start"
          onOpenAutoFocus={(e) => e.preventDefault()}
        >
          {filteredSuggestions.map((suggestion) => (
            <button
              key={suggestion}
              type="button"
              onClick={() => addTag(suggestion)}
              className="w-full rounded-sm px-2 py-1.5 text-left text-sm hover:bg-muted"
            >
              #{suggestion}
            </button>
          ))}
        </PopoverContent>
      </Popover>
    </div>
  )
}
