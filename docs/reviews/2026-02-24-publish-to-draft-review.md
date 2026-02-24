# Code Review: Publish to Draft + Editor Review

**Date**: 2026-02-24
**Branch**: main
**Design Spec**: `docs/specs/2026-02-24-publish-to-draft.md`
**Agents**: 6 (architecture-reviewer, coupling-analyzer, failure-mode-analyst, dotnet-reviewer, react-frontend-reviewer, otel-tracing-reviewer)

---

## Summary

Implementation of the publish-to-draft pipeline and editor review pass is functionally correct and all 474 tests pass. The main risks are around silent failure modes in publishing (stuck pieces with no recovery path), a TOCTOU race in `SendToDraft`, concrete type injection in the controller, and a stale closure bug in the React `PieceCard` that could silently lose user edits.

**Recommendation**: Fix the 4 Critical issues before shipping. The 🟡 Important issues are strong candidates for a fast-follow PR.

---

## 🔴 Critical (Must Fix)

### C-1 — Publer polling exhaustion stamps `SentToDraftAt` on failure
**File**: `src/ZettelWeb/Controllers/ContentController.cs` (SendToDraft) + `src/ZettelWeb/Services/Publishing/PublerPublishingService.cs` (PollForPostUrlAsync)
**Agents**: failure-mode-analyst, dotnet-reviewer

When `PollForPostUrlAsync` exhausts its 10 attempts, `PublerPublishingService.SendToDraftAsync` returns the sentinel string `"publer:draft:created"`. The controller then stamps `SentToDraftAt = DateTime.UtcNow` and `DraftReference = "publer:draft:created"` — locking the piece permanently as "sent" with a broken draft link in the UI, and no recovery path.

**Fix**: Throw an exception instead of returning a sentinel:
```csharp
// PublerPublishingService.PollForPostUrlAsync
throw new InvalidOperationException(
    $"Publer job {jobId} did not complete within the polling window.");
```
The controller's `SendToDraft` will propagate this as a 500, which is preferable to silent data corruption.

---

### C-2 — Raw string literal in `BuildFileContent` is indentation-fragile
**File**: `src/ZettelWeb/Services/Publishing/GitHubPublishingService.cs` (BuildFileContent)
**Agent**: dotnet-reviewer

The Astro frontmatter is built using a raw `"""..."""` string literal. Any future indentation change (auto-formatter, Extract Method refactor) will silently produce invalid YAML that the Astro build rejects at deploy time.

**Fix**: Build frontmatter using explicit string concatenation or a `StringBuilder` so the structure is visible and indentation cannot drift:
```csharp
var sb = new StringBuilder();
sb.AppendLine("---");
sb.AppendLine($"author: {_options.GitHub.AuthorName}");
sb.AppendLine($"title: {piece.Title}");
sb.AppendLine($"pubDatetime: {DateTime.UtcNow:O}");
sb.AppendLine($"description: {piece.Description}");
sb.AppendLine($"draft: true");
sb.AppendLine($"tags: [{string.Join(", ", piece.GeneratedTags.Select(t => $"\"{t}\""))}]");
sb.AppendLine("---");
sb.AppendLine();
sb.Append(piece.Body);
return sb.ToString();
```

---

### C-3 — `EnsureSuccessStatusCode()` silently discards error body
**File**: `src/ZettelWeb/Services/Publishing/GitHubPublishingService.cs` and `PublerPublishingService.cs`
**Agent**: dotnet-reviewer

`EnsureSuccessStatusCode()` throws `HttpRequestException` with only the status code. The response body (GitHub: `{"message":"..."}`, Publer: `{"errors":[...]}`) is lost, making debugging publishing failures very hard.

**Fix**:
```csharp
if (!response.IsSuccessStatusCode)
{
    var body = await response.Content.ReadAsStringAsync(ct);
    _logger.LogError("GitHub API returned {StatusCode}: {Body}", (int)response.StatusCode, body);
    response.EnsureSuccessStatusCode(); // now rethrows with context in logs
}
```

---

### C-4 — GitHub 401 (token expiry) returns an unhelpful 500
**File**: `src/ZettelWeb/Services/Publishing/GitHubPublishingService.cs`
**Agent**: failure-mode-analyst

