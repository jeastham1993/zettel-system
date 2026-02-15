# Design: Zettel-Web Backend Architecture

Generated: 2026-02-13
Status: Draft
Source Spec: [docs/specs/2026-02-13-zettel-web.md](specs/2026-02-13-zettel-web.md)

## Problem Statement

### Goal

Define the internal architecture for the Zettel-Web .NET backend: how the API,
business logic, data access, embedding pipeline, and search are structured. The
spec defines the external architecture (React SPA + ASP.NET Core API + PostgreSQL
+ pgvector + OpenAI). This design addresses how the backend code is organised
internally.

### Constraints

- Single developer building and maintaining the system
- Target: daily-driver status in 6 weeks
- Domain is thin (notes are documents with metadata, no complex invariants)
- The embedding pipeline must be durable (notes without embeddings break the
  core feature)
- Must support swapping OpenAI for Ollama without rewriting
- Must support v2 features (related notes, graph viz, daily digest) without
  re-architecture

### Success Criteria

- [ ] A new CRUD endpoint can be added in < 30 minutes
- [ ] Swapping the embedding provider requires only a new class + DI config
- [ ] Notes never silently lose their embedding state
- [ ] Search always returns results, even if the embedding API is down
- [ ] The backend fits in a single .NET project with < 35 files for v1

## Context

### Current State

Greenfield project. No code exists yet.

### Related Decisions

- [ADR-001](adr/ADR-001-backend-architecture.md): Backend architecture pattern

## Alternatives Considered

### Option A: Simple Layered

**Summary**: Controllers handle HTTP, Services contain business logic,
Repositories wrap EF Core. Background embedding via IHostedService + Channel<T>.
Single project.

**Architecture**:

```
Controllers/
  NotesController.cs          --> NoteService
  SearchController.cs         --> SearchService
  ImportExportController.cs   --> ImportExportService

Services/
  NoteService.cs              --> ZettelDbContext, IEmbeddingQueue
  SearchService.cs            --> ZettelDbContext, IEmbeddingProvider
  EmbeddingService.cs         --> IEmbeddingProvider, ZettelDbContext
  ImportExportService.cs      --> ZettelDbContext, IEmbeddingQueue
  BackgroundEmbeddingService.cs  (IHostedService, polls + Channel<T>)

Providers/
  IEmbeddingProvider.cs
  OpenAIEmbeddingProvider.cs

Data/
  ZettelDbContext.cs
  Migrations/

Models/
  Note.cs, Tag.cs, SearchResult.cs
```

