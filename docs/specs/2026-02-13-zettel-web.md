# Feature Specification: Zettel-Web

**Author**: James Eastham
**Date**: 2026-02-13
**Status**: Draft
**Last Updated**: 2026-02-13 (all open questions resolved)

---

## Executive Summary

Zettel-Web is a personal Zettelkasten web application that uses vector embeddings to
solve the core problem of knowledge discovery. Built for a single user with 500-2000
existing Markdown notes, it replaces a self-hosted Affine instance with a purpose-built
tool that actively surfaces connections between notes through semantic search. The system
imports existing Markdown notes, embeds them for semantic understanding, and provides a
fast, focused writing and discovery experience.

---

## Problem Statement

### The Problem

A Zettelkasten with 500-2000 notes becomes difficult to navigate using keyword search
alone. Related notes exist but go undiscovered because:

1. **Semantic gaps**: Notes about the same concept use different terminology, so keyword
   search misses them
2. **Buried knowledge**: Older notes are effectively forgotten — they exist but never
   resurface
3. **Missing connections**: The most valuable links between notes are the non-obvious
   ones, and manual discovery doesn't scale

### Evidence

- Years of personal Zettelkasten practice with growing frustration at discovery
- Affine's keyword-only search fails to surface semantically related notes
- Known limitation of all file-based Zettelkasten tools (Obsidian, Logseq, etc.)

### Current State

Using a self-hosted Affine instance with Markdown notes linked manually. Search is
keyword-based. Discovery of connections relies entirely on the user remembering what
they've written and manually creating links.

### Impact of Not Solving

The Zettelkasten degrades over time — notes accumulate but connections don't. The
system becomes a write-mostly archive rather than a thinking tool. The value of past
notes compounds only if they can be rediscovered in relevant contexts.

---

## User

### Primary User: James (sole user)

| Attribute         | Description                                                  |
|-------------------|--------------------------------------------------------------|
| Role              | Software engineer / knowledge worker                         |
| Technical Level   | Expert — comfortable self-hosting, building custom tools     |
| Goals             | Build a personal knowledge base that actively aids thinking  |
| Frustrations      | Can't find notes by meaning, misses connections, forgets     |
|                   | what he's written                                            |
| Context           | Notes come from reading, original thinking, and work.        |
|                   | Searches happen when writing, solving problems, or exploring |
|                   | topics                                                       |

### Note-Taking Patterns

- **Input sources**: Books, articles, talks, original ideas, work/project learnings
- **Note style**: Atomic Zettelkasten notes in Markdown with direct links
- **Search triggers**: Writing (blogs, docs, talks), problem-solving, topic exploration
- **Current scale**: 500-2000 notes, growing

---

## Success Metrics

### Primary Metric

| Metric                 | Current    | Target          | Timeline   |
|------------------------|------------|-----------------|------------|
| Daily active use       | N/A        | Used most days  | 3 months   |
|                        |            | as primary PKM  | post-launch|

### Qualitative Success Signals

- Discovers connections between notes that would not have been found manually
- Note retrieval feels faster and more natural than Affine
- Adding new notes is zero-friction
- Existing notes get rediscovered and linked in new contexts

### Deal-Breaker Signals (Stop & Rethink If...)

- Adding or finding a note is slower/clunkier than Affine
- Embedding-based suggestions are noisy or irrelevant (worse than no suggestions)
- Infrastructure maintenance becomes a recurring chore

---

## Solution

### Overview

A .NET web application with three core capabilities:

1. **Markdown note management** — Create, edit, and organise atomic notes with a
   simple Markdown editor
2. **Embedding-powered semantic search** — Every note is embedded via a pluggable
   vector embedding backend; search by meaning, not just keywords
3. **Connection discovery** — Surface related notes that the user didn't explicitly
   link, using vector similarity

### Architecture (High-Level)

```
+------------------+     +------------------+     +-------------------+
|                  |     |                  |     |                   |
|   React SPA      |---->|   .NET API       |---->|   PostgreSQL      |
|   (Tailwind CSS) |     |   (ASP.NET Core) |     |   + pgvector      |
|                  |     |                  |     |                   |
+------------------+     +------------------+     +-------------------+
                              |
                              v
                     +------------------+
                     |  Embedding       |
                     |  Provider        |
                     |  (pluggable)     |
                     +------------------+
                     Start: OpenAI text-embedding-3-large
                     Future: Local model via Ollama
```