When the GitHub PAT expires, the API returns 401. This surfaces to the user as an opaque 500 with no indication of what to do.

**Fix**: Detect 401 in `GitHubPublishingService` and throw a domain exception that the controller maps to 422 with an actionable message:
```csharp
if (response.StatusCode == HttpStatusCode.Unauthorized)
    throw new PublishingAuthException("GitHub token is invalid or expired. Update PublishingOptions:GitHub:Token.");
```
In the controller:
```csharp
catch (PublishingAuthException ex)
    return UnprocessableEntity(new { error = ex.Message });
```

---

## 🟡 Important (Should Fix)

### I-1 — Controller injects concrete publishing services instead of `IPublishingService`
**File**: `src/ZettelWeb/Controllers/ContentController.cs`
**Agents**: coupling-analyzer, architecture-reviewer

The controller has `private readonly GitHubPublishingService _github` and `private readonly PublerPublishingService _publer` — direct coupling to implementations. Adding a third medium (e.g. LinkedIn) requires changing the controller.

**Fix**: Register as keyed services and resolve by medium:
```csharp
// Program.cs
builder.Services.AddKeyedScoped<IPublishingService, GitHubPublishingService>("blog");
builder.Services.AddKeyedScoped<IPublishingService, PublerPublishingService>("social");

// Controller constructor
public ContentController(..., IServiceProvider sp) { _sp = sp; }

// SendToDraft action
var service = _sp.GetKeyedService<IPublishingService>(piece.Medium)
    ?? return UnprocessableEntity(new { error = $"No publisher configured for '{piece.Medium}'." });
```

---

### I-2 — TOCTOU race in `SendToDraft` allows duplicate sends
**File**: `src/ZettelWeb/Controllers/ContentController.cs` (SendToDraft)
**Agents**: dotnet-reviewer, failure-mode-analyst

Two concurrent requests for the same piece both read `SentToDraftAt == null`, both proceed to call the publishing service, and both write back — creating two drafts.

**Fix**: Use an atomic update with a conditional `WHERE` clause:
```csharp
var rows = await _db.ContentPieces
    .Where(p => p.Id == id && p.SentToDraftAt == null)
    .ExecuteUpdateAsync(s => s.SetProperty(p => p.SentToDraftAt, DateTime.UtcNow));
if (rows == 0) return Conflict(new { error = "Already sent to draft." });
// Now safe to call publishing service
```

---

### I-3 — `SendToDraft` does not enforce `Approved` status
**File**: `src/ZettelWeb/Controllers/ContentController.cs` (SendToDraft)
**Agent**: architecture-reviewer

A `Draft` or `Rejected` piece can currently be published. The endpoint should gate on `piece.Status == Approved`.

**Fix**:
```csharp
if (piece.Status != ContentPieceStatus.Approved)
    return UnprocessableEntity(new { error = "Only approved pieces can be sent to draft." });
```

---

### I-4 — `ParseBlogResponse` fails when LLM emits preamble text before `TITLE:`
**File**: `src/ZettelWeb/Services/ContentGenerationService.cs` (ParseBlogResponse)
**Agent**: dotnet-reviewer

The parser calls `lines.First(l => l.StartsWith("TITLE:"))` which throws `InvalidOperationException` if the LLM emits any text (e.g. "Sure! Here is your blog post:") before the structured headers.

**Fix**: Use `FirstOrDefault` and fall back gracefully:
```csharp
var titleLine = lines.FirstOrDefault(l => l.TrimStart().StartsWith("TITLE:"));
```
Also trim leading/trailing whitespace from each extracted value.

---

### I-5 — No explicit HttpClient timeout on "GitHub" and "Publer" clients
**File**: `src/ZettelWeb/Program.cs`
**Agents**: failure-mode-analyst, dotnet-reviewer

`AddHttpClient("GitHub")` and `AddHttpClient("Publer")` use the default 100-second timeout, meaning a hung publish call blocks the HTTP request thread for nearly 2 minutes before timing out.

**Fix**:
```csharp
builder.Services.AddHttpClient("GitHub", c => c.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddHttpClient("Publer", c => c.Timeout = TimeSpan.FromSeconds(30));
```

