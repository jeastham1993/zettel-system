# Design: Fleeting Notes Capture

Generated: 2026-02-14
Status: Draft
Spec: [docs/specs/2026-02-14-fleeting-notes.md](specs/2026-02-14-fleeting-notes.md)

## Problem Statement

### Goal

Enable capturing fleeting thoughts (typically a link + comment) in under 10
seconds from any context: web UI, email, or Telegram. Process them later into
permanent Zettelkasten notes. Make Zettel-Web the single system for all
knowledge capture.

### Constraints

- Single-user personal app (no multi-tenancy)
- Must not break existing note CRUD, search, or embedding pipeline
- Must work with current PostgreSQL + pgvector + EF Core InMemory test setup
- Float[] embedding column must remain (InMemory compatibility)
- Docker/Traefik deployment topology unchanged
- Timestamp-based ID generation (one note per second max)

### Success Criteria

- [ ] Fleeting notes captured from web UI, email, and Telegram
- [ ] Capture-to-done time under 10 seconds (web UI)
- [ ] Existing tests continue to pass (default status = Permanent)
- [ ] Search works across all notes (fleeting + permanent)
- [ ] Fleeting notes get embedded for semantic search/discovery

## Context

### Current State

Notes are a single entity type with no lifecycle concept. Every note goes
through the same flow: create → embed → search. There's no concept of "quick
capture" or "processing" - every note is created through the full editor.

```
[Web UI Editor] → POST /api/notes → Note (embedded, searchable)
```

### Related Decisions

- [ADR-001](adr/ADR-001-backend-architecture.md): Simple Layered Architecture
  (Controllers → Services → EF Core). No unnecessary abstractions.
- [ADR-002](adr/ADR-002-postgresql-native-search.md): PostgreSQL-native search
  with fulltext + pgvector. Raw SQL with ::vector casts.

---

## Alternatives Considered

### Option A: Inline Extension

**Summary**: Add status field to Note, extend existing NoteService and
NotesController with status-aware methods. Webhook handling and enrichment
done inline in controllers.

**Architecture**:

```
                     ┌──────────────────────┐
                     │   NotesController    │
[Web UI]   ──POST──▶│   (existing + new)   │──▶ NoteService
[Email WH] ──POST──▶│                      │    (extended)
[TG WH]   ──POST──▶│                      │
                     └──────────────────────┘
                              │
                     (inline enrichment via HttpClient)
```

**Changes**:
- `Note.cs`: Add `NoteStatus` enum + `Status` property
- `NoteService.cs`: Add `CreateFleetingAsync()`, `ListFleetingAsync()`,
  `PromoteAsync()`. Inline URL fetching in create.
- `NotesController.cs`: Add 4-5 new endpoints including webhook handling
  for email and Telegram, plus enrichment logic.

**Pros**:
- Fewest new files (0 new services, 0 new controllers)
- Fastest to implement (extends existing code)
- Easy to understand - everything in familiar places

**Cons**:
- NotesController grows from 138 lines to ~350+ (webhook parsing, auth
  verification, Telegram API response formatting all inline)
- Enrichment blocks the HTTP response (URL fetching is slow)
- Violates ADR-001: controllers doing business logic (parsing email
  payloads, verifying Telegram signatures, fetching URLs)
- NoteService gains responsibilities unrelated to note CRUD
- Testing webhook parsing requires mocking HTTP context

**Coupling Analysis**:

| Component       | Ca  | Ce  | I    | Notes                       |
|-----------------|-----|-----|------|-----------------------------|
| NotesController | 3   | 4   | 0.57 | 3 channels + enrichment in  |
| NoteService     | 1   | 3   | 0.75 | +HttpClient, +parsing deps  |
| Note model      | 5   | 0   | 0.00 | Stable - good               |

New dependencies in controller: Telegram.Bot SDK, email parsing, HttpClient.
Coupling impact: **High** - controller becomes a grab-bag.

**Failure Modes**:

| Mode                    | Sev | Occ | Det | RPN | Notes                    |
|-------------------------|-----|-----|-----|-----|--------------------------|
| URL fetch timeout       | 3   | 4   | 2   | 24  | Blocks response, 504s    |
| Telegram sig invalid    | 2   | 2   | 3   | 12  | Silent rejection         |
| Email parse failure     | 3   | 3   | 2   | 18  | Note created with junk   |
| Controller 500          | 4   | 2   | 2   | 16  | Takes down all endpoints |

