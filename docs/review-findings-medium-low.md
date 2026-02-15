# Review Findings: Medium & Low Priority

Generated: 2026-02-15
Source: Automated codebase review (Security, Performance, Architecture, .NET, React)

Items #1 (hardcoded secrets), #2 (no auth), and #6 (no TLS) are being tackled manually.
Critical and High items are being fixed by automated agents.

---

## MEDIUM Findings

### M1. Stored XSS — No Server-Side HTML Sanitization
**Source**: Security
**File**: `Controllers/NotesController.cs`, `Services/CaptureService.cs:217`
Note content is stored as raw Tiptap HTML with no server-side sanitization. Tiptap's
schema-based rendering mitigates direct XSS in the editor, but content consumed outside
Tiptap (e.g., `truncateContent` in `format.ts:19`, `StripHtml` in CaptureService) uses
naive regex stripping (`content.replace(/<[^>]*>/g, '')`) which is incomplete.
**Fix**: Add HtmlSanitizer (NuGet) on write path. Add DOMPurify on frontend for any raw
HTML rendering outside Tiptap.

### M2. CORS Disabled in Production
**Source**: Security
**File**: `Program.cs:199-200`
`app.UseCors()` only applied in Development. In production, no CORS policy exists. If
backend is ever exposed directly (bypassing Traefik), any origin can make cross-origin
API calls.
**Fix**: Apply CORS in all environments with configured allowed origins.

### M3. Docker Containers Run as Root
**Source**: Security
**Files**: `src/ZettelWeb/Dockerfile`, `src/zettel-web-ui/Dockerfile`
Neither Dockerfile specifies a non-root user. Add `USER` directive to both.

### M4. Health Endpoint Exposes Infrastructure Details
**Source**: Security
**File**: `Program.cs:203`
Health endpoint is unauthenticated and returns detailed status for database, embedding,
and SQS components.
**Fix**: Customize `HealthCheckOptions.ResponseWriter` to return only aggregate status
in production.

### M5. Traefik Docker Socket Access
**Source**: Security
**File**: `docker-compose.yml:11`
Docker socket mounted into Traefik (even read-only) allows container enumeration and
environment variable inspection. Consider `tecnativa/docker-socket-proxy`.

### M6. `DiscoverAsync` Loads Full Entities Including Embeddings
**Source**: Performance
**File**: `Services/SearchService.cs:182-233`
Loads full Note entities (Content, EnrichmentJson, Embedding) just to average 3 vectors.
**Fix**: `.Select(n => new { n.Id, n.Embedding })`.

### M7. No `CreatedAt` Index for Main Listing Sort
**Source**: Performance
**File**: `Services/NoteService.cs:82-95`
`OrderByDescending(n => n.CreatedAt)` with no index forces PostgreSQL to sort the full
table on every list request.
**Fix**: `CREATE INDEX idx_notes_created_at ON "Notes" ("CreatedAt" DESC);`

### M8. Import N+1: Individual `AnyAsync` Per File
**Source**: Performance
**File**: `Services/ImportService.cs:44`
Each Notion-format import file issues a separate `SELECT EXISTS(...)`. For 500 files =
500 round trips.
**Fix**: Pre-fetch existing IDs in bulk with a single query.

### M9. No Response Caching on Expensive Endpoints
**Source**: Performance
**Files**: `Controllers/NotesController.cs`, `Controllers/GraphController.cs`
Graph, discover, and related endpoints re-execute expensive queries on every request.
**Fix**: Add `IMemoryCache` or `[ResponseCache]` with appropriate TTLs.

### M10. No PostgreSQL Tuning in Docker
**Source**: Performance
**File**: `docker-compose.yml`
Default pg17 uses small `shared_buffers` (128MB). No connection pool config.
**Fix**: Add `postgres -c shared_buffers=256MB -c work_mem=16MB` and pool size in
connection string.

### M11. `CaptureService` Bypasses NoteService Abstraction
**Source**: Architecture
**File**: `Services/CaptureService.cs:43-49`
After calling `_noteService.CreateFleetingAsync()`, directly queries DbContext to set
`EnrichStatus`. Split ownership of note state, two `SaveChangesAsync` calls not in a
transaction.
**Fix**: Move `EnrichStatus` mutation into `INoteService.CreateFleetingAsync`.

