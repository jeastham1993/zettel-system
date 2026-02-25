# ADR-010: Large Note Detection and LLM Summarization Architecture

Date: 2026-02-25
Status: Proposed

---

## Context

Notes longer than `Embedding:MaxInputCharacters` (default 4,000 chars) are silently
truncated before embedding, degrading semantic search quality. There is no way for
users to discover which notes are affected, and no repair path.

The request is to surface these notes in the KB health dashboard and provide a
one-click LLM summarization to bring them within the embedding limit.

Two structural questions needed resolving:

1. **Where does "large note detection" live?** It is a health-quality metric, not a
   note CRUD operation — belongs in `IKbHealthService` alongside missing-embeddings
   and orphan detection.

2. **Where does "summarize note" live?** `InsertWikilinkAsync` established that
   health-repair mutations (which modify note content) belong in `KbHealthService`,
   not in `NoteService`. Summarization follows the same precedent.

3. **Sync or async?** Summarization of a single note using an already-warm LLM
   connection takes seconds. A background queue (outbox pattern) would add a
   migration, a new background service, and polling/push notification complexity —
   with no measured latency problem to solve.

## Decision

Implement large note detection and summarization as synchronous operations in
`IKbHealthService` / `KbHealthService`:

1. **`GetLargeNotesAsync()`** — queries notes where `LENGTH(Content) > threshold`,
   where threshold comes from `Embedding:MaxInputCharacters` config.

2. **`SummarizeNoteAsync(noteId)`** — calls `IChatClient` with a condensation prompt,
   saves a `NoteVersion` of the original, replaces `note.Content`, sets
   `note.EmbedStatus = Stale`.

`IChatClient` is injected into `KbHealthService` (already registered as singleton).

New endpoints:
- `GET /api/kb-health/large-notes`
- `POST /api/kb-health/large-notes/{id}/summarize`

The frontend follows the existing KB health section pattern (section + badge + action
button with loading state + toast feedback).

## Consequences

### Positive
- Zero new infrastructure (no migration, no new background service)
- Consistent with ADR-009 and the existing health mutation pattern
- Version history preserves the original — action is reversible
- `EmbedStatus = Stale` automatically triggers re-embedding on the next background cycle
- `StillLarge` flag in the response surfaces cases where the LLM output still exceeds
  the threshold, so users are not silently left with a poorly-embedded note

### Negative
- HTTP request blocks while the LLM runs (~5–30s). A loading spinner is the UX.
- `KbHealthService` now depends on `IChatClient` in addition to `ZettelDbContext`.
  This is one new coupling edge.
- `InsertWikilinkAsync` (existing) does not write version history; this inconsistency
  is acceptable as a known gap to address separately.

### Neutral
- The LENGTH() query is O(n) on content sizes but is already comparable to the full
  content load done by `GetOverviewAsync`. Acceptable at current scale.

## Alternatives Considered

### Background queue (outbox pattern)
Rejected. Would require a new DB column, migration, background service, and
push/polling mechanism — significant complexity with no measured benefit at current
scale. Can be adopted later if bulk summarization becomes a use case.

### Summarize in `INoteService`
Rejected. Health repair mutations belong in `KbHealthService` per ADR-009.
Splitting the large-notes list (health) from the summarize action (notes) would
create an inconsistent mental model.

## Related Decisions
- ADR-009: KB health uses a dedicated service; health mutations live there
- ADR-001: Controllers → Services → EF Core
- ADR-008: Content generation uses `IChatClient` synchronously (same pattern)

## Notes

Migration path if synchronous becomes a bottleneck:
1. Add `POST /api/kb-health/large-notes/{id}/summarize` → returns `202 Accepted`
2. Add `SummarizeStatus` to `Notes` table (migration)
3. Move LLM call to a new `SummarizationBackgroundService`
4. Poll `/api/kb-health/large-notes` to observe status change