**Evolvability Assessment**:
- Add WhatsApp channel: **Hard** - more webhook code in controller
- Change enrichment logic: **Hard** - tangled with create flow
- Add new enrichment types: **Hard** - inline means blocking

**Effort Estimate**: S (Small) - 2-3 days

---

### Option B: Capture Service with Background Enrichment

**Summary**: Add status field to Note. Separate `CaptureController` for
webhooks. `CaptureService` normalises channel-specific inputs into a common
capture request. Background `EnrichmentService` fetches URL metadata
asynchronously (same pattern as EmbeddingBackgroundService).

**Architecture**:

```
                           ┌──────────────────┐
[Web UI]    ──POST──▶      │ NotesController  │──▶ NoteService
                           │ (existing + list │    (+ status filter,
                           │  fleeting/promote)│     promote)
                           └──────────────────┘

                           ┌──────────────────┐
[Email WH]  ──POST──▶     │ CaptureController│──▶ CaptureService
[TG WH]    ──POST──▶      │ (new)            │    (parse, verify,
                           └──────────────────┘     normalise)
                                    │                    │
                                    │              NoteService
                                    │              .CreateAsync()
                                    │                    │
                                    ▼                    ▼
                           ┌──────────────────┐  ┌─────────────┐
                           │ EnrichmentQueue  │  │ Embed Queue │
                           │ (Channel<string>)│  │ (existing)  │
                           └────────┬─────────┘  └─────────────┘
                                    │
                           ┌────────▼─────────┐
                           │ EnrichmentBgSvc  │──▶ fetch URL metadata
                           │ (BackgroundSvc)  │    store on Note
                           └──────────────────┘
```

**Changes**:
- `Note.cs`: Add `NoteStatus` enum, `Status` property, `SourceUrl` property,
  `EnrichmentData` (JSON string for title/summary/content)
- `NoteService.cs`: Add status filter to `ListAsync()`, add
  `PromoteAsync(id)`, update `CreateAsync` with optional status
- `INoteService.cs`: Add new method signatures
- `CaptureController.cs` (new): Two endpoints - email webhook, Telegram
  webhook. Thin - delegates to CaptureService.
- `CaptureService.cs` (new): Parse email payload → capture request. Parse
  Telegram message → capture request. Verify sender. Extract URLs. Call
  NoteService.CreateAsync. Enqueue for enrichment.
- `EnrichmentService.cs` (new): Background service. Dequeues note IDs, fetches
  URL metadata (title, description, content extract), stores on Note.
- `IEnrichmentQueue.cs` (new): Channel<string> wrapper (same as
  IEmbeddingQueue pattern).

**Pros**:
- Clean separation: controllers thin, services do work (ADR-001 compliant)
- Enrichment is async - capture is fast, enrichment happens in background
- Follows existing patterns (EnrichmentService mirrors EmbeddingBackgroundService)
- CaptureService is testable without HTTP context
- Adding a new channel = new endpoint in CaptureController + parse method
  in CaptureService (nothing else changes)
- NoteService changes are minimal (status parameter + filter)

**Cons**:
- More files (4 new) than Option A
- Two background services running (enrichment + embedding)
- Enrichment data stored as JSON string (not strongly typed in DB)

**Coupling Analysis**:

| Component         | Ca  | Ce  | I    | Notes                        |
|-------------------|-----|-----|------|------------------------------|
| CaptureController | 2   | 1   | 0.33 | 2 channels → CaptureService  |
| CaptureService    | 1   | 2   | 0.67 | → NoteService, EnrichmentQ   |
| NoteService       | 2   | 2   | 0.50 | Stable, minimal additions    |
| EnrichmentBgSvc   | 0   | 2   | 1.00 | → DbContext, HttpClient      |
| Note model        | 6   | 0   | 0.00 | Stable anchor                |

New dependencies isolated: Telegram SDK in CaptureService only. HttpClient
in EnrichmentService only. Controller stays clean.
Coupling impact: **Low** - new code is isolated from existing code.

