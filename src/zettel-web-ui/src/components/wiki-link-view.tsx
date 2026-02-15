import { useState, useCallback, useRef, useEffect, useMemo } from 'react'
import { Link } from 'react-router'
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from '@/components/ui/popover'
import { Skeleton } from '@/components/ui/skeleton'
import { searchTitles, getNote } from '@/api/notes'
import type { TitleSearchResult, Note } from '@/api/types'
import { truncateContent } from '@/lib/format'

const titleCache = new Map<string, TitleSearchResult | null>()

async function resolveTitleToId(title: string): Promise<TitleSearchResult | null> {
  const cached = titleCache.get(title.toLowerCase())
  if (cached !== undefined) return cached

  try {
    const results = await searchTitles(title)
    const exact = results.find(
      (r) => r.title.toLowerCase() === title.toLowerCase(),
    )
    const result = exact ?? results[0] ?? null
    titleCache.set(title.toLowerCase(), result)
    return result
  } catch {
    return null
  }
}

interface WikiLinkProps {
  title: string
}

function WikiLink({ title }: WikiLinkProps) {
  const [resolved, setResolved] = useState<TitleSearchResult | null>(null)
  const [resolving, setResolving] = useState(false)
  const [preview, setPreview] = useState<Note | null>(null)
  const [loadingPreview, setLoadingPreview] = useState(false)
  const [open, setOpen] = useState(false)
  const hoverTimeout = useRef<ReturnType<typeof setTimeout>>(undefined)

  useEffect(() => {
    let cancelled = false
    setResolving(true)
    resolveTitleToId(title).then((result) => {
      if (!cancelled) {
        setResolved(result)
        setResolving(false)
      }
    })
    return () => { cancelled = true }
  }, [title])

  const handleMouseEnter = useCallback(() => {
    hoverTimeout.current = setTimeout(() => {
      setOpen(true)
      if (resolved && !preview && !loadingPreview) {
        setLoadingPreview(true)
        getNote(resolved.noteId)
          .then((note) => setPreview(note))
          .catch(() => {})
          .finally(() => setLoadingPreview(false))
      }
    }, 300)
  }, [resolved, preview, loadingPreview])

  const handleMouseLeave = useCallback(() => {
    if (hoverTimeout.current) {
      clearTimeout(hoverTimeout.current)
    }
    setOpen(false)
  }, [])

  useEffect(() => {
    if (open && resolved && !preview && !loadingPreview) {
      setLoadingPreview(true)
      getNote(resolved.noteId)
        .then((note) => setPreview(note))
        .catch(() => {})
        .finally(() => setLoadingPreview(false))
    }
  }, [open, resolved, preview, loadingPreview])

  if (resolving) {
    return (
      <span className="text-muted-foreground">[[{title}]]</span>
    )
  }

  if (!resolved) {
    return (
      <span className="text-muted-foreground/60 line-through">[[{title}]]</span>
    )
  }

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Link
          to={`/notes/${resolved.noteId}`}
          className="text-accent underline underline-offset-2 decoration-accent/40 hover:decoration-accent"
          onMouseEnter={handleMouseEnter}
          onMouseLeave={handleMouseLeave}
          onClick={(e) => e.stopPropagation()}
        >
          {title}
        </Link>
      </PopoverTrigger>
      <PopoverContent
        className="w-80 pointer-events-none"
        side="top"
        sideOffset={8}
        onMouseEnter={handleMouseEnter}
        onMouseLeave={handleMouseLeave}
      >
        {loadingPreview ? (
          <div className="space-y-2">
            <Skeleton className="h-4 w-3/4" />
            <Skeleton className="h-3 w-full" />
            <Skeleton className="h-3 w-2/3" />
          </div>
        ) : preview ? (
          <div>
            <p className="font-medium text-sm truncate">{preview.title}</p>
            <p className="mt-1 text-xs text-muted-foreground leading-relaxed">
              {truncateContent(preview.content, 200)}
            </p>
          </div>
        ) : (
          <p className="text-xs text-muted-foreground">Unable to load preview</p>
        )}
      </PopoverContent>
    </Popover>
  )
}

interface WikiLinkViewProps {
  html: string
  className?: string
}

export function WikiLinkView({ html, className }: WikiLinkViewProps) {
  const parts = useMemo(() => {
    const segments: Array<{ type: 'text'; html: string } | { type: 'wikilink'; title: string }> = []
    const regex = /\[\[([^\]]+)\]\]/g
    let lastIndex = 0
    let match: RegExpExecArray | null

    match = regex.exec(html)
    while (match !== null) {
      if (match.index > lastIndex) {
        segments.push({ type: 'text', html: html.slice(lastIndex, match.index) })
      }
      segments.push({ type: 'wikilink', title: match[1] })
      lastIndex = match.index + match[0].length
      match = regex.exec(html)
    }

    if (lastIndex < html.length) {
      segments.push({ type: 'text', html: html.slice(lastIndex) })
    }

    return segments
  }, [html])

  const hasWikiLinks = parts.some((p) => p.type === 'wikilink')

  if (!hasWikiLinks) {
    return (
      <div
        className={className}
        dangerouslySetInnerHTML={{ __html: html }}
      />
    )
  }

  return (
    <div className={className}>
      {parts.map((part, i) =>
        part.type === 'text' ? (
          <span key={i} dangerouslySetInnerHTML={{ __html: part.html }} />
        ) : (
          <WikiLink key={i} title={part.title} />
        ),
      )}
    </div>
  )
}
