import { useCallback, useMemo, useState } from 'react'
import { useNavigate } from 'react-router'
import ForceGraph2D from 'react-force-graph-2d'
import type { GraphData } from '@/api/types'

interface GraphViewProps {
  data: GraphData
  width: number
  height: number
}

interface GraphNode {
  id: string
  title: string
  edgeCount: number
  x?: number
  y?: number
}

interface GraphLink {
  source: string
  target: string
  type: 'wikilink' | 'semantic'
  weight: number
}

function isMobileDevice(): boolean {
  return 'ontouchstart' in window || navigator.maxTouchPoints > 0
}

export function GraphView({ data, width, height }: GraphViewProps) {
  const navigate = useNavigate()
  const [tappedNode, setTappedNode] = useState<string | null>(null)
  const isMobile = useMemo(() => isMobileDevice(), [])

  const graphData = useMemo(() => ({
    nodes: data.nodes.map((n) => ({ ...n })) as GraphNode[],
    links: data.edges.map((e) => ({
      source: e.source,
      target: e.target,
      type: e.type,
      weight: e.weight,
    })) as GraphLink[],
  }), [data])

  const handleNodeClick = useCallback(
    (node: GraphNode) => {
      if (isMobile) {
        // First tap shows title, second tap navigates
        if (tappedNode === node.id) {
          navigate(`/notes/${node.id}`)
          setTappedNode(null)
        } else {
          setTappedNode(node.id)
        }
      } else {
        navigate(`/notes/${node.id}`)
      }
    },
    [navigate, isMobile, tappedNode],
  )

  const nodeColor = useCallback((node: GraphNode) => {
    const count = node.edgeCount
    if (count >= 5) return 'hsl(36, 80%, 50%)'
    if (count >= 2) return 'hsl(36, 60%, 60%)'
    return 'hsl(30, 20%, 55%)'
  }, [])

  const linkColor = useCallback((link: GraphLink) => {
    return link.type === 'wikilink' ? 'hsl(36, 80%, 50%)' : 'hsl(210, 60%, 55%)'
  }, [])

  // On mobile, render titles as canvas text for the tapped node
  const nodeCanvasObject = useCallback(
    (node: GraphNode, ctx: CanvasRenderingContext2D, globalScale: number) => {
      if (!isMobile || tappedNode !== node.id) return
      const label = node.title
      const fontSize = 12 / globalScale
      ctx.font = `${fontSize}px sans-serif`
      ctx.textAlign = 'center'
      ctx.textBaseline = 'bottom'
      ctx.fillStyle = 'hsl(36, 80%, 50%)'
      const x = node.x ?? 0
      const y = node.y ?? 0
      ctx.fillText(label, x, y - 8)
    },
    [isMobile, tappedNode],
  )

  return (
    <ForceGraph2D
      graphData={graphData}
      width={width}
      height={height}
      nodeLabel={isMobile ? undefined : 'title'}
      nodeColor={nodeColor}
      nodeRelSize={5}
      nodeVal={(node: GraphNode) => Math.max(node.edgeCount, 1)}
      nodeCanvasObjectMode={() => 'after'}
      nodeCanvasObject={nodeCanvasObject}
      linkColor={linkColor}
      linkWidth={(link: GraphLink) => (link.type === 'wikilink' ? 2 : 1)}
      linkDirectionalArrowLength={(link: GraphLink) => (link.type === 'wikilink' ? 4 : 0)}
      linkDirectionalArrowRelPos={1}
      onNodeClick={handleNodeClick}
      onBackgroundClick={() => setTappedNode(null)}
      cooldownTicks={100}
      enableNodeDrag
    />
  )
}
