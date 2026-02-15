import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
} from '@/components/ui/dialog'

interface KeyboardShortcutsDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
}

const isMac = navigator.userAgent.includes('Mac')
const mod = isMac ? '\u2318' : 'Ctrl'

const shortcuts = [
  { keys: `${mod}+K`, description: 'Search notes' },
  { keys: `${mod}+N`, description: 'New note' },
  { keys: `${mod}+S`, description: 'Save note (in editor)' },
  { keys: `${mod}+Shift+N`, description: 'Quick capture' },
  { keys: 'Escape', description: 'Go back to home' },
  { keys: '?', description: 'Show keyboard shortcuts' },
]

export function KeyboardShortcutsDialog({
  open,
  onOpenChange,
}: KeyboardShortcutsDialogProps) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle className="font-serif">Keyboard Shortcuts</DialogTitle>
          <DialogDescription>
            Navigate faster with these keyboard shortcuts.
          </DialogDescription>
        </DialogHeader>
        <div className="space-y-1">
          {shortcuts.map((shortcut) => (
            <div
              key={shortcut.keys}
              className="flex items-center justify-between rounded-md px-2 py-2"
            >
              <span className="text-sm text-foreground">
                {shortcut.description}
              </span>
              <kbd className="inline-flex items-center gap-1 rounded border border-border bg-muted px-2 py-1 font-mono text-xs text-muted-foreground">
                {shortcut.keys}
              </kbd>
            </div>
          ))}
        </div>
      </DialogContent>
    </Dialog>
  )
}
