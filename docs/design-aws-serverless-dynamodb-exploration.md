# Design Exploration: Fully Serverless with DynamoDB + S3 Vectors

Generated: 2026-02-26
Status: Draft — Exploration (extends design-aws-serverless-deployment.md)

---

## Motivation

The recommended Option B (Lambda + Aurora Serverless v2) still requires a VPC because
Aurora must run in a VPC. This creates a meaningful Lambda cold start penalty
(~1–3 seconds for VPC-attached Lambda vs ~200–400ms for non-VPC Lambda). The question
is: could DynamoDB replace Aurora Serverless v2, eliminating the VPC entirely?

This document explores **Option D: Lambda + DynamoDB + S3 Vectors** — a fully VPC-free,
truly serverless stack. It starts from an honest audit of every query pattern in the
current codebase to determine what DynamoDB can handle natively and where gaps emerge.

---

## Access Pattern Audit

Every query against `ZettelDbContext` was catalogued to determine DynamoDB fit.
DynamoDB can only query by primary key or GSI key — no arbitrary WHERE clauses,
no JOINs, no aggregate functions.

### Entities and Tables

The current schema has 8 entities:
- `Notes` — core note content, status, type, embedStatus
- `NoteTags` — separate join table (NoteId + Tag composite key)
- `NoteVersions` — edit history per note
- `ContentGenerations` — LLM-generated content sessions (ClusterNoteIds stored as jsonb)
- `ContentPieces` — individual generated posts/tweets/etc.
- `VoiceExamples` — example posts per medium
- `VoiceConfigs` — generation config per medium
- `UsedSeedNotes` — tracks which notes have been used as content seeds

### Query-by-Query Analysis

#### Green: Straightforward DynamoDB mapping

| Query | Current pattern | DynamoDB approach |
|-------|----------------|-------------------|
| `GetByIdAsync(id)` | `WHERE id = x` | GetItem by PK |
| `DeleteAsync(id)` | `DELETE WHERE id = x` | DeleteItem by PK |
| `GetVersionsAsync(noteId)` | `WHERE noteId = x ORDER BY savedAt` | Table PK=noteId SK=savedAt |
| `GetVersionAsync(noteId, id)` | `WHERE noteId = x AND id = y` | GetItem (composite key) |
| `ContentPieces by generationId` | `WHERE generationId = x` | GSI on generationId |
| `VoiceExamples by medium` | `WHERE medium = x` | GSI on medium |
| `ContentPieces by status` | `WHERE status = x` | GSI on status |
| `UsedSeedNote lookup` | `WHERE noteId = x` | GetItem by PK |
| `CountFleetingAsync` | `COUNT WHERE status = Fleeting` | GSI on status + count (or cached counter) |
| `KbHealthService load all permanent notes` | `WHERE status = Permanent SELECT id, title, createdAt, embedStatus, content` | GSI on status (full scan viable at 2000 notes) |

#### Yellow: Feasible with application-side logic or schema changes

| Query | Current pattern | DynamoDB approach | Cost |
|-------|----------------|-------------------|------|
| `ListAsync(tag=x)` | `WHERE tags CONTAINS x` (EF Join) | Separate `TagNoteIndex` table (PK=tag, SK=noteId); BatchGetItem for note details | Extra table + scatter-gather |
| `SearchTagsAsync(prefix)` | `WHERE tag.StartsWith(prefix) DISTINCT` | Scan `TagNoteIndex` with `begins_with(tag, :prefix)` filter | Inefficient but <2000 distinct tags |
| `SearchTitlesAsync(prefix)` | `WHERE title LIKE '%prefix%'` | Scan with `contains(title, :v)` filter — full table scan, case-sensitive | Poor quality; or maintain title index |
| `GetBacklinksAsync(noteId)` | `WHERE content CONTAINS '[[noteTitle]]'` | Full Scan with `contains(content, :wikilink)` filter | Full scan every call (~4MB at 2000 notes) |
| `ReEmbedAllAsync` | Bulk `UPDATE SET embedStatus = Pending` | GSI query on embedStatus (non-Pending/Processing) → parallel UpdateItem calls | N individual writes vs 1 SQL statement |
| `GetRandomForgottenAsync` | `WHERE updatedAt < 30days ORDER BY RANDOM()` | Scan with filter → random sample in app | App-side randomization from scan |
| `GetOrphansAsync` | `WHERE tags.count = 0 AND content NOT CONTAINS '[['` | Scan with multiple filter conditions | Full scan + app-side filter |
| `GetThisDayInHistoryAsync` | `WHERE month = x AND day = y` | Store computed `monthDay` attribute (e.g. "02-26") → GSI | Pre-computed attribute on write |
| `PromoteAsync` / `MergeNoteAsync` | Multi-entity update in one transaction | DynamoDB TransactWriteItems (up to 100 items per transaction) | Minor complexity increase |
| `GetSuggestedTagsAsync` | SQL: JOIN Notes+NoteTags WHERE vector >= 0.5 GROUP BY tag | S3 Vectors → get similar noteIds → BatchGetItem → aggregate tags in app | More round-trips; loses SQL JOIN efficiency |