**Pros**:
- Lowest cognitive overhead -- every file has an obvious place
- ~15-20 files for the full v1 backend
- ASP.NET Core DI, middleware, and configuration provide all needed seams
- No third-party architecture dependencies (no MediatR, no framework-for-
  framework's-sake)
- Direct method calls are easy to debug (stack traces are short and clear)

**Cons**:
- Services can grow large if not proactively split
- No enforced architectural boundaries (discipline, not structure)
- Data layer sits in the Zone of Pain (D=1.00): concrete types everything
  depends on

**Coupling Analysis**:

| Component | Ca | Ce | I | D (distance from main sequence) |
|-----------|----|----|---|----|
| Controllers | 0 | 4 | 1.00 | 0.00 |
| Services | 5 | 5 | 0.50 | 0.36 |
| Repositories/Data | 4 | 0 | 0.00 | 1.00 |

New dependencies introduced: None beyond ASP.NET Core + EF Core + Npgsql
Coupling impact: **Low**. Services layer is the hub but at manageable scale.

**Failure Modes**:

| Mode | Severity | Occurrence | Detection | RPN |
|------|----------|------------|-----------|-----|
| Service grows too large | 3 | 5 | 2 | 30 |
| Business logic leaks into controller | 2 | 3 | 2 | 12 |

**Evolvability Assessment**:
- Swap embedding provider: **Easy** (new class, DI config)
- Add AI features: **Easy** (new service, new endpoints)
- Related notes sidebar: **Easy** (new service method, new endpoint)
- Knowledge graph: **Easy** (new service, query complexity is the challenge)
- Scale to 10K+: **Easy** (database concern, not architecture)
- Add auth: **Easy** (ASP.NET middleware, orthogonal to architecture)
- Change search ranking: **Easy** (SearchService change)

**Effort Estimate**: S (smallest)

---

### Option B: Vertical Slice (MediatR handlers per feature)

**Summary**: Each feature (CreateNote, SearchNotes, ImportNotes) is a
self-contained handler. Shared infrastructure injected. No service layer.

**Architecture**:

```
Features/
  Notes/
    CreateNote/
      CreateNoteCommand.cs
      CreateNoteHandler.cs
      CreateNoteValidator.cs
    GetNote/
      GetNoteQuery.cs
      GetNoteHandler.cs
    UpdateNote/...
    DeleteNote/...
  Search/
    SearchNotes/
      SearchNotesQuery.cs
      SearchNotesHandler.cs
  Import/
    ImportNotes/
      ImportNotesCommand.cs
      ImportNotesHandler.cs

Shared/
  ZettelDbContext.cs
  IEmbeddingProvider.cs
  OpenAIEmbeddingProvider.cs

Endpoints/
  NoteEndpoints.cs
  SearchEndpoints.cs
```

**Pros**:
- Strong feature isolation -- change one feature without touching others
- Adding a new feature is clean (new folder, new handler)
- Good if features are truly independent

**Cons**:
- Zettel-Web's features are NOT independent: embedding is shared across
  create, update, import, and re-embed. Search ranking is shared across
  search, related notes, and digest. You either duplicate logic or extract
  shared services -- recreating Option A with extra indirection.
- MediatR adds a dependency and obscures the call graph. Debugging traces
  through IMediator.Send() instead of direct method calls.
- Shared Infrastructure sits in Zone of Pain (D=0.90)
- Hidden behavioral coupling between slices via MediatR notifications

**Coupling Analysis**:

| Component | Ca | Ce | I | D |
|-----------|----|----|---|---|
| Endpoints | 0 | 7 | 1.00 | 0.00 |
| Each Slice | 1 | 2-3 | 0.67-0.75 | 0.25-0.33 |
| Shared Infrastructure | 6 | 0 | 0.00 | 0.90 |

New dependencies introduced: MediatR
Coupling impact: **Medium**. Shared Infrastructure is a high-Ca hub.

**Failure Modes**:

| Mode | Severity | Occurrence | Detection | RPN |
|------|----------|------------|-----------|-----|
| Duplicated logic across handlers | 4 | 6 | 5 | 120 |
| Hidden behavioral coupling via notifications | 5 | 4 | 7 | 140 |

**Evolvability Assessment**:
- Swap embedding provider: **Easy**
- Add AI features: **Easy** (new handler)
- Related notes sidebar: **Easy** (new handler)
- Cross-cutting changes: **Medium** (touches multiple handlers)
- Change search ranking: **Easy** (single handler)

**Effort Estimate**: M (moderate -- MediatR setup, handler boilerplate)

---

### Option C: Clean Architecture (Domain / Application / Infrastructure / API)

**Summary**: Four-project solution with strict dependency inversion. Domain
entities with no dependencies, Application use cases, Infrastructure for EF
Core and OpenAI, API as thin adapter.

**Architecture**:

```
ZettelWeb.Domain/
  Note.cs, Tag.cs, NoteId.cs (value object)

ZettelWeb.Application/
  INoteRepository.cs, ISearchService.cs, IEmbeddingProvider.cs
  CreateNoteCommand.cs, CreateNoteHandler.cs
  SearchNotesQuery.cs, SearchNotesHandler.cs
  ...

ZettelWeb.Infrastructure/
  ZettelDbContext.cs, NoteRepository.cs
  OpenAIEmbeddingProvider.cs, PgVectorSearchService.cs

ZettelWeb.API/
  NotesController.cs, SearchController.cs
```

**Pros**:
- Best coupling profile: Application layer at D=0.12 (closest to main
  sequence). Infrastructure at I=1.00 (maximally replaceable).
- Strict dependency inversion enforced via project references
- Domain and Application layers testable in complete isolation

**Cons**:
- **The domain is thin.** A Note is: ID, title, content, tags, embedding,
  timestamps. No complex invariants, no aggregate boundaries, no domain
  events cascading through business rules. The Domain layer becomes anemic
  models with no behaviour -- a project for 3-5 files.
- **Mapping tax.** Every request: API DTO -> Application DTO -> Domain Entity
  -> Repository -> DB Entity. For CRUD where API shape mirrors data shape,
  this is pure overhead.
- **35-50 files** for the same functionality Option A delivers in 15-20.
  Adding a query parameter to search means changing: Controller DTO,
  Application DTO, Use Case, Repository Interface, Repository Implementation
  (5 files for 1 parameter).
- **Architecture astronautics risk.** Time maintaining purity directly
  competes with building features. Could extend the 6-week timeline by
  30-50%.

**Coupling Analysis**:

| Component | Ca | Ce | I | D |
|-----------|----|----|---|---|
| API | 0 | 1 | 1.00 | 0.00 |
| Application | 2 | 1 | 0.33 | 0.12 |
| Domain | 2 | 0 | 0.00 | 0.80 |
| Infrastructure | 0 | 2 | 1.00 | 0.00 |

New dependencies introduced: None (but 4 projects to manage)
Coupling impact: **Low** (best theoretical profile)

**Failure Modes**:

| Mode | Severity | Occurrence | Detection | RPN |
|------|----------|------------|-----------|-----|
| Architecture astronautics (over-engineering) | 7 | 7 | 3 | 147 |
| Anemic domain (ceremony with no benefit) | 4 | 8 | 2 | 64 |
| Mapping errors between layers | 3 | 5 | 3 | 45 |

**Evolvability Assessment**:
- Swap embedding provider: **Easy**
- Add AI features: **Medium** (new use cases, interfaces, implementations,
  controllers -- thin domain logic does not benefit from isolation)
- Related notes sidebar: **Easy-Medium**
- Change search ranking: **Easy-Medium** (tension: is ranking domain or
  infrastructure?)
- All changes require 2-4x the files of Option A

**Effort Estimate**: L (largest -- 4 projects, mapping layers, ceremony)

---

## Comparison Matrix

| Criterion | Option A | Option B | Option C |
|-----------|----------|----------|----------|
| Complexity | Low | Medium | High |
| Evolvability Score | 4/5 | 3/5 | 2/5 |
| Time to First Working Version | 1-2 weeks | 2 weeks | 2-3 weeks |
| Coupling Impact | Low | Medium | Low (best metrics) |
| Failure Resilience | Medium | Medium | Medium |
| Files for v1 | ~20 | ~30 | ~40-50 |
| Proportionality (effort vs change size) | Passes all 9 changes | Passes 7/9 | Fails 3/9 |
| Architecture Astronautics Risk | Low | Medium | **High** |
| Debugging Ease | Best | Worst (MediatR) | Medium |

## Recommendation

**Recommended Option**: Option A (Simple Layered) with two guardrails

### Rationale

The coupling analysis shows Option C has the best theoretical metrics
(Application layer D=0.12 vs Services D=0.36). However, the evolvability
assessment reveals that theoretical coupling metrics do not tell the full story
for this project:

1. **The domain is thin.** Clean Architecture optimises for domain complexity
   isolation. Zettel-Web's domain is deliberately simple -- notes are documents
   with metadata. The Domain layer becomes a pass-through.

2. **Proportionality matters more than purity.** Option A passes the
   proportionality test for all 9 anticipated changes (effort matches change
   size). Option C fails for 3 of them.

3. **The real architectural risk is the embedding pipeline, not the layer
   structure.** The failure mode analysis found the two highest-RPN items
   (both 240) relate to silent loss of embedding state. The outbox pattern
   (embed_status column) addresses this regardless of which architecture
   option is chosen.

4. **Over-engineering is the dominant risk.** For a single-developer personal
   tool targeting daily-driver in 6 weeks, the over-engineering tax (paid on
   every feature, for the project lifetime) far outweighs the under-
   engineering tax (paid occasionally, quick to fix).

### Two Guardrails

These provide 90% of Clean Architecture's benefit at 20% of the cost:

1. **No business logic in controllers.** Controllers: validate input, call a
   service, return the result. Services are the testable, reusable unit.

2. **All external dependencies behind interfaces.** `IEmbeddingProvider` is
   already in the spec. Apply the same pattern to any future external
   dependency (`ICompletionProvider`, etc.). Internal services do NOT need
   interfaces -- there is only ever one implementation.

### Tradeoffs Accepted

- **No enforced architectural boundaries**: Relies on developer discipline
  rather than project structure. Acceptable for a solo developer.
- **Services layer can grow**: Mitigated by splitting by domain concept
  (NoteService, SearchService, EmbeddingService), not by adding layers.
- **Data layer in Zone of Pain (D=1.00)**: Entity changes ripple through
  all layers. Acceptable because schema changes are infrequent and the
  layer count (3) keeps the ripple manageable.

### Risks to Monitor

- **Service file size**: If any service exceeds 300 lines, split it.
  Cost: 30 minutes.
- **Missing abstraction**: If a new external provider is needed, add an
  interface. Cost: 15 minutes.
- **Controller doing too much**: Extract to service. Cost: 20 minutes.

---

## Critical Design Decision: Embedding Pipeline

The failure mode analysis identified the embedding pipeline as the highest-risk
component. The two highest-RPN failure modes (both 240) are:

- **FM-4**: Notes exist in the database without embeddings (index corruption
  or missed embedding). RPN 240. Detection score 8 (silent failure).
- **FM-7**: Note save succeeds but embedding fails (partial state). RPN 240.
  Detection score 8 (user sees "saved" but note is invisible to search).

### Recommended Strategy: Outbox Pattern + Channel<T>

Use an `embed_status` column on the notes table (outbox pattern) as the
durable source of truth, with `Channel<T>` as a responsiveness optimisation.

**Why not Channel<T> alone?**
In-memory queue is lost on process restart. Notes that were queued for
embedding but not yet processed disappear. There is no record of what needs
embedding.

**Why not a separate embed_queue table?**
Workable, but the outbox pattern is simpler: all note state in one place,
atomically set in the same transaction, no separate table to maintain.

**How it works**:

1. **On note save**: Same transaction sets content + `embed_status = 'pending'`.
   After commit, push note ID into `Channel<T>` for immediate processing.

2. **Background worker**: Reads `Channel<T>` for immediate notifications.
   Also polls DB every 30s for `pending`/`failed` notes (catches restarts).

3. **On success**: Set `embed_status = 'completed'`, store embedding vector,
   record `embedding_model`.

4. **On failure**: Set `embed_status = 'failed'`, record error, increment
   `retry_count`. Exponential backoff on retries.

5. **On startup**: Reset `processing` -> `pending` (recover in-flight items).
   Scan for any `pending`/`failed` notes.

6. **On model change**: `UPDATE notes SET embed_status = 'stale' WHERE
   embedding_model != @configuredModel`.

### Recommended Schema

```sql
CREATE TABLE notes (
    id                VARCHAR(14) PRIMARY KEY,  -- YYYYMMDDHHmmss
    title             TEXT NOT NULL,
    content           TEXT NOT NULL,
    created_at        TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at        TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    -- Embedding state (outbox pattern)
    embedding         vector(3072),
    embed_status      VARCHAR(20) NOT NULL DEFAULT 'pending'
                      CHECK (embed_status IN (
                          'pending', 'processing', 'completed',
                          'failed', 'stale'
                      )),
    embedding_model   VARCHAR(100),
    embed_error       TEXT,
    embed_retry_count INT NOT NULL DEFAULT 0,
    embed_updated_at  TIMESTAMPTZ,

    -- Full-text search
    search_vector     tsvector GENERATED ALWAYS AS (
                          to_tsvector('english',
                              coalesce(title, '') || ' ' ||
                              coalesce(content, ''))
                      ) STORED
);

CREATE TABLE note_tags (
    note_id   VARCHAR(14) REFERENCES notes(id) ON DELETE CASCADE,
    tag       TEXT NOT NULL,
    PRIMARY KEY (note_id, tag)
);

CREATE TABLE note_links (
    source_note_id  VARCHAR(14) REFERENCES notes(id) ON DELETE CASCADE,
    target_note_id  VARCHAR(14) REFERENCES notes(id) ON DELETE CASCADE,
    PRIMARY KEY (source_note_id, target_note_id)
);

-- Indexes
CREATE INDEX idx_notes_embed_status
    ON notes (embed_status)
    WHERE embed_status != 'completed';

CREATE INDEX idx_notes_search_vector
    ON notes USING gin (search_vector);

CREATE INDEX idx_notes_embedding
    ON notes USING hnsw (embedding vector_cosine_ops);

CREATE INDEX idx_note_tags_tag
    ON note_tags (tag);
```

### RPN Impact of Outbox Pattern

| Failure Mode | Before | After | Reduction |
|-------------|--------|-------|-----------|
| FM-7 (partial state) | 240 | 60 | -75% |
| FM-4 (missing embeddings) | 240 | 90 | -62% |
| FM-1 (API down on save) | 96 | 48 | -50% |
| FM-6 (bulk import) | 140 | 40 | -71% |

---

## Full Failure Mode Summary

| # | Failure Mode | S | O | D | RPN | Priority |
|---|-------------|---|---|---|-----|----------|
| FM-4 | Missing embeddings | 6 | 5 | 8 | **240** | CRITICAL |
| FM-7 | Note saves, embedding fails | 5 | 6 | 8 | **240** | CRITICAL |
| FM-8 | Irrelevant search results | 8 | 3 | 7 | **168** | HIGH |
| FM-6 | Bulk import rate limiting | 5 | 7 | 4 | **140** | HIGH |
| FM-5 | Model change invalidates embeddings | 8 | 3 | 5 | **120** | HIGH |
| FM-1 | OpenAI down during save | 4 | 4 | 6 | **96** | MEDIUM |
| FM-2 | OpenAI down during search | 7 | 4 | 2 | **56** | MEDIUM |
| FM-3 | PostgreSQL connection failure | 9 | 3 | 2 | **54** | MEDIUM |
| FM-10 | Database migration failure | 8 | 3 | 2 | **48** | MEDIUM |
| FM-9 | Container crash during editing | 7 | 2 | 3 | **42** | LOW |

### Key Mitigations (Priority Order)

1. **Outbox pattern for embedding state** -- addresses FM-4, FM-7, FM-1, FM-6
2. **Hybrid search fallback** -- when embedding API is unavailable for query
   embedding, return full-text results only with UI indicator (addresses FM-2)
3. **Store embedding_model per note** -- detect stale embeddings on model
   change, automate re-embedding (addresses FM-5)
4. **Batch embedding with rate limiting** -- process bulk imports in batches
   of 20-50 with delays (addresses FM-6)
5. **Client-side autosave** -- Tiptap saves to localStorage every 5-10s,
   restore on load (addresses FM-9)
6. **Pre-migration backup** -- pg_dump before EF Core migrations
   (addresses FM-10)

---

## Recommended Project Structure

```
zettel-web/
  src/
    ZettelWeb/                        # Single .NET project
      Controllers/
        NotesController.cs
        SearchController.cs
        ImportExportController.cs
        TagsController.cs
      Services/
        NoteService.cs
        SearchService.cs
        EmbeddingService.cs           # Orchestrates IEmbeddingProvider
        ImportExportService.cs
        TagService.cs
      Background/
        EmbeddingBackgroundService.cs  # IHostedService + Channel<T> + polling
      Providers/
        IEmbeddingProvider.cs
        OpenAIEmbeddingProvider.cs
      Data/
        ZettelDbContext.cs
        Migrations/
      Models/
        Note.cs
        Tag.cs
        NoteLink.cs
        SearchResult.cs
        EmbedStatus.cs                # enum
      ZettelWeb.csproj
      Program.cs
      appsettings.json
      Dockerfile

  src/
    zettel-web-ui/                    # React SPA (separate)
      src/
        components/
          Editor/                     # Tiptap editor
          Search/                     # Search bar + results
          NoteList/                   # Note browser
        pages/
          NotePage.tsx
          SearchPage.tsx
          ImportPage.tsx
        api/                          # API client
        App.tsx
      package.json
      vite.config.ts
      tailwind.config.ts

  docker-compose.yml
  docs/
```

~20-25 backend files for v1. Each anticipated change maps to 1-2 files.

---

## Implementation Plan

### Phase 1: Foundation (weeks 1-2)

- [ ] Scaffold ASP.NET Core project with EF Core + Npgsql + pgvector
- [ ] Scaffold React SPA with Vite + Tailwind CSS + Tiptap
- [ ] Implement Note model with Zettelkasten timestamp IDs
- [ ] Implement notes table with outbox pattern columns (embed_status, etc.)
- [ ] Implement CRUD: NotesController + NoteService + ZettelDbContext
- [ ] Implement IEmbeddingProvider + OpenAIEmbeddingProvider
- [ ] Implement EmbeddingBackgroundService (Channel<T> + DB polling)
- [ ] Implement tag system (note_tags table, TagsController)
- [ ] Implement Markdown import endpoint
- [ ] Docker Compose: API + PostgreSQL (pgvector) + SPA

### Phase 2: Search (weeks 3-4)

- [ ] Implement semantic search (pgvector cosine similarity)
- [ ] Implement full-text search (tsvector)
- [ ] Implement hybrid search with configurable weights (appsettings.json)
- [ ] Implement search fallback (full-text only when embedding API down)
- [ ] Build search UI: search bar, results with titles/snippets/scores
- [ ] Implement bulk re-embedding endpoint

### Phase 3: Daily Driver (weeks 5-6)

- [ ] Implement Tiptap wiki-link extension (`[[` autocomplete)
- [ ] Implement Markdown export (zip download)
- [ ] Add keyboard shortcuts (Cmd+N, Cmd+K, Cmd+S)
- [ ] Add client-side autosave (localStorage)
- [ ] Add embedding status indicator in UI
- [ ] Add /health endpoint
- [ ] Polish: loading states, error handling, responsive layout

### Phase 4: Discovery (weeks 7+)

- [ ] Related notes sidebar (vector similarity, exclude linked notes)
- [ ] Knowledge graph visualisation
- [ ] Daily discovery digest on home page

## Open Questions

- [ ] Tag UX: free-form text tags or predefined list? (Recommend: free-form
      with autocomplete from existing tags)
- [ ] Tiptap extensions: confirm `@tiptap/extension-link` + custom wiki-link
      extension cover autocomplete needs
- [ ] Hybrid search weight defaults: what starting ratio of semantic vs
      keyword? (Recommend: 0.7 semantic / 0.3 keyword, tune empirically)
- [ ] Similarity threshold for search results: below what cosine similarity
      should results be excluded? (Recommend: 0.3, tune empirically)
