# Implementation Progress - v2 (Spec Completion)

**Started**: 2026-02-14
**Goal**: Complete all outstanding features from spec

## Batch Summary

| Batch | Feature | Status | Tests Added |
|-------|---------|--------|-------------|
| 19 | Search similarity scores in UI | **Done** | 0 (FE only) |
| 20 | Note title search endpoint | **Done** | 6 |
| 21 | Wiki-link frontend API + hook | **Done** | 0 (FE only) |
| 22 | Wiki-link Tiptap extension | **Done** | 0 (FE only) |
| 23 | Bulk import progress | **Done** | 0 (FE only) |
| 24 | Related notes backend service | **Done** | 6 |
| 25 | Related notes backend endpoint | **Done** | 2 |
| 26 | Related notes frontend sidebar | **Done** | 0 (FE only) |
| 27 | Discovery digest backend | Pending | ~4 |
| 28 | Discovery digest frontend | Pending | 0 (FE only) |
| 29 | Knowledge graph backend | Pending | ~6 |
| 30 | Knowledge graph frontend | Pending | 0 (FE only) |

## Detailed Progress

### Batch 19: Search Similarity Scores in UI
**Status**: Done
- [x] Add rank percentage to SearchResultItem

### Batch 20: Note Title Search Endpoint
**Status**: Done (127 total tests)
- [x] RED: Write 6 failing NoteService tests
- [x] GREEN: Add TitleSearchResult model
- [x] GREEN: Implement SearchTitlesAsync
- [x] GREEN: Add search-titles endpoint
- [x] Verify: all 127 tests pass

### Batch 21: Wiki-Link Frontend API + Hook
**Status**: Done
- [x] Add TitleSearchResult type
- [x] Add searchTitles API function
- [x] Install @tiptap/suggestion
- [x] Verify: tsc passes

### Batch 22: Wiki-Link Tiptap Extension
**Status**: Done
- [x] Create wiki-link-suggestion extension (with @tiptap/suggestion)
- [x] Create wiki-link-popup component (arrow keys, enter, escape)
- [x] Create suggestion-renderer (ReactRenderer + tippy.js)
- [x] Wire into note-editor
- [x] Verify: tsc passes

### Batch 23: Bulk Import Progress
**Status**: Done
- [x] Add embed progress section to settings (progress bar + counts)
- [x] Poll health at 5s interval when embedding in progress
- [x] Auto-hide when complete (useEffect)

### Batch 24: Related Notes Backend Service
**Status**: Done (133 total tests)
- [x] RED: Write 6 failing tests
- [x] GREEN: Add FindRelatedAsync to ISearchService
- [x] GREEN: Implement in SearchService
- [x] Verify: all 133 tests pass

### Batch 25: Related Notes Backend Endpoint
**Status**: Done (135 total tests)
- [x] RED: Write 2 endpoint tests (Related_ReturnsOkWithResults, Related_DefaultsToLimit5)
- [x] GREEN: Add ISearchService to NotesController
- [x] GREEN: Add [HttpGet("{id}/related")] endpoint
- [x] Update NotesControllerTests with StubSearchService
- [x] Verify: all 135 tests pass

### Batch 26: Related Notes Frontend Sidebar
**Status**: Done
- [x] Add getRelatedNotes API function
- [x] Create useRelatedNotes hook
- [x] Create RelatedNotesSidebar component (with loading skeletons, similarity %)
- [x] Update note page layout (flex container, sticky sidebar, hidden on mobile)

### Batch 27: Discovery Digest Backend
**Status**: Pending
- [ ] RED: Write 4 failing tests
- [ ] GREEN: Add DiscoverAsync to ISearchService
- [ ] GREEN: Implement in SearchService
- [ ] GREEN: Add discover endpoint
- [ ] Verify: all tests pass

### Batch 28: Discovery Digest Frontend
**Status**: Pending
- [ ] Add discoverNotes API
- [ ] Create useDiscover hook
- [ ] Create dismissed-notes utility
- [ ] Create DiscoverySection component
- [ ] Add to home page

### Batch 29: Knowledge Graph Backend
**Status**: Pending
- [ ] RED: Write 6 failing tests
- [ ] GREEN: Create GraphModels
- [ ] GREEN: Create IGraphService + GraphService
- [ ] GREEN: Create GraphController
- [ ] Register in DI
- [ ] Verify: all tests pass

### Batch 30: Knowledge Graph Frontend
**Status**: Pending
- [ ] Install react-force-graph-2d
- [ ] Add graph types and API
- [ ] Create useGraph hook
- [ ] Create GraphView component
- [ ] Create graph page + route
- [ ] Add nav links
