/**
 * API Contract Tests
 *
 * These tests verify that the frontend API client functions correctly
 * transform backend response shapes into the TypeScript types that
 * components depend on.
 *
 * The backend uses ASP.NET System.Text.Json which serializes C# property
 * names in camelCase by default. Each test supplies a response body that
 * mirrors the actual backend JSON, then asserts the API function returns
 * the shape the frontend expects.
 *
 * If a backend model changes (e.g. a wrapper is added), the corresponding
 * test here will break before the UI does.
 */
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'

// --- Fixtures matching backend JSON serialization ---

function makeNote(overrides: Record<string, unknown> = {}) {
  return {
    id: '20260215120000000',
    title: 'Test Note',
    content: 'Some content',
    createdAt: '2026-02-15T12:00:00Z',
    updatedAt: '2026-02-15T12:00:00Z',
    embedStatus: 'Pending',
    embeddingModel: null,
    embedError: null,
    embedRetryCount: 0,
    embedUpdatedAt: null,
    tags: [{ noteId: '20260215120000000', tag: 'test' }],
    status: 'Permanent',
    source: null,
    noteType: 'Regular',
    sourceAuthor: null,
    sourceTitle: null,
    sourceUrl: null,
    sourceYear: null,
    sourceType: null,
    enrichStatus: 'None',
    ...overrides,
  }
}

function makePagedResult(items: unknown[], totalCount?: number) {
  return { items, totalCount: totalCount ?? items.length }
}

function makeSearchResult(overrides: Record<string, unknown> = {}) {
  return {
    noteId: '20260215120000000',
    title: 'Test Note',
    snippet: 'matched <b>text</b>',
    rank: 0.75,
    ...overrides,
  }
}

// --- Helpers ---

function mockFetch(body: unknown, status = 200) {
  return vi.fn().mockResolvedValue({
    ok: status >= 200 && status < 300,
    status,
    statusText: status === 200 ? 'OK' : 'Error',
    json: () => Promise.resolve(body),
    text: () => Promise.resolve(JSON.stringify(body)),
  })
}

let originalFetch: typeof globalThis.fetch

beforeEach(() => {
  originalFetch = globalThis.fetch
})

afterEach(() => {
  globalThis.fetch = originalFetch
})

// --- Notes API ---

