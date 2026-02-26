import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ExternalLink, BookOpen, Search, CheckCircle, XCircle, Loader2 } from 'lucide-react'
import { Link } from 'react-router'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Skeleton } from '@/components/ui/skeleton'
import { toast } from 'sonner'
import * as researchApi from '@/api/research'
import type { ResearchFinding } from '@/api/types'

function FindingCard({ finding }: { finding: ResearchFinding }) {
  const queryClient = useQueryClient()

  const acceptMutation = useMutation({
    mutationFn: () => researchApi.acceptFinding(finding.id),
    onSuccess: () => {
      toast.success('Added to inbox')
      queryClient.invalidateQueries({ queryKey: ['research-findings'] })
      queryClient.invalidateQueries({ queryKey: ['inbox', 'count'] }) // C4: correct key
    },
    onError: () => toast.error('Failed to accept finding'),
  })

  const dismissMutation = useMutation({
    mutationFn: () => researchApi.dismissFinding(finding.id),
    onSuccess: () => {
      toast.success('Dismissed')
      queryClient.invalidateQueries({ queryKey: ['research-findings'] })
    },
    onError: () => toast.error('Failed to dismiss finding'),
  })

  const isPending = acceptMutation.isPending || dismissMutation.isPending

  return (
    <div className="rounded-lg border border-border/50 bg-card p-4">
      <div className="mb-2 flex items-start justify-between gap-2">
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2">
            {finding.sourceType === 'Arxiv' ? (
              <BookOpen className="h-4 w-4 shrink-0 text-muted-foreground" />
            ) : (
              <Search className="h-4 w-4 shrink-0 text-muted-foreground" />
            )}
            <h3 className="truncate font-medium">{finding.title}</h3>
          </div>
          <a
            href={finding.sourceUrl}
            target="_blank"
            rel="noopener noreferrer"
            className="mt-0.5 flex items-center gap-1 text-xs text-muted-foreground hover:text-foreground"
          >
            <ExternalLink className="h-3 w-3 shrink-0" />
            <span className="truncate">{finding.sourceUrl}</span>
          </a>
        </div>
        <Badge variant="secondary" className="shrink-0 text-[10px]">
          {finding.sourceType === 'Arxiv' ? 'Arxiv' : 'Web'}
        </Badge>
      </div>

      <p className="text-sm text-muted-foreground">{finding.synthesis}</p>

      <div className="mt-3 flex justify-end gap-2">
        <Button
          size="sm"
          variant="ghost"
          className="gap-1.5 text-muted-foreground"
          onClick={() => dismissMutation.mutate()}
          disabled={isPending}
        >
          <XCircle className="h-3.5 w-3.5" />
          Dismiss
        </Button>
        <Button
          size="sm"
          className="gap-1.5"
          onClick={() => acceptMutation.mutate()}
          disabled={isPending}
        >
          <CheckCircle className="h-3.5 w-3.5" />
          Add to inbox
        </Button>
      </div>
    </div>
  )
}

export function ResearchPage() {
  const { data: findings, isLoading } = useQuery({
    queryKey: ['research-findings'],
    queryFn: researchApi.getResearchFindings,
    // I6: poll every 8s so results appear after async execution completes
    refetchInterval: (query) =>
      (query.state.data?.length ?? 0) === 0 ? 8000 : false,
  })

  return (
    <div className="mx-auto max-w-2xl px-4 py-8">
      <div className="mb-6">
        <h1 className="font-serif text-2xl font-bold tracking-tight">Research Inbox</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Review findings from the research agent. Accept to add to your fleeting notes inbox, or dismiss to discard.
        </p>
      </div>

      {isLoading && (
        <div className="space-y-3">
          <Skeleton className="h-36 w-full rounded-lg" />
          <Skeleton className="h-36 w-full rounded-lg" />
          <Skeleton className="h-36 w-full rounded-lg" />
        </div>
      )}

      {/* I6: distinguish "nothing run yet" from "results pending" */}
      {!isLoading && findings?.length === 0 && (
        <div className="rounded-lg border border-dashed border-border/50 px-6 py-12 text-center">
          <div className="mx-auto mb-3 flex h-8 w-8 items-center justify-center">
            <Loader2 className="h-6 w-6 animate-spin text-muted-foreground/50" />
          </div>
          <p className="text-sm font-medium">No findings yet</p>
          <p className="mt-1 text-xs text-muted-foreground">
            If a research run is in progress, findings will appear here automatically.
            Otherwise, trigger research from the{' '}
            {/* I10: use Link not <a> to avoid full page reload */}
            <Link to="/kb-health" className="underline hover:text-foreground">
              Knowledge Health
            </Link>{' '}
            dashboard.
          </p>
        </div>
      )}

      {!isLoading && findings && findings.length > 0 && (
        <div className="space-y-3">
          {findings.map((finding: ResearchFinding) => (
            <FindingCard key={finding.id} finding={finding} />
          ))}
        </div>
      )}
    </div>
  )
}
