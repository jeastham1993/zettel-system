import { NoteList } from '@/components/note-list'
import { DiscoverySection } from '@/components/discovery-section'
import { useNotes } from '@/hooks/use-notes'

export function HomePage() {
  const { data } = useNotes()
  const totalCount = data?.totalCount ?? 0

  return (
    <div className="mx-auto max-w-2xl px-4 py-8">
      {totalCount > 0 && (
        <p className="mb-4 text-sm text-muted-foreground">
          {totalCount} {totalCount === 1 ? 'note' : 'notes'} in your Zettelkasten
        </p>
      )}
      <DiscoverySection />
      <NoteList />
    </div>
  )
}
