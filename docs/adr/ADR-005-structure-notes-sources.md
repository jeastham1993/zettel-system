# ADR-005: Structure Notes & Source Metadata as Flat Columns

Date: 2026-02-15
Status: Proposed

## Context

Zettel-Web has grown to 200+ notes with only tags and wikilinks for
organization. The Zettelkasten method identifies two missing concepts:

1. **Structure Notes** (hub notes) - meta-notes that organize other notes into
   curated topic maps with grouped sections
2. **Source Notes** (bibliography) - reference notes tracking intellectual
   provenance (books, articles, URLs, podcasts)

We need to add a `NoteType` concept to distinguish Regular, Structure, and
Source notes, and store structured source metadata (author, title, URL, year,
source type) for bibliography queries.

Three approaches were evaluated:
- **Option A**: Flat columns on the Note table (NoteType enum + 5 source fields)
- **Option B**: Separate SourceMetadata entity with 1:0..1 relationship
- **Option C**: JSON column for source metadata (like EnrichmentJson)

## Decision

Use **flat columns on the Note table** (Option A):

- Add `NoteType` enum (Regular/Structure/Source) stored as string, default
  Regular
- Add 5 nullable source metadata columns directly on Note: `SourceAuthor`,
  `SourceTitle`, `SourceUrl`, `SourceYear`, `SourceType`
- NoteType only applies to Permanent notes; Fleeting notes are always Regular
- Types are mutually exclusive
- ALTER TABLE migration with IF NOT EXISTS guards (established pattern)

## Consequences

### Positive

- Follows the exact pattern established by Batch 20 (NoteStatus, Source,
  EnrichStatus added as flat columns to Note)
- Source fields are individually queryable via LINQ (filter by author, year,
  source type for bibliography views)
- InMemory test compatible - no navigation properties or joins
- Lowest failure risk (total RPN 32 vs 134 for Option B)
- Simplest ALTER TABLE migration - no new table creation
- Zero-migration for existing data (NoteType defaults to Regular)

### Negative

- Note entity grows from 14 to 20 properties
- Source fields are null on ~95% of notes (non-Source notes)
- No DB-level enforcement that source fields are only set when NoteType=Source
  (service layer enforces this)

### Neutral

- Same tradeoff as EnrichmentJson being null on notes without URLs
- If Note ever exceeds ~30 properties, should extract sub-entities
- Structure notes need no special model support - they use the existing
  Tiptap editor with headings and wikilinks

## Alternatives Considered

### Separate SourceMetadata Entity (Option B)

Not chosen because: Introduces navigation properties and `.Include()` calls
that have historically been problematic with InMemory provider (the project
chose float[] over pgvector Vector for the same reason). Highest failure risk
(RPN 134) driven by "forgot .Include()" (RPN 60) and InMemory nav property
bugs (RPN 40). Pattern divergence from Batch 20's flat-column approach.

### JSON Source Column (Option C)

Not chosen because: Source metadata needs to be queryable (filter by author,
year, type for bibliography views). The spec explicitly chose "structured
fields over JSON blob" for this reason. JSON would require migration or
JSON SQL to support bibliography filtering later.

## Related Decisions

- [ADR-001](ADR-001-backend-architecture.md): Simple Layered Architecture -
  flat columns fit the simple model
- [ADR-003](ADR-003-fleeting-notes-architecture.md): Fleeting Notes - same
  pattern of extending Note with enum + flat columns

## Notes

- Design doc: [docs/design-structure-notes-sources.md](../design-structure-notes-sources.md)
- Spec: [docs/specs/2026-02-15-structure-notes-sources.md](../specs/2026-02-15-structure-notes-sources.md)
- Implementation planned in Batches 21-23 (backend, frontend, polish)
