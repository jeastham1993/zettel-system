import { useRef, useState, useEffect } from 'react'
import { Link } from 'react-router'
import { GraphView } from '@/components/graph-view'
import { useGraph } from '@/hooks/use-graph'
import { Skeleton } from '@/components/ui/skeleton'
import { Plus, GitBranch } from 'lucide-react'
import { Button } from '@/components/ui/button'

export function GraphPage() {
  const { data, isLoading } = useGraph()
  const containerRef = useRef<HTMLDivElement>(null)
  const [dimensions, setDimensions] = useState({ width: 800, height: 600 })

  useEffect(() => {
    function updateSize() {
      if (containerRef.current) {
        setDimensions({
          width: containerRef.current.clientWidth,
          height: containerRef.current.clientHeight,
        })
      }
    }

    updateSize()
    window.addEventListener('resize', updateSize)
    return () => window.removeEventListener('resize', updateSize)
  }, [])

  if (isLoading) {
    return (
      <div className="flex h-[calc(100vh-4rem)] items-center justify-center">
        <Skeleton className="h-64 w-64 rounded-full" />
      </div>
    )
  }

  if (!data || data.nodes.length === 0) {
    return (
      <div className="flex h-[calc(100vh-4rem)] items-center justify-center px-4">
        <div className="max-w-sm text-center">
          <GitBranch className="mx-auto mb-4 h-12 w-12 text-muted-foreground/40" />
          <h2 className="font-serif text-lg font-semibold">
            No connections yet
          </h2>
          <p className="mt-2 text-sm text-muted-foreground">
            Your knowledge graph shows how your notes connect. Create notes
            and link them using{' '}
            <code className="rounded bg-muted px-1 py-0.5 text-xs">
              [[wiki-links]]
            </code>{' '}
            to see connections appear here. Notes with embeddings will also
            show semantic similarity links.
          </p>
          <Button variant="outline" size="sm" className="mt-4 gap-1.5" asChild>
            <Link to="/new">
              <Plus className="h-3.5 w-3.5" />
              Create a note
            </Link>
          </Button>
        </div>
      </div>
    )
  }

  return (
    <div className="flex h-[calc(100vh-4rem)] flex-col">
      {/* Mobile interaction hint */}
      <div className="border-b border-border/50 px-4 py-2 text-center text-xs text-muted-foreground sm:hidden">
        Tap a node to view the note. Drag to rearrange. Pinch to zoom.
      </div>
      <div ref={containerRef} className="flex-1">
        <GraphView data={data} width={dimensions.width} height={dimensions.height} />
      </div>
    </div>
  )
}
