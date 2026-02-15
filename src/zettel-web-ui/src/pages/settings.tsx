import { useEffect, useRef, useState } from 'react'
import { Link } from 'react-router'
import { ArrowLeft, Upload, Download, RefreshCw, Activity } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Separator } from '@/components/ui/separator'
import { ConfirmDialog } from '@/components/confirm-dialog'
import { useReEmbed } from '@/hooks/use-notes'
import { useHealth } from '@/hooks/use-health'
import { importNotes, exportNotes } from '@/api/import-export'
import { toast } from 'sonner'

export function SettingsPage() {
  const reEmbed = useReEmbed()
  const [showEmbedProgress, setShowEmbedProgress] = useState(false)
  const { data: health } = useHealth(showEmbedProgress ? 5_000 : 30_000)
  const fileInputRef = useRef<HTMLInputElement>(null)
  const [importing, setImporting] = useState(false)
  const [exporting, setExporting] = useState(false)

  const dbData = health?.entries?.database?.data
  const totalNotes = Number(dbData?.total_notes ?? 0)
  const embeddedNotes = Number(dbData?.embedded ?? 0)
  const pendingNotes = Number(dbData?.pending ?? 0)

  // Auto-hide progress when embedding is complete
  useEffect(() => {
    if (showEmbedProgress && totalNotes > 0 && pendingNotes === 0) {
      setShowEmbedProgress(false)
    }
  }, [showEmbedProgress, totalNotes, pendingNotes])

  const handleImport = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const fileList = e.target.files
    if (!fileList || fileList.length === 0) return

    setImporting(true)
    try {
      const files = await Promise.all(
        Array.from(fileList).map(async (file) => ({
          fileName: file.name,
          content: await file.text(),
        })),
      )
      const result = await importNotes(files)
      toast.success(`Imported ${result.imported} notes (${result.skipped} skipped)`)
      if (result.imported > 0) {
        setShowEmbedProgress(true)
      }
    } catch {
      toast.error('Import failed')
    } finally {
      setImporting(false)
      if (fileInputRef.current) fileInputRef.current.value = ''
    }
  }

  const handleExport = async () => {
    setExporting(true)
    try {
      await exportNotes()
      toast.success('Export downloaded')
    } catch {
      toast.error('Export failed')
    } finally {
      setExporting(false)
    }
  }

  const handleReEmbed = () => {
    reEmbed.mutate(undefined, {
      onSuccess: (result) => {
        toast.success(`Queued ${result.queued} notes for re-embedding`)
        if (result.queued > 0) {
          setShowEmbedProgress(true)
        }
      },
      onError: () => toast.error('Re-embed failed'),
    })
  }

  const embedEntry = health?.entries?.embedding

  return (
    <div className="mx-auto max-w-2xl px-4 py-8">
      <div className="mb-6">
        <Button variant="ghost" size="sm" asChild>
          <Link to="/" className="gap-1.5 text-muted-foreground">
            <ArrowLeft className="h-4 w-4" />
            Back
          </Link>
        </Button>
      </div>

      <h1 className="font-serif text-2xl font-semibold tracking-tight">Settings</h1>

      <Separator className="my-6" />

      {/* Embedding Progress */}
      {showEmbedProgress && totalNotes > 0 && (
        <>
          <section className="space-y-3">
            <h2 className="text-sm font-medium">Embedding Progress</h2>
            <div className="space-y-2">
              <div className="h-2 overflow-hidden rounded-full bg-muted">
                <div
                  className="h-full rounded-full bg-amber-500 transition-all duration-500"
                  style={{ width: `${Math.round((embeddedNotes / totalNotes) * 100)}%` }}
                />
              </div>
              <p className="text-xs tabular-nums text-muted-foreground">
                {embeddedNotes} of {totalNotes} notes embedded
                {pendingNotes > 0 && ` (${pendingNotes} pending)`}
              </p>
            </div>
          </section>
          <Separator className="my-6" />
        </>
      )}

      {/* Import */}
      <section className="space-y-3">
        <h2 className="text-sm font-medium">Import</h2>
        <p className="text-sm text-muted-foreground">
          Import markdown files (.md) as notes.
        </p>
        <input
          ref={fileInputRef}
          type="file"
          accept=".md"
          multiple
          onChange={handleImport}
          className="hidden"
        />
        <Button
          variant="outline"
          size="sm"
          onClick={() => fileInputRef.current?.click()}
          disabled={importing}
          className="gap-1.5"
        >
          <Upload className="h-3.5 w-3.5" />
          {importing ? 'Importing...' : 'Choose files'}
        </Button>
      </section>

      <Separator className="my-6" />

      {/* Export */}
      <section className="space-y-3">
        <h2 className="text-sm font-medium">Export</h2>
        <p className="text-sm text-muted-foreground">
          Download all notes as a zip of markdown files with front matter.
        </p>
        <Button
          variant="outline"
          size="sm"
          onClick={handleExport}
          disabled={exporting}
          className="gap-1.5"
        >
          <Download className="h-3.5 w-3.5" />
          {exporting ? 'Exporting...' : 'Download export'}
        </Button>
      </section>

      <Separator className="my-6" />

      {/* Re-embed */}
      <section className="space-y-3">
        <h2 className="text-sm font-medium">Re-embed</h2>
        <p className="text-sm text-muted-foreground">
          Queue all notes for re-embedding. Useful after changing the embedding model.
        </p>
        <ConfirmDialog
          trigger={
            <Button
              variant="outline"
              size="sm"
              disabled={reEmbed.isPending}
              className="gap-1.5"
            >
              <RefreshCw className={`h-3.5 w-3.5 ${reEmbed.isPending ? 'animate-spin' : ''}`} />
              {reEmbed.isPending ? 'Re-embedding...' : 'Re-embed all'}
            </Button>
          }
          title="Re-embed all notes"
          description="This will queue all notes for re-embedding. This may take a while and use API credits."
          confirmLabel="Re-embed"
          onConfirm={handleReEmbed}
        />
      </section>

      <Separator className="my-6" />

      {/* Health */}
      <section className="space-y-3">
        <h2 className="flex items-center gap-1.5 text-sm font-medium">
          <Activity className="h-3.5 w-3.5" />
          Health
        </h2>
        {health ? (
          <div className="space-y-2 text-sm">
            <div className="flex items-center gap-2">
              <span className={`h-2 w-2 rounded-full ${health.status === 'Healthy' ? 'bg-green-500' : 'bg-amber-500'}`} />
              <span className="text-muted-foreground">Status: {health.status}</span>
            </div>
            {dbData && (
              <div className="space-y-1 pl-4 text-muted-foreground">
                {Object.entries(dbData).map(([key, value]) => (
                  <p key={key}>
                    {key}: <span className="font-mono text-foreground">{String(value)}</span>
                  </p>
                ))}
              </div>
            )}
            {embedEntry?.data && (
              <div className="space-y-1 pl-4 text-muted-foreground">
                {Object.entries(embedEntry.data).map(([key, value]) => (
                  <p key={key}>
                    {key}: <span className="font-mono text-foreground">{String(value)}</span>
                  </p>
                ))}
              </div>
            )}
          </div>
        ) : (
          <p className="text-sm text-muted-foreground">Loading health data...</p>
        )}
      </section>
    </div>
  )
}
