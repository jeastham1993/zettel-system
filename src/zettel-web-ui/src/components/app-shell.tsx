import { useState, useEffect, useCallback, useMemo } from 'react'
import { Outlet } from 'react-router'
import { Header } from './header'
import { CommandMenu } from './command-menu'
import { CaptureButton } from './capture-button'
import { KeyboardShortcutsDialog } from './keyboard-shortcuts-dialog'
import { useCommandMenu } from '@/hooks/use-command-menu'
import { useKeyboardShortcuts } from '@/hooks/use-keyboard-shortcuts'
import { toast } from 'sonner'

export function AppShell() {
  const { open, setOpen } = useCommandMenu()
  const [initialQuery, setInitialQuery] = useState('')
  const [shortcutsOpen, setShortcutsOpen] = useState(false)

  // Listen for tag search events from NoteView
  useEffect(() => {
    const handler = (e: Event) => {
      const tag = (e as CustomEvent<string>).detail
      setInitialQuery(`#${tag}`)
      setOpen(true)
    }
    window.addEventListener('zettel:search-tag', handler)
    return () => window.removeEventListener('zettel:search-tag', handler)
  }, [setOpen])

  // Clear initialQuery when command menu closes
  const handleOpenChange = useCallback(
    (value: boolean) => {
      if (!value) setInitialQuery('')
      setOpen(value)
    },
    [setOpen],
  )

  // Offline indicator
  useEffect(() => {
    const handleOffline = () => {
      toast.warning('You are offline. Changes may not be saved.', {
        duration: Infinity,
        id: 'offline-indicator',
      })
    }
    const handleOnline = () => {
      toast.dismiss('offline-indicator')
      toast.success('Back online', { duration: 3000 })
    }

    window.addEventListener('offline', handleOffline)
    window.addEventListener('online', handleOnline)

    // Show immediately if already offline
    if (!navigator.onLine) {
      handleOffline()
    }

    return () => {
      window.removeEventListener('offline', handleOffline)
      window.removeEventListener('online', handleOnline)
    }
  }, [])

  const shortcutHandlers = useMemo(
    () => ({
      onShowShortcuts: () => setShortcutsOpen(true),
    }),
    [],
  )
  useKeyboardShortcuts(shortcutHandlers)

  return (
    <div className="min-h-screen">
      <Header onOpenSearch={() => setOpen(true)} />
      <main className="pb-20">
        <Outlet />
      </main>
      <CommandMenu
        open={open}
        onOpenChange={handleOpenChange}
        initialQuery={initialQuery}
      />
      <CaptureButton />
      <KeyboardShortcutsDialog
        open={shortcutsOpen}
        onOpenChange={setShortcutsOpen}
      />
    </div>
  )
}
