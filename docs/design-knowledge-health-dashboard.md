# Design: Knowledge Health Dashboard

Generated: 2026-02-24
Status: Draft

---

## Problem Statement

### Goal

Surface the structural health of the knowledge base as an actionable, weekly-review-friendly dashboard. Replace the feeling of "notes disappear into the system" with a concrete map: which clusters are rich, which notes are recent-and-isolated, and where generation capacity is untapped.

### Constraints

- Must not recompute embeddings on demand (expensive, async)
- Must not add new background jobs for MVP — query existing data only
- The existing `health.ts` / `/health` endpoint is the ASP.NET infrastructure health check — KB health must use a distinct route (`/api/kb-health`)
- One-click wikilink insertion must not corrupt Tiptap HTML — preview gate required
- Note content is HTML (Tiptap); wikilinks are `[[NoteTitle]]` inline text

### Success Criteria

- [ ] Dashboard loads in < 2 seconds for a 500-note KB
- [ ] All four sections visible on a single page (no tab switching)
- [ ] Orphan list limited to last 30 days to keep it actionable
- [ ] Accepting a connection suggestion inserts `[[Title]]` and marks note Stale for re-embed
- [ ] Full-stack in a single commit: backend + API client + UI

---

## Context

### Current State

| Capability | Location | Notes |
|---|---|---|
| Graph edge computation | `GraphService.BuildGraphAsync` | Wikilinks + semantic (pgvector). Returns `GraphData` with nodes + edges. |
| Embedding status per note | `Note.EmbedStatus` | Enum: Pending / Processing / Completed / Failed / Stale |
| Used-as-seed tracking | `UsedSeedNote` table | `NoteId` PK, `UsedAt` timestamp |
| Semantic similarity | `SearchService.FindRelatedAsync` | Uses pgvector LATERAL join — existing index |
| Note update | `NoteService.UpdateAsync` | Updates content + sets EmbedStatus = Stale |
| Infrastructure health check | `GET /health` → `health.ts` | ASP.NET health check, **separate from KB health** |

### Key Architectural Facts

1. `GraphService.BuildGraphAsync` loads all note content (for wiki-link regex) and runs a pgvector LATERAL join. It is **not cached** — each call is a full recompute.
2. Edge computation includes: wiki-link parsing (O(n) regex) + pgvector nearest-neighbor SQL (indexed, fast).
3. The "related notes" pattern in `SearchService.FindRelatedAsync` already retrieves top-k semantically similar notes for a given note ID using the existing embedding.
4. Note content is Tiptap HTML. A safe wikilink append is `<p>[[Title]]</p>` added before the closing tag of the outermost container.

### Related Decisions

- ADR-001: Backend architecture (ASP.NET Core, EF Core, service layer pattern)
- ADR-002: PostgreSQL native search + pgvector for semantic similarity
- ADR-006: OpenTelemetry observability (new endpoints should add spans)

---

## Alternatives Considered

---

### Option A: Reuse `GraphService.BuildGraphAsync` directly

**Summary**: Call `BuildGraphAsync` from a new `KbHealthController`, compute all derived metrics (orphans, clusters, averages) in-process from the returned `GraphData`, and serve everything from a single endpoint.

**Architecture**:

```
GET /api/kb-health/overview
        │
        ▼
KbHealthController
        │
        ├─► IGraphService.BuildGraphAsync(threshold=0.7)
        │       └─ Returns GraphData { Nodes[], Edges[] }
        │
        ├─► DB query: EmbedStatus counts (separate EF query)
        │
        ├─► DB query: UsedSeedNote IDs
        │
        └─ In-process: compute orphans, clusters, averages
                  │
                  └─ Return KbHealthOverview DTO
```

**In-process computation**:
- **Orphans** = nodes with `EdgeCount == 0` AND `CreatedAt > 30 days ago` (need CreatedAt — requires extending GraphNode)
- **Clusters** = union-find on edge list → top 5 components by size
- **Averages** = sum(EdgeCount) / node count
- **Never-used seeds** = nodes not in `UsedSeedNote` set, with `EmbedStatus == Completed`

**Pros**:
- Reuses existing service, no new SQL needed for graph data
- Single call to `BuildGraphAsync` computes all edge-derived metrics
- No new tables, no migrations
- Consistent with existing pattern (controller → service interface)

