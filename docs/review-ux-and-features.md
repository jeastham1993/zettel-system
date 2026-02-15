# UX Review & Feature Gap Analysis

Generated: 2026-02-15
Source: Automated specialist agent reviews

---

# Part 1: UX Review

## 1. Critical UX Issues

### 1.1 No Pagination — All Notes Loaded at Once (P0)
- **Files**: `src/zettel-web-ui/src/api/notes.ts:4-6`, `src/zettel-web-ui/src/hooks/use-notes.ts:5-9`
- **What the user experiences**: `listNotes()` calls `GET /api/notes` with no pagination
  parameters, backend defaults to `take=50`. No infinite scroll, "load more", or
  pagination controls. Once James exceeds 50 notes, older notes silently disappear.
  The command menu also fetches all notes via `useNotes()` for "Recent".
- **What the user should experience**: Infinite scroll or explicit pagination with a
  count indicator like "Showing 50 of 247 notes". Backend already supports `skip`/`take`.

### 1.2 Autosave Creates Silent Data Hazard for Existing Notes (P0)
- **Files**: `src/zettel-web-ui/src/hooks/use-autosave.ts:26-30`,
  `src/zettel-web-ui/src/components/note-editor.tsx:45`
- **What the user experiences**: `loadDraft()` only called for new notes. But
  `useAutosave` runs for ALL notes including existing ones (line 72). Drafts are saved
  to localStorage for existing notes but never restored — orphaned data. No visual
  indicator that autosave is active ("Draft saved" timestamp).
- **What the user should experience**: Either restore drafts for existing notes too
  (with "Recovered from draft" notification), or remove autosave for existing notes.
  Add "Draft saved at HH:MM" indicator near save button.

### 1.3 Escape Key Navigates Away During Editing (P1)
- **File**: `src/zettel-web-ui/src/hooks/use-keyboard-shortcuts.ts:31-37`
- **What the user experiences**: Pressing Escape while editing navigates to `/`. Checks
  for open dialogs but NOT whether user is actively typing. Produces confusing two-step:
  accidental navigation + "You have unsaved changes" dialog.
- **What the user should experience**: Escape suppressed on editor pages.

### 1.4 No Error Recovery on Note Load Failure (P1)
- **Files**: `src/zettel-web-ui/src/pages/editor.tsx:21-23`,
  `src/zettel-web-ui/src/pages/note.tsx:21-24`
- **What the user experiences**: Failed note load shows plain red text with no retry
  button, no link back, no guidance. After 2 retries the user is stuck.
- **What the user should experience**: "Try again" button (triggering `refetch()`),
  "Go home" link, specific message (404 vs network error).

### 1.5 Delete Without Feedback on Note View Page (P1)
- **File**: `src/zettel-web-ui/src/components/note-view.tsx:32-36`
- **What the user experiences**: Delete navigates to `/` with no toast. Compare inbox
  discard which properly shows `toast.success('Note discarded')`.
- **What the user should experience**: `toast.success('Note deleted')` before navigating.

---

## 2. Mobile Readiness

### 2.1 Header Touch Targets Too Small (P1)
- **Files**: `src/zettel-web-ui/src/components/header.tsx:27-98`,
  `src/zettel-web-ui/src/components/ui/button.tsx:30`
- All header buttons use `size="sm"` = `h-8` (32px). Apple HIG and WCAG recommend 44px
  minimum. 6 icon buttons crammed with `gap-1` (4px) spacing. Fat-finger errors.
- On mobile: expand to 44px or reorganize (hamburger menu / bottom nav).

### 2.2 Editor Toolbar Barely Usable on Mobile (P1)
- **File**: `src/zettel-web-ui/src/components/editor-toolbar.tsx:53-124`
- 9 formatting buttons at 32x32px in a single row. On 375px phone may overflow. No
  wrapping, no scroll indicator. Tooltips rely on hover (no touch equivalent).
- Should wrap on mobile, increase to 44px targets, replace tooltips with long-press.

### 2.3 Command Palette Is Keyboard-Only (P1)
- **Files**: `src/zettel-web-ui/src/hooks/use-command-menu.ts:6-15`,
  `src/zettel-web-ui/src/components/header.tsx:28-43`
- Triggered via `Cmd+K`. Header provides clickable Search button (works on mobile) but
  `kbd` hint hidden on mobile. Dialog has no mobile optimizations.
