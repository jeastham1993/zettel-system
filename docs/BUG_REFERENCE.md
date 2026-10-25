# Bug Reference

Last Updated: 2026-10-25 | v1.2.5-dev


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

## BUG-008: Chat sent raw prompt template with unsubstituted placeholders

**Date**: 2026-10-17
**Severity**: Critical (chat feature never worked)

**Symptom**: The assistant's chat replies complained about unfilled
template placeholders: the LLM literally received
`{{contextText}}`, `{{GetConversationHistory(session, userMessage)}}`,
and `{{userMessage.Content}}` as text.

**Root Cause**: `ChatService.BuildPrompt` used a `$$$` raw string
literal. With `$$$`, interpolation delimiters are `{{{ }}}` (triple
braces) — the double-brace placeholders in the template were literal
text, so no substitution ever happened. The template shipped unnoticed
because the chat frontend (`chat.tsx`) was simultaneously broken and
failed type-check, and no test covered prompt construction.

**Fix**: Changed the literal to `$$`, making `{{expr}}` the
interpolation delimiter — the placeholders now render. Added
`ChatServicePromptTests` regression tests asserting the user message,
context text, and conversation history appear in the prompt and no
`{{...}}` placeholders remain.

**Prevention**: In C# raw strings, the number of `$` characters sets
the number of braces used for interpolation. Mismatched `$` count vs
brace count silently produces literal text, not an error. Grep new
prompt templates for `{{` before shipping, and unit-test prompt
construction against a fake LLM.

---

## BUG-009: Related notes hit-and-miss — noise and signal share an absolute score band on Titan

**Date**: 2026-10-25
**Severity**: Moderate (core KB feature unreliable)
**Compound Doc**: [INFRA-004](compound/infrastructure/INFRA-004-embedding-similarity-thresholds-are-model-specific.md)

**Symptom**: Related-note suggestions were hit-and-miss; obviously related
notes sometimes ranked below unrelated noise; a "dawn of everything"
Source stub with empty content never matched its obvious siblings.

**Root Cause**: Titan v2 compresses weak matches and noise into the same
absolute cosine band (~0.2–0.45) as genuine matches' lower edge, so the
single absolute `Search:MinimumSimilarity` gate cannot separate them.
Additionally: (a) `FindRelatedAsync` had no `Status` filter, so fleeting
Telegram captures appeared in related lists; (b) notes with empty content
got title-only embeddings that match arbitrarily.

**Fix**: `FindRelatedAsync` now fetches a wider candidate pool, applies
the absolute floor, then drops results below `Search:RelatedRelativeFloor`
(default 0.6) × top match's similarity; adds `Status = 'Permanent'` filter;
`EmbeddingBackgroundService` marks notes with < `Embedding:MinContentCharacters`
(default 50) of stripped content as `EmbedStatus.Skipped` with a null
embedding. After deploying, run `POST /api/notes/re-embed` once so existing
thin notes get their stale embeddings cleared.

**Prevention**: Gate similarity results relative to the top match when the
model's score distribution is compressed; never embed title-only stubs.

---

## BUG-010: `Search:_suggestionThreshold` config key typo silently ignored the setting

**Date**: 2026-10-25
**Severity**: Low (default coincided with intended value)

**Symptom**: Changing `Search:SuggestionThreshold` in config had no effect
on KB health semantic edges.

**Root Cause**: `KbHealthService` read the key with a leading underscore
(`Search:_suggestionThreshold`), which never matched the documented
`Search:SuggestionThreshold` in `appsettings.json`, so only the code
default (0.3) ever applied. It worked by coincidence — the default matched
the intended Titan value.

**Fix**: Key corrected to `Search:SuggestionThreshold`.

**Prevention**: When binding `GetValue<string, T>("Section:Key", ...)` by
hand, copy the key from `appsettings.json` literally, or bind an options
class instead (typos in options binding throw or show up in startup
diagnostics). Consider a startup assertion that every documented config key
is present in configuration.

---

## BUG-011: Cohere embed-v4 Bedrock request rejected as malformed

**Date**: 2026-10-25
**Severity**: High (blocked the Cohere provider switch — every embed failed)

**Symptom**: After switching `Embedding:Model` to the Cohere embed-v4
inference-profile ARN, `EmbeddingBackgroundService` failed every note with
`Amazon.BedrockRuntime.Model.ValidationException: Malformed request. Please
check your parameter/values and try again.`

**Root Cause**: `CohereBedrockEmbeddingGenerator` was written against the
embed-v3 Bedrock schema. Two mismatches against the v4 schema:
1. The request sent `dimensions` (a v3-era field name that does not exist on
   v4). Bedrock rejects the whole request as malformed on any unknown field.
   The v4 field is `output_dimension` (allowed 256/512/1024/1536, default
   1536).
2. The response parser read the v3 nested shape
   (`embeddings: { float: [[...]] }`). v4 with a single embedding type
   returns a flat array under `embeddings`
   (`response_type: embeddings_floats`) — this would have thrown after the
   first successful call even if (1) hadn't blocked it.

**Fix**: Request now sends `output_dimension`; response parsing accepts
BOTH v4 shapes. A second live failure after the request fix showed that
production Bedrock actually returns the nested `embeddings.float` object for
embed-v4, while the AWS model-parameters page documents a flat
`embeddings` array for a single embedding type — the docs and the service
disagree, so the parser checks `ValueKind` and handles both. Tests cover
both shapes and assert `dimensions` is absent from the request.

**Prevention**: Verify the InvokeModel request/response schema against the
AWS docs page for the exact model **version**, not the model family —
Bedrock model providers make breaking body-schema changes between versions,
and unknown request fields are rejected wholesale rather than ignored.