**Cons**:
- `BuildGraphAsync` loads ALL note content (for wiki-link regex) — wasteful if we only need edge structure for health metrics. At 500 notes ~50 KB of content loaded unnecessarily.
- `GraphNode` doesn't carry `CreatedAt` — need to extend the DTO or do a second query to filter recent orphans
- No caching — each health page load triggers a full graph recompute including the pgvector LATERAL join
- `GraphService` was designed for visualization, not analytics — this creates implicit coupling between two different concerns

**Coupling Analysis**:

| Component | Afferent (Ca) | Efferent (Ce) | Instability (Ce/Ca+Ce) |
|---|---|---|---|
| `KbHealthController` | 0 | 2 (IGraphService, IKbHealthService) | High |
| `GraphService` | 2 (GraphController + KbHealthController) | 1 (ZettelDbContext) | Medium |

New dependency: `KbHealthController → IGraphService`. GraphService gains a second consumer.

**Failure Modes**:

| Mode | Severity | Occurrence | Detection | RPN |
|---|---|---|---|---|
| pgvector LATERAL join times out on large KB | High | Low | 504 on health page | 4 |
| GraphNode missing CreatedAt forces 2nd query with N+1 risk | Medium | High | Slow page load | 6 |
| Content load in BuildGraphAsync OOMs under large KB | Medium | Low | Server OOM | 3 |

**Evolvability**:
- Adding trend history: Hard — requires a snapshot table, graph service not designed for it
- Adding cluster naming by AI: Medium — post-process the cluster list
- Adding tag-based breakdown: Hard — GraphService has no tag awareness

**Effort**: M (2–3 days)

---

### Option B: New `IKbHealthService` with purpose-built queries

**Summary**: Extract all KB health computation into a new `IKbHealthService` with queries written specifically for health analytics. The service avoids loading note content for graph computation — it only needs edge structure and metadata.

**Architecture**:

```
GET /api/kb-health/overview
        │
        ▼
KbHealthController
        │
        └─► IKbHealthService.GetOverviewAsync()
                │
                ├─ SQL: note counts + embed status histogram
                │        (single GROUP BY query)
                │
                ├─ SQL: wikilink edges (regex on content, same as GraphService)
                │        SELECT Id, Content FROM Notes WHERE Status='Permanent'
                │
                ├─ SQL: semantic edges (pgvector LATERAL, threshold 0.6)
                │        (same as GraphService but different threshold)
                │
                ├─ SQL: UsedSeedNote IDs
                │
                └─ In-process:
                        union-find → clusters
                        orphan detection (edge count = 0, CreatedAt > 30d)
                        averages, never-used seeds

GET /api/kb-health/orphan/{id}/suggestions
        │
        └─► IKbHealthService.GetConnectionSuggestionsAsync(noteId)
                │
                └─ pgvector LATERAL: top 5 similar notes (threshold 0.6)
                         (reuses same SQL pattern as SearchService.FindRelatedAsync)

POST /api/kb-health/orphan/{id}/link
        │
        └─► IKbHealthService.InsertWikilinkAsync(orphanId, targetId)
                │
                ├─ Load both notes by ID
                ├─ Append <p>[[TargetTitle]]</p> to orphan content
                ├─ Note.UpdatedAt = now
                ├─ Note.EmbedStatus = Stale
                └─ SaveChangesAsync
```

**Pros**:
- Single-concern service — KB health analytics are fully encapsulated
- Queries are optimized for health (no content load for non-wikilink notes, or load minimally)
- `GraphService` remains coupled only to graph visualization — clean separation
- `CreatedAt` is naturally available in the health query; no DTO extension needed
- Easy to add caching at the service boundary without touching `GraphService`
- `GetConnectionSuggestionsAsync` can delegate to `SearchService.FindRelatedAsync` — reuse without duplication
- New endpoints map cleanly to testable service methods

**Cons**:
- Duplicates the pgvector LATERAL join SQL that `GraphService` already has — two copies to maintain
- Duplicates the wiki-link regex — need to extract it to a shared utility or copy it
- More surface area: new interface, new implementation, new controller, 3 new endpoints

**Coupling Analysis**:

| Component | Afferent (Ca) | Efferent (Ce) | Instability |
|---|---|---|---|
| `KbHealthController` | 0 | 1 (IKbHealthService) | High |
| `KbHealthService` | 1 | 3 (ZettelDbContext, ISearchService, WikiLinkParser) | Medium |
| `GraphService` | 1 | 1 (ZettelDbContext) | **Unchanged** — no new coupling |
| `ISearchService` | 2 (SearchController + KbHealthService) | — | Stable |

**Failure Modes**:

| Mode | Severity | Occurrence | Detection | RPN |
|---|---|---|---|---|
| pgvector LATERAL join slow at scale | High | Low | Slow health page | 4 |
| Wikilink append corrupts Tiptap HTML | High | Low | Visual corruption in editor | 3 |
| Orphan list too long (100+ notes) | Medium | Medium | UX degradation | 4 |
| Suggestions return stale data (note just linked) | Low | Medium | Stale suggestion shown | 2 |

**Evolvability**:
- Adding trend history: Easy — add snapshot method to `IKbHealthService`
- Adding caching: Easy — decorate service or add MemoryCache in implementation
- Adding cluster naming by AI: Easy — post-process in service method
- Adding tag breakdown: Easy — extend the overview query with tag join
- Wikilink regex shared across codebase: extract to `WikiLinkParser` static helper

**Effort**: L (3–4 days)

---

### Option C: Precomputed health snapshot (background job + snapshot table)

**Summary**: Add a background service that runs nightly (or on note change), computes KB health metrics, and persists them to a `KbHealthSnapshot` table. The dashboard reads from the snapshot instantly.

**Architecture**:

```
Background job (nightly + on-demand trigger)
        │
        └─ KbHealthSnapshotService
                ├─ Compute all metrics
                └─ Upsert KbHealthSnapshot row

GET /api/kb-health/overview
        │
        └─ SELECT * FROM KbHealthSnapshots ORDER BY CreatedAt DESC LIMIT 1

KbHealthSnapshot table:
  Id, CreatedAt, TotalNotes, EmbeddedPercent, OrphanCount,
  AvgConnections, OrphanNoteIds (jsonb), ClusterJson (jsonb),
  UnusedSeedIds (jsonb)
```

**Pros**:
- Sub-millisecond reads — dashboard is instant regardless of KB size
- Enables trend history trivially (keep all snapshots)
- Background job is the right place for expensive graph computation

**Cons**:
- Requires migration (new table)
- Adds a background job — more moving parts
- Dashboard data can be up to 24 hours stale
- The link-insertion flow still needs real-time execution
- Premature — the graph computation is fast enough for MVP; optimise when it's measured as slow
- Over-engineers the MVP; adds operational complexity before proving value

**Coupling Analysis**:

New `KbHealthSnapshotService` → `ZettelDbContext`, `IGraphService`, `ISearchService`. Background worker registration adds to DI surface. New migration required.

**Failure Modes**:

| Mode | Severity | Occurrence | Detection | RPN |
|---|---|---|---|---|
| Background job fails silently | High | Low | Stale data shown forever | 6 |
| Snapshot schema evolves, old snapshots invalid | Medium | Low | Deserialization failure | 4 |

**Evolvability**:
- Trend history: Trivial — keep all rows
- On-demand refresh: Medium — need a trigger mechanism
- Real-time orphan update after link insertion: Hard — snapshot is stale until next run

**Effort**: XL (5–7 days)

---

## Comparison Matrix

| Criterion | Option A: Reuse GraphService | Option B: Dedicated service | Option C: Snapshot table |
|---|---|---|---|
| Implementation complexity | 🟡 Medium | 🟡 Medium | 🔴 High |
| Query efficiency | 🟡 Medium (loads note content) | 🟢 Good (metadata-only where possible) | 🟢 Instant reads |
| Coupling to GraphService | 🔴 Tight (new consumer) | 🟢 None — clean separation | 🟡 Medium (snapshot job uses it) |
| Evolvability | 🔴 Low (GraphService not designed for analytics) | 🟢 High (dedicated service, easy to extend) | 🟡 Medium (complex job management) |
| Time to implement | 🟢 Fastest | 🟡 Medium | 🔴 Slowest |
| Failure resilience | 🟡 Medium | 🟢 Good | 🟡 Medium (stale data risk) |
| No new migrations needed | 🟢 Yes | 🟢 Yes | 🔴 No |

