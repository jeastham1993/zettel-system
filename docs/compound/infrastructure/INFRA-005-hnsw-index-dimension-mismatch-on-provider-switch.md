---
type: problem-solution
category: infrastructure
tags: [pgvector, hnsw, embedding, dimensions, bedrock, ollama, index, postgresql]
created: 2026-02-25
updated: 2026-02-25
confidence: high
languages: [dotnet, sql]
related: [INFRA-003, INFRA-004, PAT-001]
---

# HNSW Index Dimension Mismatch When Switching Embedding Providers

## Problem

After switching from one embedding provider to another with different output dimensions
(e.g. Ollama `nomic-embed-text` at 768 → Bedrock Titan v2 at 1024), the startup code
silently fails to update the HNSW index, leaving it pointing at the wrong dimension.

This causes search queries to fail at runtime with a pgvector dimension mismatch error,
or — if old and new embeddings coexist in the table — produces wrong results.

## Root Cause

The HNSW index is created at startup in `Program.cs`:

```csharp
db.Database.ExecuteSqlRaw(
    $"CREATE INDEX IF NOT EXISTS idx_notes_embedding_hnsw " +
    $"ON \"Notes\" USING hnsw ((\"Embedding\"::vector({dimensions})) vector_cosine_ops) " +
    $"WHERE \"Embedding\" IS NOT NULL;");
```

The `IF NOT EXISTS` clause checks by index **name**, not by definition. When `Dimensions`
changes from 768 to 1024 in config, the old `vector(768)` index already exists under
that name, so the new `CREATE INDEX` does nothing. The index remains at the old dimension.

New embeddings stored as `real[]` with 1024 elements are then cast to `vector(1024)` at
query time, which does not match the `vector(768)` index, producing an error or
incorrect results.

## Why the Column is Fine

The `Embedding` column type is `real[]` (a plain PostgreSQL float array with no fixed
width). PostgreSQL stores any length of array in it without complaint. The mismatch
problem is **only** in the HNSW index definition, which has a fixed vector dimension.

## Solution

Before restarting the app with new dimension config, manually drop the old index:

```sql
DROP INDEX IF EXISTS idx_notes_embedding_hnsw;
```

On next startup, `Program.cs` creates a fresh index at the new dimensions:

```sql
CREATE INDEX IF NOT EXISTS idx_notes_embedding_hnsw
ON "Notes" USING hnsw (("Embedding"::vector(1024)) vector_cosine_ops)
WHERE "Embedding" IS NOT NULL;
```

## Full Provider Switch Checklist

```sql
-- 1. Drop the old dimension-locked HNSW index
DROP INDEX IF EXISTS idx_notes_embedding_hnsw;

-- 2. Clear stored embeddings (wrong dimension, can't compare with new ones)
--    and reset embed status so the background service re-processes all notes
UPDATE "Notes"
SET "EmbedStatus" = 'Pending',
    "EmbedRetryCount" = 0,
    "EmbedError" = NULL,
    "Embedding" = NULL
WHERE "Status" = 'Permanent';
```

Then restart the app. On startup:
- A new HNSW index is created at the correct dimensions
- The embedding background service picks up all `Pending` notes

Alternatively, use the "Re-embed all notes" button in the Settings page UI, or
`POST /api/notes/re-embed` — but **drop the index first** before restarting.

## Future Improvement

The `IF NOT EXISTS` pattern could be replaced with a dimension-aware check:

```sql
DO $$ BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_indexes
    WHERE indexname = 'idx_notes_embedding_hnsw'
      AND indexdef LIKE '%vector(1024)%'
  ) THEN
    DROP INDEX IF EXISTS idx_notes_embedding_hnsw;
    CREATE INDEX idx_notes_embedding_hnsw
      ON "Notes" USING hnsw (("Embedding"::vector(1024)) vector_cosine_ops)
      WHERE "Embedding" IS NOT NULL;
  END IF;
END $$;
```

This would make dimension changes automatically self-healing at startup.
