import { useState, useCallback } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { Plus, Trash2, Save, Loader2, Mic } from 'lucide-react'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { Separator } from '@/components/ui/separator'
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
  DialogFooter,
  DialogClose,
} from '@/components/ui/dialog'
import { ConfirmDialog } from '@/components/confirm-dialog'
import { toast } from 'sonner'
import { truncateContent } from '@/lib/format'
import * as contentApi from '@/api/content'
import type { VoiceExample } from '@/api/types'

type MediumTab = 'all' | 'blog' | 'social'

const MEDIUM_TABS: { label: string; value: MediumTab }[] = [
  { label: 'All', value: 'all' },
  { label: 'Blog', value: 'blog' },
  { label: 'Social', value: 'social' },
]

function TabBar({
  active,
  onChange,
}: {
  active: MediumTab
  onChange: (tab: MediumTab) => void
}) {
  return (
    <div className="flex gap-1">
      {MEDIUM_TABS.map((tab) => (
        <Button
          key={tab.value}
          variant={active === tab.value ? 'secondary' : 'ghost'}
          size="xs"
          onClick={() => onChange(tab.value)}
        >
          {tab.label}
        </Button>
      ))}
    </div>
  )
}

function AddExampleDialog({
  open,
  onOpenChange,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
}) {
  const queryClient = useQueryClient()
  const [medium, setMedium] = useState<'blog' | 'social'>('blog')
  const [title, setTitle] = useState('')
  const [content, setContent] = useState('')
  const [source, setSource] = useState('')

  const addExample = useMutation({
    mutationFn: contentApi.addVoiceExample,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['voice-examples'] })
      toast.success('Example added')
      onOpenChange(false)
      setMedium('blog')
      setTitle('')
      setContent('')
      setSource('')
    },
    onError: () => toast.error('Failed to add example'),
  })

  const handleSubmit = useCallback(
    (e: React.FormEvent) => {
      e.preventDefault()
      if (!content.trim()) return
      addExample.mutate({
        medium,
        title: title.trim() || undefined,
        content: content.trim(),
        source: source.trim() || undefined,
      })
    },
    [medium, title, content, source, addExample],
  )

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Add writing example</DialogTitle>
          <DialogDescription>
            Provide a sample of your writing to help guide content generation.
          </DialogDescription>
        </DialogHeader>
        <form onSubmit={handleSubmit} className="space-y-4">
          <div className="space-y-1.5">
            <label className="text-sm font-medium">Medium</label>
            <div className="flex gap-1">
              <Button
                type="button"
                variant={medium === 'blog' ? 'secondary' : 'ghost'}
                size="xs"
                onClick={() => setMedium('blog')}
              >
                Blog
              </Button>
              <Button
                type="button"
                variant={medium === 'social' ? 'secondary' : 'ghost'}
                size="xs"
                onClick={() => setMedium('social')}
              >
                Social
              </Button>
            </div>
          </div>

          <div className="space-y-1.5">
            <label className="text-sm font-medium">
              Title <span className="text-muted-foreground">(optional)</span>
            </label>
            <input
              type="text"
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              placeholder="e.g. My best blog post"
              className="flex w-full rounded-md border bg-transparent px-3 py-2 text-sm outline-none placeholder:text-muted-foreground focus:ring-1 focus:ring-ring"
            />
          </div>

          <div className="space-y-1.5">
            <label className="text-sm font-medium">Content</label>
            <textarea
              value={content}
              onChange={(e) => setContent(e.target.value)}
              placeholder="Paste your writing sample here..."
              rows={8}
              className="flex w-full rounded-md border bg-transparent px-3 py-2 text-sm leading-relaxed outline-none placeholder:text-muted-foreground focus:ring-1 focus:ring-ring"
              required
            />
          </div>

          <div className="space-y-1.5">
            <label className="text-sm font-medium">
              Source <span className="text-muted-foreground">(optional)</span>
            </label>
            <input
              type="text"
              value={source}
              onChange={(e) => setSource(e.target.value)}
              placeholder="e.g. https://myblog.com/post"
              className="flex w-full rounded-md border bg-transparent px-3 py-2 text-sm outline-none placeholder:text-muted-foreground focus:ring-1 focus:ring-ring"
            />
          </div>

          <DialogFooter>
            <DialogClose asChild>
              <Button type="button" variant="outline">
                Cancel
              </Button>
            </DialogClose>
            <Button type="submit" disabled={addExample.isPending || !content.trim()}>
              {addExample.isPending ? (
                <Loader2 className="size-4 animate-spin" />
              ) : (
                <Plus className="size-4" />
              )}
              Add example
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}

function ExampleItem({ example }: { example: VoiceExample }) {
  const queryClient = useQueryClient()

  const deleteExample = useMutation({
    mutationFn: () => contentApi.deleteVoiceExample(example.id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['voice-examples'] })
      toast.success('Example deleted')
    },
    onError: () => toast.error('Failed to delete example'),
  })

  return (
    <div className="group rounded-lg border border-border/50 px-4 py-3 transition-colors hover:border-border">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0 flex-1">
          {example.title && (
            <p className="text-sm font-medium">{example.title}</p>
          )}
          <p className="mt-1 text-sm leading-relaxed text-muted-foreground">
            {truncateContent(example.content, 200)}
          </p>
          <div className="mt-2 flex items-center gap-2">
            <Badge variant="secondary" className="text-xs font-normal">
              {example.medium}
            </Badge>
            {example.source && (
              <span className="text-xs text-muted-foreground">
                {example.source}
              </span>
            )}
          </div>
        </div>
        <ConfirmDialog
          trigger={
            <Button
              variant="ghost"
              size="icon-xs"
              className="shrink-0 text-muted-foreground opacity-0 group-hover:opacity-100"
            >
              <Trash2 className="size-3" />
            </Button>
          }
          title="Delete example?"
          description="This will permanently remove this writing example."
          confirmLabel="Delete"
          onConfirm={() => deleteExample.mutate()}
        />
      </div>
    </div>
  )
}

