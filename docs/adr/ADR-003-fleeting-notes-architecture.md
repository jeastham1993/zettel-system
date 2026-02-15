# ADR-003: Fleeting Notes Capture Architecture

Date: 2026-02-14
Status: Proposed

## Context

Zettel-Web currently treats all notes equally - every note is created through
the full editor, immediately embedded, and searchable. There is no concept of
"quick capture" or note lifecycle.

Users (James) frequently have fleeting thoughts while reading, on mobile, or at
the desk. The friction of opening the full editor means most thoughts are either
lost or scattered across other apps (Apple Notes, self-emails). Both capture
speed and later processing are broken.

We need to support:
1. Quick capture from the web UI (floating button, <10 seconds)
2. Capture via email (send to a dedicated address)
3. Capture via Telegram bot (message the bot)
4. Asynchronous URL enrichment (fetch title, summary, content)
5. Inbox for reviewing and processing fleeting notes into permanent ones

## Decision

Use a **Capture Service with Background Enrichment** pattern:

- **Status field on Note model**: `NoteStatus` enum (Permanent/Fleeting) added
  to the existing Note entity. Default is Permanent so existing behaviour is
  unchanged. Fleeting notes are first-class notes that participate in search
  and embedding.

- **CaptureController + CaptureService**: New controller handles email and
  Telegram webhooks. CaptureService parses channel-specific payloads into a
  common capture request, verifies sender identity, and delegates to
  NoteService for persistence. Controllers stay thin per ADR-001.

- **Background EnrichmentService**: Follows the same pattern as
  EmbeddingBackgroundService (channel-based queue + DB polling). When a
  fleeting note contains URLs, the enrichment service fetches page metadata
  asynchronously and stores it as a JSON string on the Note. Capture stays
  fast; enrichment happens in the background.

- **Enrichment stored as JSON string**: EnrichmentJson field on Note stores
  URL metadata as a serialised JSON string. This matches the pattern used for
  embeddings (float[] stored directly) and avoids InMemory test
  compatibility issues that come with complex types or separate tables.

## Consequences

### Positive

- Follows established patterns (mirrors EmbeddingBackgroundService, thin
  controllers, service layer does the work)
- No new architectural concepts - anyone who understands the embedding
  pipeline understands the enrichment pipeline
- Capture is fast (~milliseconds) because enrichment is async
- Channel-specific parsing is isolated in CaptureService - adding a new
  channel later (WhatsApp, Shortcuts) only touches CaptureService +
  CaptureController
- Fleeting notes get embedded and appear in search/discovery, encouraging
  processing

### Negative

- 4 new files (CaptureController, CaptureService, EnrichmentBackgroundService,
  IEnrichmentQueue) adds to the codebase
- Two background services running (embedding + enrichment), though both are
  lightweight and wake-on-demand
- JSON string for enrichment data is not queryable via SQL (acceptable for
  display-only metadata)

### Neutral

- Note model grows by 4 fields (Status, Source, EnrichmentJson, EnrichStatus)
- Existing API contract is backwards-compatible (Status defaults to Permanent)
- Search queries need no changes (fleeting notes are just notes)

## Alternatives Considered

### Inline Extension (Option A)

Extend existing NotesController and NoteService with webhook handling and
synchronous enrichment. Fewest new files but violates ADR-001 (controllers
doing business logic), makes enrichment synchronous (slow captures), and
results in a 350+ line controller doing webhook parsing, auth verification,
URL fetching, and note CRUD.

Not chosen because: Poor separation of concerns, synchronous enrichment
blocks capture, hard to test webhook parsing without HTTP mocking.

### Event-Driven Pipeline (Option C)

Full event-driven architecture with ICaptureChannel providers, in-process
event bus, and separate handlers for persistence, enrichment, and
notification. Maximum extensibility.

Not chosen because: Over-engineered for 2 channels in a single-user app.
Introduces new architectural concepts (event bus, handlers) not used
elsewhere in the codebase. 10+ new files. The codebase principle is
"three similar lines is better than a premature abstraction."

## Related Decisions

- [ADR-001](ADR-001-backend-architecture.md): Simple Layered Architecture -
  this decision follows the same patterns
- [ADR-002](ADR-002-postgresql-native-search.md): PostgreSQL Native Search -
  fleeting notes participate in the same search infrastructure

## Notes

- Spec: [docs/specs/2026-02-14-fleeting-notes.md](../specs/2026-02-14-fleeting-notes.md)
- Design: [docs/design-fleeting-notes.md](../design-fleeting-notes.md)
- WhatsApp integration deferred to v2 - email + Telegram cover the primary
  use cases with simpler, free APIs
