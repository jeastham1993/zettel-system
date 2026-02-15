import { useQuery } from '@tanstack/react-query'
import { listVersions, getVersion } from '@/api/versions'

export function useVersions(noteId: string | undefined) {
  return useQuery({
    queryKey: ['versions', noteId],
    queryFn: () => listVersions(noteId!),
    enabled: !!noteId,
  })
}

export function useVersion(noteId: string | undefined, versionId: number | undefined) {
  return useQuery({
    queryKey: ['versions', noteId, versionId],
    queryFn: () => getVersion(noteId!, versionId!),
    enabled: !!noteId && versionId !== undefined,
  })
}
