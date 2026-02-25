import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router'
import {
  Unlink,
  Network,
  Sprout,
  ChevronRight,
  X,
  Link2,
  Loader2,
  ExternalLink,
  FileText,
  Layers,
  RefreshCw,
  AlertCircle,
  Scissors,
  Split,
} from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Skeleton } from '@/components/ui/skeleton'
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from '@/components/ui/dialog'
import { toast } from 'sonner'
import { relativeDate } from '@/lib/format'
import * as kbHealthApi from '@/api/kb-health'
import type { UnconnectedNote, ConnectionSuggestion, UnembeddedNote, LargeNote, SplitSuggestion, SuggestedNote } from '@/api/types'

// ── Scorecard ────────────────────────────────────────────────────────────────

function ScoreCard({
  label,
  value,
  icon: Icon,
  sub,
}: {
  label: string
  value: string | number
  icon: React.ElementType
  sub?: string
}) {
  return (
    <div className="rounded-lg border border-border/50 bg-card p-4">
      <div className="mb-2 flex items-center gap-2 text-muted-foreground">
        <Icon className="h-4 w-4" />
        <span className="text-xs font-medium uppercase tracking-wide">{label}</span>
      </div>
      <p className="text-2xl font-semibold tabular-nums">{value}</p>
      {sub && <p className="mt-0.5 text-xs text-muted-foreground">{sub}</p>}
    </div>
  )
}

// ── Suggestion side panel ────────────────────────────────────────────────────

function SuggestionPanel({
  orphan,
  onClose,
  onLinkSelected,
}: {
  orphan: UnconnectedNote
  onClose: () => void
  onLinkSelected: (suggestion: ConnectionSuggestion) => void
}) {
  const { data: suggestions, isLoading } = useQuery({
    queryKey: ['kb-health-suggestions', orphan.id],
    queryFn: () => kbHealthApi.getConnectionSuggestions(orphan.id),
  })

  return (
    <div className="flex h-full flex-col">
      <div className="flex items-start justify-between border-b border-border/50 pb-3">
        <div>
          <p className="text-xs text-muted-foreground">Suggestions for</p>
          <h3 className="font-medium">{orphan.title}</h3>
        </div>
        <Button variant="ghost" size="sm" onClick={onClose} className="h-7 w-7 p-0">
          <X className="h-4 w-4" />
        </Button>
      </div>

      <div className="mt-3 flex-1 space-y-2 overflow-auto">
        {isLoading && (
          <>
            <Skeleton className="h-12 w-full" />
            <Skeleton className="h-12 w-full" />
            <Skeleton className="h-12 w-full" />
          </>
        )}

        {!isLoading && suggestions?.length === 0 && (
          <p className="text-sm text-muted-foreground">
            No suggestions found. This note may need an embedding first.
          </p>
        )}

        {suggestions?.map((s) => (
          <div
            key={s.noteId}
            className="flex items-center justify-between rounded-md border border-border/50 bg-muted/30 px-3 py-2"
          >
            <div className="min-w-0 flex-1">
              <p className="truncate text-sm font-medium">{s.title}</p>
              <Badge variant="secondary" className="mt-0.5 text-[10px]">
                {s.similarity >= 0.8 ? 'High' : 'Medium'} match
              </Badge>
            </div>
            <Button
              size="sm"
              variant="ghost"
              className="ml-2 shrink-0 gap-1.5"
              onClick={() => onLinkSelected(s)}
            >
              <Link2 className="h-3.5 w-3.5" />
              Add link
            </Button>
          </div>
        ))}
      </div>
    </div>
  )
}

// ── Link preview dialog ──────────────────────────────────────────────────────

