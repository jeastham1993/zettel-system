# Design: Structure Notes & Sources

Generated: 2026-02-15
Status: Draft

## Problem Statement

### Goal

Add Zettelkasten-native note types (Structure, Source) to Zettel-Web so that
200+ notes can be organized into navigable topic hierarchies with traceable
intellectual provenance. See full spec:
[docs/specs/2026-02-15-structure-notes-sources.md](specs/2026-02-15-structure-notes-sources.md)

### Constraints

- Must follow Simple Layered Architecture (ADR-001)
- Must work with InMemory EF Core provider for unit tests
- Must use established ALTER TABLE migration pattern (DB-001)
- No increase in friction for creating regular notes
- Existing notes must continue to work unchanged (default to Regular)
- Types are mutually exclusive: Regular | Structure | Source
- NoteType only applies to Permanent notes (Fleeting has no type)

### Success Criteria

- [ ] NoteType persisted and filterable via API and UI
- [ ] Source notes have queryable structured metadata fields
- [ ] Structure notes are visually distinguished in the notes list
- [ ] All existing tests pass unchanged
- [ ] Zero-migration for existing data (defaults handle everything)

## Context

### Current State

The Note model has:
- `NoteStatus` enum (Permanent/Fleeting) stored as string, added in Batch 20
- `Source` field for capture channel ("web"/"email"/"telegram")
- `EnrichmentJson` for URL metadata stored as JSON string
- Tags via separate NoteTag table (M:N relationship)
- float[] Embedding for vector search

Key patterns established:
- Enums stored as string via `HasConversion<string>()` with `HasMaxLength()`
- New columns added via ALTER TABLE with IF NOT EXISTS guards in Program.cs
- Controllers delegate to services; DTOs are records defined in controller files
- Frontend types mirror backend in `api/types.ts`
- InMemory provider compatibility constrains column types

### Related Decisions

- [ADR-001](adr/ADR-001-backend-architecture.md): Simple Layered Architecture
- [ADR-003](adr/ADR-003-fleeting-notes-architecture.md): Fleeting Notes (same
  model extension pattern)
- [DB-001](compound/DB-001-ensure-created.md): EnsureCreated migration pattern

## Alternatives Considered

### Option A: Flat Columns on Note Table

**Summary**: Add NoteType enum + 5 nullable source metadata columns directly
on the Note entity, mirroring the Batch 20 pattern.

**Data Model**:
```csharp
// New enum
public enum NoteType { Regular = 0, Structure = 1, Source = 2 }

// New fields on Note
public NoteType NoteType { get; set; } = NoteType.Regular;
public string? SourceAuthor { get; set; }
public string? SourceTitle { get; set; }
public string? SourceUrl { get; set; }
public int? SourceYear { get; set; }
public string? SourceType { get; set; } // "book"|"article"|"web"|"podcast"|"other"
```

**API Changes**:
```csharp
// Extend CreateNoteRequest
public record CreateNoteRequest(
    string? Title, string Content, string[]? Tags = null,
    string? Status = null, string? Source = null,
    string? NoteType = null,         // NEW
    string? SourceAuthor = null,     // NEW
    string? SourceTitle = null,      // NEW
    string? SourceUrl = null,        // NEW
    int? SourceYear = null,          // NEW
    string? SourceType = null);      // NEW

// Extend UpdateNoteRequest
public record UpdateNoteRequest(
    string Title, string Content, string[]? Tags = null,
    string? NoteType = null,         // NEW
    string? SourceAuthor = null,     // NEW
    string? SourceTitle = null,      // NEW
    string? SourceUrl = null,        // NEW
    int? SourceYear = null,          // NEW
    string? SourceType = null);      // NEW

// Extend List endpoint with type filter
[HttpGet] List(..., [FromQuery] string? noteType = null)
```

**Migration SQL**:
```sql
IF NOT EXISTS (... column_name = 'NoteType') THEN
    ALTER TABLE "Notes" ADD COLUMN "NoteType"
        character varying(20) NOT NULL DEFAULT 'Regular';
END IF;
IF NOT EXISTS (... column_name = 'SourceAuthor') THEN
    ALTER TABLE "Notes" ADD COLUMN "SourceAuthor" text;
END IF;
-- ... repeat for SourceTitle, SourceUrl, SourceYear, SourceType
```