### Key Design Decisions

| Decision                        | Choice                    | Rationale                |
|---------------------------------|---------------------------|--------------------------|
| Backend framework               | ASP.NET Core Web API      | User preference, strong  |
|                                 |                           | ecosystem                |
| Frontend framework              | React SPA + Tailwind CSS  | Best ecosystem for       |
|                                 |                           | Markdown editors, graph  |
|                                 |                           | viz, and polished        |
|                                 |                           | minimal UI. Blazor's     |
|                                 |                           | component library is too |
|                                 |                           | limited for this.        |
| Database                        | PostgreSQL + pgvector     | Native vector similarity |
|                                 |                           | search, single database  |
|                                 |                           | for notes + embeddings   |
| Embedding model                 | OpenAI text-embedding-    | Best quality available;  |
|                                 | 3-large (3072 dims)       | cost is negligible at    |
|                                 |                           | this scale (~$0.07 for   |
|                                 |                           | 2000 notes). Bad         |
|                                 |                           | suggestions are a deal-  |
|                                 |                           | breaker, so maximize     |
|                                 |                           | quality.                 |
| Embedding backend               | Pluggable interface,      | Start with OpenAI, swap  |
|                                 | `IEmbeddingProvider`      | to Ollama later without  |
|                                 |                           | rewriting                |
| Note storage format             | Database (Markdown        | Better for search and    |
|                                 | content in text column)   | querying; Markdown       |
|                                 |                           | import/export for        |
|                                 |                           | portability              |
| Note IDs                        | Zettelkasten-style        | Preserves Zettelkasten   |
|                                 | timestamp IDs             | conventions; IDs like    |
|                                 | (YYYYMMDDHHmmss)          | `20260213143052` are     |
|                                 |                           | human-readable, sortable |
|                                 |                           | and unique               |
| Editor                          | Tiptap (ProseMirror-      | WYSIWYG Markdown editing |
|                                 | based) with Markdown      | — formatting renders     |
|                                 | support                   | inline as you type.      |
|                                 |                           | Large ecosystem, good    |
|                                 |                           | extension support for    |
|                                 |                           | wiki-link autocomplete.  |
| Deployment                      | Docker Compose            | Single `docker compose   |
|                                 |                           | up` for API + PostgreSQL |
|                                 |                           | + SPA. Minimal ops.      |
| Authentication                  | None (single-user)        | Out of scope for v1;     |
|                                 |                           | deploy behind VPN/tunnel |
|                                 |                           | if needed                |

### Why This Approach

- **PostgreSQL + pgvector** keeps the architecture simple — one database for content
  and vectors, no separate vector DB to maintain
- **Pluggable embedding interface** means starting with OpenAI embeddings today and
  switching to local models later requires only a new implementation of the interface
