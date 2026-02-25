# Design: Large Note Detection and Summarization

Generated: 2026-02-25
Status: Draft

---

## Problem Statement

### Goal

Notes longer than `Embedding:MaxInputCharacters` (default 4,000 chars) are silently
truncated before embedding, degrading semantic search quality. The KB health dashboard
has no way to surface these notes, and there is no repair path to bring them within
the embedding limit.

### Constraints

- Must fit the existing KB health pattern (new section in KB health page + new
  endpoint on `KbHealthController`)
- Summarization must use the existing `IChatClient` (no new infrastructure)
- The original content must be preserved in version history before replacing
- After summarization, embedding must be queued for refresh
- "Large" should be configurable, defaulting to `Embedding:MaxInputCharacters`
- Summarization must not silently fail — errors should be surfaced to the user

### Success Criteria

- [ ] KB health page shows a "Large Notes" section listing all notes above the threshold
- [ ] Each row shows note title, current character count, and a "Summarize" button
- [ ] Clicking "Summarize" replaces note content with an LLM-generated condensed version
- [ ] Original content is preserved in `NoteVersions` before replacement
- [ ] Note `EmbedStatus` is set to `Stale` after summarization
- [ ] Summarized note length is confirmed to be below `MaxInputCharacters`
- [ ] Error toast shown if summarization fails

---

## Context

### Current State

- `EmbeddingBackgroundService.ProcessNoteAsync` truncates content at
  `Embedding:MaxInputCharacters` (default 4,000) before calling the embedding model
- The truncation is silent — the note author has no visibility that their note was cut
- There is no way to discover which notes are affected except by inspecting content lengths
- `KbHealthService` already does content mutations (`InsertWikilinkAsync`) and has
  a consistent query → mutate → mark-stale pattern
- `IChatClient` is registered as a singleton, already used by `ContentGenerationService`
- Version history (`NoteVersions`) exists but is not written to by health service mutations today

### Related Decisions

- ADR-009: KB health uses a dedicated `IKbHealthService`; mutations belong here, not in `GraphService`
- ADR-001: Controllers → Services → EF Core; no LLM calls in controllers
- INFRA-001 (compound doc): Embedding truncation at 4,000 chars explained and justified
- INFRA-003 (compound doc): Timeout fix; embedded at 300s — long enough for slow Ollama

---

## Alternatives Considered

### Option A: Synchronous summarization in `KbHealthService` (Recommended)

**Summary**: Add `GetLargeNotesAsync` + `SummarizeNoteAsync` to `IKbHealthService`.
The summarize action calls the LLM, replaces content, saves version history, and marks
embedding Stale — all in a single HTTP request that returns when done.

**Architecture**:

```
GET /api/kb-health/large-notes
  → KbHealthController.GetLargeNotes()
  → KbHealthService.GetLargeNotesAsync()
  → EF Core: WHERE LENGTH("Content") > threshold
  ← LargeNote[]

POST /api/kb-health/large-notes/{id}/summarize
  → KbHealthController.SummarizeNote(id)
  → KbHealthService.SummarizeNoteAsync(id)
  → IChatClient.GetResponseAsync(summarizePrompt)
  → Write NoteVersion (original preserved)
  → note.Content = summary
  → note.EmbedStatus = Stale
  ← Note (updated)
```

**Pros**:
- Follows exact existing KB health pattern (same as `InsertWikilinkAsync`)
- No new background jobs, no migrations beyond what's needed
- IChatClient is already registered — zero new infrastructure
- Frontend pattern is identical to "Requeue" button in Missing Embeddings section
- Version history preserves the original (reversible)
- Simple error surface: HTTP 500 → error toast

**Cons**:
- HTTP request blocks while LLM generates summary (~5–30s depending on provider/note size)
- If user closes browser mid-summarize, nothing bad happens (idempotent retry is safe)
- No streaming progress — user sees a loading spinner for the full duration

**Coupling Analysis**:

