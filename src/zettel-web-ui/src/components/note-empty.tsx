import { Link } from 'react-router'
import { FileText } from 'lucide-react'
import { Button } from '@/components/ui/button'

export function NoteEmpty() {
  return (
    <div className="flex flex-col items-center justify-center py-24 text-center">
      <FileText className="h-10 w-10 text-muted-foreground/40" />
      <h2 className="mt-4 font-serif text-lg font-medium">No notes yet</h2>
      <p className="mt-1 text-sm text-muted-foreground">
        Start writing to build your knowledge base.
      </p>
      <Button asChild variant="outline" size="sm" className="mt-6">
        <Link to="/new">Create your first note</Link>
      </Button>
    </div>
  )
}
