# Code Review: Fleeting Notes Feature

Generated: 2026-02-14
Agents: 4 (architecture, .NET, React frontend, failure modes)
Raw findings: 69
After deduplication: 38
Files reviewed: ~70 (37 modified + ~30 new)

## Summary

The implementation follows ADR-001 (thin controllers, logic in services) and
ADR-003 (capture service + background enrichment) well. The enrichment
pipeline correctly mirrors the embedding pipeline pattern. Component
boundaries are clean, React hooks are well-composed, TypeScript is strongly
typed. Test coverage is good (293 tests passing).

**However, the external-facing webhook and URL-fetching code has significant
security gaps that must be addressed before deployment.**

**Recommendation**: Block merge until critical issues are resolved.

---

## Critical Issues (4) -- Must Fix

### 1. Unauthenticated webhook endpoints

**Agents**: Architecture, .NET, Failure Modes
**Files**: `CaptureController.cs:21-45`, `CaptureService.cs:100-111`

Both `/api/capture/email` and `/api/capture/telegram` accept arbitrary POST
requests with zero transport-level authentication. Sender allowlisting in
CaptureService is the only defense, but:
- Email `from` field is trivially spoofable (no DKIM/SPF at app level)
- Telegram chat ID is a sequential integer (guessable)
- No `X-Telegram-Bot-Api-Secret-Token` header verification

**Impact**: Unauthorized note creation, embedding API cost, inbox spam.