#### Red: Significant capability gaps

| Query | Current pattern | DynamoDB approach | Gap |
|-------|----------------|-------------------|-----|
| `FullTextSearchAsync` | PostgreSQL `ts_rank + plainto_tsquery` (stemming, ranking, headline extraction) | **No native equivalent** | Core feature gap |
| `SemanticSearchAsync` | pgvector ANN (`<=>` cosine) | S3 Vectors query → BatchGetItem for note details | Functional equivalent (see below) |
| `HybridSearchAsync` | Full-text + semantic, merged with weighted scores | S3 Vectors for semantic half; **FTS half has no good DynamoDB solution** | Hybrid search becomes semantic-only |
| `DiscoverAsync` | Average recent embeddings → ANN search | S3 Vectors query with averaged vector | Functional equivalent |
| `FindRelatedAsync` | ANN search using note's stored embedding | S3 Vectors query (look up note vector → query) | Functional equivalent |
| `CheckDuplicateAsync` | ANN search, find nearest neighbour | S3 Vectors query | Functional equivalent |

---

## The Full-Text Search Problem

This is the most critical gap. The `HybridSearchAsync` in `SearchService.cs` is a
first-class feature that combines:

1. PostgreSQL `plainto_tsquery` with English stemming ("running" matches "run")
2. `ts_rank` for relevance scoring
3. `ts_headline` for rich result snippets with match highlighting
4. Normalized weighted combination with semantic results

DynamoDB's `contains()` filter expression:
- Is an **exact substring match** (no stemming)
- Requires a **full table scan** on every search
- Returns no relevance ranking
- Provides no snippet extraction

There is no way to faithfully replicate PostgreSQL FTS in DynamoDB. The alternatives are:

### Alternative A: Accept semantic-only search
Remove the FTS half of hybrid search. All search goes through S3 Vectors. For a
Zettelkasten use case, this is often "good enough" — semantic search finds conceptually
related notes even without exact keyword matches. However, exact keyword lookup (finding
a note mentioning a specific person, ISBN, or URL) becomes unreliable.

**Impact**: The `FullTextSearchAsync` endpoint becomes a client-side scan or is removed.
`HybridSearchAsync` becomes `SemanticSearchAsync`. Quality regression for exact-term queries.

### Alternative B: Client-side full-text search (in-memory)
On startup (or on first search), load all note titles + content into Lambda memory
and use a library like `Lunr.NET` or simple LINQ for in-memory text search. Results
are merged with S3 Vectors semantic results for hybrid ranking.

- 2000 notes × ~3KB average content = ~6MB in memory — viable
- Lambda is stateless; this index must be rebuilt on every cold start (~100-500ms)
- No stemming unless using a proper library
- No `ts_headline`-style snippet extraction

This is a reasonable degradation for a personal app. The in-memory index could be
stored in a static field and reused across warm invocations.

### Alternative C: DynamoDB + OpenSearch Serverless
Add OpenSearch Serverless for full-text search. Notes are written to DynamoDB as
primary store and streamed to OpenSearch via DynamoDB Streams + Lambda.