| Component | Change | New dependency |
|-----------|--------|----------------|
| `IKbHealthService` | +2 methods | — |
| `KbHealthService` | +2 methods, inject `IChatClient` | `IChatClient` (already in DI) |
| `KbHealthController` | +2 actions | — |
| `KbHealthModels` | +1 record (`LargeNote`) | — |
| `kb-health.ts` (API client) | +2 functions | — |
| `kb-health.tsx` (page) | +1 section component | — |

New afferent coupling: `KbHealthService` → `IChatClient` (one new edge).
`ContentGenerationService` already has this dependency, so the pattern is established.

**Failure Modes**:

| Mode | Severity | Mitigation |
|------|----------|------------|
| LLM returns content still > threshold | Medium | Re-check length post-summarize; surface warning toast if still large |
| LLM call times out / errors | Low | Catch exception, return HTTP 500, error toast in UI |
| Note not found | Low | Return 404, existing pattern |
| Version save fails | Medium | Wrap in transaction; either both writes succeed or neither |

**Evolvability**:
- Future: streaming progress → can swap to SSE/SignalR without changing the service interface
- Future: configurable summarization prompt per user → add `IOptions<SummarizationOptions>`
- Future: approve-before-save preview → can split into `/preview` and `/apply` endpoints

**Effort Estimate**: S (small — 2–3 days)

---

### Option B: Background queue (outbox pattern, same as embedding)

**Summary**: Add a `SummarizeStatus` enum column to `Notes`, a channel-based queue,
and a new `SummarizationBackgroundService`. The UI polls or uses websockets for completion.

**Pros**:
- Non-blocking HTTP — button press returns immediately
- Natural retry logic
- Consistent with embedding architecture

**Cons**:
- Requires a new EF Core migration (new column)
- New background service with full lifecycle management
- UI needs polling or push notification for completion feedback
- ~3–4× implementation effort vs Option A
- No evidence of a performance requirement justifying the complexity

**Effort Estimate**: M–L

**Verdict**: Over-engineered for a single-button health repair action. Adopt only if
synchronous LLM timeouts become a problem in practice.

---

### Option C: Summarize inside `INoteService`

**Summary**: Add `SummarizeAsync` to `INoteService` and expose it via
`NotesController`. The KB health page calls the notes endpoint, not kb-health.

**Pros**:
- Summarization is semantically a note operation, not a health operation

**Cons**:
- Breaks the separation between content editing (NoteService) and health repair (KbHealthService)
- Requires injecting `IChatClient` into `NoteService` for this one operation
- The "large notes list" still belongs in health — so you'd need both services to change
- ADR-009 explicitly put health mutations in `KbHealthService`

**Verdict**: Rejected. Inconsistent with ADR-009 and creates split responsibility.

---

## Comparison Matrix

| Criterion | Option A (Sync, KbHealth) | Option B (Background queue) | Option C (NoteService) |
|-----------|---------------------------|-----------------------------|------------------------|
| Complexity | 🟢 Low | 🔴 High | 🟡 Medium |
| Evolvability | 🟡 Medium | 🟢 High | 🔴 Low |
| Time to implement | 🟢 2–3 days | 🔴 1 week | 🟡 3–4 days |
| Coupling impact | 🟢 One new edge | 🟡 New service | 🔴 Splits responsibility |
| Pattern consistency | 🟢 Matches existing | 🟡 New pattern | 🔴 Breaks ADR-009 |
| UX blocking | 🟡 Spinner during LLM | 🟢 Immediate | 🟡 Spinner during LLM |
| Migration required | 🟢 No | 🔴 Yes | 🟢 No |

---

## Recommendation

**Option A: Synchronous summarization in `KbHealthService`**

The existing pattern is synchronous HTTP for all health mutations. The LLM call for
a single note summary is brief enough (a short prompt, typically <200 tokens output)
that a loading spinner is acceptable UX. The version history preserves the original,
making the action safe and reversible.

**Tradeoffs Accepted**:
- Blocking HTTP: acceptable because summarization is a deliberate single-note action,
  not a bulk operation. If 5 notes need summarizing, the user clicks 5 buttons.
