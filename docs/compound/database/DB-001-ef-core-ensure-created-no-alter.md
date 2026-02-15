---
type: problem-solution
category: database
tags: [ef-core, ensureCreated, schema-migration, postgresql, startup]
created: 2026-02-14
confidence: high
languages: [dotnet]
related: [ADR-001]
---

# EF Core EnsureCreated Does Not Alter Existing Tables

## Problem

After adding new columns to the EF Core model (Batch 20: Fleeting Notes),
the backend crashed on startup with:

```
42703: column "Status" does not exist
```

Raw SQL in `Program.cs` attempted to create an index on the new `Status`
column, but the column didn't exist in the database.

## Root Cause

`db.Database.EnsureCreated()` only creates tables that don't exist. If the
database already has the `Notes` table from a prior schema version, it does
**nothing** — no new columns, no altered types, no new indexes.

Any raw SQL that references new columns will fail on existing databases.

## Solution

Add idempotent migration SQL that runs after `EnsureCreated()`:

```csharp
db.Database.ExecuteSqlRaw("""
    DO $$ BEGIN
        IF NOT EXISTS (
            SELECT 1 FROM information_schema.columns
            WHERE table_name = 'Notes' AND column_name = 'Status'
        ) THEN
            ALTER TABLE "Notes" ADD COLUMN "Status"
                character varying(20) NOT NULL DEFAULT 'Permanent';
        END IF;
    END $$;
    """);
```

Pattern: wrap each `ALTER TABLE ADD COLUMN` in an `IF NOT EXISTS` guard
against `information_schema.columns`. This is idempotent — safe on both
fresh databases (where EnsureCreated already created the column) and
existing databases (where the column is missing).

Similarly, guard index creation on new columns:

```csharp
// Only create index if the column exists
db.Database.ExecuteSqlRaw("""
    DO $$ BEGIN
        IF EXISTS (
            SELECT 1 FROM information_schema.columns
            WHERE table_name = 'Notes' AND column_name = 'Status'
        ) THEN
            CREATE INDEX IF NOT EXISTS idx_notes_status ON "Notes" ("Status");
        END IF;
    END $$;
    """);
```

## Also: Column Width Changes

`EnsureCreated` also won't widen existing columns. When the Note ID format
changed from `yyyyMMddHHmmss` (14 chars) to `yyyyMMddHHmmssfff` (17 chars),
the same pattern applies:

```csharp
IF EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'Notes' AND column_name = 'Id'
      AND character_maximum_length < 17
) THEN
    ALTER TABLE "Notes" ALTER COLUMN "Id" TYPE character varying(17);
END IF;
```

Don't forget FK columns — `NoteTags.NoteId` must also be widened.

## Prevention

When adding new columns or changing column types in a project that uses
`EnsureCreated()` (no migrations), always add corresponding idempotent
`ALTER TABLE` SQL in the startup block.

## Key Takeaway

`EnsureCreated()` is all-or-nothing per table. For incremental schema
changes without EF Core migrations, maintain a startup migration block
with `IF NOT EXISTS` / `IF EXISTS` guards.