describe('notes API contract', () => {
  describe('listNotes', () => {
    it('returns a PagedResponse with items array and totalCount', async () => {
      const notes = [makeNote(), makeNote({ id: '20260215120000001' })]
      globalThis.fetch = mockFetch(makePagedResult(notes, 2))

      const { listNotes } = await import('@/api/notes')
      const result = await listNotes()

      expect(result).toHaveProperty('items')
      expect(result).toHaveProperty('totalCount')
      expect(Array.isArray(result.items)).toBe(true)
      expect(result.items).toHaveLength(2)
      expect(result.totalCount).toBe(2)
      expect(result.items[0].id).toBe('20260215120000000')
      expect(result.items[0].tags).toEqual([
        { noteId: '20260215120000000', tag: 'test' },
      ])
    })

    it('passes noteType filter as query parameter', async () => {
      globalThis.fetch = mockFetch(makePagedResult([], 0))

      const { listNotes } = await import('@/api/notes')
      await listNotes(0, 50, undefined, 'Structure')

      const url = (globalThis.fetch as ReturnType<typeof vi.fn>).mock.calls[0][0] as string
      expect(url).toContain('noteType=structure')
    })
  })

  describe('listInbox', () => {
    it('unwraps PagedResult and returns a plain Note array', async () => {
      const notes = [
        makeNote({ id: '1', status: 'Fleeting', source: 'web' }),
        makeNote({ id: '2', status: 'Fleeting', source: 'email' }),
      ]
      globalThis.fetch = mockFetch(makePagedResult(notes, 2))

      const { listInbox } = await import('@/api/notes')
      const result = await listInbox()

      // This is the exact assertion that would have caught the original bug:
      // components call result.filter(), so it MUST be an array
      expect(Array.isArray(result)).toBe(true)
      expect(result).toHaveLength(2)
      expect(result[0].id).toBe('1')
      expect(result[0].status).toBe('Fleeting')
    })

    it('returns empty array when backend returns empty PagedResult', async () => {
      globalThis.fetch = mockFetch(makePagedResult([], 0))

      const { listInbox } = await import('@/api/notes')
      const result = await listInbox()

      expect(Array.isArray(result)).toBe(true)
      expect(result).toHaveLength(0)
    })
  })

  describe('inboxCount', () => {
    it('returns an object with count property', async () => {
      globalThis.fetch = mockFetch({ count: 5 })

      const { inboxCount } = await import('@/api/notes')
      const result = await inboxCount()

      expect(result).toEqual({ count: 5 })
      expect(typeof result.count).toBe('number')
    })
  })

  describe('getNote', () => {
    it('returns a single Note object', async () => {
      const note = makeNote()
      globalThis.fetch = mockFetch(note)

      const { getNote } = await import('@/api/notes')
      const result = await getNote('20260215120000000')

      expect(result.id).toBe('20260215120000000')
      expect(result.title).toBe('Test Note')
      expect(Array.isArray(result.tags)).toBe(true)
    })

    it('returns noteType and source metadata fields', async () => {
      const note = makeNote({
        noteType: 'Source',
        sourceAuthor: 'Robert C. Martin',
        sourceTitle: 'Clean Code',
        sourceUrl: 'https://example.com',
        sourceYear: 2008,
        sourceType: 'book',
      })
      globalThis.fetch = mockFetch(note)

      const { getNote } = await import('@/api/notes')
      const result = await getNote('20260215120000000')

      expect(result.noteType).toBe('Source')
      expect(result.sourceAuthor).toBe('Robert C. Martin')
      expect(result.sourceTitle).toBe('Clean Code')
      expect(result.sourceUrl).toBe('https://example.com')
      expect(result.sourceYear).toBe(2008)
      expect(result.sourceType).toBe('book')
    })
  })

  describe('createNote', () => {
    it('returns the created Note', async () => {
      const note = makeNote()
      globalThis.fetch = mockFetch(note, 201)

      const { createNote } = await import('@/api/notes')
      const result = await createNote({
        title: 'Test',
        content: 'Content',
      })

      expect(result.id).toBe('20260215120000000')
    })
  })

  describe('updateNote', () => {
    it('returns the updated Note', async () => {
      const note = makeNote({ title: 'Updated' })
      globalThis.fetch = mockFetch(note)

      const { updateNote } = await import('@/api/notes')
      const result = await updateNote('20260215120000000', {
        title: 'Updated',
        content: 'Content',
      })

      expect(result.title).toBe('Updated')
    })
  })

  describe('deleteNote', () => {
    it('returns void on 204', async () => {
      globalThis.fetch = vi.fn().mockResolvedValue({
        ok: true,
        status: 204,
        statusText: 'No Content',
        json: () => Promise.resolve(undefined),
        text: () => Promise.resolve(''),
      })

      const { deleteNote } = await import('@/api/notes')
      const result = await deleteNote('20260215120000000')

      expect(result).toBeUndefined()
    })
  })

  describe('promoteNote', () => {
    it('returns the promoted Note with Permanent status', async () => {
      const note = makeNote({ status: 'Permanent' })
      globalThis.fetch = mockFetch(note)

      const { promoteNote } = await import('@/api/notes')
      const result = await promoteNote('20260215120000000')

      expect(result.status).toBe('Permanent')
    })

    it('passes noteType as query parameter when provided', async () => {
      const note = makeNote({ status: 'Permanent', noteType: 'Source' })
      globalThis.fetch = mockFetch(note)

      const { promoteNote } = await import('@/api/notes')
      await promoteNote('20260215120000000', 'Source')

      const url = (globalThis.fetch as ReturnType<typeof vi.fn>).mock.calls[0][0] as string
      expect(url).toContain('noteType=source')
    })
  })

  describe('reEmbedAll', () => {
    it('returns an object with queued count', async () => {
      globalThis.fetch = mockFetch({ queued: 10 })

      const { reEmbedAll } = await import('@/api/notes')
      const result = await reEmbedAll()

      expect(result).toEqual({ queued: 10 })
      expect(typeof result.queued).toBe('number')
    })
  })

  describe('searchTitles', () => {
    it('returns a plain array of TitleSearchResult', async () => {
      const results = [
        { noteId: '1', title: 'First' },
        { noteId: '2', title: 'Second' },
      ]
      globalThis.fetch = mockFetch(results)

      const { searchTitles } = await import('@/api/notes')
      const result = await searchTitles('test')

      expect(Array.isArray(result)).toBe(true)
      expect(result).toHaveLength(2)
      expect(result[0]).toHaveProperty('noteId')
      expect(result[0]).toHaveProperty('title')
    })
  })

  describe('getRelatedNotes', () => {
    it('returns a plain array of SearchResult', async () => {
      const results = [makeSearchResult(), makeSearchResult({ noteId: '2' })]
      globalThis.fetch = mockFetch(results)

      const { getRelatedNotes } = await import('@/api/notes')
      const result = await getRelatedNotes('20260215120000000')

      expect(Array.isArray(result)).toBe(true)
      expect(result[0]).toHaveProperty('noteId')
      expect(result[0]).toHaveProperty('snippet')
      expect(result[0]).toHaveProperty('rank')
    })
  })

  describe('discoverNotes', () => {
    it('returns a plain array of SearchResult', async () => {
      globalThis.fetch = mockFetch([makeSearchResult()])

      const { discoverNotes } = await import('@/api/notes')
      const result = await discoverNotes()

      expect(Array.isArray(result)).toBe(true)
    })
  })

  describe('getBacklinks', () => {
    it('returns a plain array of BacklinkResult', async () => {
      const backlinks = [
        { id: '1', title: 'Linking note' },
        { id: '2', title: 'Another link' },
      ]
      globalThis.fetch = mockFetch(backlinks)

      const { getBacklinks } = await import('@/api/notes')
      const result = await getBacklinks('20260215120000000')

      expect(Array.isArray(result)).toBe(true)
      expect(result[0]).toHaveProperty('id')
      expect(result[0]).toHaveProperty('title')
    })
  })

  describe('mergeNote', () => {
    it('returns the merged Note', async () => {
      const note = makeNote()
      globalThis.fetch = mockFetch(note)

      const { mergeNote } = await import('@/api/notes')
      const result = await mergeNote('fleeting1', 'target1')

      expect(result.id).toBe('20260215120000000')
    })
  })

  describe('checkDuplicate', () => {
    it('returns a DuplicateCheckResult', async () => {
      const body = {
        isDuplicate: true,
        similarNoteId: '123',
        similarNoteTitle: 'Similar',
        similarity: 0.92,
      }
      globalThis.fetch = mockFetch(body)

      const { checkDuplicate } = await import('@/api/notes')
      const result = await checkDuplicate('test content')

      expect(result.isDuplicate).toBe(true)
      expect(result.similarNoteId).toBe('123')
      expect(typeof result.similarity).toBe('number')
    })
  })

  describe('getSuggestedTags', () => {
    it('returns a plain string array', async () => {
      globalThis.fetch = mockFetch(['tag1', 'tag2', 'tag3'])

      const { getSuggestedTags } = await import('@/api/notes')
      const result = await getSuggestedTags('20260215120000000')

      expect(Array.isArray(result)).toBe(true)
      expect(result).toEqual(['tag1', 'tag2', 'tag3'])
    })
  })
})