- Mixing LLM into KbHealthService: `InsertWikilinkAsync` already establishes that
  health mutations can modify note content; LLM-assisted mutation is the same pattern.

**Risks to Monitor**:
- If users have many large notes and want bulk summarization: add a "Summarize all"
  endpoint using `Task.WhenAll` with a semaphore, or migrate to Option B.
- If the LLM summary still exceeds the threshold: surface a warning in the response
  rather than silently embedding a still-truncated note.

---

## Implementation Plan

### Backend

- [ ] Add `LargeNote` record to `KbHealthModels.cs`
  ```csharp
  public record LargeNote(string Id, string Title, DateTime UpdatedAt, int CharacterCount);
  ```
- [ ] Add `SummarizeNoteResponse` record to `KbHealthModels.cs`
  ```csharp
  public record SummarizeNoteResponse(string NoteId, int OriginalLength, int SummarizedLength, bool StillLarge);
  ```
- [ ] Add `GetLargeNotesAsync` + `SummarizeNoteAsync` to `IKbHealthService`
- [ ] Inject `IChatClient` into `KbHealthService` constructor
- [ ] Implement `GetLargeNotesAsync`:
  - Read threshold from `IConfiguration` (`Embedding:MaxInputCharacters`, default 4000)
  - Query `WHERE LENGTH("Content") > threshold AND Status = Permanent`
  - Return `LargeNote[]` ordered by descending character count
- [ ] Implement `SummarizeNoteAsync`:
  - Load note, 404 if not found
  - Save `NoteVersion` of original content
  - Call `IChatClient` with condensation prompt (target: under threshold characters, preserve key ideas)
  - Replace `note.Content`, set `note.EmbedStatus = Stale`, `note.UpdatedAt = now`
  - Return `SummarizeNoteResponse` with lengths and `StillLarge` flag
- [ ] Add controller actions to `KbHealthController`:
  - `GET /api/kb-health/large-notes`
  - `POST /api/kb-health/large-notes/{id}/summarize`
- [ ] Add OTel activity spans (`kb_health.get_large_notes`, `kb_health.summarize_note`)

### Frontend

- [ ] Add `getLargeNotes` and `summarizeNote` to `src/api/kb-health.ts`
  - `getLargeNotes`: `GET /api/kb-health/large-notes`
  - `summarizeNote`: `POST /api/kb-health/large-notes/{id}/summarize`
- [ ] Add `LargeNote`, `SummarizeNoteResponse` to `src/api/types.ts`
- [ ] Add `LargeNotesSection` component to `kb-health.tsx`:
  - Section header with count badge (matches other sections)
  - Each row: note title (link), character count badge, "Summarize" button
  - Loading spinner on Summarize button while mutation is in flight
  - Success toast: "Note summarized — embedding queued for refresh"
  - Warning toast if `stillLarge` is true: "Note still exceeds limit — consider manual editing"
  - Error toast on failure
  - Invalidate `['kb-health-large-notes']` + `['kb-health']` query keys on success

### Tests

- [ ] Unit test `GetLargeNotesAsync` — notes below/above threshold
- [ ] Unit test `SummarizeNoteAsync` — verify version saved, status set to Stale, content replaced
- [ ] Unit test `SummarizeNoteAsync` — 404 when note not found
- [ ] Integration test with fake `IChatClient` (same approach as existing content generation tests)

---

## Open Questions

- [ ] **Summarization prompt**: Should the prompt target exactly `MaxInputCharacters` or
  a shorter target (e.g., 80% of the limit to give headroom)? Recommend 80%.
- [ ] **Still-large handling**: If the LLM still produces content above the threshold,
  should we truncate automatically or just warn? Recommend warn-only (don't silently truncate).
- [ ] **Version history in health mutations**: `InsertWikilinkAsync` does NOT currently
  write a `NoteVersion`. Should we fix that too, or only add versioning to summarization?
  Recommend: add versioning to summarization now; backfill wikilink is a separate task.
