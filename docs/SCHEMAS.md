# Database Schemas

Last Updated: 2026-02-22 | Migration: `20260222141707_AddContentGenerator`

This document covers the database tables introduced by the content generator feature.
For the core Zettelkasten tables (Notes, NoteTags, NoteVersions), see the initial
migration at `src/ZettelWeb/Data/Migrations/20260215182317_InitialCreate.cs`.

---

## Content Generator Tables

### ContentGenerations

Represents a single content generation pipeline run, seeded from a cluster of
related notes.

| Column | Type | Nullable | Default | Notes |
|---|---|---|---|---|
| Id | `character varying(21)` | NO | -- | PK. Timestamp-based ID (same format as Notes). |
| SeedNoteId | `character varying(21)` | NO | -- | ID of the primary seed note. |
| ClusterNoteIds | `jsonb` | NO | -- | JSON array of note IDs in the topic cluster. |
| TopicSummary | `text` | NO | -- | LLM-generated summary of the topic cluster. |
| TopicEmbedding | `real[]` | YES | -- | Embedding vector for similarity search. Cast to `vector` in queries. |
| Status | `character varying(20)` | NO | `"Pending"` | Enum stored as string. |
| GeneratedAt | `timestamp with time zone` | NO | -- | When the pipeline ran (UTC). |
| ReviewedAt | `timestamp with time zone` | YES | -- | When the user reviewed (UTC). |

**Primary key:** `PK_ContentGenerations (Id)`

**Relationships:**
- One-to-many with `ContentPieces` via `GenerationId` (cascade delete)

**Enum: `GenerationStatus`**
- `Pending` -- Generation is queued or in progress
- `Generated` -- Content has been generated and awaits review
- `Approved` -- All content pieces have been approved
- `Rejected` -- The generation was rejected by the user

---

### ContentPieces

An individual piece of generated content (blog post or social media post),
belonging to a `ContentGeneration` run.

| Column | Type | Nullable | Default | Notes |
|---|---|---|---|---|
| Id | `character varying(21)` | NO | -- | PK. Timestamp-based ID. |
| GenerationId | `character varying(21)` | NO | -- | FK to `ContentGenerations.Id`. |
| Medium | `character varying(20)` | NO | -- | `"blog"` or `"social"`. |
| Title | `text` | YES | -- | Optional; typically null for social posts. |
| Body | `text` | NO | -- | Markdown content. |
| Status | `character varying(20)` | NO | `"Draft"` | Enum stored as string. |
| Sequence | `integer` | NO | -- | Ordering within the generation run. |
| CreatedAt | `timestamp with time zone` | NO | -- | When created (UTC). |
| ApprovedAt | `timestamp with time zone` | YES | -- | When approved (UTC). |

**Primary key:** `PK_ContentPieces (Id)`

**Foreign keys:**
- `FK_ContentPieces_ContentGenerations_GenerationId` -> `ContentGenerations.Id`
  (ON DELETE CASCADE)

**Indexes:**
- `IX_ContentPieces_GenerationId` on `GenerationId`
- `IX_ContentPieces_Status` on `Status`

**Enum: `ContentPieceStatus`**
- `Draft` -- Content is in draft form awaiting review
- `Approved` -- Content has been approved for publishing
- `Rejected` -- Content has been rejected

---

### VoiceExamples

User-provided writing samples used to guide voice and tone in generated content.

| Column | Type | Nullable | Default | Notes |
|---|---|---|---|---|
| Id | `character varying(21)` | NO | -- | PK. Timestamp-based ID. |
| Medium | `character varying(20)` | NO | -- | `"blog"`, `"social"`, or `"all"`. |
| Title | `text` | YES | -- | Optional title of the writing sample. |
| Content | `text` | NO | -- | The writing sample body. |
| Source | `text` | YES | -- | Optional source attribution. |
| CreatedAt | `timestamp with time zone` | NO | -- | When created (UTC). |

**Primary key:** `PK_VoiceExamples (Id)`

**Indexes:**
- `IX_VoiceExamples_Medium` on `Medium`

---

### VoiceConfigs

User-defined voice style configuration per content medium.

| Column | Type | Nullable | Default | Notes |
|---|---|---|---|---|
| Id | `character varying(21)` | NO | -- | PK. Timestamp-based ID. |
| Medium | `character varying(20)` | NO | -- | `"blog"`, `"social"`, or `"all"`. |
| StyleNotes | `text` | YES | -- | Free-form style/tone instructions. |
| UpdatedAt | `timestamp with time zone` | NO | -- | When last updated (UTC). |

**Primary key:** `PK_VoiceConfigs (Id)`

**Indexes:**
- `IX_VoiceConfigs_Medium` on `Medium`

---

### UsedSeedNotes

Tracks which notes have been used as seeds for content generation, preventing
reuse.

| Column | Type | Nullable | Default | Notes |
|---|---|---|---|---|
| NoteId | `character varying(21)` | NO | -- | PK. References a Note ID. |
| UsedAt | `timestamp with time zone` | NO | -- | When the note was used as a seed (UTC). |

**Primary key:** `PK_UsedSeedNotes (NoteId)`

---

## ID Generation

All content generator entities use the same ID format as Notes: a 17-digit
UTC timestamp (`yyyyMMddHHmmssfff`) followed by a 4-digit random suffix,
totaling 21 characters. IDs are generated at the application layer, not by
the database.

## Design Decisions

- **ClusterNoteIds as `jsonb`**: Chosen over comma-separated text to enable
  native Postgres JSON querying and cleaner EF Core `List<string>` mapping.
- **TopicEmbedding as `real[]`**: Follows the same pattern as `Notes.Embedding`.
  Cast to `vector` type in raw SQL queries for pgvector operations. Kept as
  `float[]` in the C# model for InMemory test provider compatibility.
- **Enums as strings**: All enums use `HasConversion<string>()` with
  `HasMaxLength(20)`, consistent with the existing Note entity pattern.