// --- Search API ---

describe('search API contract', () => {
  it('returns a plain array of SearchResult', async () => {
    const results = [makeSearchResult(), makeSearchResult({ noteId: '2' })]
    globalThis.fetch = mockFetch(results)

    const { search } = await import('@/api/search')
    const result = await search('test query')

    expect(Array.isArray(result)).toBe(true)
    expect(result).toHaveLength(2)
    expect(result[0]).toHaveProperty('noteId')
    expect(result[0]).toHaveProperty('title')
    expect(result[0]).toHaveProperty('snippet')
    expect(result[0]).toHaveProperty('rank')
  })
})

// --- Graph API ---

describe('graph API contract', () => {
  it('returns GraphData with nodes and edges arrays', async () => {
    const graphData = {
      nodes: [
        { id: '1', title: 'Note 1', edgeCount: 2 },
        { id: '2', title: 'Note 2', edgeCount: 1 },
      ],
      edges: [{ source: '1', target: '2', type: 'wikilink', weight: 1.0 }],
    }
    globalThis.fetch = mockFetch(graphData)

    const { getGraph } = await import('@/api/graph')
    const result = await getGraph()

    expect(Array.isArray(result.nodes)).toBe(true)
    expect(Array.isArray(result.edges)).toBe(true)
    expect(result.nodes[0]).toHaveProperty('id')
    expect(result.nodes[0]).toHaveProperty('title')
    expect(result.nodes[0]).toHaveProperty('edgeCount')
    expect(result.edges[0]).toHaveProperty('source')
    expect(result.edges[0]).toHaveProperty('target')
    expect(result.edges[0]).toHaveProperty('type')
    expect(result.edges[0]).toHaveProperty('weight')
  })
})

