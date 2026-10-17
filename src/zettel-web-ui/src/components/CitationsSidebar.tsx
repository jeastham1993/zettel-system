import { Link } from 'react-router'
import { BookOpen, ExternalLink } from 'lucide-react'

export interface Citation {
  id: string
  title: string
}

export function CitationsSidebar({ citations }: { citations: Citation[] }) {
  return (
    <div className="w-64 shrink-0 rounded-lg border border-border/50 bg-card p-4">
      <div className="mb-3 flex items-center gap-2 text-muted-foreground">
        <BookOpen className="h-4 w-4" />
        <span className="text-xs font-medium uppercase tracking-wide">Sources</span>
      </div>
      <ul className="space-y-1.5">
        {citations.map((c) => (
          <li key={c.id}>
            <Link
              to={`/notes/${c.id}`}
              className="flex items-center gap-1.5 rounded-md px-2 py-1.5 text-sm font-medium hover:bg-muted/50 transition-colors"
            >
              <span className="min-w-0 flex-1 truncate">{c.title}</span>
              <ExternalLink className="h-3 w-3 shrink-0 text-muted-foreground" />
            </Link>
          </li>
        ))}
      </ul>
    </div>
  )
}
