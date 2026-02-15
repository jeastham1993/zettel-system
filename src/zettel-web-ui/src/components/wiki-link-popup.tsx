import {
  forwardRef,
  useEffect,
  useImperativeHandle,
  useState,
} from 'react'
import type { TitleSearchResult } from '@/api/types'

interface WikiLinkPopupProps {
  items: TitleSearchResult[]
  command: (item: TitleSearchResult) => void
}

export interface WikiLinkPopupRef {
  onKeyDown: (props: { event: KeyboardEvent }) => boolean
}

export const WikiLinkPopup = forwardRef<WikiLinkPopupRef, WikiLinkPopupProps>(
  function WikiLinkPopup({ items, command }, ref) {
    const [selectedIndex, setSelectedIndex] = useState(0)

    useEffect(() => {
      setSelectedIndex(0)
    }, [items])

    useImperativeHandle(ref, () => ({
      onKeyDown: ({ event }) => {
        if (event.key === 'ArrowUp') {
          setSelectedIndex((prev) => (prev + items.length - 1) % items.length)
          return true
        }
        if (event.key === 'ArrowDown') {
          setSelectedIndex((prev) => (prev + 1) % items.length)
          return true
        }
        if (event.key === 'Enter') {
          if (items[selectedIndex]) {
            command(items[selectedIndex])
          }
          return true
        }
        return false
      },
    }))

    if (items.length === 0) {
      return null
    }

    return (
      <div className="z-50 overflow-hidden rounded-md border bg-popover p-1 shadow-md">
        {items.map((item, index) => (
          <button
            key={item.noteId}
            onClick={() => command(item)}
            className={`flex w-full items-center rounded-sm px-2 py-1.5 text-left text-sm ${
              index === selectedIndex
                ? 'bg-accent text-accent-foreground'
                : 'text-popover-foreground hover:bg-accent/50'
            }`}
          >
            {item.title}
          </button>
        ))}
      </div>
    )
  },
)