Rejected: OpenSearch Serverless minimum cost is ~$700/month (two OCUs minimum per
collection). Completely disproportionate for a personal application.

### Alternative D: Amazon Kendra
Rejected: $810/month minimum for the Developer Edition. Not suitable.

**Conclusion**: If moving to DynamoDB, either accept semantic-only search (Alternative A)
or implement in-memory full-text search (Alternative B). Both are meaningful regressions
from the current PostgreSQL FTS quality.

---

## DynamoDB Table Design

If Option D is pursued, the recommended DynamoDB schema:

### Table: `zettel-notes`

```
PK: noteId (String)          ← "20260226143501234001"

Attributes stored inline (denormalized):
- title, content
- status, noteType, embedStatus, enrichStatus
- source, sourceAuthor, sourceTitle, sourceUrl, sourceYear, sourceType
- createdAt (ISO-8601), updatedAt (ISO-8601)
- monthDay (computed: "02-26" for GetThisDayInHistory)
- tags (StringSet SS) ← denormalized from NoteTags join table
- wikiLinks (StringSet SS)
- wordCount (Number)
- embedRetryCount, embedError
- embeddingModel

Global Secondary Indexes:
- GSI1: status-createdAt-index  PK=status, SK=createdAt   (ListAsync, CountFleeting)
- GSI2: embedStatus-index       PK=embedStatus             (EmbeddingWorker poll)
- GSI3: monthDay-index          PK=monthDay, SK=createdAt  (GetThisDayInHistory)
```

**Key denormalization decision**: Tags are stored as a `StringSet` directly on the note
item (eliminating the `NoteTags` join table). This means:
- `contains(tags, :tag)` works as a FilterExpression on a GSI1 scan
- Tag autocomplete requires scanning all notes or maintaining a separate index
- Tag updates are a single UpdateItem instead of a join table cascade

### Table: `zettel-tag-index`

```
PK: tag (String)             ← "kubernetes"
SK: noteId (String)          ← "20260226143501234001"
Attributes: title, status, createdAt  (for list display without BatchGetItem)
```

This table supports efficient `ListAsync(tag=x)` without a full scan, and
`SearchTagsAsync(prefix)` via `begins_with` on a query (not scan).

### Table: `zettel-note-versions`

```
PK: noteId (String)
SK: savedAt (ISO-8601 String)
Attributes: title, content, tags (StringSet), autoId (Number)
```

Direct replacement for the `NoteVersions` EF entity. No GSIs needed.

### Table: `zettel-content-generations`

```
PK: generationId (String)
Attributes: seedNoteId, topicSummary, clusterNoteIds (List), status, createdAt

GSI1: status-index  PK=status  (list by status for ContentSchedule worker)
```

The `clusterNoteIds` jsonb column maps naturally to a DynamoDB `List` attribute.

### Table: `zettel-content-pieces`

```
PK: generationId (String)    ← FK to content-generations
SK: pieceId (String)
Attributes: medium, body, generatedTags (List), status, publishedAt

GSI1: status-index  PK=status  (list drafts, published pieces)
GSI2: medium-status-index  PK=medium, SK=status
```

### Table: `zettel-voice`

```
PK: entityType (String)      ← "example" | "config"
SK: id (String)              ← entityId
Attributes: medium, content (for examples), configData (for configs)

GSI1: medium-index  PK=medium
```

Combining VoiceExamples and VoiceConfigs into a single table follows the
"single-table design" pattern appropriate for these small lookup tables.

### Table: `zettel-used-seeds`

```
PK: noteId (String)
Attributes: usedAt
```

Simple lookup table, no GSIs needed.

---

## The EF Core Rewrite Assessment

Moving to DynamoDB means abandoning EF Core entirely. The cost is significant:

### Files that must be completely rewritten

- `src/ZettelWeb/Data/ZettelDbContext.cs` — replaced by DynamoDB table configs
- All service files (NoteService, SearchService, DiscoveryService, KbHealthService,
  ContentGenerationService, GraphService, CaptureService, etc.) — every method that
  touches `_db` must be rewritten using `AWSSDK.DynamoDBv2` or a DynamoDB document model