**Fix**:
- Telegram: Validate `X-Telegram-Bot-Api-Secret-Token` header in controller
  before calling CaptureService (Telegram's recommended approach)
- Email: Validate webhook signature (HMAC) from the chosen provider
  (SendGrid/Mailgun/SES all provide this)
- Defense-in-depth: Add rate limiting on `/api/capture/*` endpoints

### 2. SSRF vulnerability in URL enrichment

**Agents**: Architecture, Failure Modes
**Files**: `EnrichmentBackgroundService.cs:228-247`

`FetchUrlMetadataAsync` makes HTTP GET to any URL extracted from note content
with no validation. Malicious URLs like `http://169.254.169.254/latest/meta-data/`
or `http://localhost:5432/` cause the server to access internal services.

**Impact**: Cloud credential leak, internal service information disclosure.

**Fix**:
- Validate URL scheme (HTTPS only, or explicit HTTP opt-in)
- Resolve hostname and reject private IP ranges (10.x, 172.16-31.x,
  192.168.x, 127.x, 169.254.x, ::1)
- Consider custom `SocketsHttpHandler.ConnectCallback` for IP validation

### 3. Regex ReDoS risk on untrusted HTML

**Agents**: .NET, Failure Modes
**Files**: `EnrichmentBackgroundService.cs:28-54`

`ScriptStyleRegex` uses `.*?` with `Singleline` and backreference `\1` on
arbitrary HTML fetched from the internet. Combined with unbounded response
size, this can cause catastrophic backtracking on crafted input.

**Impact**: CPU exhaustion in background service, enrichment pipeline stalls.

**Fix**:
- Truncate HTML to ~100KB before regex processing (title/description are
  in the first few KB)
- Add `RegexOptions.NonBacktracking` (.NET 7+) to all HTML-parsing regexes
- Convert to `[GeneratedRegex]` for consistency with CaptureService

### 4. Unbounded HTTP response body causes OOM

**Agents**: .NET, Architecture, Failure Modes
**Files**: `EnrichmentBackgroundService.cs:237`

`ReadAsStringAsync` loads the entire response into memory. A multi-GB
response kills the process. Since the background service runs in the same
process as the web API, this crashes everything.

**Impact**: Application-wide crash (OutOfMemoryException).

**Fix**:
```csharp
var stream = await response.Content.ReadAsStreamAsync(cts.Token);
using var reader = new StreamReader(stream);
var buffer = new char[512_000]; // 500KB max
var charsRead = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
var html = new string(buffer, 0, charsRead);
```

---

## Important Issues (14) -- Should Fix

### 5. No Processing guard state in enrichment pipeline

**Agents**: Architecture, Failure Modes | **Files**: `EnrichmentBackgroundService.cs:145-174`

Unlike `EmbeddingBackgroundService` which uses `Processing` status as a
mutex, the enrichment service has no equivalent. The DB poller and channel
consumer can process the same note concurrently, causing duplicate URL
fetches and potential EF Core concurrency exceptions.

**Fix**: Add `EnrichStatus.Processing` value. Set it before starting work.
Exclude from DB poller query.

### 6. RecoverPendingNotesAsync is a no-op

**Agents**: .NET, Architecture, Failure Modes | **Files**: `EnrichmentBackgroundService.cs:116-131`

Logs warnings about stuck notes but does not re-enqueue them or change
status. Compare with `EmbeddingBackgroundService.RecoverProcessingNotesAsync`
which actually resets stuck notes.

**Fix**: Either enqueue pending note IDs onto the channel, or rename the
method to `LogPendingNotesAsync` to match its actual behavior.

### 7. EnrichStatus never set to Pending at creation time

**Agents**: Architecture | **Files**: `CaptureService.cs:37-41`

`CaptureService.CaptureAsync` enqueues for enrichment if URLs are detected
but never sets `EnrichStatus = Pending` on the note. If the queue message is
lost (app restart between create and enqueue), the note will never be
enriched. This breaks the outbox pattern that makes the embedding pipeline
reliable.

**Fix**: Set `note.EnrichStatus = EnrichStatus.Pending` before/after
enqueuing. Requires adding the parameter to `CreateFleetingAsync` or a
follow-up DB update.

### 8. BackgroundService crash is non-recoverable

**Agents**: Failure Modes | **Files**: `EnrichmentBackgroundService.cs:81-87`

`ProcessChannelAsync` has no per-item try/catch. An unexpected exception
kills the channel consumer permanently. ASP.NET Core does not restart
hosted services.

**Fix**: Wrap `ProcessNoteAsync` call with try/catch in the channel
consumer loop (same pattern needed in `EmbeddingBackgroundService`).

### 9. Duplicate note creation logic

**Agents**: .NET, Architecture | **Files**: `NoteService.cs:19-89`

`CreateAsync` and `CreateFleetingAsync` share ~30 lines of identical logic
(ID generation, tag wiring, save, embedding enqueue). Changes to the
creation flow must be made in two places.

**Fix**: Extract `private CreateCoreAsync(title, content, status, source, tags)`.

### 10. No rate limiting on capture endpoints

**Agents**: Failure Modes | **Files**: `CaptureController.cs`

Unauthenticated endpoints with no rate limiting = DoS vector. Thread pool
exhaustion affects all endpoints.

**Fix**: Add `builder.Services.AddRateLimiter()` with fixed-window policy
on `/api/capture/*` (e.g., 10 req/min per IP), or rate limit at Traefik.

### 11. ID collision on sub-second fleeting note creation

**Agents**: Failure Modes | **Files**: `NoteService.cs:58`

Two fleeting notes created in the same second (e.g., Telegram + email
arriving simultaneously) cause a primary key violation. The second note
is lost with no retry.

**Fix**: Use millisecond precision (`yyyyMMddHHmmssfff`) for fleeting note
IDs, or catch `DbUpdateException` and retry with modified ID.

### 12. GraphService loads ALL notes for O(n^2) comparison

**Agents**: .NET | **Files**: `GraphService.cs:16-78`

`await _db.Notes.ToListAsync()` loads every note including embeddings.
At 1000 notes with 1536-dim embeddings: ~6MB data + ~500K comparisons.
Also missing `AsNoTracking()`.

**Fix**: Add `AsNoTracking()`, project only needed fields, add a note
count threshold for server-side edge computation.

### 13. ListFleetingAsync is redundant

**Agents**: Architecture | **Files**: `INoteService.cs:11`, `NoteService.cs:114-124`

`ListFleetingAsync(skip, take)` is identical to
`ListAsync(skip, take, NoteStatus.Fleeting)`. Unnecessary interface surface.

**Fix**: Remove `ListFleetingAsync`. Use `ListAsync` with status filter.

### 14. `take` parameter unbounded on list endpoints

**Agents**: .NET, Failure Modes | **Files**: `NotesController.cs:67-70`

`?take=1000000` loads entire database into memory.

**Fix**: Add `[Range(1, 200)]` or clamp: `take = Math.Min(take, 200)`.

### 15. `useDeleteNote` doesn't invalidate inbox query

**Agents**: React | **Files**: `use-notes.ts:42-50`

Deleting from inbox leaves stale data because `['inbox']` and
`['inbox', 'count']` query keys aren't invalidated.

**Fix**: Add inbox query invalidation to `useDeleteNote`, or create
`useDeleteInboxNote`.

### 16. `isDirty` compares raw HTML on every render

**Agents**: React | **Files**: `note-editor.tsx:71-78`

`editor?.getHTML()` runs every render cycle. For long notes this lags
on each keystroke. Also, format mismatch between initial content (plain
text from draft) and editor content (HTML) can cause permanent false-dirty.

**Fix**: Track dirty state via `editor.on('update', ...)` with a boolean
flag.

### 17. `graphData` not memoized -- restarts force simulation

**Agents**: React | **Files**: `graph-view.tsx:32-39`

Object rebuilt via `.map()` every render. ForceGraph2D uses reference
comparison, causing simulation restart on parent re-render.

**Fix**: Wrap with `useMemo(() => ({ nodes, links }), [data])`.

### 18. `navigator.platform` is deprecated

**Agents**: React | **Files**: `capture-button.tsx:120`

Will eventually be removed from browsers.

**Fix**: Use `navigator.userAgent.includes('Mac')` or extract shared
`isMac` utility.

---

## Suggestions (20) -- Nice to Have

### Code Quality
19. `EnrichmentResult`/`UrlEnrichment` should be records (immutability)
20. Move HTML parsing helpers to separate `HtmlMetadataExtractor` class
21. URL regex duplicated between CaptureService and EnrichmentService
22. `EnrichmentBackgroundService` methods are unnecessarily `public`
23. Convert enrichment regexes to `[GeneratedRegex]` for consistency
24. `NotionParseResult.Tags` should be `IReadOnlyList<string>`
25. `CaptureConfig.TelegramBotToken` exists but is never read
26. EnrichmentBackgroundService test `CreateContext` leaks scope
27. `CaptureService` registered as concrete type (no interface)

### Data Integrity
28. PromoteAsync should guard against promoting already-permanent notes
29. No re-enrichment when note content is edited (stale enrichment data)
30. GraphService.ToDictionary throws on duplicate titles (use TryAdd)
31. ExportService path traversal stripping is incomplete (single pass)
32. HybridSearchAsync error handling may misattribute failures

### Frontend
33. Tooltip shortcut text hardcodes "Ctrl" (wrong on macOS)
34. Magic string `'auto'` for fleeting note title
35. Missing `aria-label` on inbox icon link
36. Discovery dismiss button invisible to keyboard users (needs focus-visible)
37. `truncateContent` doesn't strip HTML tags
38. `recentNotes.sort()` mutates React Query cache (use `.slice().sort()`)

---

## Architecture Assessment

| Principle        | Score | Notes                                         |
|------------------|-------|-----------------------------------------------|
| Evolvability     | 4/5   | Good patterns, easy to add channels           |
| Encapsulation    | 4/5   | Good JsonIgnore usage, minor model co-location |
| Coupling         | 4/5   | Clean dependency direction, concrete CaptureService |
| Understanding    | 3/5   | 7 regex fields in enrichment hurt readability  |
| Failure Modes    | 2/5   | SSRF, OOM, no rate limiting, weak auth         |

## Positive Patterns Observed

- `AsNoTracking()` on all read-only queries
- `[JsonIgnore]` on internal fields (embedding, enrichment)
- `RequestSizeLimit` on mutating endpoints
- Consolidated test fakes in `Tests/Fakes/`
- Webhooks return 200 even for invalid payloads (correct for Telegram/email)
- Channel<T> queue mirrors proven embedding pattern
- CaptureService parsing is easily unit-testable
- Good separation: thin controllers, logic in services (ADR-001 compliant)

---

## Next Steps

Run `/triage` to process these findings one by one.
Critical items should be resolved before any deployment.
