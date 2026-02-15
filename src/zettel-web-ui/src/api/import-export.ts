import { post, getBlob } from './client'
import type { ImportFile, ImportResult } from './types'

export function importNotes(files: ImportFile[]): Promise<ImportResult> {
  return post<ImportResult>('/api/import', files)
}

export async function exportNotes(): Promise<void> {
  const blob = await getBlob('/api/export')
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = 'zettel-export.zip'
  a.click()
  URL.revokeObjectURL(url)
}