function StyleNotesSection({ activeTab }: { activeTab: MediumTab }) {
  const queryClient = useQueryClient()

  const { data: configs, isLoading } = useQuery({
    queryKey: ['voice-config'],
    queryFn: () => contentApi.getVoiceConfig(),
  })

  const medium = activeTab === 'all' ? 'blog' : activeTab
  const config = configs?.find((c) => c.medium === medium)
  const [notes, setNotes] = useState('')
  const [initialized, setInitialized] = useState(false)

  // Sync state when config loads or medium changes
  if (config && (!initialized || config.styleNotes !== notes)) {
    if (!initialized) {
      setNotes(config.styleNotes ?? '')
      setInitialized(true)
    }
  }

  // Reset when medium changes
  const [prevMedium, setPrevMedium] = useState(medium)
  if (medium !== prevMedium) {
    setPrevMedium(medium)
    setNotes(config?.styleNotes ?? '')
  }

  const saveConfig = useMutation({
    mutationFn: contentApi.updateVoiceConfig,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['voice-config'] })
      toast.success('Style notes saved')
    },
    onError: () => toast.error('Failed to save style notes'),
  })

  const handleSave = useCallback(() => {
    saveConfig.mutate({ medium, styleNotes: notes.trim() || undefined })
  }, [medium, notes, saveConfig])

  if (isLoading) {
    return (
      <div className="space-y-3">
        <Skeleton className="h-4 w-32" />
        <Skeleton className="h-32 w-full" />
      </div>
    )
  }

  return (
    <div className="space-y-3">
      <div className="flex items-center justify-between">
        <div>
          <p className="text-sm text-muted-foreground">
            Guide the tone, vocabulary, and style for{' '}
            <span className="font-medium text-foreground">{medium}</span> content.
          </p>
        </div>
      </div>
      <textarea
        value={notes}
        onChange={(e) => setNotes(e.target.value)}
        placeholder={`e.g. Use a conversational tone. Avoid jargon. Write for a technical audience who values clarity...`}
        rows={6}
        className="flex w-full rounded-md border bg-transparent px-3 py-2 text-sm leading-relaxed outline-none placeholder:text-muted-foreground focus:ring-1 focus:ring-ring"
      />
      <Button
        variant="outline"
        size="sm"
        onClick={handleSave}
        disabled={saveConfig.isPending}
        className="gap-1.5"
      >
        {saveConfig.isPending ? (
          <Loader2 className="h-3.5 w-3.5 animate-spin" />
        ) : (
          <Save className="h-3.5 w-3.5" />
        )}
        Save
      </Button>
    </div>
  )
}

export function VoiceConfigPage() {
  const [styleTab, setStyleTab] = useState<MediumTab>('blog')
  const [examplesTab, setExamplesTab] = useState<MediumTab>('all')
  const [addOpen, setAddOpen] = useState(false)

  const { data: examples, isLoading: examplesLoading } = useQuery({
    queryKey: ['voice-examples'],
    queryFn: contentApi.listVoiceExamples,
  })

  const filteredExamples =
    examplesTab === 'all'
      ? examples ?? []
      : (examples ?? []).filter((e) => e.medium === examplesTab)

  return (
    <div className="mx-auto max-w-2xl px-4 py-8">
      <div className="mb-6">
        <h1 className="font-serif text-2xl font-bold tracking-tight">
          Voice & Style
        </h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Configure how generated content should sound.
        </p>
      </div>

      {/* Style Notes */}
      <section className="space-y-3">
        <h2 className="text-sm font-medium">Style Notes</h2>
        <TabBar active={styleTab} onChange={setStyleTab} />
        <StyleNotesSection activeTab={styleTab} />
      </section>

      <Separator className="my-8" />

      {/* Writing Examples */}
      <section className="space-y-3">
        <div className="flex items-center justify-between">
          <h2 className="text-sm font-medium">Writing Examples</h2>
          <Button
            variant="outline"
            size="sm"
            onClick={() => setAddOpen(true)}
            className="gap-1.5"
          >
            <Plus className="h-3.5 w-3.5" />
            Add example
          </Button>
        </div>

        <TabBar active={examplesTab} onChange={setExamplesTab} />

        {examplesLoading ? (
          <div className="space-y-3">
            {Array.from({ length: 3 }).map((_, i) => (
              <div key={i} className="space-y-2 rounded-lg border border-border/50 px-4 py-3">
                <Skeleton className="h-4 w-2/3" />
                <Skeleton className="h-4 w-full" />
                <Skeleton className="h-3 w-20" />
              </div>
            ))}
          </div>
        ) : filteredExamples.length === 0 ? (
          <div className="flex flex-col items-center justify-center py-12 text-center">
            <Mic className="size-10 text-muted-foreground/30" />
            <h3 className="mt-3 font-serif text-base font-medium">
              No examples yet
            </h3>
            <p className="mt-1 text-sm text-muted-foreground">
              Add writing samples to teach the generator your style.
            </p>
          </div>
        ) : (
          <div className="space-y-2">
            {filteredExamples.map((example) => (
              <ExampleItem key={example.id} example={example} />
            ))}
          </div>
        )}
      </section>

      <AddExampleDialog open={addOpen} onOpenChange={setAddOpen} />
    </div>
  )
}