- All EF Core migrations — replaced by Terraform `aws_dynamodb_table` resources
- `Program.cs` — remove all EF Core/Npgsql/pgvector registrations, `db.Database.Migrate()`,
  HNSW index DDL

### Test infrastructure impact

The test suite uses two strategies:
1. `Microsoft.EntityFrameworkCore.InMemory` — for fast unit-style tests
2. `Testcontainers` with real PostgreSQL — for integration tests

Both must be replaced:
- InMemory provider → DynamoDB Local (local Docker container for tests)
- Testcontainers PostgreSQL → DynamoDB Local container in Testcontainers
- All test helpers that seed EF Core DbContext must be rewritten

**Estimated rewrite effort**: 3–5 weeks. This is not a migration — it is a rewrite of
the entire persistence layer, test infrastructure, and several service methods.

---

## The "No VPC" Advantage

This is the genuine architectural win of DynamoDB, and it's significant:

| | Option B (Aurora, VPC Lambda) | Option D (DynamoDB, no VPC) |
|---|---|---|
| Lambda cold start (container) | 1–3 seconds | 200–400ms |
| Networking setup | VPC, private subnets, security groups, VPC endpoints | None required |
| Terraform networking module | ~100 lines | 0 lines |
| RDS Proxy needed | Yes (connection pooling) | No (HTTP API, stateless) |
| Aurora scale-to-zero resume latency | 30–60 seconds from 0 ACU | N/A (no DB warmup) |
| IAM complexity | Lambda execution role + RDS auth | Lambda execution role + DynamoDB policies |

The connection model is fundamentally different. PostgreSQL uses persistent TCP connections
that Lambda instances hold open and re-use — which is why RDS Proxy is recommended to
manage the pool. DynamoDB uses signed HTTP/2 requests — each SDK call is independent,
stateless, and doesn't hold open connections. This eliminates the connection exhaustion
failure mode that requires RDS Proxy in Option B.

---

## The Middle Ground: Aurora Serverless v2 with min_capacity=0

Before committing to either Option B or Option D, it is worth noting that Aurora
Serverless v2 now supports `min_capacity = 0` (true scale to zero) in eu-west-1 as of
2024. This changes the cost calculation:

| | Option B (min 0.5 ACU) | Option B+ (min 0 ACU) | Option D (DynamoDB) |
|---|---|---|---|
| DB cost at zero traffic | ~$4/month | **$0/month** | ~$0/month |
| DB resume latency from 0 | 30–60s | 30–60s | N/A |
| VPC required | Yes | Yes | No |
| Lambda cold start | 1–3s | 1–3s | 200–400ms |
| Full-text search | Native PostgreSQL FTS | Native PostgreSQL FTS | Semantic-only or in-memory |
| Code changes | Minimal | Minimal | Full rewrite |

With min_capacity=0, Option B's cost drops to essentially Lambda + S3/CloudFront costs
(~$2–5/month at personal use), which is comparable to Option D. The only remaining
advantage of DynamoDB is the faster cold start from removing the VPC.

---

## Comparison: Option B vs Option D

| Criterion | Option B (Lambda + Aurora + pgvector) | Option D (Lambda + DynamoDB + S3 Vectors) |
|-----------|--------------------------------------|------------------------------------------|
| Cold start | 🔴 1–3s (VPC) | 🟢 200–400ms (no VPC) |
| True scale to zero | 🟡 Yes (with min_capacity=0) | 🟢 Yes |
| Monthly cost (personal use) | 🟢 ~$2–5/month (min_capacity=0) | 🟢 ~$2–5/month |
| Full-text search | 🟢 Native PostgreSQL FTS | 🔴 Semantic-only or degraded |
| Hybrid search quality | 🟢 Weighted FTS + semantic | 🔴 Semantic only (FTS gap) |
| EF Core preserved | 🟢 Yes (minimal changes) | 🔴 Full rewrite required |
| Test infrastructure | 🟢 Testcontainers unchanged | 🔴 DynamoDB Local setup required |
| Connection pooling concern | 🔴 RDS Proxy recommended | 🟢 Not applicable |
| Terraform complexity | 🟡 VPC + Aurora modules | 🟡 DynamoDB table definitions |
| Migration/schema evolution | 🟡 EF Core migrations | 🟢 No migrations (schema-less) |
| Rewrite effort | 🟢 Small (BackgroundService only) | 🔴 3–5 week full rewrite |
| Risk | 🟢 Low (proven stack) | 🟡 Medium (new stack, feature gap) |