### M12. `Note` Model Is Anemic — No Invariant Enforcement
**Source**: Architecture
**File**: `Models/Note.cs:29-63`
All properties have public setters. No encapsulation of state transitions for
`EmbedStatus`, `EnrichStatus`, `EmbedRetryCount`. Invariants scattered across services.
**Fix**: Add mutation methods with internal setters for status fields.

### M13. Hand-Rolled DDL in Program.cs
**Source**: Architecture
**File**: `Program.cs:116-196`
80-line raw SQL migration block with `IF NOT EXISTS` guards. No ordering, no rollback,
no tracking of applied migrations.
**Fix**: Consider switching to EF Core migrations while schema is still small.

### M14. `EmbeddingBackgroundService` Channel Processor No Per-Item Error Handling
**Source**: .NET
**File**: `Background/EmbeddingBackgroundService.cs:46-50`
Unlike `EnrichmentBackgroundService`, the embedding channel processor has no per-item
try/catch. An exception terminates the entire channel loop.
**Fix**: Add per-item try/catch matching the enrichment pattern.

### M15. `NpgsqlDataSource` Never Disposed
**Source**: .NET
**File**: `Program.cs:19-22`
`NpgsqlDataSourceBuilder.Build()` returns `IDisposable` but is never registered with DI.
**Fix**: `builder.Services.AddSingleton(dataSource);`

### M16. `CaptureService` Not Behind Interface
**Source**: .NET, Architecture
**File**: `Program.cs:36`
Registered as `AddScoped<CaptureService>()` — only service without an interface.
**Fix**: Extract `ICaptureService`.

### M17. `SqsPollingBackgroundService` JsonDocument Leak
**Source**: .NET
**File**: `Background/SqsPollingBackgroundService.cs:105`
`JsonDocument.Parse(message.Body).RootElement` — no `using` statement.
**Fix**: `using var doc = JsonDocument.Parse(message.Body);`

### M18. EnrichmentBackgroundService Logging Bug
**Source**: .NET
**File**: `Background/EnrichmentBackgroundService.cs:112-113`
Logs "was {Status}" but status is already overwritten to Pending. Always logs "was Pending".
**Fix**: Capture original status before overwriting.

### M19. `CommandMenu` Sorts All Notes Every Render Without useMemo
**Source**: React
**File**: `src/zettel-web-ui/src/components/command-menu.tsx:30-33`
`recentNotes` computed with `.slice().sort()` on every render.
**Fix**: Wrap in `useMemo`.

### M20. `NoteList` Shows Empty State Instead of Error State
**Source**: React
**File**: `src/zettel-web-ui/src/components/note-list.tsx`
`error` is destructured from `useNotes()` but never displayed. Failed fetch shows
"No notes yet" instead of error message.
**Fix**: Check `error` and display error UI.

### M21. `NoteView` Creates Full Tiptap Editor for Read-Only Rendering
**Source**: React
**File**: `src/zettel-web-ui/src/components/note-view.tsx:23-30`
Creates entire editor instance just to render static HTML.
**Fix**: Use `generateHTML` from `@tiptap/html` or render HTML directly.

### M22. Keyboard Shortcut Hints Show Mac-Only Symbols
**Source**: React
**Files**: `components/header.tsx:38`, `components/note-editor.tsx:161`
Shows Mac symbol on all platforms. `CaptureButton` already has platform detection.
**Fix**: Apply `navigator.userAgent.includes('Mac')` check consistently.

### M23. Database Credentials Hardcoded in docker-compose.yml
**Source**: Security
**File**: `docker-compose.yml:17,54-56`
Weak password `zettel_dev` hardcoded. Move to `.env` file.

---

## LOW Findings

### L1. No Rate Limiting on Core API Endpoints
Only capture endpoints have rate limiting. CRUD, search, import, graph are unlimited.

### L2. No CancellationToken Passthrough on Service Methods
`CreateAsync`, `UpdateAsync`, `DeleteAsync`, `ListAsync`, `GetByIdAsync` don't accept
or forward CancellationTokens.