- Search button should be more prominent on mobile. Dialog should be full-screen.

### 2.4 Related Notes Sidebar Hidden on Mobile (P2)
- **File**: `src/zettel-web-ui/src/pages/note.tsx:29`
- `<aside className="hidden w-56 shrink-0 lg:block">` — invisible below 1024px.
  For a Zettelkasten app, hiding related notes on mobile is a significant loss.
- Should appear below note content on mobile (stacked layout) or as expandable section.

### 2.5 Capture Button Overlaps Content (P2)
- **File**: `src/zettel-web-ui/src/components/capture-button.tsx:77-79`
- Fixed `bottom-6 right-6` FAB overlaps last items in scrollable lists. No bottom
  padding on page content.
- Add `pb-20` to main content area.

### 2.6 Graph Page Has No Mobile Controls (P2)
- **Files**: `src/zettel-web-ui/src/pages/graph.tsx:42-46`,
  `src/zettel-web-ui/src/components/graph-view.tsx:58-75`
- Node labels via hover (no touch equivalent). Click navigates immediately (no preview).
  No pinch-to-zoom instructions. Hardcoded 0.8 threshold.
- Tap should show title popover with "Open note" action. Add legend and controls.

---

## 3. Flow Analysis

### 3.1 Note Creation Flow
- **Entry points**: Header "New" button, `Cmd+N`, command palette, empty state CTA
- **Friction**:
  - (P2) Title required but no inline validation — error only shows as toast on Save
  - (P3) After creating, no "Create another" action visible
  - (P2) Draft recovery for new notes is silent — no "Restored from draft" notification

### 3.2 Note Editing Flow
- **Entry**: Note view -> Edit button, or `/notes/:id/edit`
- **Friction**:
  - (P2) View-to-edit is a full page navigation with layout shift
  - (P3) `Cmd+S` hint hidden on mobile

### 3.3 Search Flow
- **Entry**: Header search icon, `Cmd+K`
- **Friction**:
  - (P2) No search type selection (hardcoded hybrid)
  - (P3) Search score percentage unexplained
  - (P2) Command palette doesn't preserve query on navigate-back

### 3.4 Quick Capture Flow
- **Entry**: FAB click, `Ctrl+Shift+N`
- **Friction**:
  - (P3) No "Go to inbox" link in capture toast
  - (P2) "auto" title never explained; shows up confusingly in inbox

### 3.5 Inbox/Fleeting Notes Flow
- **Entry**: Header inbox icon (with badge)
- **Friction**:
  - (P1) "Process" opens editor with title "auto" — no guidance to add real title
  - (P2) "Promote" keeps "auto" title — note list shows "auto" titles
  - (P2) No bulk actions for stale notes (discard one-by-one with confirmation each)

### 3.6 Import/Export Flow
- **Entry**: Settings page only
- **Friction**:
  - (P3) No drag-and-drop import
  - (P3) No preview before import; no detail on which notes skipped

### 3.7 Graph Exploration Flow
- **Entry**: Header graph icon, command palette
- **Friction**:
  - (P1) No context on empty state — no guidance on what to do
  - (P2) Click navigates immediately — no preview
  - (P3) No controls (zoom, threshold, filter)

### 3.8 Tag Management Flow
- **Entry**: Tag input in editor only
- **Friction**:
  - (P2) Tags on note list/view are purely decorative — clicking does nothing
  - (P3) No tag management page (rename across notes, delete unused)

---

## 4. Missing UX Patterns

| Priority | Pattern | Description |
|----------|---------|-------------|
| P2 | Offline indicator | No "You're offline" banner when network drops during editing |
| P2 | Undo for destructive actions | Delete is permanent; soft-delete with 5s undo toast would be safer |
| P3 | Note count/stats on home | No "247 notes, 12 tags, 3 in inbox" dashboard |
| P3 | Keyboard shortcut cheat sheet | No "?" shortcut to show help modal |
| P2 | Promote confirmation | Promote button acts immediately — may keep "auto" title |
| P3 | Search loading state | Command menu shows "No results" during search (should show spinner) |

---

## 5. Quick Wins

