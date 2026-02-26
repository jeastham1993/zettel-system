# Code Review: Autonomous Research Agent

**Generated:** 2026-02-26
**Agents:** 6 parallel (architecture, .NET, failure-modes, React/TS, OTel, database)
**Total findings:** 42 raw → 28 unique after deduplication
**Recommendation:** ⚠️ Approve with required changes — 4 critical issues must be addressed

---

## Summary

The feature is well-structured, the security thinking is sound, and the test coverage is
strong. However a single structural decision — using `Task.Run` fire-and-forget in the
controller — creates **four independent critical failures** that all reviewers flagged
independently. Fixing that one root cause resolves three of the four critical issues
simultaneously. The fourth critical issue (React query key mismatch) is a two-word fix.

---

## Critical Issues (Must fix before production use)

### C1 — Scoped DbContext disposed before ExecuteAgendaAsync runs
*Confirmed by: .NET reviewer, architecture reviewer, database reviewer, failure-mode analyst*

`ResearchAgentService` is `Scoped`, which means its `ZettelDbContext` is owned by the
HTTP request's DI scope. When `ApproveAgenda` spawns `Task.Run(...)` and returns 202,
ASP.NET Core disposes the request scope — and the `DbContext` — before the background
task has run its first EF Core operation. Every `_db.*` call in `ExecuteAgendaAsync`
can throw `ObjectDisposedException`.

The fix already exists in this codebase: `EnrichmentBackgroundService` and
`EmbeddingBackgroundService` both use a `Channel<T>` + `BackgroundService` pattern
where the background service creates its own `IServiceScope` per job. Apply the same
pattern:

```csharp
// ResearchController.ApproveAgenda
await _researchQueue.EnqueueAsync(new ResearchJob(agendaId, request.BlockedTaskIds ?? []));
return Accepted();

// ResearchExecutionBackgroundService : BackgroundService
await foreach (var job in _queue.ReadAllAsync(stoppingToken))
{
    using var scope = _scopeFactory.CreateScope();
    var svc = scope.ServiceProvider.GetRequiredService<IResearchAgentService>();
    await svc.ExecuteAgendaAsync(job.AgendaId, job.BlockedTaskIds, stoppingToken);
}
```

---

### C2 — Agenda permanently stuck in "Executing" if process dies mid-run
*Confirmed by: architecture reviewer, .NET reviewer, database reviewer, failure-mode analyst*

`ExecuteAgendaAsync` writes `Status = Executing` at the start and `Status = Completed`
at the end. If the process restarts between those two writes, the agenda is stuck in
`Executing` forever with no recovery path.

The existing `EnrichmentBackgroundService.RecoverStuckNotesAsync` and
`EmbeddingBackgroundService.RecoverProcessingNotesAsync` demonstrate the exact pattern
needed: on startup, reset any records in a processing state back to a retriable state.

**Minimum fix:** Add `Failed` to `ResearchAgendaStatus` and wrap `ExecuteAgendaAsync`
in a `try/finally` that saves `Failed` status if an exception escapes. Separately, add
a startup scan that resets any `Executing` agendas older than a threshold to `Failed`.

---

### C3 — No LLM timeout + no concurrency guard = thread pool starvation
*Confirmed by: failure-mode analyst, architecture reviewer*

Two independent issues that compound badly:

**No timeout:** Neither the `TriggerAsync` LLM call nor the `SynthesiseAsync` calls
have a timeout. If Bedrock/OpenAI hangs, the `Task.Run` thread is held indefinitely.
A hung thread also holds the (disposed) `DbContext` connection.

```csharp
// Program.cs — add timeouts to match existing pattern for "GitHub"/"Publer" clients
builder.Services.AddHttpClient("BraveSearch", c => c.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddHttpClient("Arxiv",       c => c.Timeout = TimeSpan.FromSeconds(15));
// For IChatClient, pass a linked CancellationTokenSource with timeout into ExecuteAgendaAsync
```