---

## Recommendation

**Recommended Option: B — Dedicated `IKbHealthService`**

**Rationale**:

Option A is tempting but creates the wrong coupling: `GraphService` was designed to produce visualization-ready graph data, not analytics. Stretching it to serve both purposes would require adding `CreatedAt` to `GraphNode`, running health computations inside a visualization-oriented service, and making health load time dependent on graph render performance.

Option C is overkill for MVP. The graph computation at 500 notes completes in well under 2 seconds (pgvector LATERAL with a limit-5 per node is O(n log n) with the index). There's no measured performance problem to solve yet.

Option B keeps concerns clean: `GraphService` owns visualization, `KbHealthService` owns analytics. The wiki-link regex duplication is addressed by extracting it to a `WikiLinkParser` static class (which benefits `GraphService` too). The pgvector SQL is an intentional copy at a lower threshold (0.6 vs 0.8) — the different semantics justify separate queries.

**Tradeoffs Accepted**:
- **Duplicated pgvector SQL**: Acceptable. The similarity threshold differs (0.6 for suggestions vs 0.8 for graph edges) and the query shape differs (top-5 per note vs top-5 for a single note). These are genuinely different operations.
- **No caching in MVP**: The pgvector LATERAL join is fast at current scale. Add `IMemoryCache` to `KbHealthService` later if measured as slow.

**Risks to Monitor**:
- If KB grows beyond ~2,000 notes, the wiki-link content load in `BuildHealthOverviewAsync` (loading `Content` for regex parsing) may become slow. Mitigation: pre-extract wikilinks to a separate `NoteLinks` table (future).
- Wikilink append must always be `<p>[[Title]]</p>` — never mid-content injection. Validate in service + test.

---

## Detailed Implementation Plan

### New Files

| File | Purpose |
|---|---|
| `src/ZettelWeb/Services/IKbHealthService.cs` | Interface |
| `src/ZettelWeb/Services/KbHealthService.cs` | Implementation |
| `src/ZettelWeb/Controllers/KbHealthController.cs` | 3 endpoints |
| `src/ZettelWeb/Models/KbHealthModels.cs` | DTOs (overview, suggestions, link request) |
| `src/ZettelWeb/Services/WikiLinkParser.cs` | Extracted shared regex (also used by GraphService) |
| `src/zettel-web-ui/src/api/kb-health.ts` | Frontend API client |
| `src/zettel-web-ui/src/pages/kb-health.tsx` | Dashboard page |

### Modified Files

| File | Change |
|---|---|
| `src/ZettelWeb/Services/GraphService.cs` | Replace inline regex with `WikiLinkParser.ExtractLinks()` |
| `src/ZettelWeb/Program.cs` | Register `IKbHealthService` → `KbHealthService` |
| `src/zettel-web-ui/src/app.tsx` | Add `/kb-health` route (lazy loaded) |
| `src/zettel-web-ui/src/api/types.ts` | Add KB health DTOs |
| `docs/API_REFERENCE.md` | Document 3 new endpoints |

### No migrations needed. No new tables.

---

### Phase 1: Backend (models + service + controller)

**Step 1: `WikiLinkParser.cs`**

Extract `[GeneratedRegex(@"\[\[([^\]]+)\]\]")]` from `GraphService` into a shared static class. Update `GraphService` to use it.

```csharp
// src/ZettelWeb/Services/WikiLinkParser.cs
public static partial class WikiLinkParser
{
    public static IEnumerable<string> ExtractLinkedTitles(string htmlContent)
        => WikiLinkRegex().Matches(htmlContent).Select(m => m.Groups[1].Value);

    [GeneratedRegex(@"\[\[([^\]]+)\]\]")]
    private static partial Regex WikiLinkRegex();
}
```

**Step 2: `KbHealthModels.cs`**