| Priority | Effort | Change | File |
|----------|--------|--------|------|
| P1 | ~5 min | Add `toast.success('Note deleted')` on delete | `note-view.tsx:32-36` |
| P2 | ~30 min | Make tags clickable for filtering | `note-list-item.tsx:28-37`, `note-view.tsx:58-66` |
| P2 | ~10 min | Add "Draft restored" notification | `note-editor.tsx:44-49` |
| P3 | ~10 min | Show loading spinner in command menu search | `command-menu.tsx:54-55` |
| P2 | ~2 min | Add `pb-20` to main for FAB clearance | `app-shell.tsx:13` |
| P1 | ~10 min | Suppress Escape on editor pages | `use-keyboard-shortcuts.ts:31-37` |
| P3 | ~5 min | Add "Go to inbox" link in capture toast | `capture-button.tsx:40` |
| P2 | ~15 min | Improve graph empty state with guidance | `graph.tsx:34-39` |

---

## Priority Summary

| Priority | Count | Examples |
|----------|-------|---------|
| P0 | 2 | Pagination (silent data loss), autosave data hazard |
| P1 | 7 | Escape navigation, error recovery, delete feedback, mobile touch targets |
| P2 | 12 | Tags not clickable, related notes hidden on mobile, FAB overlap |
| P3 | 8 | Loading spinner, keyboard shortcut help, import preview |

---

# Part 2: Feature Gap Analysis

## 1. Feature Gaps (Missing Essentials)

### 1A. Backlinks / Incoming Link Display
- **What's missing**: No way to see which notes link *to* the current note. The
  `RelatedNotesSidebar` shows semantically similar notes, not explicit backlinks.
- **Why it matters**: Backlinks are the defining Zettelkasten feature. Without them,
  wiki-links are one-directional dead ends. Ideas don't accrete context over time.
- **Complexity**: S — GraphService already parses `[[...]]` links. Need `GET
  /api/notes/{id}/backlinks` endpoint + sidebar section.
- **Builds on**: `GraphService.WikiLinkRegex()`, `SearchTitlesAsync`,
  `RelatedNotesSidebar` component.

### 1B. Clickable Wiki-Links in Note View
- **What's missing**: `[[Note Title]]` rendered as plain text in view mode. No custom
  Tiptap extension to render as clickable navigation links.
- **Why it matters**: If you can't click a link to navigate, the linking system is
  display-only. Users must copy title, search, and navigate manually.
- **Complexity**: M — Custom Tiptap extension to detect `[[...]]` and wrap in anchors.
- **Builds on**: `WikiLinkSuggestion`, `searchTitles` API, Tiptap extensions.

### 1C. Note Filtering by Tag
- **What's missing**: Tags displayed but not clickable. No tag filter on note list.
  Backend `ListAsync` only filters by `NoteStatus`, not tag.
- **Why it matters**: Tags are the primary organizational mechanism (no folders). As
  collection grows, finding notes requires search every time.
- **Complexity**: S — Backend needs `tag` query param. Frontend needs clickable badges
  and filter state.
- **Builds on**: `NoteTag` model, `SearchTagsAsync`.

### 1D. Markdown Content Storage
- **What's missing**: Content stored as HTML. Exports contain HTML inside `.md` files.
  Embeddings include HTML tag noise. Breaks Obsidian interoperability.
- **Why it matters**: HTML storage reduces embedding quality, breaks export
  compatibility, and prevents proper full-text search.
- **Complexity**: M — Store parallel markdown or convert on export/embed.

### 1E. Pagination (Being Fixed)
- Already in progress via fix agents.

### 1F. Note Version History / Undo
- **What's missing**: No version history. `UpdateAsync` overwrites in-place. No audit
  trail. Autosave drafts expire after 24 hours.
- **Why it matters**: Accidentally overwriting content in a long-lived knowledge base is
  a significant risk.
- **Complexity**: M — `NoteVersion` table with snapshots on save. Diff viewer frontend.

---

## 2. Enhancement Opportunities

### 2A. Graph View: Local Neighborhood Mode
- **What exists**: Full graph of all notes (O(n^2) computation).
- **What's lacking**: No way to explore starting from a specific note.
- **Suggested**: `GET /api/graph/{noteId}?depth=2` for focused neighborhood view.
- **Impact**: Transforms graph from curiosity into daily navigation tool.

### 2B. Inbox: Merge Into Existing Note
- **What exists**: Process, Promote, or Discard fleeting notes.
- **What's lacking**: Can't merge a fleeting thought into an existing permanent note.
- **Suggested**: "Merge into..." action with note picker. Appends content, deletes
  fleeting note.
