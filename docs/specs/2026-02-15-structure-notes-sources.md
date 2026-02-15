# Feature Specification: Structure Notes & Sources

**Author**: James Eastham
**Date**: 2026-02-15
**Status**: Draft
**Last Updated**: 2026-02-15

---

## Executive Summary

Add Zettelkasten-native note types to Zettel-Web: **Structure Notes** (hub notes
that organize and connect other notes into topic maps) and **Source Notes**
(bibliography entries that track where ideas originated). This transforms
Zettel-Web from a flat note collection into a proper Zettelkasten with
navigable topic hierarchies and traceable provenance.

---

## Problem Statement

### The Problem

With 200+ notes, the collection has outgrown flat listing and search. Three
specific pain points:

1. **No big picture** - Notes exist in isolation. There's no way to see how
   notes on a topic fit together into a larger argument or knowledge area.
2. **Hard to navigate** - Finding related notes requires search every time.
   There are no curated paths through the knowledge base.
3. **Missing provenance** - No way to track where ideas came from (books,
   articles, URLs, podcasts). The `Source` field on fleeting notes only
   records the capture channel ("web"/"email"/"telegram"), not the
   intellectual origin.

### Evidence

- 200+ notes in the system with tag-based organization only
- The existing graph view shows connections exist but provides no curated
  navigation paths
- The Zettelkasten method (zettelkasten.de) identifies structure notes and
  source tracking as foundational to the method's effectiveness at scale

### Current State

- Notes have `NoteStatus` (Permanent/Fleeting) but no concept of note *type*
- Tags provide informal categorization but no structural hierarchy
- Wikilinks connect notes but without curatorial context
- Graph view shows raw connections but can't distinguish curated paths
  from incidental links

### Impact of Not Solving

Without structure notes, the knowledge base becomes increasingly difficult to
navigate as it grows. Without source tracking, intellectual provenance is lost
- ideas become disconnected from the material that inspired them.

---

## Users

### Primary Persona: James

| Attribute | Description |
|-----------|-------------|
| Role | Solo knowledge worker / developer |
| Technical Level | Expert |
| Goals | Build a personal knowledge base that compounds over time |
| Frustrations | Can't see the big picture, can't trace idea origins |
| Context | Daily reading, writing, and research workflow |

### Workflow Context

- Creates notes while reading books, articles, and documentation
- Periodically reviews notes to find connections and build topic maps
- Wants to trace any idea back to its source material
- Needs both bottom-up emergence (notes → structure) and top-down
  navigation (structure → notes)

---

## Success Metrics

### Primary Metric

| Metric | Current | Target | Timeline |
|--------|---------|--------|----------|
| Notes with type assigned | 0% | 30%+ of permanent notes | 4 weeks |

### Qualitative Success

- Can click into a structure note and traverse an entire topic area
  through curated links
- For any note, can trace back to the original source that inspired it
- Structure notes form organically as the collection grows

### Guardrail Metrics

- Note creation friction should NOT increase for regular notes
- Existing notes continue to work unchanged (type defaults to Regular)

---

## Solution

### Overview

Add a `NoteType` enum (Regular, Structure, Source) to permanent notes.
Source notes get structured metadata fields (author, title, URL, year,
source type). Structure notes use the existing Tiptap editor with grouped
sections of wikilinks. The home page gains type filtering, and the editor
adapts its UI based on note type.

### Data Model Changes

```
NoteType enum: Regular (default) | Structure | Source

Source metadata (only populated when NoteType = Source):
  - SourceAuthor: string?
  - SourceTitle: string?
  - SourceUrl: string?
  - SourceYear: int?
  - SourceType: string? ("book" | "article" | "web" | "podcast" | "other")
```

**Key constraints:**
- `NoteType` only applies to Permanent notes. Fleeting notes have no type.
- Types are **mutually exclusive** - a note is exactly one of
  Regular/Structure/Source.
- `NoteType` defaults to `Regular` for all existing notes (zero migration).

### Why This Approach

- **Structured fields over JSON blob**: Source metadata needs to be
  queryable (list all books, filter by author). Structured fields enable
  this without JSON parsing.
- **Enum over tags**: Tags could achieve 80% of this (user's own
  assessment), but a first-class type enables type-specific UI, filtering,
  and future features like auto-detection.
- **Mutually exclusive types**: Keeps the model simple. A structure note
  that references a source can wikilink to a Source note rather than being
  both types.

### Alternatives Considered