```csharp
// src/ZettelWeb/Models/KbHealthModels.cs

public record KbHealthScorecard(
    int TotalNotes,
    int EmbeddedPercent,
    int OrphanCount,
    double AverageConnections);

public record UnconnectedNote(
    string Id,
    string Title,
    DateTime CreatedAt,
    int SuggestionCount);   // count of suggestions, loaded lazily per-note

public record ClusterSummary(
    string HubNoteId,
    string HubTitle,
    int NoteCount);

public record UnusedSeedNote(
    string Id,
    string Title,
    int ConnectionCount);

public record KbHealthOverview(
    KbHealthScorecard Scorecard,
    IReadOnlyList<UnconnectedNote> NewAndUnconnected,
    IReadOnlyList<ClusterSummary> RichestClusters,
    IReadOnlyList<UnusedSeedNote> NeverUsedAsSeeds);

public record ConnectionSuggestion(
    string NoteId,
    string Title,
    double Similarity);

public record AddLinkRequest(string TargetNoteId);
```

**Step 3: `IKbHealthService.cs`**

```csharp
public interface IKbHealthService
{
    Task<KbHealthOverview> GetOverviewAsync();
    Task<IReadOnlyList<ConnectionSuggestion>> GetConnectionSuggestionsAsync(string noteId, int limit = 5);
    Task<Note?> InsertWikilinkAsync(string orphanNoteId, string targetNoteId);
}
```

**Step 4: `KbHealthService.cs`** — key implementation decisions:

*Overview computation*:
```
1. Load permanent notes: SELECT Id, Title, CreatedAt, EmbedStatus, Content FROM Notes
   WHERE Status = 'Permanent'
   (Content needed for wikilink parsing — unavoidable for MVP)

2. Build edge map (in-process):
   - Wiki-link edges via WikiLinkParser.ExtractLinkedTitles(content)
   - Semantic edges: pgvector LATERAL JOIN at threshold 0.6, top-5 per note
   - Build Set<string> of connected note IDs per note (adjacency list)

3. Union-Find for clusters:
   - Initialize each note as its own component
   - Union nodes connected by any edge
   - Find top-5 components by size
   - For each component: hub = node with max edge count; label = hub.Title

4. Orphans = notes with edge count == 0 AND CreatedAt > DateTime.UtcNow.AddDays(-30)
   SuggestionCount for each orphan = COUNT of embedded notes in DB (can be 5 as constant,
   or pre-queried). For MVP: set to 0 and let the side panel load lazily.
   UPDATE: Set SuggestionCount to 5 if note has embedding, 0 if not — single field check.

5. Embed stats = GROUP BY EmbedStatus in memory from loaded notes.

6. Never-used seeds = notes not in UsedSeedNote, with EmbedStatus=Completed,
   sorted by edge count descending. Load UsedSeedNote IDs in a single query.
```

*Wikilink insertion*:
```
1. Load orphanNote and targetNote by ID
2. Validate targetNote exists
3. Build wikilink: $"<p>[[{targetNote.Title}]]</p>"
4. Append to orphanNote.Content
5. orphanNote.UpdatedAt = DateTime.UtcNow
6. orphanNote.EmbedStatus = EmbedStatus.Stale
7. SaveChangesAsync
8. Return updated orphanNote
```

*Connection suggestions*:
```
Delegate to existing pgvector query pattern:

SELECT n2."Id", n2."Title",
       (1 - (n1."Embedding"::vector <=> n2."Embedding"::vector))::float8 AS "Similarity"
FROM "Notes" n1
CROSS JOIN LATERAL (
    SELECT "Id", "Title", "Embedding"
    FROM "Notes" n2
    WHERE n2."Id" != n1."Id"
      AND n2."Embedding" IS NOT NULL
      AND n2."Status" = 'Permanent'
    ORDER BY n1."Embedding"::vector <=> n2."Embedding"::vector
    LIMIT {limit}
) n2
WHERE n1."Id" = {noteId}
  AND n1."Embedding" IS NOT NULL
  AND (1 - (n1."Embedding"::vector <=> n2."Embedding"::vector)) > 0.6
```

**Step 5: `KbHealthController.cs`**

```
[ApiController]
[Route("api/kb-health")]

GET  /api/kb-health/overview         → GetOverviewAsync()
GET  /api/kb-health/orphan/{id}/suggestions → GetConnectionSuggestionsAsync(id)
POST /api/kb-health/orphan/{id}/link → InsertWikilinkAsync(id, body.TargetNoteId)
```