- **Simple Markdown editor** avoids the massive effort of building a rich editor
  (Affine's editor took years) while still being comfortable for a Markdown-native user
- **No auth** eliminates an entire category of complexity for a single-user tool

### Alternatives Considered

| Alternative              | Pros                    | Cons                     | Why Not              |
|--------------------------|-------------------------|--------------------------|----------------------|
| Enhance Affine with      | No new app to build     | Affine's codebase is     | Too coupled to       |
| plugins                  |                         | complex; embedding        | Affine's roadmap     |
|                          |                         | integration is deep work  |                      |
| Obsidian + plugins       | Existing ecosystem,     | Still file-based search; | Doesn't solve the    |
| (e.g. Smart Connections) | graph view built-in     | plugin quality varies     | core semantic gap    |
|                          |                         |                           | well enough          |
| Python (FastAPI) stack   | Natural fit for ML/     | User prefers .NET;        | Embedding is via API |
|                          | embeddings              | maintaining Python infra  | anyway, so ML        |
|                          |                         | is more overhead          | ecosystem not needed |
| Dedicated vector DB      | Purpose-built for       | Extra service to run;     | pgvector is enough   |
| (Qdrant, Pinecone)       | similarity search       | added maintenance         | for this scale       |

---

## User Stories

### Epic 1: Note Import & Management

#### Story 1.1: Import Markdown notes
**As a** Zettelkasten user migrating from Affine
**I want** to import my existing Markdown files into the system
**So that** I can start using the new tool with my full knowledge base

**Acceptance Criteria**:
- [ ] Given a folder of .md files, when I trigger import, then all notes are
      created in the database with their Markdown content preserved
- [ ] Given notes with `[[wiki-links]]` or `[markdown](links)`, when imported,
      then internal links are preserved and resolved where possible
- [ ] Given an imported note, when I export it, then I get valid Markdown that
      matches the original content
- [ ] Given 2000 notes, when I import them, then the process completes in under
      5 minutes

**Notes**: Affine exports raw Markdown. Import should handle standard Markdown
with wiki-links (`[[note-title]]`) and standard Markdown links.

#### Story 1.2: Create a new note
**As a** knowledge worker
**I want** to create a new atomic note with a Markdown editor
**So that** I can capture ideas quickly

**Acceptance Criteria**:
- [ ] Given I'm on any page, when I trigger "new note" (keyboard shortcut),
      then a new note editor opens immediately (< 200ms)
- [ ] Given the editor, when I write Markdown, then I see a live preview
- [ ] Given I save a note, when it's stored, then it's automatically embedded
      for semantic search (async, non-blocking)

#### Story 1.3: Edit an existing note
**As a** knowledge worker
**I want** to edit any existing note
**So that** I can refine and update my knowledge

**Acceptance Criteria**:
- [ ] Given a note, when I click edit, then the Markdown editor opens with the
      note content
- [ ] Given I edit and save a note, when the content changes, then the embedding
      is re-generated automatically
- [ ] Given I'm editing, when I type `[[`, then I get autocomplete suggestions
      for linking to other notes (by title)

#### Story 1.4: Export notes as Markdown
**As a** user concerned about data portability
**I want** to export all my notes as Markdown files
**So that** I'm never locked into this tool

**Acceptance Criteria**:
- [ ] Given my note collection, when I trigger export, then I get a zip/folder
      of .md files
- [ ] Given exported files, when I read them, then all content and links are
      preserved in standard Markdown

### Epic 2: Semantic Search

#### Story 2.1: Search notes by meaning
**As a** knowledge worker looking for related ideas
**I want** to search my notes using natural language
**So that** I find relevant notes even when I use different words than the original

**Acceptance Criteria**:
- [ ] Given a search query like "how to handle team conflict", when I search,
      then notes about "difficult conversations", "management challenges", or
      "interpersonal skills" are returned even if they don't contain the exact
      search terms
- [ ] Given search results, when displayed, then each result shows the note
      title, a relevant snippet, and a similarity score
- [ ] Given a query, when results return, then response time is under 500ms
      for a 2000-note collection
- [ ] Given a search, when I click a result, then I navigate directly to that
      note

#### Story 2.2: Hybrid search (semantic + keyword)
**As a** user who sometimes knows the exact term
**I want** search to combine semantic similarity with keyword matching
**So that** I get the best of both approaches

**Acceptance Criteria**:
- [ ] Given a search for an exact term like "pgvector", when I search, then
      notes containing that exact term rank highly even if semantically distant
- [ ] Given a vague conceptual search, when I search, then semantic results
      dominate over keyword matches

#### Story 2.3: Embed notes on save
**As a** the system
**I want** to automatically generate and store embeddings when notes are created
      or updated
**So that** all notes are always searchable by meaning

**Acceptance Criteria**:
- [ ] Given a new note is saved, when processing completes, then its embedding
      is stored in pgvector
- [ ] Given an existing note is edited, when saved, then its embedding is
      regenerated
- [ ] Given the embedding API is unavailable, when a note is saved, then the
      note is still saved and embedding is retried later
- [ ] Given bulk import of 2000 notes, when embedding completes, then all notes
      have embeddings (with progress indication)

### Epic 3: Connection Discovery (v2)

#### Story 3.1: Related notes sidebar
**As a** knowledge worker viewing a note
**I want** to see semantically related notes in a sidebar
**So that** I discover connections I wouldn't have found manually

**Acceptance Criteria**:
- [ ] Given I'm viewing a note, when the page loads, then a sidebar shows the
      top 5-10 most semantically similar notes
- [ ] Given the sidebar results, when displayed, then they exclude notes already
      explicitly linked from the current note
- [ ] Given a sidebar suggestion, when I click it, then I navigate to that note
- [ ] Given a sidebar suggestion, when I click "link", then a link is added to
      my current note

#### Story 3.2: Knowledge graph visualisation
**As a** knowledge worker wanting to see the big picture
**I want** a visual graph of my notes and their connections
**So that** I can identify clusters, gaps, and unexpected relationships

**Acceptance Criteria**:
- [ ] Given my note collection, when I open graph view, then I see nodes (notes)
      connected by edges (explicit links + strong semantic similarity)
- [ ] Given the graph, when I click a node, then I see the note title and can
      navigate to it
- [ ] Given the graph, when I zoom/pan, then performance remains smooth for
      2000 nodes
- [ ] Given the graph, when I filter by tag/topic, then only relevant notes
      are shown

#### Story 3.3: Daily discovery digest
**As a** knowledge worker who forgets what they've written
**I want** the system to surface a few relevant or forgotten notes periodically
**So that** old knowledge stays active in my thinking

**Acceptance Criteria**:
- [ ] Given I open the app, when the home page loads, then I see 3-5 suggested
      notes to revisit
- [ ] Given the suggestions, when generated, then they favour notes that are
      semantically relevant to recently viewed/created notes but haven't been
      visited in a while
- [ ] Given a suggestion, when I dismiss it, then it doesn't reappear for at
      least 30 days

---

## Scope

### In Scope (v1 — MVP)

- Markdown note import (from files)
- Markdown note CRUD with Tiptap WYSIWYG Markdown editor
- Tag/category system (notes can have multiple tags)
- Embedding generation on save (pluggable backend, start with OpenAI)
- Semantic search across all notes
- Hybrid search (semantic + keyword)
- Markdown export
- Single-user, no auth
- Wiki-link autocomplete (`[[` trigger)
- Docker Compose deployment

### In Scope (v2 — Discovery)

- Related notes sidebar
- Knowledge graph visualisation
- Daily discovery digest

### Out of Scope

- Multi-user / authentication / sharing
- Mobile application (web-responsive is fine)
- Real-time collaboration
- AI writing assistance / auto-summarisation (may revisit)
- Whiteboard / canvas features
- File attachments / image management (v1 — text notes only)

### Future Considerations

- Local embedding model support (Ollama) when hardware allows
- AI-powered features: summarisation, question-answering over notes
- Browser extension for quick capture
- Spaced repetition integration

---

## Risks & Mitigations

| Risk                          | Likelihood | Impact | Mitigation                    |
|-------------------------------|------------|--------|-------------------------------|
| Embedding quality too low     | Medium     | High   | Start with best available     |
| for meaningful connections    |            |        | model (OpenAI text-embedding- |
|                               |            |        | 3-large); tune similarity     |
|                               |            |        | thresholds empirically        |
| Building a rich enough editor | Medium     | Medium | Use existing Markdown editor  |
| is a rabbit hole              |            |        | component (e.g. Monaco,       |
|                               |            |        | CodeMirror); don't build      |
|                               |            |        | from scratch                  |
| pgvector performance at scale | Low        | Medium | 2000 notes is small for       |
|                               |            |        | pgvector; add HNSW index      |
|                               |            |        | if needed                     |
| Embedding API costs           | Low        | Low    | At 2000 notes, embedding cost |
|                               |            |        | is negligible (< $1 total);   |
|                               |            |        | per-search cost is minimal    |
| Two codebases to maintain     | Medium     | Medium | Keep React frontend thin —    |
| (.NET API + React SPA)        |            |        | business logic lives in the   |
|                               |            |        | API. Frontend is purely       |
|                               |            |        | presentation + search UX.     |
| Scope creep into rich editor  | High       | High   | Hard constraint: Markdown     |
|                               |            |        | only for v1. No block editor. |
| Maintenance burden of         | Medium     | High   | Use Docker Compose for single |
| self-hosted infra             |            |        | command deploy; PostgreSQL is  |
|                               |            |        | low-maintenance               |

---

## Dependencies

| Dependency                  | Owner    | Status    | Blocker? |
|-----------------------------|----------|-----------|----------|
| Cloud embedding API access  | James    | Available | No       |
| PostgreSQL + pgvector       | James    | To set up | No       |
| Affine note export          | James    | Confirmed | No —     |
|                             |          | (raw MD)  | clean MD |
| Markdown editor component   | OSS      | Available | No       |
| (JS library)                |          |           |          |

---

## Resolved Questions

- [x] **Affine export format**: Exports raw Markdown. No custom parsing needed.
- [x] **Frontend**: React SPA + Tailwind CSS. Blazor's component ecosystem is
      too limited for a polished Markdown editor and graph visualisation.
- [x] **Embedding model**: OpenAI text-embedding-3-large (3072 dimensions).
      Maximize quality at negligible cost for this scale.
- [x] **Note IDs**: Zettelkasten-style timestamp IDs (YYYYMMDDHHmmss).
- [x] **Deployment**: Docker Compose (API + PostgreSQL + SPA in one command).
- [x] **Tags**: In scope for v1. Notes can have multiple free-form tags.
- [x] **Editor**: Tiptap (ProseMirror-based) WYSIWYG Markdown editor. Renders
      formatting inline as you type. Extensible for wiki-link autocomplete.

## Open Questions

All major questions resolved. Minor implementation details to decide during build:

- [ ] Tag UX: free-form text tags or predefined tag list? (Recommend free-form
      with autocomplete from existing tags)
- [ ] Tiptap extensions: confirm the `@tiptap/extension-link` and a custom
      wiki-link extension cover the autocomplete needs

---

## Suggested Phasing

### Phase 1: Foundation (weeks 1-2)

- [ ] Set up ASP.NET Core API project with PostgreSQL + pgvector
- [ ] Set up React SPA with Tailwind CSS (Vite)
- [ ] Note model: create, read, update, delete (API + UI)
- [ ] Zettelkasten timestamp ID generation (YYYYMMDDHHmmss)
- [ ] Markdown import endpoint (accepts folder of .md files)
- [ ] Tiptap WYSIWYG Markdown editor (view + edit)
- [ ] Tag system (free-form tags on notes, autocomplete from existing)
- [ ] Embedding generation on note save (OpenAI text-embedding-3-large)
- [ ] `IEmbeddingProvider` interface with OpenAI implementation

### Phase 2: Search (weeks 3-4)

- [ ] Semantic search API endpoint (vector cosine similarity)
- [ ] Full-text search with PostgreSQL tsvector
- [ ] Hybrid search: weighted combination of semantic + keyword
- [ ] Search UI: search bar, results with titles, snippets, scores
- [ ] Bulk re-embedding endpoint (for model changes)

### Phase 3: Daily Driver (weeks 5-6)

- [ ] Wiki-link autocomplete (`[[` trigger via Tiptap extension)
- [ ] Markdown export (all notes as .md zip)
- [ ] Keyboard shortcuts (new note, search focus, save)
- [ ] Polish: loading states, error handling, responsive layout
- [ ] Docker Compose deployment (API + PostgreSQL + SPA)

### Phase 4: Discovery (weeks 7+)

- [ ] Related notes sidebar (vector similarity, exclude existing links)
- [ ] Knowledge graph visualisation (D3.js or Sigma.js)
- [ ] Daily discovery digest on home page
- [ ] Tag/category system

---

## Appendix

### Zettelkasten Principles to Preserve

The tool should support, not undermine, Zettelkasten methodology:

1. **Atomicity**: Each note captures one idea. The tool should encourage small,
   focused notes.
2. **Connectivity**: Notes derive value from links. The tool should make linking
   easy and surface missing connections.
3. **Personal voice**: Notes are in your own words. No AI-generated content in v1.
4. **Organic growth**: The system grows bottom-up. No forced taxonomies or rigid
   folder structures.

### Reference: Embedding Dimensions & Storage

| Model                          | Dimensions | Approx. storage per note |
|--------------------------------|------------|--------------------------|
| OpenAI text-embedding-3-small  | 1536       | ~6 KB                    |
| OpenAI text-embedding-3-large  | 3072       | ~12 KB                   |
| Local (e.g. nomic-embed-text)  | 768        | ~3 KB                    |

For 2000 notes: 6-24 MB total vector storage. Negligible.

### Technical Notes for Engineering

- **pgvector index**: Use HNSW index (`CREATE INDEX ON notes USING hnsw
  (embedding vector_cosine_ops)`) for fast approximate nearest neighbour search
- **Chunking strategy**: For atomic Zettelkasten notes (typically < 500 words),
  embed the full note as a single vector. No chunking needed.
- **Embedding interface**: Define `IEmbeddingProvider` with `Task<float[]>
  GenerateEmbedding(string text)` and `Task<float[][]>
  GenerateEmbeddings(string[] texts)` — implementations for OpenAI, Ollama, etc.
- **Search ranking**: Combine cosine similarity score with full-text search rank
  using a weighted formula. Tune weights empirically.
