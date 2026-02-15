import { useEffect } from 'react'
import { useNavigate, useLocation } from 'react-router'

interface ShortcutHandlers {
  onSave?: () => void
  onShowShortcuts?: () => void
}

export function useKeyboardShortcuts(handlers?: ShortcutHandlers) {
  const navigate = useNavigate()
  const location = useLocation()

  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      const isMod = e.metaKey || e.ctrlKey
      const target = e.target as HTMLElement
      const isTyping =
        target.tagName === 'INPUT' ||
        target.tagName === 'TEXTAREA' ||
        target.isContentEditable

      // Cmd+N -> new note
      if (isMod && e.key === 'n') {
        e.preventDefault()
        navigate('/new')
        return
      }

      // Cmd+S -> save (editor only)
      if (isMod && e.key === 's') {
        e.preventDefault()
        handlers?.onSave?.()
        return
      }

      // ? -> show keyboard shortcuts (only when not typing)
      // On US keyboards, ? is Shift+/ so e.key === '?' and e.shiftKey === true
      if (e.key === '?' && !isMod && !isTyping) {
        handlers?.onShowShortcuts?.()
        return
      }

      // Escape -> go back (or home if already on home)
      if (e.key === 'Escape' && !e.shiftKey && !isMod) {
        // Don't navigate back if a dialog/command menu is open
        const dialogOpen = document.querySelector('[role="dialog"]')
        if (dialogOpen) return
        if (location.pathname === '/') return
        // Don't navigate away from editor pages (would lose unsaved work)
        if (location.pathname.includes('/edit') || location.pathname === '/new') return
        navigate('/')
      }
    }

    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [navigate, handlers, location.pathname])
}