---

## Recommendation: Augmented Option B

**Do not replace Aurora with DynamoDB** at this stage. The rewrite cost is disproportionate
to the benefit, and the loss of full-text search is a meaningful quality regression for
a Zettelkasten where exact-keyword lookup is a primary use case.

**Do adopt these Option D improvements into Option B**:

1. **Set Aurora `min_capacity = 0`** in Terraform. This brings Option B to true scale-to-zero
   at effectively $0/month for the DB tier at idle, matching DynamoDB's idle cost.

2. **Keep the VPC but minimise it**. Use VPC endpoints for DynamoDB (future), Bedrock, SQS,
   and Secrets Manager instead of a NAT gateway. This reduces VPC cost to ~$8/month
   for the endpoints.

3. **Add S3 Vectors as a future enhancement** (Option C), not now, once the .NET SDK
   stabilises. The `ISearchService` interface makes this a clean swap without touching
   the DynamoDB vs Aurora question.

### When DynamoDB becomes the right answer

Consider migrating to DynamoDB if:
- **Cold start latency is unacceptable** and provisioned concurrency cost is not justified
- **You add multi-user support** where connection pooling at Aurora becomes a real concern
  (DynamoDB's HTTP model scales effortlessly to concurrent users)
- **Full-text search is replaced by pure semantic search** as a deliberate product decision
  (i.e., you decide that good-quality vector search is sufficient for all search use cases)
- **A new greenfield notes schema is warranted** anyway (e.g., a v2 data model) — at that
  point, design for DynamoDB from the start rather than migrating an EF Core schema

---

## Terraform Impact Summary (Option D, for reference)

If Option D were pursued, the Terraform structure would simplify in some ways and
add complexity in others:

**Removed from Option B**:
- `modules/networking/` — VPC, subnets, VPC endpoints, security groups (gone entirely)
- `modules/database/` — Aurora cluster, parameter group, RDS Proxy (gone entirely)
- `aws_lambda_invocation` for migrations (no migrations in DynamoDB)

**Added for Option D**:
- `modules/dynamodb/` — 7 `aws_dynamodb_table` resources with GSI definitions
- IAM policies for DynamoDB access on all Lambda execution roles
- DynamoDB Streams (optional, for future OpenSearch integration)
- DynamoDB Local in CI test environment

**Broadly unchanged**:
- `modules/api/` — Lambda + API Gateway
- `modules/workers/` — EventBridge + worker Lambdas
- `modules/frontend/` — S3 + CloudFront
- `modules/secrets/` — Secrets Manager
- `modules/monitoring/` — CloudWatch

The removal of the networking module is a significant simplification, but the addition
of 7 DynamoDB tables with GSIs and the service rewrite makes the overall effort larger.

---

## Open Questions (Option D specific)

- [ ] **In-memory FTS library**: If Alternative B (in-memory full-text) is chosen, which
      .NET library? `Lunr.NET` (port of Lunr.js), a custom LINQ tokenizer, or accept
      DynamoDB `contains()` filter as-is?
- [ ] **DynamoDB single-table vs multi-table**: The design above uses multi-table (one per
      entity type), which is easier to understand and evolve independently. Single-table
      design would reduce Terraform resources but increases query complexity.
- [ ] **Cold start benchmark**: Before committing to Option D, benchmark the actual cold
      start difference between VPC-Lambda (Option B) and non-VPC Lambda (Option D) using
      .NET 10 on the specific memory/architecture settings chosen.
