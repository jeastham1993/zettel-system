import type { NoteType } from '../api/types'

export const queryKeys = {
  notes: {
    all: ['notes'] as const,
    lists: () => [...queryKeys.notes.all, 'list'] as const,
    list: (params: { skip: number; take: number; tag?: string; noteType?: NoteType }) =>
      [...queryKeys.notes.lists(), params] as const,
    details: () => [...queryKeys.notes.all, 'detail'] as const,
    detail: (id: string) => [...queryKeys.notes.details(), id] as const,
    related: (id: string, limit: number) =>
      [...queryKeys.notes.detail(id), 'related', limit] as const,
    backlinks: (id: string) => [...queryKeys.notes.detail(id), 'backlinks'] as const,
    discover: (limit: number) => [...queryKeys.notes.all, 'discover', limit] as const,
  },
  inbox: {
    all: ['inbox'] as const,
    list: () => [...queryKeys.inbox.all, 'list'] as const,
    count: () => [...queryKeys.inbox.all, 'count'] as const,
  },
  search: {
    all: ['search'] as const,
    query: (q: string, type: string) => [...queryKeys.search.all, q, type] as const,
  },
  health: {
    all: ['health'] as const,
  },
}
