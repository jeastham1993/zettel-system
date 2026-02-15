---
type: problem-solution
category: database
tags: [ef-core, migrations, postgresql, ensureCreated, schema, startup, docker]
created: 2026-02-15
confidence: high
languages: [dotnet]
related: [ADR-001]
---

# EF Core EnsureCreated() Silently Skips Table Creation on Existing Databases

## Problem

Application deployed to a home server via Docker Compose fails at runtime with:

```
42P01: relation "Notes" does not exist
```

The error occurs in background services (`EmbeddingBackgroundService`, `EnrichmentBackgroundService`) that poll the `Notes` table on startup.

## Symptoms

- Application starts without errors during database initialization
- Background services immediately fail with `42P01` (undefined table)
- The failure repeats on every polling interval
- The database exists but contains no application tables

## Root Cause

`DbContext.Database.EnsureCreated()` has a subtle no-op behavior:

| Database state | Tables exist? | What EnsureCreated() does |
|---|---|---|
| Doesn't exist | N/A | Creates DB + all tables |
| Exists | No tables | Creates all tables |
| Exists | **Any tables present** | **Does nothing (no-op)** |

In a Docker Compose setup, PostgreSQL's `POSTGRES_DB` environment variable pre-creates the database. If the `pgdata` volume already contains any tables from a prior run (or even system catalog entries that EnsureCreated interprets as "has tables"), it becomes a permanent no-op.

Additionally, `EnsureCreated()` and EF Core Migrations are **mutually exclusive**:
- `EnsureCreated()` does NOT create a `__EFMigrationsHistory` table
- EF Migrations cannot run on a database created by `EnsureCreated()` without manual intervention
- There is no built-in way to evolve the schema once `EnsureCreated()` has run

## Solution

Replace `EnsureCreated()` with proper EF Core Migrations:

```csharp
// Before (fragile):
db.Database.EnsureCreated();
db.Database.ExecuteSqlRaw("ALTER TABLE ...");  // hand-written migrations
db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS ...");

// After (robust):
db.Database.Migrate();
```

1. Generate an initial migration: `dotnet ef migrations add InitialCreate`
2. Customize the migration to include PostgreSQL-specific indexes (partial indexes, GIN full-text, etc.) via `migrationBuilder.Sql()`
3. Replace `EnsureCreated()` + all inline SQL with `db.Database.Migrate()`
4. Keep only truly config-dependent DDL (like HNSW index with runtime dimensions) in Program.cs

## Why This Works

`Migrate()` uses the `__EFMigrationsHistory` table to track applied migrations:
- On a **fresh database**: applies all migrations (creates tables, indexes, extensions)
- On an **existing database**: applies only unapplied migrations
- **Idempotent**: safe to call on every startup
- **Evolvable**: future schema changes are just new migration files

## Gotchas

- Partial indexes (`WHERE` clause) and GIN indexes can't be expressed through `migrationBuilder.CreateIndex()` — use `migrationBuilder.Sql()` for those
- The pgvector extension is handled automatically via `modelBuilder.HasPostgresExtension("vector")` in the DbContext, which the migration scaffolder picks up as an `AlterDatabase` annotation
- HNSW indexes depend on embedding model dimensions (a runtime config value), so they can't live in a static migration — keep these in Program.cs with `CREATE INDEX IF NOT EXISTS`