// --- Tags API ---

describe('tags API contract', () => {
  it('returns a plain string array', async () => {
    globalThis.fetch = mockFetch(['rust', 'react', 'testing'])

    const { searchTags } = await import('@/api/tags')
    const result = await searchTags('re')

    expect(Array.isArray(result)).toBe(true)
    expect(result).toEqual(['rust', 'react', 'testing'])
  })
})

// --- Versions API ---

describe('versions API contract', () => {
  describe('listVersions', () => {
    it('returns a plain array of NoteVersion', async () => {
      const versions = [
        {
          id: 1,
          noteId: '20260215120000000',
          title: 'V1',
          content: 'First',
          savedAt: '2026-02-15T12:00:00Z',
        },
        {
          id: 2,
          noteId: '20260215120000000',
          title: 'V2',
          content: 'Second',
          savedAt: '2026-02-15T13:00:00Z',
        },
      ]
      globalThis.fetch = mockFetch(versions)

      const { listVersions } = await import('@/api/versions')
      const result = await listVersions('20260215120000000')

      expect(Array.isArray(result)).toBe(true)
      expect(result).toHaveLength(2)
      expect(result[0]).toHaveProperty('id')
      expect(result[0]).toHaveProperty('noteId')
      expect(result[0]).toHaveProperty('title')
      expect(result[0]).toHaveProperty('content')
      expect(result[0]).toHaveProperty('savedAt')
    })
  })

  describe('getVersion', () => {
    it('returns a single NoteVersion object', async () => {
      const version = {
        id: 1,
        noteId: '20260215120000000',
        title: 'V1',
        content: 'First',
        savedAt: '2026-02-15T12:00:00Z',
      }
      globalThis.fetch = mockFetch(version)

      const { getVersion } = await import('@/api/versions')
      const result = await getVersion('20260215120000000', 1)

      expect(result.id).toBe(1)
      expect(result.noteId).toBe('20260215120000000')
    })
  })
})

// --- Import API ---

describe('import API contract', () => {
  it('returns ImportResult with counts and noteIds array', async () => {
    const importResult = {
      total: 3,
      imported: 2,
      skipped: 1,
      noteIds: ['id1', 'id2'],
    }
    globalThis.fetch = mockFetch(importResult)

    const { importNotes } = await import('@/api/import-export')
    const result = await importNotes([
      { fileName: 'test.md', content: '# Test' },
    ])

    expect(result.total).toBe(3)
    expect(result.imported).toBe(2)
    expect(result.skipped).toBe(1)
    expect(Array.isArray(result.noteIds)).toBe(true)
    expect(result.noteIds).toEqual(['id1', 'id2'])
  })
})

// --- Health API ---

describe('health API contract', () => {
  it('returns HealthReport with status and entries', async () => {
    const healthReport = {
      status: 'Healthy',
      entries: {
        database: {
          status: 'Healthy',
          description: null,
          data: { responseTime: '5ms' },
        },
      },
    }
    globalThis.fetch = mockFetch(healthReport)

    const { getHealth } = await import('@/api/health')
    const result = await getHealth()

    expect(result.status).toBe('Healthy')
    expect(result.entries).toHaveProperty('database')
    expect(result.entries.database.status).toBe('Healthy')
    expect(result.entries.database).toHaveProperty('data')
  })
})

// --- Client error handling ---

describe('API client error handling', () => {
  it('throws ApiError on non-OK responses', async () => {
    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: false,
      status: 404,
      statusText: 'Not Found',
      text: () => Promise.resolve(''),
    })

    const { getNote } = await import('@/api/notes')

    await expect(getNote('nonexistent')).rejects.toThrow()
    await expect(getNote('nonexistent')).rejects.toMatchObject({
      status: 404,
    })
  })
})