---

### I-6 — Stale closure: `description` and `tags` state never re-syncs after refetch
**File**: `src/zettel-web-ui/src/pages/content-review.tsx` (PieceCard)
**Agent**: react-frontend-reviewer

`useState(piece.description)` initialises once. After a parent refetch returns an updated `piece` prop, the local state still holds the stale value. The user sees correct data, edits it, and saves — but the save sends the stale initial value, silently losing the server-side update.

**Fix**:
```typescript
useEffect(() => {
  setDescription(piece.description ?? '')
}, [piece.description])

useEffect(() => {
  setTags(piece.generatedTags.join(', '))
}, [piece.generatedTags])
```

---

### I-7 — `onBlur` tag comparison is unreliable
**File**: `src/zettel-web-ui/src/pages/content-review.tsx` (PieceCard, tags onBlur)
**Agent**: react-frontend-reviewer

`tags !== original` compares strings, but ordering and spacing differences mean "a, b" !== "b, a" even though they represent the same tag set. This can trigger spurious mutations.

**Fix**: Normalize before comparing:
```typescript
const normalize = (s: string) => s.split(',').map(t => t.trim()).filter(Boolean).sort()
const isDirty = normalize(tags).join() !== normalize(piece.generatedTags.join(', ')).join()
if (isDirty) updateTagsMutation.mutate(...)
```

---

### I-8 — Missing OTel spans on publishing operations
**File**: `src/ZettelWeb/Services/Publishing/GitHubPublishingService.cs`, `PublerPublishingService.cs`, `ContentGenerationService.cs`
**Agent**: otel-tracing-reviewer

`SendToDraftAsync` in both publishing services has no custom span. `GenerateEditorFeedbackAsync` has no child span. Without spans, failures in production are invisible in traces.

**Fix** (pattern to apply to each):
```csharp
using var activity = ZettelTelemetry.Source.StartActivity("publishing.github.send_to_draft");
activity?.SetTag("content.piece_id", piece.Id);
activity?.SetTag("content.medium", piece.Medium);
```
Also add `content.editor_feedback` span in `GenerateEditorFeedbackAsync`.

---

### I-9 — Missing publishing metrics
**File**: `src/ZettelWeb/Telemetry/ZettelTelemetry.cs`
**Agent**: otel-tracing-reviewer

No metrics track publishing outcomes. There is no way to alert on publishing failures or measure editor feedback generation rate.

**Fix** — add to `ZettelTelemetry`:
```csharp
public static readonly Counter<long> DraftsSent =
    Meter.CreateCounter<long>("content.drafts_sent");
public static readonly Counter<long> DraftSendFailures =
    Meter.CreateCounter<long>("content.draft_send_failures");
public static readonly Counter<long> EditorFeedbackGenerated =
    Meter.CreateCounter<long>("content.editor_feedback_generated");
public static readonly Histogram<double> PublishingDurationMs =
    Meter.CreateHistogram<double>("content.publishing_duration_ms");
```

---

### I-10 — No happy-path integration test for `SendToDraft`
**File**: `src/ZettelWeb.Tests/Controllers/ContentHttpIntegrationTests.cs`
**Agent**: architecture-reviewer

Only negative-path tests exist for `SendToDraft` (404, 409, 422). There is no test that approves a piece and then successfully sends it to draft.

**Fix**: Add a `POST_SendToDraft_Returns200_AndStampsSentAt` test that:
1. Triggers generation
2. Approves the blog piece
3. Registers a mock `IPublishingService` (or skips when not configured)
4. POSTs to `send-to-draft`
5. Asserts 200, non-null `sentToDraftAt`, non-empty `draftReference`

---

## 🟢 Suggestions (Nice to Have)

### S-1 — Remove unused `descriptionRef` useRef
**File**: `src/zettel-web-ui/src/pages/content-review.tsx`
`const descriptionRef = useRef(null)` is declared but never referenced. Delete it.

### S-2 — Use `ApiError.status` instead of `err.message.includes('422')`
**File**: `src/zettel-web-ui/src/pages/content-review.tsx` (sendToDraftMutation onError)
The API client wraps errors in an `ApiError` with a `status` field. Use `err.status === 422` for a more robust check.