**Pros**:
- Follows the exact pattern established by Batch 20 (NoteStatus, Source)
- Zero new files for the data layer
- No join needed - Note entity has all fields
- InMemory test compatible (no joins, no JSON parsing)
- Source fields are individually queryable via LINQ
- Simplest ALTER TABLE migration

**Cons**:
- Note entity grows by 6 fields (from 14 to 20 properties)
- Source fields are null for 95%+ of notes (wasted columns)
- No enforcement that source fields are only set when NoteType=Source

**Coupling Analysis**:
| Component | Ca (in) | Ce (out) | I (instability) |
|-----------|---------|----------|------------------|
| Note model | 8 | 0 | 0.00 (stable) |
| NoteService | 4 | 3 | 0.43 |
| NotesController | 1 | 3 | 0.75 |

New dependencies introduced: None
Coupling impact: **Low** - extends existing entity, no new relationships

**Failure Modes**:
| Mode | Severity | Occurrence | Detection | RPN |
|------|----------|------------|-----------|-----|
| NoteType not set on create | 2 | 2 | 1 | 4 |
| Source fields set on non-Source note | 2 | 3 | 2 | 12 |
| ALTER TABLE fails on existing DB | 8 | 1 | 2 | 16 |

**Evolvability Assessment**:
- Add auto-detection suggestion: **Easy** - query wikilink count, no model change
- Add "notes citing this source" backlink: **Easy** - query by wikilink target
- Add source import from Zotero: **Easy** - map BibTeX fields to flat columns
- Add nested structure notes: **Easy** - no model change needed, just UI
- Change to separate SourceMetadata table later: **Medium** - data migration

**Effort Estimate**: Small (1-2 batches)

---

### Option B: Separate SourceMetadata Entity

**Summary**: Add NoteType enum on Note, create a new SourceMetadata table with
a 1:0..1 relationship. Source metadata lives in its own entity.

**Data Model**:
```csharp
public enum NoteType { Regular = 0, Structure = 1, Source = 2 }

// On Note
public NoteType NoteType { get; set; } = NoteType.Regular;
public SourceMetadata? SourceMetadata { get; set; }

// New entity
public class SourceMetadata
{
    public required string NoteId { get; set; }
    public string? Author { get; set; }
    public string? Title { get; set; }
    public string? Url { get; set; }
    public int? Year { get; set; }
    public string? Type { get; set; }
}
```