- **Impact**: High — directly supports building atomic notes over time.

### 2C. Search: Tag and Status Filters
- **What exists**: Search supports `q` and `type` only.
- **What's lacking**: Can't scope search by tags or note status.
- **Suggested**: Add `tags[]` and `status` params. Filter chips in command menu.

### 2D. Editor: Inline Preview / Transclusion
- **What exists**: `[[wiki links]]` with autocomplete.
- **What's lacking**: Can't see linked note content without navigating away.
- **Suggested**: Hover preview tooltips showing first ~200 chars of linked note.

### 2E. Discovery: More Serendipity Algorithms
- **What exists**: "Rediscover" averages recent embeddings to find similar older notes.
- **What's lacking**: Only one algorithm, biased toward recent activity.
- **Suggested**: Add "Random forgotten" (30+ days untouched), "Orphans" (no tags/links),
  "This day in history" (created on same date in previous years).

### 2F. Quick Capture: Richer Input
- **What exists**: Plain textarea + tags.
- **What's lacking**: No images, voice memos, rich text. Telegram ignores photos/voice.
- **Suggested**: Image upload in web capture. Handle Telegram photo/document/voice.

---

## 3. AI-Powered Features (Leveraging Embeddings)

### 3A. AI-Suggested Tags (S)
Use nearest-neighbor embeddings to find common tags among related notes. Suggest during
editing. Builds on `FindRelatedAsync` + `TagInput` autocomplete.

### 3B. Cluster Detection / Topic Map (L)
Periodically cluster embeddings (k-means/DBSCAN). Show topic areas and gaps. Color-code
graph view by cluster.

### 3C. Duplicate / Near-Duplicate Detection (S)
On note create, check embedding similarity >0.95. Warn: "Very similar to [[Existing
Note]]. Update that instead?" Reuses `SemanticSearchAsync`.

### 3D. AI-Generated Note Summaries (M)
One-sentence summary per note via `IChatClient` (Microsoft.Extensions.AI). Use in search
results, note list, graph labels instead of raw truncation.

### 3E. "What's Missing?" Gap Analysis (L)
Identify pairs of notes in same semantic neighborhood with no explicit links. Suggest
bridging notes. Depends on clustering (3B).

---

## 4. Capture & Integration Opportunities

### 4A. Browser Extension for Web Clipping (M)
Highlight text on any webpage -> "Send to Zettel". POSTs to capture API. Enrichment
pipeline handles URL metadata automatically.

### 4B. Mobile PWA (M)
Responsive breakpoints + service worker manifest. Capture dialog already suitable for
mobile. Install on phone home screen.

### 4C. Read-Later / Bookmark Integration (M)
Accept URLs via `POST /api/capture/bookmark`. Fetch full article text via readability
parser. Create fleeting note with content.

### 4D. Daily Digest Email (M)
Inbox count, rediscovery suggestions, orphan notes, recent notes summary. Background
service with SMTP. Creates habit loop for inbox review.

### 4E. Obsidian Vault Sync (L)
Export in Obsidian-compatible format (markdown + YAML front matter + `[[links]]`).
Depends on fixing HTML-to-markdown content (1D).

---

## 5. Prioritized Roadmap

### Phase 1: Linking Foundations (S effort, highest impact)
1. Backlinks display
2. Clickable wiki-links in view mode
3. Tag-based filtering
4. Pagination / infinite scroll

### Phase 2: Workflow Refinement
5. Merge fleeting note into existing note
6. Search filters (tag, status)
7. AI-suggested tags
8. Duplicate detection on create

### Phase 3: Discovery & Navigation
9. Additional discovery algorithms (random forgotten, orphans, this day)
10. Local neighborhood graph
11. HTML-to-markdown content pipeline

### Phase 4: Capture & Mobile
12. Browser extension for web clipping
13. PWA / mobile responsiveness

### Phase 5: Safety & Polish
14. Note version history
15. Hover preview on wiki-links
16. AI-generated summaries

### Phase 6: Advanced AI
17. Topic clustering / map
18. Daily digest email
19. Read-later / bookmark capture
20. Obsidian vault sync
21. "What's missing?" gap analysis
22. Rich capture (images, voice)