**Failure Modes**:

| Mode                     | Sev | Occ | Det | RPN | Notes                       |
|--------------------------|-----|-----|-----|-----|-----------------------------|
| URL fetch timeout         | 2   | 4   | 2   | 16  | Async - note saved anyway   |
| Telegram sig invalid      | 2   | 2   | 3   | 12  | Rejected at CaptureService  |
| Email parse failure       | 3   | 3   | 2   | 18  | CaptureService logs, rejects|
| Enrichment svc crash      | 2   | 1   | 3   | 6   | Notes exist, enrichment retries |
| CaptureController 500     | 2   | 2   | 2   | 8   | Isolated from NotesController |

**Evolvability Assessment**:
- Add WhatsApp channel: **Easy** - new endpoint + parse method
- Change enrichment logic: **Easy** - isolated in EnrichmentService
- Add new enrichment types (e.g., LLM summary): **Easy** - extend
  EnrichmentService pipeline
- Add Apple Shortcuts: **Easy** - already works via POST /api/notes
  (just send status=fleeting)

**Effort Estimate**: M (Medium) - 4-5 days

---

### Option C: Event-Driven Capture Pipeline

**Summary**: Full event-driven architecture with `ICaptureChannel` provider
pattern, capture events published to an in-process bus, handlers for
persistence, enrichment, and notification. Maximum extensibility.

**Architecture**:

```
[Web UI]    ──▶ ICaptureChannel(WebCapture)    ──┐
[Email WH]  ──▶ ICaptureChannel(EmailCapture)   ├──▶ CaptureEvent
[TG WH]    ──▶ ICaptureChannel(TelegramCapture) ──┘       │
                                                           ▼
                                                    EventBus
                                                    ┌──┼──┐
                                                    ▼  ▼  ▼
                                               Persist Enrich Notify
                                               Handler Handler Handler
```

**Changes**:
- Everything in Option B, plus:
- `ICaptureChannel.cs` (new): Interface for channel adapters
- `WebCaptureChannel.cs`, `EmailCaptureChannel.cs`,
  `TelegramCaptureChannel.cs` (new): One per channel
- `CaptureEvent.cs` (new): Event model with source, content, metadata
- `ICaptureEventBus.cs` (new): In-process publish/subscribe
- `PersistenceHandler.cs`, `EnrichmentHandler.cs` (new): Event handlers

**Pros**:
- Maximum extensibility - add a channel without touching any existing code
- Clean event model - handlers are independently testable
- Could add notification handler (e.g., push count to UI) trivially
- Textbook separation of concerns

**Cons**:
- 10+ new files for a feature that has 2 channels (email + Telegram)
- In-process event bus is over-engineering for a single-user app
- Provider pattern for 2 implementations is premature abstraction
- Debugging event flow is harder than direct calls
- Adds concepts (events, handlers, bus) that don't exist in the codebase
- Violates project principle: "three similar lines of code is better than
  a premature abstraction"

**Coupling Analysis**:

| Component           | Ca  | Ce  | I    | Notes                     |
|---------------------|-----|-----|------|---------------------------|
| Each CaptureChannel | 0   | 1   | 1.00 | Very unstable (leaf)      |
| CaptureEventBus     | 3   | 0   | 0.00 | Very stable (depended on) |
| Each Handler        | 0   | 1-2 | 1.00 | Leaf nodes                |

Coupling is theoretically better but introduces a new architectural concept
(event bus) that nothing else in the codebase uses. The conceptual coupling
("you need to understand events to work here") is high.
Coupling impact: **Medium** - low structural coupling, high conceptual load.

**Failure Modes**:

| Mode                 | Sev | Occ | Det | RPN | Notes                        |
|----------------------|-----|-----|-----|-----|------------------------------|
| Event lost in bus    | 4   | 1   | 4   | 16  | In-memory, no persistence    |
| Handler exception    | 3   | 2   | 3   | 18  | Must handle per-handler      |
| Event ordering       | 2   | 2   | 4   | 16  | Persist before enrich?       |
| Debugging difficulty | 2   | 3   | 4   | 24  | Flow harder to trace         |

