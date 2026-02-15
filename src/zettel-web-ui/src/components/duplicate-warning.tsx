import { Link } from 'react-router'
import { AlertTriangle } from 'lucide-react'
import { useDuplicateCheck } from '@/hooks/use-duplicate-check'

interface DuplicateWarningProps {
  content: string
  enabled?: boolean
}

export function DuplicateWarning({ content, enabled = true }: DuplicateWarningProps) {
  const { data, isLoading } = useDuplicateCheck(content, enabled)

  if (isLoading || !data || !data.isDuplicate) return null

  const percentage = Math.round(data.similarity * 100)

  return (
    <div className="flex items-start gap-2 rounded-lg border border-amber-200 bg-amber-50 p-3 dark:border-amber-900/50 dark:bg-amber-950/30">
      <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-amber-600 dark:text-amber-400" />
      <div className="text-sm">
        <p className="font-medium text-amber-800 dark:text-amber-200">
          Possible duplicate detected
        </p>
        <p className="mt-0.5 text-amber-700 dark:text-amber-300">
          This note is very similar to{' '}
          {data.similarNoteId ? (
            <Link
              to={`/notes/${data.similarNoteId}`}
              className="font-medium underline underline-offset-2"
            >
              {data.similarNoteTitle ?? 'an existing note'}
            </Link>
          ) : (
            <span className="font-medium">{data.similarNoteTitle ?? 'an existing note'}</span>
          )}{' '}
          ({percentage}% match).
        </p>
      </div>
    </div>
  )
}