---

### Phase 2: Frontend

**`src/zettel-web-ui/src/api/types.ts` additions**:

```typescript
export interface KbHealthScorecard {
  totalNotes: number
  embeddedPercent: number
  orphanCount: number
  averageConnections: number
}

export interface UnconnectedNote {
  id: string
  title: string
  createdAt: string
  suggestionCount: number
}

export interface ClusterSummary {
  hubNoteId: string
  hubTitle: string
  noteCount: number
}

export interface UnusedSeedNote {
  id: string
  title: string
  connectionCount: number
}

export interface KbHealthOverview {
  scorecard: KbHealthScorecard
  newAndUnconnected: UnconnectedNote[]
  richestClusters: ClusterSummary[]
  neverUsedAsSeeds: UnusedSeedNote[]
}

export interface ConnectionSuggestion {
  noteId: string
  title: string
  similarity: number
}
```

**`src/zettel-web-ui/src/api/kb-health.ts`**:

```typescript
import { get, post } from './client'
import type { KbHealthOverview, ConnectionSuggestion, Note } from './types'

export function getKbHealthOverview(): Promise<KbHealthOverview> {
  return get<KbHealthOverview>('/api/kb-health/overview')
}

export function getConnectionSuggestions(noteId: string): Promise<ConnectionSuggestion[]> {
  return get<ConnectionSuggestion[]>(`/api/kb-health/orphan/${encodeURIComponent(noteId)}/suggestions`)
}

export function addLink(orphanId: string, targetNoteId: string): Promise<Note> {
  return post<Note>(`/api/kb-health/orphan/${encodeURIComponent(orphanId)}/link`, { targetNoteId })
}
```

**`src/zettel-web-ui/src/pages/kb-health.tsx`** — page structure:

```
KbHealthPage
├─ useQuery(['kb-health']) → getKbHealthOverview()
├─ ScoreCard section (4 stat tiles)
├─ NewAndUnconnectedSection
│   └─ OrphanRow (per note)
│       └─ onClick → open SuggestionPanel (sheet/side panel)
│           └─ useQuery(['kb-health-suggestions', noteId])
│               └─ SuggestionItem with "Add Link" button
│                   └─ onClick → open LinkPreviewDialog
│                       └─ useMutation → addLink()
│                           → on success: toast + invalidate ['kb-health']
├─ RichestClustersSection (top 5 list)
│   └─ each → link to note: /notes/{hubNoteId}
└─ NeverUsedSeedsSection
    └─ each → link to note, future: "Generate from this"
```

**React Query cache keys**:

| Key | Invalidated by |
|---|---|
| `['kb-health']` | `addLink` mutation success |
| `['kb-health-suggestions', noteId]` | (not invalidated — stale after link added, panel closes) |

**Route addition in `app.tsx`**:

```typescript
const KbHealthPage = lazy(() =>
  import('./pages/kb-health').then((m) => ({ default: m.KbHealthPage }))
)

// In router:
{ path: '/kb-health', element: <Suspense fallback={<LazyFallback />}><KbHealthPage /></Suspense> }
```

---

### Phase 3: Polish & registration

- Register `IKbHealthService` → `KbHealthService` as scoped in `Program.cs`
- Add nav link to `/kb-health` in `AppShell`
- Add OTel span in `GetOverviewAsync` (follow ADR-006 pattern)
- Update `docs/API_REFERENCE.md`

---

## Open Questions

- [ ] Should `SuggestionCount` on `UnconnectedNote` be pre-computed (requires counting embedding-eligible notes) or hardcoded as a constant for MVP? **Proposed**: hardcode as 5 if `EmbedStatus == Completed`, 0 otherwise — single field check, no extra query.
- [ ] Should accepting a link also remove the note from the local React Query cache immediately (optimistic update)? **Proposed**: no optimistic update for MVP — invalidate and refetch; simpler and correctness-first.
- [ ] What similarity threshold for orphan suggestions? **Proposed**: 0.6 (lower than graph's 0.8 to surface more options for isolated notes with limited connections).
- [ ] Should "Generate from this note" on the Never-Used-Seeds list be implemented in this iteration? **Proposed**: render the note title as a link to `/notes/{id}` only for MVP; generation shortcut is a future enhancement.