**No concurrency guard:** Nothing prevents a user from approving multiple agendas
simultaneously. Combined with hung LLM calls, this saturates the thread pool and
degrades the entire application.

```csharp
// Minimum: check DB for any Executing agenda before starting a new Task.Run
var alreadyRunning = await _db.ResearchAgendas
    .AnyAsync(a => a.Status == ResearchAgendaStatus.Executing);
if (alreadyRunning)
    return Conflict("A research run is already in progress");
```

Both issues go away cleanly if C1 is fixed with a proper `BackgroundService` (which
processes items sequentially from a channel, providing natural concurrency control).

---

### C4 — Inbox badge never updates after accepting a finding (React)
*Confirmed by: React reviewer*

`research.tsx` invalidates `['inbox-count']` on finding acceptance:

```tsx
// research.tsx — WRONG
queryClient.invalidateQueries({ queryKey: ['inbox-count'] })
```

But `useInboxCount` registers under `['inbox', 'count']`. TanStack Query array key
matching treats these as completely different keys — the badge is permanently stale
after a finding is accepted.

```tsx
// research.tsx — CORRECT (matches hooks/use-inbox.ts)
queryClient.invalidateQueries({ queryKey: ['inbox', 'count'] })
```

---

## Important Issues (Should fix — acceptable as follow-up)

### I1 — IConfiguration read directly instead of IOptions<ResearchOptions>
*Confirmed by: .NET reviewer, architecture reviewer*

`ResearchAgentService` and `BraveSearchClient` read config values directly from
`IConfiguration` in constructors. The rest of the codebase uses `IOptions<T>`
(see `CaptureConfig`, `TopicDiscoveryOptions`, `ContentGenerationOptions`). This
makes tests harder and bypasses validation-on-startup.

```csharp
// New: src/ZettelWeb/Options/ResearchOptions.cs
public class ResearchOptions
{
    public const string SectionName = "Research";
    public int MaxFindingsPerRun { get; set; } = 5;
    public double DeduplicationThreshold { get; set; } = 0.85;
    public BraveSearchOptions BraveSearch { get; set; } = new();
}
public class BraveSearchOptions { public string ApiKey { get; set; } = ""; }

// Program.cs
builder.Services.Configure<ResearchOptions>(builder.Configuration.GetSection("Research"));
```

---

### I2 — Cascade delete silently destroys accepted findings
*Confirmed by: database reviewer*

`ResearchAgenda → ResearchTask → ResearchFinding` all cascade on delete. An accepted
`ResearchFinding` has `AcceptedFleetingNoteId` pointing to a live note in the KB, but
deleting the agenda (e.g., a future admin cleanup) destroys the finding and its
provenance record silently, while the fleeting note survives as an orphan with no
traceable origin.

Change `ResearchFinding`'s delete behaviour in `OnModelCreating`:
```csharp
entity.HasMany(e => e.Findings)
    .WithOne()
    .HasForeignKey(f => f.TaskId)
    .OnDelete(DeleteBehavior.Restrict); // was: Cascade
```

---

### I3 — Missing FK constraints on Note references
*Confirmed by: database reviewer*

Three columns reference `Notes.Id` but have no FK constraints: `TriggeredFromNoteId`,
`MotivationNoteId`, `AcceptedFleetingNoteId`. A deleted note leaves dangling IDs with
no referential integrity check. Add FK config in `OnModelCreating` with appropriate
`OnDelete(DeleteBehavior.SetNull)` for nullable columns.

---

### I4 — ResearchRunsTotal counter incremented at wrong point
*Confirmed by: OTel reviewer*

`ZettelTelemetry.ResearchRunsTotal.Add(1)` is called at the top of `TriggerAsync`,
which only creates an agenda. Agendas can be created and never approved. A counter
named `runs_total` should increment at the start of `ExecuteAgendaAsync`. Rename the
existing counter to `zettel.research.agendas_created` and add a new
`zettel.research.runs_total` counter in `ExecuteAgendaAsync`.

