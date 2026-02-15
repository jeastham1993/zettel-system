import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import * as notesApi from '../api/notes'
import { queryKeys } from './query-keys'
import type { CreateNoteRequest, UpdateNoteRequest, NoteType } from '../api/types'

export function useNotes(skip = 0, take = 50, tag?: string, noteType?: NoteType) {
  return useQuery({
    queryKey: queryKeys.notes.list({ skip, take, tag, noteType }),
    queryFn: () => notesApi.listNotes(skip, take, tag, noteType),
  })
}

export function useNote(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.notes.detail(id!),
    queryFn: () => notesApi.getNote(id!),
    enabled: !!id,
  })
}

export function useCreateNote() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (req: CreateNoteRequest) => notesApi.createNote(req),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.notes.all })
    },
  })
}

export function useUpdateNote() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, ...req }: UpdateNoteRequest & { id: string }) =>
      notesApi.updateNote(id, req),
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({ queryKey: queryKeys.notes.all })
    },
  })
}

export function useDeleteNote() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => notesApi.deleteNote(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.notes.all })
      queryClient.invalidateQueries({ queryKey: queryKeys.inbox.all })
    },
  })
}

export function useDiscoverNotes(limit = 4) {
  return useQuery({
    queryKey: queryKeys.notes.discover(limit),
    queryFn: () => notesApi.discoverNotes(limit),
  })
}

export function useRelatedNotes(id: string | undefined, limit = 5) {
  return useQuery({
    queryKey: queryKeys.notes.related(id!, limit),
    queryFn: () => notesApi.getRelatedNotes(id!, limit),
    enabled: !!id,
  })
}

export function useBacklinks(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.notes.backlinks(id!),
    queryFn: () => notesApi.getBacklinks(id!),
    enabled: !!id,
  })
}
