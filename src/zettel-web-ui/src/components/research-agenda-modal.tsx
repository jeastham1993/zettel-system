import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { Loader2, Search, BookOpen, X } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from '@/components/ui/dialog'
import { toast } from 'sonner'
import * as researchApi from '@/api/research'
import type { ResearchAgenda, ResearchTask } from '@/api/types'

interface ResearchAgendaModalProps {
  agenda: ResearchAgenda
  onClose: () => void
  onApproved: () => void
}

export function ResearchAgendaModal({ agenda, onClose, onApproved }: ResearchAgendaModalProps) {
  const [blockedIds, setBlockedIds] = useState<Set<string>>(new Set())

  const approveMutation = useMutation({
    mutationFn: () => researchApi.approveAgenda(agenda.id, [...blockedIds]),
    onSuccess: () => {
      toast.success('Research started — findings will appear in the Research inbox')
      onApproved()
      onClose()
    },
    onError: () => {
      toast.error('Failed to start research')
    },
  })

  const toggleBlock = (taskId: string) => {
    setBlockedIds((prev) => {
      const next = new Set(prev)
      if (next.has(taskId)) next.delete(taskId)
      else next.add(taskId)
      return next
    })
  }

  const approvedCount = agenda.tasks.length - blockedIds.size

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="max-w-lg">
        <DialogHeader>
          <DialogTitle>Research Agenda</DialogTitle>
        </DialogHeader>

        <p className="text-sm text-muted-foreground">
          The agent will run {approvedCount} of {agenda.tasks.length} research{' '}
          {agenda.tasks.length === 1 ? 'query' : 'queries'}. Uncheck any you want to skip.
        </p>

        <div className="mt-2 max-h-[360px] space-y-2 overflow-y-auto pr-1">
          {agenda.tasks.map((task: ResearchTask) => {
            const isBlocked = blockedIds.has(task.id)
            return (
              <button
                key={task.id}
                role="checkbox"
                aria-checked={!isBlocked}
                aria-label={`${isBlocked ? 'Blocked' : 'Include'}: ${task.query}`}
                onClick={() => toggleBlock(task.id)}
                className={`flex w-full items-start gap-3 rounded-md border px-3 py-2.5 text-left transition-colors ${
                  isBlocked
                    ? 'border-border/30 bg-muted/20 opacity-50'
                    : 'border-border/50 bg-card hover:bg-muted/30'
                }`}
              >
                <div className="mt-0.5 shrink-0">
                  <div
                    className={`h-4 w-4 rounded border-2 ${
                      isBlocked ? 'border-muted-foreground/40' : 'border-foreground bg-foreground'
                    } flex items-center justify-center`}
                    aria-hidden="true"
                  >
                    {!isBlocked && <X className="h-2.5 w-2.5 text-background" />}
                  </div>
                </div>
                <div className="min-w-0 flex-1">
                  <div className="flex items-center gap-1.5">
                    {task.sourceType === 'Arxiv' ? (
                      <BookOpen className="h-3.5 w-3.5 shrink-0 text-muted-foreground" />
                    ) : (
                      <Search className="h-3.5 w-3.5 shrink-0 text-muted-foreground" />
                    )}
                    <span className="text-sm font-medium">{task.query}</span>
                    <Badge variant="outline" className="ml-auto shrink-0 text-[10px]">
                      {task.sourceType === 'Arxiv' ? 'Arxiv' : 'Web'}
                    </Badge>
                  </div>
                  <p className="mt-0.5 text-xs text-muted-foreground">{task.motivation}</p>
                </div>
              </button>
            )
          })}
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={approveMutation.isPending}>
            Cancel
          </Button>
          <Button
            onClick={() => approveMutation.mutate()}
            disabled={approveMutation.isPending || approvedCount === 0}
            className="gap-1.5"
          >
            {approveMutation.isPending && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
            Start research ({approvedCount})
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
