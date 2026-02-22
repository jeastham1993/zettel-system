import { useState } from 'react'
import { Link } from 'react-router'
import { Plus, Search, GitBranch, Inbox, Menu, Sparkles, Mic } from 'lucide-react'
import { Button } from '@/components/ui/button'
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from '@/components/ui/tooltip'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { ThemeToggle } from './theme-toggle'
import { useInboxCount } from '@/hooks/use-inbox'

const isMac = navigator.userAgent.includes('Mac')
const mod = isMac ? '\u2318' : 'Ctrl'

interface HeaderProps {
  onOpenSearch: () => void
}

export function Header({ onOpenSearch }: HeaderProps) {
  const { data: inboxCount } = useInboxCount()
  const count = inboxCount?.count ?? 0
  const [menuOpen, setMenuOpen] = useState(false)

  return (
    <header className="border-b border-border/50">
      <div className="mx-auto flex max-w-2xl items-center justify-between px-4 py-3 sm:py-4">
        <Link to="/" className="font-serif text-xl font-semibold tracking-tight">
          Zettel
        </Link>

        {/* Desktop navigation */}
        <div className="hidden items-center gap-1 sm:flex">
          <Tooltip>
            <TooltipTrigger asChild>
              <Button
                variant="ghost"
                size="sm"
                onClick={onOpenSearch}
                className="gap-1.5 text-muted-foreground"
              >
                <Search className="h-4 w-4" />
                <kbd className="pointer-events-none hidden select-none rounded border border-border/50 bg-muted px-1.5 py-0.5 font-mono text-[10px] font-medium text-muted-foreground sm:inline">
                  {mod}K
                </kbd>
              </Button>
            </TooltipTrigger>
            <TooltipContent>Search notes</TooltipContent>
          </Tooltip>

          <Tooltip>
            <TooltipTrigger asChild>
              <Button variant="ghost" size="sm" asChild>
                <Link to="/new" className="gap-1.5 text-muted-foreground">
                  <Plus className="h-4 w-4" />
                  <span className="hidden sm:inline">New</span>
                </Link>
              </Button>
            </TooltipTrigger>
            <TooltipContent>New note ({mod}N)</TooltipContent>
          </Tooltip>

          <Tooltip>
            <TooltipTrigger asChild>
              <Button variant="ghost" size="sm" asChild>
                <Link to="/inbox" className="relative text-muted-foreground" aria-label={`Inbox${count > 0 ? `, ${count} unprocessed` : ''}`}>
                  <Inbox className="h-4 w-4" />
                  {count > 0 && (
                    <span className="absolute -top-1 -right-1 flex size-4 items-center justify-center rounded-full bg-amber-500 text-[10px] font-medium text-white">
                      {count > 9 ? '9+' : count}
                    </span>
                  )}
                </Link>
              </Button>
            </TooltipTrigger>
            <TooltipContent>
              Inbox{count > 0 ? ` (${count})` : ''}
            </TooltipContent>
          </Tooltip>

          <Tooltip>
            <TooltipTrigger asChild>
              <Button variant="ghost" size="sm" asChild>
                <Link to="/content" className="text-muted-foreground">
                  <Sparkles className="h-4 w-4" />
                </Link>
              </Button>
            </TooltipTrigger>
            <TooltipContent>Content review</TooltipContent>
          </Tooltip>

          <Tooltip>
            <TooltipTrigger asChild>
              <Button variant="ghost" size="sm" asChild>
                <Link to="/voice" className="text-muted-foreground">
                  <Mic className="h-4 w-4" />
                </Link>
              </Button>
            </TooltipTrigger>
            <TooltipContent>Voice & style</TooltipContent>
          </Tooltip>

          <Tooltip>
            <TooltipTrigger asChild>
              <Button variant="ghost" size="sm" asChild>
                <Link to="/graph" className="text-muted-foreground">
                  <GitBranch className="h-4 w-4" />
                </Link>
              </Button>
            </TooltipTrigger>
            <TooltipContent>Knowledge graph</TooltipContent>
          </Tooltip>

          <ThemeToggle />
        </div>

        {/* Mobile navigation */}
        <div className="flex items-center gap-1 sm:hidden">
          <Button
            variant="ghost"
            className="h-10 w-10 p-0 text-muted-foreground"
            onClick={onOpenSearch}
            aria-label="Search notes"
          >
            <Search className="h-5 w-5" />
          </Button>

          <Button
            variant="ghost"
            className="h-10 w-10 p-0 text-muted-foreground"
            asChild
          >
            <Link to="/new" aria-label="New note">
              <Plus className="h-5 w-5" />
            </Link>
          </Button>

          <DropdownMenu open={menuOpen} onOpenChange={setMenuOpen}>
            <DropdownMenuTrigger asChild>
              <Button
                variant="ghost"
                className="relative h-10 w-10 p-0 text-muted-foreground"
                aria-label="Menu"
              >
                <Menu className="h-5 w-5" />
                {count > 0 && (
                  <span className="absolute top-1 right-1 flex size-2 rounded-full bg-amber-500" />
                )}
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end" className="w-48">
              <DropdownMenuItem asChild>
                <Link to="/inbox" className="gap-2" onClick={() => setMenuOpen(false)}>
                  <Inbox className="h-4 w-4" />
                  Inbox
                  {count > 0 && (
                    <span className="ml-auto text-xs text-amber-500">{count}</span>
                  )}
                </Link>
              </DropdownMenuItem>
              <DropdownMenuItem asChild>
                <Link to="/content" className="gap-2" onClick={() => setMenuOpen(false)}>
                  <Sparkles className="h-4 w-4" />
                  Content review
                </Link>
              </DropdownMenuItem>
              <DropdownMenuItem asChild>
                <Link to="/voice" className="gap-2" onClick={() => setMenuOpen(false)}>
                  <Mic className="h-4 w-4" />
                  Voice & style
                </Link>
              </DropdownMenuItem>
              <DropdownMenuItem asChild>
                <Link to="/graph" className="gap-2" onClick={() => setMenuOpen(false)}>
                  <GitBranch className="h-4 w-4" />
                  Knowledge graph
                </Link>
              </DropdownMenuItem>
              <DropdownMenuItem asChild>
                <Link to="/settings" className="gap-2" onClick={() => setMenuOpen(false)}>
                  Settings
                </Link>
              </DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>

          <ThemeToggle />
        </div>
      </div>
    </header>
  )
}