### S-3 — Use `setQueryData` for immediate UI update after `sendToDraft`
**File**: `src/zettel-web-ui/src/pages/content-review.tsx`
`sendToDraftMutation.onSuccess` currently calls `invalidateQueries`, which requires a network round-trip. Use `setQueryData` to immediately update the cached piece for snappier UI:
```typescript
onSuccess: (updatedPiece) => {
  queryClient.setQueryData(['generations', piece.generationId], (old) => ({
    ...old,
    pieces: old.pieces.map(p => p.id === updatedPiece.id ? updatedPiece : p)
  }))
}
```

### S-4 — Add `aria-expanded` to editor feedback collapsible button
**File**: `src/zettel-web-ui/src/pages/content-review.tsx`
The amber feedback panel toggle button is missing `aria-expanded={editorOpen}` for screen-reader accessibility.

### S-5 — Wrap `PieceCard` in `React.memo`
**File**: `src/zettel-web-ui/src/pages/content-review.tsx`
`PieceCard` re-renders whenever the parent generation list re-fetches. `React.memo` would prevent unnecessary re-renders for unchanged pieces.

### S-6 — Editor feedback should default to open (spec mismatch)
**File**: `src/zettel-web-ui/src/pages/content-review.tsx`
Design spec says editor feedback panel should default to expanded. Code uses `useState(false)`. Change to `useState(true)`.

### S-7 — `PublishingOptions` properties should use `init` not `set`
**File**: `src/ZettelWeb/Services/Publishing/PublishingOptions.cs`
Properties bound via `Configure<T>` should be `init`-only to prevent accidental mutation after startup.

### S-8 — Pass `CancellationToken` to `FindAsync` calls
**File**: `src/ZettelWeb/Controllers/ContentController.cs`
`FindAsync(id)` should be `FindAsync([id], ct)` so that connection-cancelled requests don't hold EF Core database connections longer than necessary.

### S-9 — Append `piece.Id[..8]` to GitHub slug to prevent filename collisions
**File**: `src/ZettelWeb/Services/Publishing/GitHubPublishingService.cs`
Two blog posts with similar titles produce the same slug. Appending a short ID suffix (`{slug}-{piece.Id[..8]}`) eliminates collisions without sacrificing readability.

### S-10 — Consider parallel blog + social generation
**File**: `src/ZettelWeb/Services/ContentGenerationService.cs`
Blog post generation, editor feedback, and social post generation run sequentially. Blog and social are independent and could be run in parallel with `Task.WhenAll`, reducing generation latency at the cost of slightly increased LLM concurrency.

---

## Architecture Assessment

| Principle | Score | Notes |
|-----------|-------|-------|
| Evolvability | 3/5 | Concrete injection prevents adding new mediums without controller changes |
| Encapsulation | 4/5 | Publishing services cleanly isolated; leaks in controller medium dispatch |
| Coupling | 3/5 | Controller coupled to two concrete implementations |
| Understanding | 4/5 | Code readable; missing OTel makes prod debugging harder |
| Failure Modes | 2/5 | C-1 and C-4 create silent failures with no recovery path |

---

## FMEA Top 5 (by RPN)

| FM | Scenario | RPN | Critical Issue |
|----|----------|-----|----------------|
| FM-2 | Publer polling timeout → piece permanently stuck | 168 | C-1 |
| FM-7 | GitHub token expiry → unhelpful 500 | 144 | C-4 |
| FM-4 | DB write failure after external publish succeeds | 98 | (new: compensating action needed) |
| FM-1 | GitHub rate limit → publish fails | 60 | C-3 (better error body) |
| FM-5 | Network timeout mid-request | 48 | I-5 (explicit timeouts) |

---

## Next Steps

1. Fix **C-1 through C-4** in a single commit (all are backend-only, low blast radius)
2. Fix **I-1, I-2, I-3** together (DI + TOCTOU + status check — all in controller)
3. Fix **I-6, I-7** together (React stale closure + tag comparison — same component)
4. Add **I-10** happy-path test alongside I-1/I-2/I-3 fixes
5. Add OTel spans + metrics (I-8, I-9) as observability follow-up
6. Remaining 🟢 suggestions at discretion