**Evolvability Assessment**:
- Add WhatsApp channel: **Trivial** - new ICaptureChannel implementation
- Change enrichment logic: **Easy** - swap handler
- Add notification system: **Trivial** - new handler
- Understand the codebase: **Hard** - new architectural pattern to learn

**Effort Estimate**: L (Large) - 7-10 days

---

## Comparison Matrix

| Criterion          | Option A: Inline  | Option B: Service  | Option C: Events   |
|--------------------|--------------------|--------------------|---------------------|
| Complexity         | Low                | Medium             | High                |
| ADR-001 Compliance | Violates           | Follows            | Over-applies        |
| Evolvability       | Low                | Medium             | High                |
| Time to Implement  | 2-3 days           | 4-5 days           | 7-10 days           |
| Coupling Impact    | High               | Low                | Medium              |
| Failure Resilience | Low (sync enrich)  | High (async)       | Medium (event loss) |
| Test Complexity    | High (HTTP mocks)  | Low (unit-testable)| Medium (event setup)|
| New Files          | 0                  | 4                  | 10+                 |
| Conceptual Load    | Low                | Low                | High                |

---

## Recommendation

**Recommended Option: B - Capture Service with Background Enrichment**

### Rationale

Option B is the sweet spot for this codebase:

1. **Follows established patterns**: EnrichmentService mirrors
   EmbeddingBackgroundService. CaptureController follows the same thin
   controller pattern as NotesController. No new architectural concepts.

2. **Right level of separation**: Channel-specific parsing (email format,
   Telegram signatures) stays in CaptureService, not in controllers or
   NoteService. Enrichment runs async so capture stays fast.

3. **Not over-engineered**: 4 new files for 3 new capabilities (email
   capture, Telegram capture, URL enrichment). No provider patterns, no
   event buses, no abstractions for hypothetical future channels.

4. **Testable**: CaptureService can be unit-tested with fake NoteService
   and enrichment queue. EnrichmentService can be tested with fake
   HttpClient. No HTTP context mocking needed.

5. **Async enrichment is critical**: The spec requires "full enrichment"
   (title + summary + content extraction). This takes 1-3 seconds per URL.
   Doing this synchronously (Option A) means capture takes 3+ seconds
   instead of <1 second. Background processing is the right answer.

### Tradeoffs Accepted

- **More files than Option A**: 4 new files is worth it for clean
  separation. The alternative is a 350-line controller doing everything.
- **Not as extensible as Option C**: If we need 5+ channels, Option C
  would be better. But for 2 channels + web UI, a provider pattern is
  premature. We can refactor if that day comes.
- **Two background services**: Both are lightweight (channel-based, wake
  on demand). The resource cost is negligible.

### Risks to Monitor

- **Email provider choice**: Need to pick SendGrid vs Mailgun vs SES.
  CaptureService abstracts this, so switching later is just changing
  the parse method.
- **Enrichment reliability**: URL fetching can be flaky. The background
  service pattern handles this (retry on failure), but we should add
  a timeout and max-retry count.
- **Inbox graveyard**: The biggest risk is behavioural, not technical.
  Age indicators help, but the real test is whether the processing
  workflow is frictionless enough.

---

## Detailed Design (Option B)

### Data Model Changes

```csharp
// Note.cs additions
public enum NoteStatus
{
    Permanent = 0,  // Default - all existing notes
    Fleeting = 1
}

public class Note
{
    // ... existing fields ...

    public NoteStatus Status { get; set; } = NoteStatus.Permanent;

    // Source tracking for fleeting notes
    public string? Source { get; set; }  // "web", "email", "telegram"

    // Enrichment data (JSON) - populated async by EnrichmentService
    [JsonIgnore]
    public string? EnrichmentJson { get; set; }
}
```

Enrichment JSON structure (stored as string for InMemory compat):

```json
{
  "urls": [
    {
      "url": "https://example.com/article",
      "title": "Page Title",
      "description": "Meta description text",
      "contentExcerpt": "First ~500 chars of main content",
      "fetchedAt": "2026-02-14T10:30:00Z"
    }
  ]
}
```

### API Design

```
Existing (modified):
  GET  /api/notes?status=fleeting|permanent  # optional filter
  POST /api/notes                            # add optional status field

New (NotesController):
  GET  /api/notes/inbox                      # alias: fleeting notes
  POST /api/notes/{id}/promote               # fleeting → permanent

New (CaptureController):
  POST /api/capture/email                    # inbound email webhook
  POST /api/capture/telegram                 # Telegram bot webhook
```

