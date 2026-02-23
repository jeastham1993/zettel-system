import { useState, useCallback } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  Sparkles,
  ChevronDown,
  ChevronRight,
  Check,
  X,
  Download,
  FileText,
  MessageSquare,
  Loader2,
  RotateCcw,
  Trash2,
} from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { toast } from 'sonner'
import { relativeDate } from '@/lib/format'
import * as contentApi from '@/api/content'
import type {
  ContentGeneration,
  ContentPiece,
  ContentPieceStatus,
} from '@/api/types'

type StatusFilter = 'all' | 'Pending' | 'Approved' | 'Rejected'

function statusBadgeVariant(status: ContentPieceStatus | string) {
  switch (status) {
    case 'Approved':
      return 'default' as const
    case 'Rejected':
      return 'destructive' as const
    default:
      return 'secondary' as const
  }
}

function pieceSummary(pieces: ContentPiece[] | undefined) {
  if (!pieces || pieces.length === 0) return 'No pieces'
  const blog = pieces.filter((p) => p.medium === 'blog').length
  const social = pieces.filter((p) => p.medium === 'social').length
  const parts: string[] = []
  if (blog > 0) parts.push(`${blog} blog ${blog === 1 ? 'post' : 'posts'}`)
  if (social > 0) parts.push(`${social} social ${social === 1 ? 'post' : 'posts'}`)
  return parts.join(', ')
}

function PieceCard({ piece }: { piece: ContentPiece }) {
  const queryClient = useQueryClient()
  const isBlog = piece.medium === 'blog'

  const approve = useMutation({
    mutationFn: () => contentApi.approvePiece(piece.id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['generations'] })
      toast.success('Piece approved')
    },
    onError: () => toast.error('Failed to approve piece'),
  })

  const reject = useMutation({
    mutationFn: () => contentApi.rejectPiece(piece.id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['generations'] })
      toast.success('Piece rejected')
    },
    onError: () => toast.error('Failed to reject piece'),
  })

  const regenerate = useMutation({
    mutationFn: () => contentApi.regenerateMedium(piece.generationId, piece.medium as 'blog' | 'social'),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['generations', piece.generationId] })
      toast.success(`${isBlog ? 'Blog' : 'Social'} content regenerated`)
    },
    onError: () => toast.error('Failed to regenerate content'),
  })

  const handleExport = useCallback(async () => {
    try {
      const blob = await contentApi.exportPiece(piece.id)
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = `${piece.title ?? piece.medium}-${piece.id.slice(0, 8)}.md`
      a.click()
      URL.revokeObjectURL(url)
    } catch {
      toast.error('Failed to export piece')
    }
  }, [piece.id, piece.title, piece.medium])

  const isDraft = piece.status === 'Draft'

  return (
    <div className="rounded-lg border border-border/50 p-4">
      <div className="flex items-start justify-between gap-3">
        <div className="flex items-center gap-2">
          {isBlog ? (
            <FileText className="size-4 text-muted-foreground" />
          ) : (
            <MessageSquare className="size-4 text-muted-foreground" />
          )}
          <span className="text-xs font-medium uppercase text-muted-foreground">
            {piece.medium}
          </span>
          <Badge variant={statusBadgeVariant(piece.status)} className="text-xs">
            {piece.status}
          </Badge>
        </div>
        <time className="shrink-0 text-xs text-muted-foreground">
          {relativeDate(piece.createdAt)}
        </time>
      </div>

      {piece.title && (
        <h4 className="mt-2 font-serif text-base font-semibold">{piece.title}</h4>
      )}

      <div className="mt-2 whitespace-pre-wrap text-sm leading-relaxed text-foreground/90">
        {piece.body}
      </div>

      <div className="mt-3 flex items-center gap-1.5">
        {isDraft && (
          <>
            <Button
              variant="ghost"
              size="xs"
              onClick={() => approve.mutate()}
              disabled={approve.isPending || reject.isPending || regenerate.isPending}
              className="gap-1 text-green-600 hover:text-green-700 dark:text-green-400"
            >
              <Check className="size-3" />
              Approve
            </Button>
            <Button
              variant="ghost"
              size="xs"
              onClick={() => reject.mutate()}
              disabled={approve.isPending || reject.isPending || regenerate.isPending}
              className="gap-1 text-red-600 hover:text-red-700 dark:text-red-400"
            >
              <X className="size-3" />
              Reject
            </Button>
            <Button
              variant="ghost"
              size="xs"
              onClick={() => regenerate.mutate()}
              disabled={approve.isPending || reject.isPending || regenerate.isPending}
              className="gap-1 text-muted-foreground"
            >
              {regenerate.isPending ? (
                <Loader2 className="size-3 animate-spin" />
              ) : (
                <RotateCcw className="size-3" />
              )}
              Regenerate
            </Button>
          </>
        )}
        <Button
          variant="ghost"
          size="xs"
          onClick={handleExport}
          className="gap-1 text-muted-foreground"
        >
          <Download className="size-3" />
          Export
        </Button>
      </div>
    </div>
  )
}