---

### I5 — Fire-and-forget execution orphans all trace spans
*Confirmed by: OTel reviewer, architecture reviewer*

When `ApproveAgenda` fires `Task.Run` and returns 202, `Activity.Current` is gone.
The `research.execute` span has no parent — it appears as an isolated root span with
a brand new trace ID, completely disconnected from the `POST /approve` request that
triggered it. Trace correlation between trigger and execution is broken in production.

The fix requires capturing `Activity.Current?.Context` before the lambda:
```csharp
var parentContext = Activity.Current?.Context ?? default;
_ = Task.Run(async () =>
{
    using var activity = ZettelTelemetry.ActivitySource
        .StartActivity("research.execute", ActivityKind.Internal, parentContext);
    ...
});
```
This problem also disappears if C1 is fixed with a proper `BackgroundService`.

---

### I6 — UX gap: no feedback while research runs asynchronously
*Confirmed by: React reviewer*

After approving an agenda, the user navigates to `/research` and sees an empty state
("No research findings yet") with no indication that work is in progress. The empty
state message is actively misleading when findings are being generated.

Two changes:
1. Add `refetchInterval: 8000` to the findings query (conditional on `findings?.length === 0`)
2. Change empty state copy to distinguish "nothing has run" from "results pending"

---

### I7 — Custom checkbox inaccessible in ResearchAgendaModal
*Confirmed by: React reviewer*

The task toggle uses a styled `<button>` with a drawn `<div>` that mimics a checkbox
but has no `role`, `aria-pressed`, or `aria-checked`. Screen readers announce it as a
generic unnamed button. Use the ShadCN `Checkbox` component (already in the project)
or add `role="checkbox"` and `aria-checked={!isBlocked}` as a minimum.

---

### I8 — ArxivApiClient uses HTTP for its API base URL
*Confirmed by: .NET reviewer, architecture reviewer*

`http://export.arxiv.org/api/query` should be `https://`. `NormaliseArxivUrl` already
upgrades result URLs to HTTPS but the outbound API fetch is unencrypted.

---

### I9 — Missing composite index on ResearchFindings (Status, CreatedAt)
*Confirmed by: database reviewer*

`GetPendingFindingsAsync` queries `WHERE Status = 'Pending' ORDER BY CreatedAt DESC`.
The current single-column `Status` index requires a post-filter sort. As findings
accumulate (no pruning mechanism exists), this sort becomes expensive.

```csharp
// ZettelDbContext.cs — replace single Status index with composite
entity.HasIndex(e => new { e.Status, e.CreatedAt });
```

---

### I10 — Empty state in ResearchPage uses `<a>` instead of `<Link>`
*Confirmed by: React reviewer*

```tsx
// research.tsx — causes full page reload
<a href="/kb-health">Knowledge Health</a>

// Should be
<Link to="/kb-health">Knowledge Health</Link>
```

---

## Suggestions (Nice to have)

### S1 — ResearchAgentService is over the ADR-001 advisory size threshold
462 lines, 9 injected dependencies (Ce=11, I=0.92). ADR-001 flags services over ~300
lines for proactive splitting. Natural seam: extract `IResearchSynthesiser` (covering
`SynthesiseAsync` + `IsNearDuplicateAsync`). Not urgent for a personal-scale tool but
worth tracking.

### S2 — Dedup query transmits the embedding vector three times
The `{pgVector}` interpolation hole appears three times in the same query (SELECT,
WHERE, ORDER BY), transmitting a potentially-large float array three times as separate
parameters. A CTE that references the parameter once would be cleaner.

### S3 — ParseResearchTasks silently returns empty list on parse failure
If the LLM produces a malformed response, `ParseResearchTasks` returns `[]` with no
log entry. `TriggerAsync` then persists an empty agenda. Add a `LogWarning` when
`tasks.Count == 0` after parsing.

