import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import * as notesApi from '@/api/notes'
import type { CreateNoteRequest, UpdateNoteRequest, NoteType } from '@/api/types'

export function useNotes(skip = 0, take = 50, tag?: string, noteType?: NoteType) {
  return useQuery({
    queryKey: ['notes', { skip, take, tag, noteType }],
    queryFn: () => notesApi.listNotes(skip, take, tag, noteType),
  })
}

export function useNote(id: string | undefined) {
  return useQuery({
    queryKey: ['notes', id],
    queryFn: () => notesApi.getNote(id!),
    enabled: !!id,
  })
}

export function useCreateNote() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (req: CreateNoteRequest) => notesApi.createNote(req),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['notes'] })
    },
  })
}

export function useUpdateNote() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id, ...req }: UpdateNoteRequest & { id: string }) =>
      notesApi.updateNote(id, req),
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({ queryKey: ['notes'] })
      queryClient.invalidateQueries({ queryKey: ['notes', variables.id] })
    },
  })
}

export function useDeleteNote() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => notesApi.deleteNote(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['notes'] })
      queryClient.invalidateQueries({ queryKey: ['inbox'] })
      queryClient.invalidateQueries({ queryKey: ['inbox', 'count'] })
    },
  })
}

export function useReEmbed() {
  return useMutation({
    mutationFn: notesApi.reEmbedAll,
  })
}