function LinkPreviewDialog({
  orphan,
  suggestion,
  onClose,
  onConfirm,
  isPending,
}: {
  orphan: UnconnectedNote
  suggestion: ConnectionSuggestion
  onClose: () => void
  onConfirm: () => void
  isPending: boolean
}) {
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle>Preview link insertion</DialogTitle>
        </DialogHeader>

        <div className="space-y-3">
          <p className="text-sm text-muted-foreground">
            The following wikilink will be appended to{' '}
            <span className="font-medium text-foreground">{orphan.title}</span>:
          </p>
          <div className="rounded-md border border-border/50 bg-muted/50 px-3 py-2 font-mono text-sm">
            [[{suggestion.title}]]
          </div>
          <p className="text-xs text-muted-foreground">
            The note's embedding will be queued for refresh after saving.
          </p>
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={isPending}>
            Cancel
          </Button>
          <Button onClick={onConfirm} disabled={isPending} className="gap-1.5">
            {isPending && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
            Insert link
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

// ── Embed status badge ───────────────────────────────────────────────────────

const embedStatusStyles: Record<string, string> = {
  Failed: 'bg-destructive/10 text-destructive border-destructive/20',
  Stale: 'bg-amber-500/10 text-amber-600 border-amber-500/20',
  Pending: 'bg-muted text-muted-foreground border-border/50',
  Processing: 'bg-blue-500/10 text-blue-600 border-blue-500/20',
}

function EmbedStatusBadge({ status }: { status: string }) {
  return (
    <span
      className={`inline-flex items-center rounded border px-1.5 py-0.5 text-[10px] font-medium ${embedStatusStyles[status] ?? 'bg-muted text-muted-foreground border-border/50'}`}
    >
      {status}
    </span>
  )
}

// ── Missing Embeddings section ───────────────────────────────────────────────

function MissingEmbeddingsSection() {
  const queryClient = useQueryClient()

  const { data: notes, isLoading } = useQuery({
    queryKey: ['kb-health-missing-embeddings'],
    queryFn: kbHealthApi.getNotesWithoutEmbeddings,
  })

  const requeueMutation = useMutation({
    mutationFn: (noteId: string) => kbHealthApi.requeueNoteEmbedding(noteId),
    onSuccess: () => {
      toast.success('Note queued for embedding')
      queryClient.invalidateQueries({ queryKey: ['kb-health-missing-embeddings'] })
      queryClient.invalidateQueries({ queryKey: ['kb-health'] })
    },
    onError: () => {
      toast.error('Failed to requeue note')
    },
  })

  return (
    <section>
      <div className="mb-3 flex items-center gap-2">
        <AlertCircle className="h-4 w-4 text-muted-foreground" />
        <h2 className="font-medium">Missing Embeddings</h2>
        {notes && (
          <Badge variant="secondary" className="ml-auto">
            {notes.length}
          </Badge>
        )}
      </div>

      {isLoading && <Skeleton className="h-32 w-full rounded-lg" />}

      {!isLoading && notes?.length === 0 && (
        <p className="text-sm text-muted-foreground">
          All permanent notes have completed embeddings.
        </p>
      )}

      {!isLoading && notes && notes.length > 0 && (
        <ul className="space-y-1.5">
          {notes.map((note: UnembeddedNote) => (
            <li
              key={note.id}
              className="flex items-center justify-between rounded-md border border-border/50 bg-card px-3 py-2 text-sm"
            >
              <div className="min-w-0 flex-1">
                <div className="flex items-center gap-2">
                  <Link
                    to={`/notes/${note.id}`}
                    className="truncate font-medium hover:underline"
                  >
                    {note.title}
                  </Link>
                  <EmbedStatusBadge status={note.embedStatus} />
                </div>
                {note.embedError && (
                  <p className="mt-0.5 truncate text-xs text-destructive">{note.embedError}</p>
                )}
              </div>
              <Button
                size="sm"
                variant="ghost"
                className="ml-3 shrink-0 gap-1.5"
                disabled={requeueMutation.isPending}
                onClick={() => requeueMutation.mutate(note.id)}
              >
                <RefreshCw className="h-3.5 w-3.5" />
                Requeue
              </Button>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}

// ── Split preview dialog ─────────────────────────────────────────────────────

function SplitDialog({
  suggestion,
  onClose,
  onConfirm,
  isPending,
}: {
  suggestion: SplitSuggestion
  onClose: () => void
  onConfirm: (notes: SuggestedNote[]) => void
  isPending: boolean
}) {
  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="max-w-2xl">
        <DialogHeader>
          <DialogTitle>Split: {suggestion.originalTitle}</DialogTitle>
        </DialogHeader>

        <div className="space-y-3">
          <p className="text-sm text-muted-foreground">
            The LLM suggests splitting this note into {suggestion.notes.length} atomic notes.
            The original note will be preserved.
          </p>

          <div className="max-h-[400px] space-y-2 overflow-y-auto pr-1">
            {suggestion.notes.map((note, i) => (
              <div
                key={i}
                className="rounded-md border border-border/50 bg-muted/30 px-3 py-2"
              >
                <p className="text-sm font-medium">{note.title}</p>
                <p className="mt-1 line-clamp-3 text-xs text-muted-foreground">{note.content}</p>
              </div>
            ))}
          </div>

          {suggestion.notes.length === 0 && (
            <p className="text-sm text-destructive">
              The LLM did not return any valid split suggestions. Try again.
            </p>
          )}
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={isPending}>
            Cancel
          </Button>
          <Button
            onClick={() => onConfirm(suggestion.notes)}
            disabled={isPending || suggestion.notes.length === 0}
            className="gap-1.5"
          >
            {isPending && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
            Create {suggestion.notes.length} notes
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

// ── Large Notes section ──────────────────────────────────────────────────────

function LargeNotesSection() {
  const queryClient = useQueryClient()
  const [splitSuggestion, setSplitSuggestion] = useState<SplitSuggestion | null>(null)
  const [suggestingNoteId, setSuggestingNoteId] = useState<string | null>(null)

  const { data: notes, isLoading } = useQuery({
    queryKey: ['kb-health-large-notes'],
    queryFn: kbHealthApi.getLargeNotes,
  })

  const summarizeMutation = useMutation({
    mutationFn: (noteId: string) => kbHealthApi.summarizeNote(noteId),
    onSuccess: (response) => {
      if (response.stillLarge) {
        toast.warning(
          `Summarized but still large (${response.summarizedLength} chars) — consider manual editing`,
        )
      } else {
        toast.success(
          `Summarized: ${response.originalLength} → ${response.summarizedLength} chars. Embedding queued.`,
        )
      }
      queryClient.invalidateQueries({ queryKey: ['kb-health-large-notes'] })
      queryClient.invalidateQueries({ queryKey: ['kb-health-missing-embeddings'] })
    },
    onError: () => {
      toast.error('Failed to summarize note')
    },
  })

  const splitSuggestMutation = useMutation({
    mutationFn: (noteId: string) => kbHealthApi.getSplitSuggestions(noteId),
    onSuccess: (data) => {
      setSplitSuggestion(data)
      setSuggestingNoteId(null)
    },
    onError: () => {
      setSuggestingNoteId(null)
      toast.error('Failed to generate split suggestions')
    },
  })

  const applySplitMutation = useMutation({
    mutationFn: ({ noteId, notes: splitNotes }: { noteId: string; notes: SuggestedNote[] }) =>
      kbHealthApi.applySplit(noteId, splitNotes),
    onSuccess: (response) => {
      toast.success(`Created ${response.createdNoteIds.length} notes. Original note preserved.`)
      setSplitSuggestion(null)
      queryClient.invalidateQueries({ queryKey: ['kb-health-large-notes'] })
      queryClient.invalidateQueries({ queryKey: ['kb-health'] })
    },
    onError: () => {
      toast.error('Failed to create notes from split')
    },
  })

  const handleSplitClick = (noteId: string) => {
    setSuggestingNoteId(noteId)
    splitSuggestMutation.mutate(noteId)
  }

  return (
    <section>
      <div className="mb-3 flex items-center gap-2">
        <Scissors className="h-4 w-4 text-muted-foreground" />
        <h2 className="font-medium">Large Notes</h2>
        {notes && (
          <Badge variant="secondary" className="ml-auto">
            {notes.length}
          </Badge>
        )}
      </div>

      {isLoading && <Skeleton className="h-32 w-full rounded-lg" />}

      {!isLoading && notes?.length === 0 && (
        <p className="text-sm text-muted-foreground">
          All permanent notes are within the embedding character limit.
        </p>
      )}

      {!isLoading && notes && notes.length > 0 && (
        <ul className="space-y-1.5">
          {notes.map((note: LargeNote) => (
            <li
              key={note.id}
              className="flex items-center justify-between rounded-md border border-border/50 bg-card px-3 py-2 text-sm"
            >
              <div className="min-w-0 flex-1">
                <div className="flex items-center gap-2">
                  <Link
                    to={`/notes/${note.id}`}
                    className="truncate font-medium hover:underline"
                  >
                    {note.title}
                  </Link>
                  <Badge variant="outline" className="shrink-0 text-[10px]">
                    {note.characterCount.toLocaleString()} chars
                  </Badge>
                </div>
              </div>
              <div className="ml-3 flex shrink-0 gap-1">
                <Button
                  size="sm"
                  variant="ghost"
                  className="gap-1.5"
                  disabled={summarizeMutation.isPending && summarizeMutation.variables === note.id}
                  onClick={() => summarizeMutation.mutate(note.id)}
                >
                  {summarizeMutation.isPending && summarizeMutation.variables === note.id ? (
                    <Loader2 className="h-3.5 w-3.5 animate-spin" />
                  ) : (
                    <Scissors className="h-3.5 w-3.5" />
                  )}
                  Summarize
                </Button>
                <Button
                  size="sm"
                  variant="ghost"
                  className="gap-1.5"
                  disabled={suggestingNoteId === note.id}
                  onClick={() => handleSplitClick(note.id)}
                >
                  {suggestingNoteId === note.id ? (
                    <Loader2 className="h-3.5 w-3.5 animate-spin" />
                  ) : (
                    <Split className="h-3.5 w-3.5" />
                  )}
                  Split
                </Button>
              </div>
            </li>
          ))}
        </ul>
      )}

      {splitSuggestion && (
        <SplitDialog
          suggestion={splitSuggestion}
          onClose={() => setSplitSuggestion(null)}
          onConfirm={(confirmedNotes) =>
            applySplitMutation.mutate({ noteId: splitSuggestion.noteId, notes: confirmedNotes })
          }
          isPending={applySplitMutation.isPending}
        />
      )}
    </section>
  )
}

// ── Main page ────────────────────────────────────────────────────────────────

export function KbHealthPage() {
  const queryClient = useQueryClient()

  const [selectedOrphan, setSelectedOrphan] = useState<UnconnectedNote | null>(null)
  const [pendingLink, setPendingLink] = useState<ConnectionSuggestion | null>(null)

  const { data: overview, isLoading } = useQuery({
    queryKey: ['kb-health'],
    queryFn: kbHealthApi.getKbHealthOverview,
  })

  const addLinkMutation = useMutation({
    mutationFn: ({ orphanId, targetId }: { orphanId: string; targetId: string }) =>
      kbHealthApi.addLink(orphanId, targetId),
    onSuccess: () => {
      toast.success('Link added — note queued for re-embedding')
      setPendingLink(null)
      setSelectedOrphan(null)
      queryClient.invalidateQueries({ queryKey: ['kb-health'] })
    },
    onError: () => {
      toast.error('Failed to add link')
    },
  })

  const scorecard = overview?.scorecard

  return (
    <div className="mx-auto max-w-2xl px-4 py-8">
      <div className="mb-6">
        <h1 className="font-serif text-2xl font-bold tracking-tight">Knowledge Health</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Weekly view of your KB's structure — clusters, orphans, and untapped seeds.
        </p>
      </div>

      {/* Scorecard */}
      <div className="mb-8 grid grid-cols-2 gap-3 sm:grid-cols-4">
        {isLoading ? (
          Array.from({ length: 4 }).map((_, i) => (
            <Skeleton key={i} className="h-24 w-full rounded-lg" />
          ))
        ) : (
          <>
            <ScoreCard
              label="Notes"
              value={scorecard?.totalNotes ?? 0}
              icon={FileText}
              sub="permanent"
            />
            <ScoreCard
              label="Embedded"
              value={`${scorecard?.embeddedPercent ?? 0}%`}
              icon={Layers}
              sub="with vectors"
            />
            <ScoreCard
              label="Orphans"
              value={scorecard?.orphanCount ?? 0}
              icon={Unlink}
              sub="last 30 days"
            />
            <ScoreCard
              label="Avg links"
              value={scorecard?.averageConnections ?? 0}
              icon={Network}
              sub="per note"
            />
          </>
        )}
      </div>

      <div className={`flex gap-6 ${selectedOrphan ? 'items-start' : ''}`}>
        {/* Left column: lists */}
        <div className="min-w-0 flex-1 space-y-8">

          {/* New & Unconnected */}
          <section>
            <div className="mb-3 flex items-center gap-2">
              <Unlink className="h-4 w-4 text-muted-foreground" />
              <h2 className="font-medium">New &amp; Unconnected</h2>
              {overview && (
                <Badge variant="secondary" className="ml-auto">
                  {overview.newAndUnconnected.length}
                </Badge>
              )}
            </div>

            {isLoading && <Skeleton className="h-32 w-full rounded-lg" />}

            {!isLoading && overview?.newAndUnconnected.length === 0 && (
              <p className="text-sm text-muted-foreground">
                No recent orphans — your new notes are well connected.
              </p>
            )}

            {!isLoading && (
              <ul className="space-y-1.5">
                {overview?.newAndUnconnected.map((note) => (
                  <li key={note.id}>
                    <button
                      onClick={() =>
                        setSelectedOrphan(selectedOrphan?.id === note.id ? null : note)
                      }
                      className={`flex w-full items-center justify-between rounded-md border px-3 py-2 text-left text-sm transition-colors hover:bg-muted/50 ${
                        selectedOrphan?.id === note.id
                          ? 'border-border bg-muted/50'
                          : 'border-border/50 bg-card'
                      }`}
                    >
                      <div className="min-w-0">
                        <p className="truncate font-medium">{note.title}</p>
                        <p className="text-xs text-muted-foreground">
                          {relativeDate(note.createdAt)}
                          {note.suggestionCount > 0 &&
                            ` · ${note.suggestionCount} suggestions`}
                        </p>
                      </div>
                      <ChevronRight
                        className={`ml-2 h-4 w-4 shrink-0 text-muted-foreground transition-transform ${
                          selectedOrphan?.id === note.id ? 'rotate-90' : ''
                        }`}
                      />
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </section>

          {/* Richest Clusters */}
          <section>
            <div className="mb-3 flex items-center gap-2">
              <Network className="h-4 w-4 text-muted-foreground" />
              <h2 className="font-medium">Richest Clusters</h2>
            </div>

            {isLoading && <Skeleton className="h-32 w-full rounded-lg" />}

            {!isLoading && overview?.richestClusters.length === 0 && (
              <p className="text-sm text-muted-foreground">
                No clusters yet — add more notes and connections.
              </p>
            )}

            {!isLoading && (
              <ol className="space-y-1.5">
                {overview?.richestClusters.map((cluster, i) => (
                  <li
                    key={cluster.hubNoteId}
                    className="flex items-center justify-between rounded-md border border-border/50 bg-card px-3 py-2 text-sm"
                  >
                    <div className="flex items-center gap-3 min-w-0">
                      <span className="shrink-0 font-mono text-xs text-muted-foreground">
                        {i + 1}
                      </span>
                      <Link
                        to={`/notes/${cluster.hubNoteId}`}
                        className="truncate font-medium hover:underline"
                      >
                        {cluster.hubTitle}
                      </Link>
                    </div>
                    <div className="ml-2 flex shrink-0 items-center gap-1.5">
                      <Badge variant="secondary">{cluster.noteCount} notes</Badge>
                      <Link to={`/notes/${cluster.hubNoteId}`}>
                        <ExternalLink className="h-3.5 w-3.5 text-muted-foreground hover:text-foreground" />
                      </Link>
                    </div>
                  </li>
                ))}
              </ol>
            )}
          </section>

          {/* Never Used as Seeds */}
          <section>
            <div className="mb-3 flex items-center gap-2">
              <Sprout className="h-4 w-4 text-muted-foreground" />
              <h2 className="font-medium">Never Used as Seeds</h2>
              {overview && (
                <Badge variant="secondary" className="ml-auto">
                  {overview.neverUsedAsSeeds.length}
                </Badge>
              )}
            </div>

            {isLoading && <Skeleton className="h-32 w-full rounded-lg" />}

            {!isLoading && overview?.neverUsedAsSeeds.length === 0 && (
              <p className="text-sm text-muted-foreground">
                All embedded notes have been used as seeds.
              </p>
            )}

            {!isLoading && (
              <ul className="space-y-1.5">
                {overview?.neverUsedAsSeeds.slice(0, 10).map((note) => (
                  <li
                    key={note.id}
                    className="flex items-center justify-between rounded-md border border-border/50 bg-card px-3 py-2 text-sm"
                  >
                    <Link
                      to={`/notes/${note.id}`}
                      className="min-w-0 truncate font-medium hover:underline"
                    >
                      {note.title}
                    </Link>
                    <Badge variant="outline" className="ml-2 shrink-0 text-xs">
                      {note.connectionCount} links
                    </Badge>
                  </li>
                ))}
              </ul>
            )}
          </section>

          {/* Missing Embeddings */}
          <MissingEmbeddingsSection />

          {/* Large Notes */}
          <LargeNotesSection />
        </div>

        {/* Right column: suggestion panel */}
        {selectedOrphan && (
          <div className="w-72 shrink-0 rounded-lg border border-border/50 bg-card p-4">
            <SuggestionPanel
              orphan={selectedOrphan}
              onClose={() => setSelectedOrphan(null)}
              onLinkSelected={(s) => setPendingLink(s)}
            />
          </div>
        )}
      </div>

      {/* Link preview dialog */}
      {pendingLink && selectedOrphan && (
        <LinkPreviewDialog
          orphan={selectedOrphan}
          suggestion={pendingLink}
          onClose={() => setPendingLink(null)}
          onConfirm={() =>
            addLinkMutation.mutate({
              orphanId: selectedOrphan.id,
              targetId: pendingLink.noteId,
            })
          }
          isPending={addLinkMutation.isPending}
        />
      )}
    </div>
  )
}