**Pros**:
- Clean separation - Note doesn't grow with source-specific fields
- DB enforcement possible: SourceMetadata only exists for Source notes
- Source fields are never null on non-Source notes (they just don't exist)
- SourceMetadata could gain fields without touching Note

**Cons**:
- New entity + DbSet + OnModelCreating config + migration
- Requires `.Include(n => n.SourceMetadata)` on every Note query
- **InMemory test risk**: Navigation properties with InMemory can be brittle
  (same concern that led to float[] over Vector for embeddings)
- More files: SourceMetadata.cs, DbContext changes, migration SQL for new table
- JOIN cost on every note list query (even though 95% have no source metadata)
- Pattern divergence: Batch 20 added flat columns, this creates a relationship

**Coupling Analysis**:
| Component | Ca (in) | Ce (out) | I (instability) |
|-----------|---------|----------|------------------|
| Note model | 8 | 1 | 0.11 |
| SourceMetadata | 1 | 0 | 0.00 |
| NoteService | 4 | 4 | 0.50 |
| ZettelDbContext | 5 | 3 | 0.38 |

New dependencies introduced: Note → SourceMetadata (navigation property)
Coupling impact: **Medium** - new entity introduces new relationship

**Failure Modes**:
| Mode | Severity | Occurrence | Detection | RPN |
|------|----------|------------|-----------|-----|
| Forgot .Include() on query | 5 | 4 | 3 | 60 |
| InMemory nav property bug | 5 | 2 | 4 | 40 |
| Orphaned SourceMetadata | 3 | 2 | 3 | 18 |
| CREATE TABLE fails | 8 | 1 | 2 | 16 |

**Evolvability Assessment**:
- Add auto-detection suggestion: **Easy** - no model change
- Add "notes citing this source" backlink: **Easy** - same as Option A
- Add source import from Zotero: **Easy** - map fields to SourceMetadata
- Add nested structure notes: **Easy** - no model change
- Add new source fields: **Easy** - just add to SourceMetadata entity

**Effort Estimate**: Medium (2-3 batches)

---

### Option C: JSON Source Column

**Summary**: Add NoteType enum on Note, store source metadata as a single
JSON string column (same pattern as EnrichmentJson).

**Data Model**:
```csharp
public enum NoteType { Regular = 0, Structure = 1, Source = 2 }

// On Note
public NoteType NoteType { get; set; } = NoteType.Regular;
[JsonIgnore]
public string? SourceJson { get; set; }  // serialized SourceMetadata
```

**Pros**:
- Only 2 new columns (NoteType + SourceJson)
- Flexible schema - source fields can change without ALTER TABLE
- Follows EnrichmentJson precedent
- InMemory compatible (just a string)

**Cons**:
- **Not queryable via SQL/LINQ** - can't filter by author, year, source type
  without JSON parsing (the spec explicitly wants "list all books, filter by
  author" which requires structured fields)
- Need serialize/deserialize logic in service layer
- Frontend needs to parse JSON from API response
- Diverges from spec requirement: "Structured fields over JSON blob: Source
  metadata needs to be queryable"

**Coupling Analysis**:
| Component | Ca (in) | Ce (out) | I (instability) |
|-----------|---------|----------|------------------|
| Note model | 8 | 0 | 0.00 |
| NoteService | 4 | 3 | 0.43 |

New dependencies introduced: None
Coupling impact: **Low** - just one new string column

**Failure Modes**:
| Mode | Severity | Occurrence | Detection | RPN |
|------|----------|------------|-----------|-----|
| JSON parse failure | 5 | 2 | 3 | 30 |
| Schema drift (old JSON vs new code) | 4 | 3 | 4 | 48 |
| Can't query by author/year | 6 | 5 | 1 | 30 |

**Evolvability Assessment**:
- Add auto-detection suggestion: **Easy**
- Add "notes citing this source" backlink: **Easy**
- Add source import from Zotero: **Easy**
- Filter sources by author/year: **Hard** - requires JSON SQL or migration
- Add new source fields: **Easy** - just add to JSON, no ALTER TABLE

**Effort Estimate**: Small (1-2 batches)

## Comparison Matrix

| Criterion | Option A: Flat | Option B: Separate | Option C: JSON |
|-----------|---------------|-------------------|---------------|
| Complexity | Low | Medium | Low |
| Pattern consistency | High (mirrors Batch 20) | Low (new pattern) | Medium (mirrors EnrichmentJson) |
| Queryability | High (SQL/LINQ) | High (SQL/LINQ) | Low (JSON parse) |
| InMemory compat | High | Medium (nav props) | High |
| Note model growth | 6 fields | 1 field + 1 entity | 2 fields |
| Evolvability | Medium | High | Medium |
| Failure risk (RPN) | 32 total | 134 total | 108 total |
| Effort | Small | Medium | Small |

## Recommendation

**Recommended Option**: Option A (Flat Columns on Note Table)

**Rationale**:

1. **Pattern consistency**: Batch 20 established the pattern of adding enum +
   nullable columns to Note for fleeting notes (NoteStatus, Source,
   EnrichmentJson, EnrichStatus, EnrichRetryCount). This is the same pattern.
   The codebase should feel consistent - someone who understands the fleeting
   notes implementation immediately understands this one.

2. **Queryability**: The spec explicitly requires filtering sources by author,
   year, and type for bibliography views. Flat columns give this for free via
   LINQ. JSON (Option C) would require JSON SQL or migration later.

3. **InMemory safety**: Option B introduces navigation properties and joins
   that have historically been problematic with InMemory provider. The project
   explicitly chose float[] over pgvector Vector to avoid InMemory issues
   (noted in MEMORY.md). Same principle applies here.

4. **Lowest failure risk**: Total RPN of 32 vs 134 (Option B) and 108
   (Option C). The "forgot .Include()" failure mode alone (RPN 60) exceeds
   Option A's entire risk profile.

5. **Right-sized**: Note grows from 14 to 20 properties. For a single-entity
   app with no complex domain logic (ADR-001), this is acceptable. The model
   is still a flat document with metadata - not a complex aggregate.

**Tradeoffs Accepted**:

- **Note entity grows by 6 fields**: Acceptable for a single-table app. If
  Note ever exceeds ~30 fields, consider extracting a SourceMetadata table
  (Option B). Currently at 20 with this change.
- **Null source fields on non-Source notes**: Acceptable. Same tradeoff as
  EnrichmentJson being null on notes without URLs. The service layer can
  enforce the invariant (clear source fields if type changes away from Source).
- **No DB-level enforcement of type-field consistency**: The service layer
  validates that source fields are only meaningful on Source notes. This
  matches the existing pattern where EnrichStatus is managed by service code,
  not DB constraints.

**Risks to Monitor**:

- **Note model bloat**: If a future feature adds more fields, revisit whether
  to extract sub-entities. Current count after this change: 20 properties.
- **Type-field consistency**: Source fields could be set on non-Source notes
  via direct DB access. Mitigated by single-user app with no external write
  access.

## Implementation Plan

### Batch 21: Backend - Note Types + Source Fields

**Backend model + service + API changes:**

1. Add `NoteType` enum and source metadata fields to Note model
2. Configure NoteType in ZettelDbContext (string conversion, default Regular)
3. Add ALTER TABLE migration SQL in Program.cs for all 6 new columns
4. Extend `INoteService` / `NoteService`:
   - `CreateAsync` gains optional `NoteType` + source metadata params
   - `UpdateAsync` gains optional `NoteType` + source metadata params
   - `ListAsync` gains optional `NoteType?` filter parameter
   - `PromoteAsync` gains optional `NoteType?` target type parameter
5. Extend `CreateNoteRequest` / `UpdateNoteRequest` with new fields
6. Extend `NotesController`:
   - `Create` passes NoteType + source fields through
   - `Update` passes NoteType + source fields through
   - `List` accepts `noteType` query parameter
   - `Promote` accepts optional `noteType` query parameter
7. Write tests:
   - Create note with each type
   - Update note type
   - List with type filter
   - Promote fleeting to specific type
   - Source fields persisted and returned
   - Source fields cleared when type changes away from Source
   - Reject NoteType on fleeting note creation

### Batch 22: Frontend - Type Selector + Source Form

**Frontend UI changes:**

1. Add `NoteType` and source metadata fields to `api/types.ts`
2. Add `noteType` and source fields to `CreateNoteRequest` / `UpdateNoteRequest`
3. Note editor:
   - Add type selector dropdown (Regular / Structure / Source)
   - Show source metadata form fields when type is Source
   - All source fields optional, collapsible section
4. Note list:
   - Add type filter tabs/pills (All / Regular / Structure / Source)
   - Visual distinction: icon or badge for Structure and Source notes
   - Pass `noteType` query parameter to API
5. Note detail view:
   - Show source metadata in a formatted header card for Source notes
   - Show type badge on all note types
6. Cmd+K search:
   - Add type filter option
7. Update `listNotes` API call to accept optional type filter

### Batch 23: Polish + Bibliography View (if needed)

- Bibliography-style card view when filtering by Source type
- Sort sources by year, author, or date added
- Graph view node coloring by type (post-MVP consideration)

## Open Questions

- [ ] Should `NoteType` be included in fulltext search index? (Probably not -
  it's a filter, not searchable content)
- [ ] Should source metadata be included in embedding input? (Could help
  semantic search find "notes from book X" but adds noise)
- [ ] Should the Cmd+K command palette have dedicated "New Structure Note" and
  "New Source Note" commands? Or just the type selector in the editor?