### L3. No Nginx Cache Headers for Static Assets
`nginx.conf` has gzip but no `Cache-Control` for Vite's content-hashed assets.
Add `expires 1y; add_header Cache-Control "public, immutable";` for `/assets/`.

### L4. No Frontend Tests
No test files in the UI source tree.

### L5. No Document Title Management Per Page
Browser tab always shows default Vite title. Add `react-helmet-async` or Router meta.

### L6. Search Debounce at 250ms Triggers Embedding API Calls
Fast typist triggers 2-3 embedding API calls/second. Increase to 400-500ms.

### L7. Duplicate UrlRegex Across 3 Classes
Same regex defined in NoteService, CaptureService, EnrichmentBackgroundService.

### L8. `RegexOptions.Compiled` on GeneratedRegex (No-Op)
`CaptureService.cs:220` — Compiled flag is ignored on source-generated regexes.

### L9. No Global Exception Handling Middleware
No `app.UseExceptionHandler()`. Unhandled exceptions produce default responses.

### L10. Predictable Timestamp-Based Note IDs
IDs are `yyyyMMddHHmmssfff` — predictable and enumerable.

### L11. No Query Length Limit on Search
No `[RequestSizeLimit]` or max length check on the `q` parameter.

### L12. SQS Message Body Parsed Without Graceful Error Handling
Malformed JSON messages retry indefinitely until DLQ threshold.

### L13. No Content-Type Validation on Webhook Payloads
Webhooks accept any content type via `[FromBody] JsonElement`.

### L14. `useAutosave` Restarts Interval on Every Tag Change
`tags` array identity changes on every `setTags`, resetting the 5-second interval.

### L15. `editorContent` Reads HTML on Every Render
`note-editor.tsx:71` calls `editor?.getHTML()` on every render, not just for autosave.

### L16. GraphPage Resize Handler Not Debounced
`window.addEventListener('resize', updateSize)` causes re-renders of force graph.

### L17. `nodeColor`/`linkColor` useCallback with Empty Deps Unnecessary
Plain functions outside the component would work the same.

### L18. No Runtime Validation of API Responses
API client uses `response.json() as Promise<T>` — type assertion, not validation.

### L19. Lambda Webhook Relay Has No Input Validation
Forwards any request body to SQS without size limit or JSON validation.

### L20. Background Services Fetch Unbounded Pending IDs
After ReEmbedAll, fetches all pending IDs. Add `.Take(100)` batch limit.

### L21. Embedding Processes Notes One at a Time
Sequential processing. Batching with `GenerateAsync(IEnumerable<string>)` would be faster.

### L22. `react-force-graph-2d` Not Lazy-Loaded
~80KB gzipped loaded on every page even if user never visits /graph.
(Being fixed as part of code splitting work.)

### L23. `RecoverStuckNotesAsync` Loads Full Entities to Flip Status
`EnrichmentBackgroundService.cs:99-120` — use ExecuteUpdateAsync or project only IDs.

### L24. `FindRelatedAsync` Loads Full Tracked Entity
`SearchService.cs:151-157` — loads entire Note just to read Embedding. Add AsNoTracking
and project only Embedding.

### L25. `SearchTagsAsync` Unbounded Results, No Tag Index
`NoteService.cs:203-212` — no `.Take()` limit and no index on NoteTags.Tag.

### L26. Inbox Count Polled Every 30s
Adds baseline DB load. Consider increasing interval or using SSE.

### L27. `handleResponse` Swallows Error Body
`client.ts:15` — `await response.text().catch(() => '')` silently drops error details.

### L28. Settings Page Import Uses Bare Catch
`settings.tsx:49` — actual error discarded, only shows generic "Import failed".

### L29. Export Download `URL.revokeObjectURL` Runs Synchronously
`import-export.ts:15` — may revoke before browser starts download on some browsers.

### L30. SQS Queue URL Exposes AWS Account ID
`docker-compose.yml:22` — reveals account ID 469909854323.

### L31. Full-Text Search Doesn't Filter by NoteStatus
Search results include fleeting/draft notes alongside permanent notes.

### L32. Four Separate COUNT Queries Per Health Check
`Health/DatabaseHealthCheck.cs:21-27` — consolidate into single grouped query.