| Alternative | Pros | Cons | Why Not |
|-------------|------|------|---------|
| Tags only (#structure, #source) | Zero schema changes, flexible | No type-specific UI, no structured source fields, informal | Gets 80% there but limits future features |
| JSON metadata blob | Flexible schema, one field | Can't query efficiently, no type safety | Source queries (by author, year) need structure |
| Markdown frontmatter | No DB changes, portable | Parsing complexity, no DB-level filtering | Moves structure out of the queryable layer |

---

## User Stories

### Epic: Note Types

#### Story 1: Set Note Type on Create

**As a** Zettelkasten user
**I want** to specify whether a new permanent note is Regular, Structure,
or Source
**So that** the system understands the role this note plays

**Acceptance Criteria:**
- [ ] Note creation API accepts optional `noteType` parameter
- [ ] Defaults to `Regular` if not specified
- [ ] Only valid for Permanent notes (rejected for Fleeting)
- [ ] UI shows type selector when creating a new note

#### Story 2: Change Note Type on Edit

**As a** user reviewing my notes
**I want** to change a note's type after creation
**So that** I can reclassify notes as my understanding evolves

**Acceptance Criteria:**
- [ ] Note update API accepts optional `noteType` parameter
- [ ] Changing to Source shows source metadata fields
- [ ] Changing from Source clears source metadata (with confirmation)
- [ ] Type change is reflected immediately in the UI

#### Story 3: Filter Notes by Type

**As a** user navigating my collection
**I want** to filter the notes list by type
**So that** I can quickly find all my structure notes or sources

**Acceptance Criteria:**
- [ ] Home page has type filter (All / Regular / Structure / Source)
- [ ] Filter persists across navigation
- [ ] Count badge shows number of each type
- [ ] Cmd+K search can filter by type

### Epic: Source Notes

#### Story 4: Create Source Note with Metadata

**As a** reader capturing a reference
**I want** to create a source note with structured metadata
**So that** I can build a queryable bibliography

**Acceptance Criteria:**
- [ ] When type is Source, editor shows metadata fields:
      Author, Title, URL, Year, Source Type
- [ ] Source Type is a dropdown: Book, Article, Web, Podcast, Other
- [ ] All source fields are optional (progressive disclosure)
- [ ] Source metadata displays in a formatted header on the note view

#### Story 5: List Sources as Bibliography

**As a** researcher
**I want** to see all my sources in a bibliography-style list
**So that** I can review what I've read and find gaps

**Acceptance Criteria:**
- [ ] Filtering by Source type shows bibliography-style cards
- [ ] Cards show: Author, Title, Year, Source Type, linked note count
- [ ] Sortable by year, author, or date added
- [ ] Click through to the full source note

### Epic: Structure Notes

#### Story 6: Create Structure Note with Sections

**As a** knowledge organizer
**I want** to create a structure note with grouped sections of links
**So that** I can build curated topic maps

**Acceptance Criteria:**
- [ ] Structure notes use the existing Tiptap editor
- [ ] Encouraged pattern: H2/H3 headings with wikilinks beneath
- [ ] No special structure-note editor needed for MVP
- [ ] Structure notes are visually distinguished in the notes list

#### Story 7: Structure Note Suggestion (Post-MVP)

**As a** note-taker
**I want** the system to suggest when a note could be a structure note
**So that** topic maps emerge organically

**Acceptance Criteria:**
- [ ] When a note has 5+ outgoing wikilinks, show a subtle hint
- [ ] Hint: "This note links to N other notes - mark as structure note?"
- [ ] Dismissible, doesn't reappear for that note
- [ ] One-click to change type to Structure

---

## Scope

### In Scope (MVP)

- `NoteType` enum on Note model (Regular/Structure/Source)
- Source metadata fields (Author, Title, URL, Year, SourceType)
- Type filter on home page and Cmd+K
- Source-specific form fields in editor
- Visual distinction for Structure and Source notes in list
- API changes for type + source metadata
- ALTER TABLE migration for existing database

### Out of Scope

- Citation formatting (APA/MLA/Chicago)
- Zotero or external reference manager import
- Nested structure notes (structure notes linking to other structure notes
  as a hierarchy)
- Auto-detection / suggestion UI (post-MVP)
- Drag-and-drop section ordering in structure notes
- Source note backlinks ("which notes cite this source?") - future feature

### Future Considerations

- Auto-detect structure notes based on outgoing wikilink count
- "Which notes cite this source?" backlink view
- Structure note outline/tree visualization
- Import from Zotero/BibTeX
- Promote fleeting note directly to Source type

---

## Risks & Mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Type adds friction to note creation | Medium | Medium | Default to Regular, type selector is optional |
| Source fields rarely used | Low | Low | All fields optional, progressive disclosure |
| Mutually exclusive types too rigid | Low | Medium | Can revisit if real need emerges; wikilinks bridge types |
| ALTER TABLE on existing DB | Low | High | IF NOT EXISTS guards, tested migration SQL |

---

## Dependencies

| Dependency | Owner | Status | Blocker? |
|------------|-------|--------|----------|
| EnsureCreated migration pattern | Existing | Done (DB-001) | No |
| Tiptap wikilinks | Existing | Done (F-series) | No |
| Home page filtering | Existing | Done (NoteStatus filter) | No |

---

## Open Questions

- [ ] Should promoting a fleeting note allow choosing the target type
      (Regular/Structure/Source) at promotion time?
- [ ] Should the graph view visually distinguish node types
      (e.g., different colors for Structure/Source)?
- [ ] Should source metadata be searchable via fulltext/semantic search?

---

## Technical Notes

- `NoteType` stored as string enum (same pattern as `NoteStatus`)
- Source fields are nullable columns on the Note table (not a separate table)
  to keep the model simple and avoid joins
- ALTER TABLE migration in Program.cs startup block with IF NOT EXISTS
  guards (established pattern from Batch 20)
- Frontend: type selector component, conditional source metadata form
- API: extend CreateNoteRequest/UpdateNoteRequest with type + source fields