function GenerationCard({ generation }: { generation: ContentGeneration }) {
  const [expanded, setExpanded] = useState(false)
  const queryClient = useQueryClient()

  const detailQuery = useQuery({
    queryKey: ['generations', generation.id],
    queryFn: () => contentApi.getGeneration(generation.id),
    enabled: expanded,
  })

  const regenerateFull = useMutation({
    mutationFn: () => contentApi.regenerateGeneration(generation.id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['generations'] })
      toast.success('New generation created from the same notes')
    },
    onError: () => toast.error('Failed to regenerate content'),
  })

  const deleteMutation = useMutation({
    mutationFn: () => contentApi.deleteGeneration(generation.id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['generations'] })
      toast.success('Generation deleted')
    },
    onError: () => toast.error('Failed to delete generation'),
  })

  const pieces = detailQuery.data?.pieces ?? generation.pieces ?? []
  const blogPieces = pieces.filter((p) => p.medium === 'blog')
  const socialPieces = pieces.filter((p) => p.medium === 'social')

  return (
    <div className="rounded-lg border border-border/50 transition-colors hover:border-border">
      <div className="flex w-full items-start gap-3 px-4 py-4">
        <button
          onClick={() => {
            setExpanded(!expanded)
            if (!expanded) {
              queryClient.invalidateQueries({
                queryKey: ['generations', generation.id],
              })
            }
          }}
          className="flex min-w-0 flex-1 items-start gap-3 text-left"
        >
          <div className="mt-0.5 shrink-0 text-muted-foreground">
            {expanded ? (
              <ChevronDown className="size-4" />
            ) : (
              <ChevronRight className="size-4" />
            )}
          </div>
          <div className="min-w-0 flex-1">
            <p className="text-sm font-medium">{generation.topicSummary}</p>
            <div className="mt-1 flex flex-wrap items-center gap-2">
              <Badge
                variant={statusBadgeVariant(generation.status)}
                className="text-xs"
              >
                {generation.status}
              </Badge>
              <span className="text-xs text-muted-foreground">
                {pieceSummary(pieces.length > 0 ? pieces : undefined)}
              </span>
            </div>
          </div>
        </button>
        <div className="flex shrink-0 items-center gap-2">
          <time className="text-xs text-muted-foreground">
            {relativeDate(generation.generatedAt)}
          </time>
          {generation.status !== 'Approved' && (
            <Button
              variant="ghost"
              size="xs"
              onClick={() => regenerateFull.mutate()}
              disabled={regenerateFull.isPending || deleteMutation.isPending}
              className="gap-1 text-muted-foreground"
            >
              {regenerateFull.isPending ? (
                <Loader2 className="size-3 animate-spin" />
              ) : (
                <RotateCcw className="size-3" />
              )}
              Regenerate
            </Button>
          )}
          <Button
            variant="ghost"
            size="xs"
            onClick={() => deleteMutation.mutate()}
            disabled={regenerateFull.isPending || deleteMutation.isPending}
            className="gap-1 text-destructive hover:text-destructive"
          >
            {deleteMutation.isPending ? (
              <Loader2 className="size-3 animate-spin" />
            ) : (
              <Trash2 className="size-3" />
            )}
            Delete
          </Button>
        </div>
      </div>

      {expanded && (
        <div className="border-t border-border/50 px-4 py-4">
          {detailQuery.isLoading && (
            <div className="space-y-3">
              <Skeleton className="h-24 w-full" />
              <Skeleton className="h-16 w-full" />
            </div>
          )}

          {detailQuery.isError && (
            <p className="text-sm text-destructive">
              Failed to load generation details.
            </p>
          )}

          {!detailQuery.isLoading && pieces.length === 0 && (
            <p className="text-sm text-muted-foreground">
              No content pieces generated yet.
            </p>
          )}

          {blogPieces.length > 0 && (
            <div className="space-y-3">
              <h3 className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
                Blog Posts
              </h3>
              {blogPieces.map((piece) => (
                <PieceCard key={piece.id} piece={piece} />
              ))}
            </div>
          )}

          {socialPieces.length > 0 && (
            <div className={`space-y-3 ${blogPieces.length > 0 ? 'mt-4' : ''}`}>
              <h3 className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
                Social Posts
              </h3>
              {socialPieces.map((piece) => (
                <PieceCard key={piece.id} piece={piece} />
              ))}
            </div>
          )}
        </div>
      )}
    </div>
  )
}

