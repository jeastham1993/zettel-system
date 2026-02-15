import { useState, useRef, useEffect } from 'react'
import { useNavigate } from 'react-router'
import { Feather } from 'lucide-react'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
} from '@/components/ui/dialog'
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from '@/components/ui/tooltip'
import { TagInput } from '@/components/tag-input'
import { useCaptureNote } from '@/hooks/use-inbox'
import { toast } from 'sonner'

export function CaptureButton() {
  const navigate = useNavigate()
  const [open, setOpen] = useState(false)
  const [content, setContent] = useState('')
  const [tags, setTags] = useState<string[]>([])
  const textareaRef = useRef<HTMLTextAreaElement>(null)
  const capture = useCaptureNote()

  const reset = () => {
    setContent('')
    setTags([])
  }

  const handleSubmit = () => {
    const trimmed = content.trim()
    if (!trimmed) return

    capture.mutate(
      { content: trimmed, tags: tags.length > 0 ? tags : undefined },
      {
        onSuccess: () => {
          toast.success('Thought captured', {
            action: {
              label: 'View inbox',
              onClick: () => navigate('/inbox'),
            },
          })
          reset()
          setOpen(false)
        },
        onError: () => {
          toast.error('Failed to capture note')
        },
      },
    )
  }

  const handleKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === 'Enter' && (e.metaKey || e.ctrlKey)) {
      e.preventDefault()
      handleSubmit()
    }
  }

  const isMac = navigator.userAgent.includes('Mac')

  // Keyboard shortcut: Ctrl+Shift+N / Cmd+Shift+N
  useEffect(() => {
    const handleGlobalKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'N' && e.shiftKey && (e.metaKey || e.ctrlKey)) {
        e.preventDefault()
        setOpen(true)
      }
    }
    document.addEventListener('keydown', handleGlobalKeyDown)
    return () => document.removeEventListener('keydown', handleGlobalKeyDown)
  }, [])

  return (
    <>
      <Tooltip>
        <TooltipTrigger asChild>
          <Button
            size="icon-lg"
            className="fixed bottom-6 right-6 z-40 rounded-full shadow-lg"
            onClick={() => setOpen(true)}
          >
            <Feather className="size-5" />
            <span className="sr-only">Quick capture</span>
          </Button>
        </TooltipTrigger>
        <TooltipContent side="left">Quick capture ({isMac ? '\u2318' : 'Ctrl'}+Shift+N)</TooltipContent>
      </Tooltip>

      <Dialog
        open={open}
        onOpenChange={(value) => {
          if (!value) reset()
          setOpen(value)
        }}
      >
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle className="font-serif">Quick Capture</DialogTitle>
            <DialogDescription>
              Jot down a thought, link, or idea. Process it later.
            </DialogDescription>
          </DialogHeader>
          <div className="space-y-3">
            <textarea
              ref={textareaRef}
              value={content}
              onChange={(e) => setContent(e.target.value)}
              onKeyDown={handleKeyDown}
              placeholder="Quick thought, link, or idea..."
              autoFocus
              rows={4}
              className="w-full resize-none rounded-md border border-input bg-transparent px-3 py-2 text-sm shadow-xs placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
            />
            <div className="rounded-md border border-input px-3 py-2">
              <TagInput tags={tags} onChange={setTags} />
            </div>
            <div className="flex items-center justify-between">
              <p className="text-xs text-muted-foreground">
                {isMac ? '\u2318' : 'Ctrl'}+Enter to save
              </p>
              <Button
                size="sm"
                onClick={handleSubmit}
                disabled={!content.trim() || capture.isPending}
              >
                {capture.isPending ? 'Saving...' : 'Capture'}
              </Button>
            </div>
          </div>
        </DialogContent>
      </Dialog>
    </>
  )
}
