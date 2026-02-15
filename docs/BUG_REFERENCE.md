# Bug Reference

Last Updated: 2026-02-14 | v0.1

## BUG-001: Backend crash on startup — column "Status" does not exist

**Date**: 2026-02-14
**Severity**: Critical (app won't start)
**Compound Doc**: [DB-001](compound/database/DB-001-ef-core-ensure-created-no-alter.md)

**Symptom**: `Npgsql.PostgresException: 42703: column "Status" does not exist`
on startup when running against a database created before Batch 20.

**Root Cause**: `EnsureCreated()` doesn't alter existing tables. New columns
from Batch 20 (Status, Source, EnrichmentJson, EnrichStatus, EnrichRetryCount)
were missing. Raw SQL index creation on `Status` failed.

**Fix**: Added idempotent migration SQL in `Program.cs` startup block that
adds missing columns with `IF NOT EXISTS` guards before index creation.

**Prevention**: Always add `ALTER TABLE ADD COLUMN` migration SQL when adding
new model properties to a project using `EnsureCreated()`.

---

## BUG-002: varchar(14) too short for new Note IDs

**Date**: 2026-02-14
**Severity**: Critical (can't create notes)
**Compound Doc**: [DB-001](compound/database/DB-001-ef-core-ensure-created-no-alter.md)

**Symptom**: `22001: value too long for type character varying(14)` when
creating or capturing notes.

**Root Cause**: Note ID format changed from `yyyyMMddHHmmss` (14 chars) to
`yyyyMMddHHmmssfff` (17 chars) but `EnsureCreated()` didn't widen the
existing column. Both `Notes.Id` and `NoteTags.NoteId` needed widening.

**Fix**: Added `ALTER COLUMN TYPE varchar(17)` in startup migration block.

---

## BUG-003: Embedding fails — input length exceeds context length (RECURRED)

**Date**: 2026-02-14 (recurred same day)
**Severity**: Medium (notes created but not embedded)
**Compound Doc**: [INFRA-001](compound/infrastructure/INFRA-001-ollama-embedding-context-limit.md)

**Symptom**: `OllamaException: the input length exceeds the context length`
for long notes (26K+ chars), even after truncation to 8,000 chars.

**Root Cause**: Original fix truncated to 8,000 chars assuming the model's
*maximum* context (8192 tokens). But Ollama defaults `num_ctx` to 2048
tokens. At ~3-4 chars/token, 8,000 chars ≈ 2,000-2,667 tokens — right at
the default limit. Additionally, no max retry limit meant the failed note
retried every 30 seconds in an infinite loop.

**Fix**:
1. Reduced default to 4,000 chars (configurable via `Embedding:MaxInputCharacters`)
2. Added max retry limit (configurable via `Embedding:MaxRetries`, default 3)
   matching the existing `EnrichmentBackgroundService` pattern

**Prevention**: When setting character limits based on token context windows,
use the model's *default* `num_ctx`, not its maximum.

---

## BUG-004: useBlocker crash — must be used within a data router

**Date**: 2026-02-14
**Severity**: Critical (can't create/edit notes in UI)
**Compound Doc**: [FE-001](compound/frontend/FE-001-react-router-data-router-required.md)

**Symptom**: `useBlocker must be used within a data router` error when
navigating to note editor.

**Root Cause**: App used legacy `<BrowserRouter>` but `useBlocker` (used
for unsaved changes dialog) requires a data router.

**Fix**: Converted to `createBrowserRouter` + `<RouterProvider>` in
`app.tsx` and `main.tsx`.

---

## BUG-005: Webhook endpoints skip auth when secrets are empty

**Date**: 2026-02-15
**Severity**: High (security bypass)

**Symptom**: Email and Telegram webhook endpoints accept all requests
without validation when `WebhookSecret` or `TelegramBotToken` are not
configured (empty string, which is the default).

**Root Cause**: `CaptureController` checked
`if (!string.IsNullOrEmpty(secret))` before validating the header.
When the secret was not configured, the entire auth block was skipped,
allowing any unauthenticated request to create notes.

**Fix**: Inverted the logic to reject requests when secrets are not
configured. Returns `Ok()` (not 401) to avoid information leakage
(standard webhook security pattern). Added log warning for diagnosis.

**Prevention**: Default-deny pattern for webhook authentication. When
a secret is not configured, reject rather than skip validation.

---

## BUG-006: ExportAllAsZipAsync loads tracked entities with embeddings

**Date**: 2026-02-15
**Severity**: Low (performance/memory)

**Symptom**: Export loads all notes as tracked entities with full
`float[]` embedding arrays into memory, even though embeddings are
never used in the markdown export.

**Root Cause**: `ExportService.ExportAllAsZipAsync()` used
`ToListAsync()` without `AsNoTracking()`, causing EF Core to track
all returned entities including their embedding data.

**Fix**: Added `.AsNoTracking()` to the query chain. This avoids
change tracker overhead and reduces memory pressure from large
embedding arrays.

**Prevention**: Always use `AsNoTracking()` for read-only queries,
especially when loading large collections or entities with heavy
payloads like embeddings.