export function ContentReviewPage() {
  const [filter, setFilter] = useState<StatusFilter>('all')
  const queryClient = useQueryClient()

  const { data, isLoading } = useQuery({
    queryKey: ['generations'],
    queryFn: () => contentApi.listGenerations(0, 50),
  })

  const generate = useMutation({
    mutationFn: contentApi.triggerGeneration,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['generations'] })
      toast.success('Content generation started')
    },
    onError: () => toast.error('Failed to generate content. Are there eligible notes?'),
  })

  const generations = data?.items ?? []
  const filtered =
    filter === 'all'
      ? generations
      : generations.filter((g) => g.status === filter)

  const filters: { label: string; value: StatusFilter }[] = [
    { label: 'All', value: 'all' },
    { label: 'Pending', value: 'Pending' },
    { label: 'Approved', value: 'Approved' },
    { label: 'Rejected', value: 'Rejected' },
  ]

  if (isLoading) {
    return (
      <div className="mx-auto max-w-2xl px-4 py-8">
        <Skeleton className="mb-6 h-8 w-48" />
        <div className="space-y-4">
          {Array.from({ length: 3 }).map((_, i) => (
            <div key={i} className="space-y-2 rounded-lg border border-border/50 px-4 py-4">
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
      <div className="mb-6 flex items-center justify-between">
        <div>
          <h1 className="font-serif text-2xl font-bold tracking-tight">
            Content Review
          </h1>
          {generations.length > 0 && (
            <p className="mt-1 text-sm text-muted-foreground">
              {generations.length}{' '}
              {generations.length === 1 ? 'generation' : 'generations'}
            </p>
          )}
        </div>
        <Button
          onClick={() => generate.mutate()}
          disabled={generate.isPending}
          size="sm"
          className="gap-1.5"
        >
          {generate.isPending ? (
            <Loader2 className="size-4 animate-spin" />
          ) : (
            <Sparkles className="size-4" />
          )}
          Generate Now
        </Button>
      </div>

      <div className="mb-4 flex gap-1">
        {filters.map((f) => (
          <Button
            key={f.value}
            variant={filter === f.value ? 'secondary' : 'ghost'}
            size="xs"
            onClick={() => setFilter(f.value)}
          >
            {f.label}
          </Button>
        ))}
      </div>

      {filtered.length === 0 ? (
        <div className="flex flex-col items-center justify-center py-20 text-center">
          <Sparkles className="size-12 text-muted-foreground/30" />
          <h2 className="mt-4 font-serif text-lg font-medium">
            {filter === 'all' ? 'No content generated yet' : `No ${filter.toLowerCase()} content`}
          </h2>
          <p className="mt-1 text-sm text-muted-foreground">
            {filter === 'all'
              ? 'Click "Generate Now" to create content from your notes.'
              : 'Try a different filter or generate new content.'}
          </p>
        </div>
      ) : (
        <div className="space-y-3">
          {filtered.map((gen) => (
            <GenerationCard key={gen.id} generation={gen} />
          ))}
        </div>
      )}
    </div>
  )
}