### S4 — IResearchAgentService.ExecuteAgendaAsync XML comment is wrong
The comment says "Returns immediately (202 pattern)" but the method is fully
synchronous. The fire-and-forget is the caller's responsibility, not the method's.
Fix the comment to accurately describe what the method does.

### S5 — `FindAsync(new object[] { id }, ct)` — use modern collection expression
```csharp
// Old API
var finding = await _db.ResearchFindings.FindAsync(new object[] { findingId }, ct);
// Modern
var finding = await _db.ResearchFindings.FindAsync([findingId], ct);
```

### S6 — `IsPrivateAddress` should not be on the public IUrlSafetyChecker interface
It is an implementation detail used only by `UrlSafetyChecker.IsUrlSafeAsync` itself.
Exposing it forces every test double to implement a method that has no business meaning
at the interface level.

### S7 — SynthesiseAsync does not set ActivityStatusCode.Error in fallback path
When the LLM call fails and falls back to raw text, the `research.synthesise` span
ends with `Unset` status (displayed as OK in most trace backends). The `catch` block
should call `activity?.SetStatus(ActivityStatusCode.Error, ex.Message)`.

### S8 — AcceptFinding and DismissFinding have no trace spans
Both are user-triggered actions that permanently affect the KB. `KbHealthService`
instruments simpler operations (e.g. `kb_health.insert_wikilink`). Add
`research.accept_finding` and `research.dismiss_finding` spans.

### S9 — HtmlSanitiser delegation statics on EnrichmentBackgroundService
The three delegating statics (`ExtractTitle`, `ExtractDescription`,
`ExtractContentExcerpt`) were documented as transitional. A search for callers outside
tests is likely to find none — if so, remove them.

### S10 — SimilarNoteIds is persisted but never populated
`ResearchFinding.SimilarNoteIds` is a `List<string>` stored as jsonb but
`ResearchAgentService` never writes to it. Either populate it from the dedup check
result, or remove the column to avoid misleading future developers.

---

## Confirmed Non-Issues (explicitly cleared by reviewers)

| Question | Verdict |
|---|---|
| Raw SQL injection risk in dedup query | **Safe.** EF Core `FormattableString` parameterises `{pgVector}` and `{threshold}` correctly. Same pattern used in `KbHealthService`, `TopicDiscoveryService`, `SearchService`. |
| Singleton clients injected into Scoped service | **Safe.** `BraveSearchClient` and `ArxivApiClient` are Singletons; `ResearchAgentService` is Scoped. Singletons consumed by Scoped services is correct — no captive dependency. |
| SaveChangesAsync batching | **Correct.** Findings accumulate in the change tracker and flush in one batch at the end of the run. No N+1 write concern. |
| Migration column types | **Clean.** All IDs `varchar(21)`, enums `varchar(20)` with defaults, nullable/non-nullable matches model, cascade deletes match DbContext config. |

---

## Execution Summary

| Agent | Focus | Key findings |
|---|---|---|
| architecture-reviewer | Coupling, SRP, failure modes | C1 root cause, C3 concurrency, DNS rebinding, ADR-001 size |
| dotnet-reviewer | .NET idioms, async, SQL safety | C1 DbContext scope, C3 CT, SQL confirmed safe, IOptions |
| failure-mode-analyst | Blast radius, RPN scoring | C2 stuck status, C3 timeout+concurrency, I2 cascade |
| react-frontend-reviewer | Hooks, UX, accessibility | C4 query key bug, I6 UX gap, I7 a11y, I10 Link vs a |
| otel-tracing-reviewer | Spans, counters, context | I4 counter placement, I5 orphaned spans, S7 error status |
| database-reviewer | EF Core, pgvector, indexes | C2 confirmed, I2 cascade confirmed, I3 FKs, I9 index |
