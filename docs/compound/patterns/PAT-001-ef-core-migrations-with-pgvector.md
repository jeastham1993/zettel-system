---
type: pattern
category: database
tags: [ef-core, migrations, pgvector, postgresql, indexes, dotnet]
created: 2026-02-15
confidence: high
languages: [dotnet]
related: [ADR-001, DB-001]
---

# EF Core Migrations with pgvector and Custom PostgreSQL Indexes

## Context

When using EF Core with PostgreSQL + pgvector, the schema includes elements that the standard migration scaffolder can't fully express: partial indexes, GIN indexes for full-text search, and HNSW indexes for vector similarity. A pattern is needed to combine scaffolded migrations with PostgreSQL-specific DDL.

## Pattern

### 1. Let the scaffolder handle standard schema

```bash
dotnet ef migrations add MigrationName --output-dir Data/Migrations
```

The scaffolder handles:
- Table creation with all columns, types, and constraints
- Foreign keys and cascading deletes
- Standard B-tree indexes
- pgvector extension (via `HasPostgresExtension("vector")` in DbContext)

### 2. Add PostgreSQL-specific indexes via raw SQL in the migration

```csharp
// Partial indexes (WHERE clause) — not supported by CreateIndex
migrationBuilder.Sql("""
    CREATE INDEX idx_notes_embed_status
    ON "Notes" ("EmbedStatus")
    WHERE "EmbedStatus" IN ('Pending', 'Failed', 'Stale');
    """);

// GIN full-text search index
migrationBuilder.Sql("""
    CREATE INDEX idx_notes_fulltext
    ON "Notes" USING GIN (to_tsvector('english', "Title" || ' ' || "Content"));
    """);
```

### 3. Keep config-dependent DDL in Program.cs

```csharp
// HNSW index depends on runtime config (embedding dimensions vary by model)
var dimensions = app.Configuration.GetValue<int>("Embedding:Dimensions");
if (dimensions > 0 && dimensions <= 4096)
{
    db.Database.ExecuteSqlRaw(
        $"CREATE INDEX IF NOT EXISTS idx_notes_embedding_hnsw " +
        $"ON \"Notes\" USING hnsw ((\"Embedding\"::vector({dimensions})) vector_cosine_ops) " +
        $"WHERE \"Embedding\" IS NOT NULL;");
}
```

### 4. Always add Down() operations for custom indexes

```csharp
protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.DropIndex(name: "idx_notes_fulltext", table: "Notes");
    migrationBuilder.DropIndex(name: "idx_notes_embed_status", table: "Notes");
    // ... then drop tables
}
```

## When to Use

- Any project combining EF Core with PostgreSQL-specific features (pgvector, full-text search, partial indexes)
- Projects that need to evolve their schema over time (i.e., all production applications)

## Trade-offs

| Aspect | Benefit | Cost |
|---|---|---|
| Migrations | Tracked, reversible schema changes | Need `dotnet ef` tooling |
| Raw SQL in migrations | Full PostgreSQL feature support | Not database-portable |
| Config-dependent DDL in startup | Adapts to runtime environment | Not tracked in migration history |

## Vector Column Type Note

The `Embedding` column is stored as `real[]` (not `vector`) for EF Core `float[]` compatibility. The `SearchService` casts to `::vector` in raw SQL queries. This avoids needing the `Pgvector.Vector` type in the entity model, which would break InMemory test compatibility.
