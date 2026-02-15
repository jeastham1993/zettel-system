# UX & Features Fix Progress

Generated: 2026-02-15
Source: Three parallel agents implementing fixes from `docs/review-ux-and-features.md`

---

## Agent 1: Frontend UX + Mobile + PWA (20/20 complete)

| # | Issue | Status | Files |
|---|-------|--------|-------|
| 1 | Delete toast on note view | Done | `note-view.tsx` |
| 2 | Escape key suppression during editing | Done | `use-keyboard-shortcuts.ts` |
| 3 | FAB bottom padding (pb-20) | Done | `app-shell.tsx` |
| 4 | Error recovery with retry + Go home | Done | `editor.tsx`, `note.tsx` |
| 5 | Draft restored notification | Done | `note-editor.tsx` |
| 6 | Autosave data hazard fix (skip existing notes) | Done | `use-autosave.ts`, `note-editor.tsx` |
| 7 | Capture toast with "View inbox" link | Done | `capture-button.tsx` |
| 8 | Graph empty state with guidance | Done | `graph.tsx` |
| 9 | Search loading spinner in command menu | Done | `command-menu.tsx` |
| 10 | Cross-platform Cmd/Ctrl keyboard hints | Done | `header.tsx`, `note-editor.tsx` |
| 11 | Clickable tags for filtering (via event) | Done | `note-view.tsx`, `app-shell.tsx` |
| 12 | Note count on home page | Done | `home.tsx` |
| 13 | Mobile touch targets (40px+) + hamburger menu | Done | `header.tsx` |
| 14 | Editor toolbar mobile wrap + larger buttons | Done | `editor-toolbar.tsx` |
| 15 | Command menu mobile responsive width | Done | `command-menu.tsx` |
| 16 | Related notes visible on mobile (collapsible) | Done | `note.tsx` |
| 17 | Graph mobile tap-to-show + tap-to-navigate | Done | `graph-view.tsx`, `graph.tsx` |
| 18 | Keyboard shortcuts cheat sheet (? key) | Done | NEW: `keyboard-shortcuts-dialog.tsx` |
| 19 | Offline indicator (toast on disconnect) | Done | `app-shell.tsx` |
| 20 | PWA manifest + service worker | Done | `vite.config.ts`, `index.html`, `package.json` |

**Validation**: `tsc --noEmit` PASS, `npm run build` PASS (0 errors, PWA sw generated)

---

## Agent 2: Backend Features (9/10 complete)

| # | Feature | Status | Files |
|---|---------|--------|-------|
| 1 | Tag filtering on ListAsync | Done | `INoteService.cs`, `NoteService.cs`, `NotesController.cs` |
| 2 | Backlinks endpoint | Done | `NoteService.cs`, `NotesController.cs` |
| 3 | Auto-title for fleeting notes (HTML strip) | Done | `NoteService.cs` |
| 4 | Inbox merge (fleeting into permanent) | Done | `NoteService.cs`, `NotesController.cs` |
| 5 | AI suggested tags (pgvector similarity) | Done | `NoteService.cs`, `NotesController.cs` |
| 6 | Duplicate detection on create | Done | `NoteService.cs`, `NotesController.cs` |
| 7 | Discovery algorithms (random/orphans/today) | Done | NEW: `IDiscoveryService.cs`, `DiscoveryService.cs`, `DiscoveryController.cs` |
| 8 | Note version history | Done | `Note.cs` (NoteVersion model), `ZettelDbContext.cs`, `NoteService.cs`, `NotesController.cs`, `Program.cs` |
| 9 | HTML stripping for embeddings | Done | `EmbeddingBackgroundService.cs` |
| 10 | Markdown content storage/export | Deferred | ExportService was outside file ownership |

**New test files**:
- `NoteServiceNewFeaturesTests.cs` (38 tests)
- `DiscoveryControllerTests.cs` (5 tests)
- `NotesControllerNewEndpointsTests.cs` (12 tests)
- `EmbeddingHtmlStrippingTests.cs` (3 tests)

**Validation**: `dotnet test` PASS -- 407 tests (58 new), 0 failures

---

## Agent 3: Frontend New Features (9/9 complete)

| # | Feature | Status | Files |
|---|---------|--------|-------|
| 1 | Clickable wiki-links in view mode | Done | NEW: `wiki-link-view.tsx`, `extensions/wiki-link-renderer.ts` |
| 2 | Wiki-link hover preview | Done | `wiki-link-view.tsx` (Popover with first 200 chars) |
| 3 | Tag filter UI on note list | Done | `note-list.tsx`, `note-list-item.tsx` |
| 4 | Backlinks section in sidebar | Done | `related-notes-sidebar.tsx` |
| 5 | Inbox merge + bulk discard UI | Done | `inbox.tsx` |
| 6 | Discovery mode selector | Done | `discovery-section.tsx` |
| 7 | Duplicate warning component | Done | NEW: `duplicate-warning.tsx` |
| 8 | AI suggested tags component | Done | NEW: `suggested-tags.tsx` |
| 9 | Version history page | Done | NEW: `versions.tsx` |

**New hooks**: `use-backlinks.ts`, `use-duplicate-check.ts`,
`use-suggested-tags.ts`, `use-versions.ts`

**New API files**: `api/discover.ts`, `api/versions.ts`

**Validation**: `tsc --noEmit` PASS, `npm run build` PASS (0 errors)

---

## Summary

| Agent | Tasks | Completed | Skipped |
|-------|-------|-----------|---------|
| Frontend UX + Mobile + PWA | 20 | 20 | 0 |
| Backend Features | 10 | 9 | 1 (markdown export) |
| Frontend New Features | 9 | 9 | 0 |
| **Total** | **39** | **38** | **1** |

## Excluded by User Request

These items were explicitly excluded from this implementation round:
- Obsidian Vault Sync
- Browser Extension for web clipping
- AI-generated note summaries
- Rich capture (images, voice)

## Deferred Items (Not Excluded, But Not Yet Done)

- **Markdown content storage** (1D) -- Backend agent couldn't touch
  ExportService. Content still stored as HTML.
- **Daily digest email** -- Requires SMTP infrastructure
- **Cluster detection / topic map** -- Complex ML task
- **"What's missing?" gap analysis** -- Depends on clustering
- **Read-later/bookmark capture** -- Could be a future batch

## Known Issues

1. **Pre-existing test failure**: `NotesHttpIntegrationTests.GET_Notes_Returns200WithList`
   deserializes `PagedResult<Note>` as `List<NoteResponse>` (from earlier
   pagination change). Not caused by these agents.
2. **Vite chunk size warning**: `index-*.js` is ~1MB (315KB gzip). The
   graph/inbox/settings pages are already code-split. Further splitting of
   the main bundle could target Tiptap dependencies.
3. **CSS @import warning**: Pre-existing Tailwind/Google Fonts ordering
   warning in production build. Cosmetic only.