### CaptureService

Responsibilities:
1. Parse channel-specific payloads into a common `CaptureRequest`
2. Verify sender identity (email address / Telegram chat ID)
3. Extract URLs from content
4. Call `NoteService.CreateAsync()` with `status=Fleeting`
5. Enqueue note for enrichment if URLs detected

```csharp
public class CaptureService
{
    private readonly INoteService _noteService;
    private readonly IEnrichmentQueue _enrichmentQueue;
    private readonly CaptureConfig _config;

    public record CaptureRequest(
        string Content,
        string Source,
        IEnumerable<string>? Tags = null);

    // Called by CaptureController after parsing channel payload
    public async Task<Note> CaptureAsync(CaptureRequest request) { ... }

    // Channel-specific parsing
    public CaptureRequest? ParseEmailPayload(EmailPayload payload) { ... }
    public CaptureRequest? ParseTelegramMessage(TelegramUpdate update) { ... }

    // Sender verification
    public bool IsAllowedEmailSender(string fromAddress) { ... }
    public bool IsAllowedTelegramChat(long chatId) { ... }
}
```

### EnrichmentService (Background)

Follows the same pattern as EmbeddingBackgroundService:

```csharp
public class EnrichmentBackgroundService : BackgroundService
{
    // Dual-trigger: channel queue + DB poll
    // Fetches URL metadata via HttpClient
    // Stores JSON on Note.EnrichmentJson
    // Statuses: None → Pending → Completed / Failed
}
```

Add enrichment status tracking to Note:

```csharp
public enum EnrichStatus { None, Pending, Completed, Failed }

// On Note model:
public EnrichStatus EnrichStatus { get; set; } = EnrichStatus.None;
```

### Configuration

```json
// appsettings.json additions
{
  "Capture": {
    "AllowedEmailSenders": ["james@example.com"],
    "AllowedTelegramChatIds": [123456789],
    "TelegramBotToken": "",
    "EnrichmentTimeoutSeconds": 10,
    "EnrichmentMaxRetries": 3
  }
}
```

### Search Integration

Search should include fleeting notes by default (they're knowledge too).
The raw SQL queries in SearchService don't need changes since fleeting notes
are just regular notes with a different status value.

Discovery (`DiscoverAsync`) should also include fleeting notes - surfacing
a related fleeting note during browsing is a natural prompt to process it.

### Frontend Changes

1. **Floating Capture Button**: Fixed position button (bottom-right) on all
   pages. Opens a minimal form: text area + optional tags + submit.
2. **Inbox Page**: New route `/inbox`. Lists fleeting notes with age
   indicators. Actions: promote, edit, discard.
3. **Navigation Badge**: Fleeting note count in nav/sidebar.
4. **API Client**: New functions: `listInbox()`, `promote(id)`, `capture()`.

### Implementation Order

| Phase | What                                    | Depends On |
|-------|-----------------------------------------|------------|
| 1     | Data model: NoteStatus + Status field   | Nothing    |
| 2     | API: Status filter on list, promote     | Phase 1    |
| 3     | UI: Floating capture + inbox page       | Phase 2    |
| 4     | Enrichment: background service + queue  | Phase 1    |
| 5     | Email: CaptureController + parsing      | Phase 2, 4 |
| 6     | Telegram: Bot + webhook                 | Phase 2, 4 |
| 7     | Polish: Age indicators, badges          | Phase 3    |

---

## Open Questions

- [ ] Which inbound email provider? (SendGrid vs Mailgun vs AWS SES) -
      affects email webhook payload format in CaptureService
- [ ] Should enrichment use an LLM to summarise? Or just meta tags +
      first paragraphs? (LLM adds cost + latency + dependency)
- [ ] Embed fleeting notes immediately? (Recommended: yes - they should
      appear in semantic search and discovery)
- [ ] Telegram bot commands beyond `/capture`? (Keep simple for v1)
- [ ] EnrichmentJson as JSON string vs separate table? (JSON string is
      simpler, matches InMemory compat pattern used for embeddings)
