# ADR-002: PostgreSQL-Native Search (pgvector + tsvector)

Date: 2026-02-14
Status: Accepted

## Context

Zettel-Web's search implementation loads all notes into memory for both
full-text and semantic search. For full-text, every note is fetched via
`ToListAsync()` and filtered with `String.Contains`. For semantic search,
every embedded note is loaded and cosine similarity is computed in C#.

This works for 3 notes but will degrade linearly:
- **Full-text**: O(n) memory, O(n * query_terms) string comparisons
- **Semantic**: O(n) memory, O(n * embedding_dimensions) floating-point ops
- At 2,000 notes with 768-dimension embeddings, each search loads ~6MB of
  vectors into memory and performs ~1.5M float multiplications

The database already runs `pgvector/pgvector:pg17` with
`Pgvector.EntityFrameworkCore 0.3.0` installed, and PostgreSQL has built-in
full-text search via `tsvector`/`tsquery`. Both capabilities are available
but unused.

Four options were evaluated:
- **Option 1**: Push both searches to PostgreSQL (pgvector + tsvector)
- **Option 2**: Add OpenSearch/Elasticsearch as a search layer
- **Option 3**: Add a dedicated vector database (Qdrant, Weaviate, Milvus)
- **Option 4**: Keep in-memory search with optimizations

## Decision

Use **PostgreSQL-native search** (Option 1):

1. **Semantic search**: Use pgvector's `<=>` cosine distance operator with an
   HNSW index. The database performs approximate nearest-neighbor search in
   O(log n) instead of brute-force O(n).

2. **Full-text search**: Use PostgreSQL's `to_tsvector`/`plainto_tsquery` with
   a functional GIN index. The database handles tokenization, stemming, and
   relevance ranking (`ts_rank`) without loading notes into application memory.

3. **Queries use raw SQL** (`Database.SqlQuery<T>`) rather than LINQ, because
   pgvector distance operators and tsvector matching don't have LINQ
   equivalents when using `float[]` column types.

4. **The `float[]` column type is preserved** for the Embedding property to
   maintain InMemory test compatibility for non-search tests (CRUD, tags,
   embedding pipeline). Search-specific tests use Testcontainers with a real
   PostgreSQL instance.

## Consequences

### Positive

- Zero additional infrastructure: uses the existing PostgreSQL instance
- Search performance scales to tens of thousands of notes without degradation
- HNSW index provides O(log n) approximate nearest-neighbor search
- GIN index provides sub-millisecond full-text matching with stemming support
- No data synchronization complexity (single source of truth)
- PostgreSQL's `ts_headline` provides better snippet generation than custom C#
- Embedding and full-text data stay co-located with the notes they belong to

### Negative

- Search queries use raw SQL, not LINQ, reducing type safety
- Search tests require Docker (Testcontainers with pgvector image) rather than
  InMemory provider, adding ~3s startup overhead per test class
- PostgreSQL-specific: migrating to a different database would require
  rewriting the search layer (acceptable for a personal app)
- `plainto_tsquery` uses PostgreSQL's English stemmer which may behave
  differently from the previous exact `String.Contains` matching

### Neutral

- The `HybridSearchAsync` merge logic remains in C# (combining normalized
  full-text and semantic scores is application logic, not a database concern)
- The `ISearchService` interface is unchanged; the change is purely in the
  `SearchService` implementation
- The `float[]` Embedding column maps to PostgreSQL's `vector` type via
  `Pgvector.EntityFrameworkCore`, so no schema change is needed

## Alternatives Considered

### OpenSearch / Elasticsearch (Option 2)

Not chosen because it adds a second service to the Docker stack, requires
data synchronization between PostgreSQL and the search index, and is designed
for millions of documents across distributed clusters. For a personal
Zettelkasten with <10K notes, this adds operational complexity without
proportionate benefit.

### Dedicated Vector Database (Option 3)

Not chosen for the same reasons as Option 2, plus the additional split of
needing PostgreSQL for relational data and a vector DB for embeddings. The
data consistency problem is worse than OpenSearch since there's no built-in
full-text search, requiring two external systems.

### In-Memory with Optimizations (Option 4)

Not chosen because it doesn't address the fundamental O(n) scaling problem.
Caching, pagination, and lazy loading can defer the issue but don't eliminate
it. The database already has the data and the indexes to search it
efficiently.

### When to Reconsider

Revisit this decision if:
- Full-text search needs features PostgreSQL lacks (faceting, fuzzy matching
  with Levenshtein distance, language detection)
- Vector search needs features pgvector lacks (multi-vector queries, filtered
  vector search with complex predicates, real-time index updates at >100K
  vectors)
- The application becomes multi-tenant and needs index isolation per tenant

None of these are anticipated in the current spec.

## Related Decisions

- ADR-001: Backend architecture pattern (Simple Layered)
- Embedding outbox pattern (design document)
- `float[]` vs `Vector` type decision (Batch 12, preserved for InMemory
  test compatibility)
