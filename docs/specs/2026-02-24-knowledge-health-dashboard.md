# Feature Specification: Knowledge Health Dashboard

**Author**: James Eastham
**Date**: 2026-02-24
**Status**: Draft
**Last Updated**: 2026-02-24

---

## Executive Summary

The Knowledge Health Dashboard gives you a weekly view of the shape of your knowledge base — where clusters are forming, which notes are recent and still isolated, and where you've never generated content from. The primary value is replacing a vague sense that "notes disappear into the system" with a concrete, actionable picture of KB structure. It surfaces orphaned notes alongside AI-suggested connections that can be accepted in one click (with preview confirmation).

---

## Problem Statement

### The Problem

The knowledge base is a black box. Two concrete frustrations drive this:

1. **"I don't know which areas are worth developing further"** — There's no signal about which topic areas are rich and ready to generate from vs. thin and needing more writing investment.
2. **"Notes go in and I forget about them"** — Notes added recently may have no connections and never resurface. They're effectively lost until you happen to search for them.

### Evidence

Validated by user interview. Both problems identified as equally important.

### Current State

- The graph service already computes wikilink and semantic edges per note
- `UsedSeedNote` tracks which notes have been generation seeds
- `EmbedStatus` tracks embedding completeness
- None of this data is surfaced in the UI

### Impact of Not Solving

The content generation flywheel depends on graph density. Sparse areas produce weaker generation. Without visibility into KB health, there's no mechanism to deliberately improve the graph — users add notes randomly and hope the system figures it out.

---

## Users

### Primary Persona: James (solo knowledge worker / indie creator)

| Attribute | Description |
|-----------|-------------|
| Role | Technical founder / content creator |
| Technical Level | High — comfortable with concepts like graph density, embeddings |
| Goals | Turn accumulated knowledge into published content consistently |
| Frustrations | Notes disappear; can't tell which areas are thin vs. rich |
| Context | Weekly review session, usually at desk |

### Usage Trigger

Once per week, as part of a review ritual — not contextually during capture.

---

## Success Metrics

### Primary Metric

| Metric | Current | Target | Timeline |
|--------|---------|--------|----------|
| Orphaned notes (0 connections) | Unknown | Trending down over 4 weeks | 4 weeks post-launch |

### Qualitative Success Signal

*"I understand the shape of my knowledge for the first time."*

The real success is a mental model shift — the user develops a sense of which topic areas are strong and where to invest writing effort. This is hard to measure directly but shows up as: intentional note-adding in thin areas, higher graph density over time, more diverse generation seeds.

### Guardrail Metrics

- Page load time for the health overview must be < 2 seconds (queries run on existing indexed data)
- Auto-link insertion must never corrupt note HTML (preview + confirm gate)

### Validation Approach

Check-in after 4 weeks of weekly use: are orphan counts trending down? Is the user finding the clusters list useful for directing generation seeds?

---

## Solution

### Overview

A new `/health` page with a hybrid layout:
- **Scorecard header**: key KB stats at a glance
- **Three actionable lists** below: New & Unconnected, Richest Clusters, Never Used as Seed

For the "New & Unconnected" list: each orphaned note shows a count of AI-suggested connections. Clicking a note opens a side panel showing the top suggested links. Accepting a suggestion shows a preview modal, then inserts `[[NoteTitle]]` at the end of the orphan note's content.

### Why This Approach

The user thinks in **emergent semantic clusters** (not tags) and wants **recency** as a filter for orphans (recent additions concern them most). The "focused lists" pattern matched their stated weekly review ritual — a page they actively open with intent, not passive notifications.

One-click auto-insert was chosen over manual editing for friction reduction, but a preview gate was added because auto-modifying note content without visibility felt risky to the user.

### Alternatives Considered

| Alternative | Pros | Cons | Why Not |
|-------------|------|------|---------|
| Visual graph with health overlays | Visually rich, already have graph UI | Slow to load; hard to act from | Action is the goal, not exploration |
| Tag-based cluster grouping | Simple to query | User explicitly doesn't think in tags | Misses the semantic/emergent clusters need |
| Notifications for new orphans | Proactive | Adds noise; user wants weekly pull, not push | Rhythm mismatch |

---

## User Stories

### Epic: Knowledge Health Overview

#### Story 1: View KB Health Scorecard
**As a** knowledge worker
**I want** to see headline metrics about my KB at a glance
**So that** I can assess its overall health in seconds during a weekly review

**Acceptance Criteria**:
- [ ] Given I navigate to `/health`, I see a scorecard with: total permanent notes, % with embeddings completed, total orphaned notes (0 connections), average connections per note
- [ ] Given embedding is still pending for some notes, the % figure reflects only `EmbedStatus.Completed` notes
- [ ] Given the page loads, all metrics load in a single API call under 2 seconds

---

#### Story 2: View New & Unconnected Notes
**As a** knowledge worker
**I want** to see recently-added notes that have no connections
**So that** I can act on them before they disappear into the system

**Acceptance Criteria**:
- [ ] Given notes added in the last 30 days with zero wikilink or semantic edges, they appear in the "New & Unconnected" list sorted newest-first
- [ ] Given a note in the list, it shows: title, date added, and count of available suggested connections ("3 suggestions")
- [ ] Given there are no recent orphans, the section shows "No recent orphans — your new notes are well connected"

---

#### Story 3: View Richest Semantic Clusters
**As a** knowledge worker
**I want** to see which topic areas have the most interconnected notes
**So that** I know where my KB is richest and can direct generation seeds accordingly

**Acceptance Criteria**:
- [ ] Given the top 5 connected clusters in the graph, each cluster is shown with: cluster anchor title (the hub note with highest edge count), note count in the cluster, a link to the hub note
- [ ] Given clusters are computed from connected components at semantic similarity ≥ 0.7 and all wikilinks, the list reflects the actual graph structure
- [ ] Clusters are ranked by note count descending

---

#### Story 4: View Notes Never Used as Seeds
**As a** knowledge worker
**I want** to see notes that have never been a generation seed
**So that** I can identify untapped content potential in my KB

**Acceptance Criteria**:
- [ ] Given notes not in `UsedSeedNote` that have `EmbedStatus.Completed` and are `NoteStatus.Permanent`, they appear in the "Never Used as Seed" list
- [ ] The list is sorted by edge count descending (most connected unused seeds first — highest generation potential)
- [ ] Each item shows: title, connection count, and a direct "Generate from this note" action that pre-seeds the discovery flow

---

### Epic: Orphan Connection Suggestions

#### Story 5: See AI-Suggested Connections for an Orphan Note
**As a** knowledge worker
**I want** to see which existing notes an orphan note is semantically similar to
**So that** I can decide which connections to create

**Acceptance Criteria**:
- [ ] Given I click on an orphan in the New & Unconnected list, a side panel opens showing top 5 semantically similar notes (by embedding cosine similarity)
- [ ] Each suggestion shows: target note title, a similarity score indicator (High / Medium), and an "Add Link" button
- [ ] Given no suggestions exist (no embeddings available or no similar notes above threshold), the panel shows "No suggestions found — embed this note first or add content"
- [ ] The suggestions endpoint returns results in < 1 second (uses existing pgvector index)

---

#### Story 6: Accept a Suggested Connection (Preview & Confirm)
**As a** knowledge worker
**I want** to insert a wikilink into an orphan note with preview before saving
**So that** I don't accidentally corrupt note content

**Acceptance Criteria**:
- [ ] Given I click "Add Link" on a suggestion, a modal opens showing: the orphan note's current content with `[[TargetTitle]]` appended as a new paragraph, highlighted
- [ ] Given I confirm in the modal, the API updates the note content and the note is removed from the orphan list
- [ ] Given I cancel in the modal, no changes are made
- [ ] Given the note content is updated, the note's `UpdatedAt` is refreshed and the embedding is marked `EmbedStatus.Stale` (so it re-embeds with the new content)
- [ ] Given a note had 0 connections before and now has 1 after linking, the orphan count in the scorecard decrements on next load

---

## API Design

### `GET /api/health/overview`

Returns all scorecard and list data in a single call.

**Response**:
```json
{
  "scorecard": {
    "totalNotes": 342,
    "embeddedPercent": 87,
    "orphanCount": 23,
    "averageConnections": 4.2
  },
  "newAndUnconnected": [
    { "id": "...", "title": "Async Rust", "createdAt": "...", "suggestionCount": 3 }
  ],
  "richestClusters": [
    { "hubNoteId": "...", "hubTitle": "Systems Design", "noteCount": 42 }
  ],
  "neverUsedAsSeeds": [
    { "id": "...", "title": "Mental Models", "connectionCount": 12 }
  ]
}
```

### `GET /api/health/orphan/{noteId}/suggestions`

Returns top 5 semantically similar notes for a given orphan note.

**Response**:
```json
[
  { "noteId": "...", "title": "Ownership in Rust", "similarity": 0.91 }
]
```

### `POST /api/health/orphan/{noteId}/link`

Inserts `[[targetTitle]]` as a new paragraph at the end of the orphan note's HTML content.

**Request body**:
```json
{ "targetNoteId": "..." }
```

**Response**: `200 OK` with updated note, or `400` if target note not found.

**Side effect**: Sets orphan note's `EmbedStatus` to `Stale`.

---

## Scope

### In Scope (MVP)
- Scorecard header (4 metrics)
- New & Unconnected list (last 30 days, 0-edge notes)
- Richest Clusters list (top 5 connected components)
- Never Used as Seed list (sorted by edge count)
- Orphan suggestion side panel (top 5 semantic matches)
- Preview-and-confirm wikilink insertion

### Out of Scope
- Filtering the health view by tag or time range
- Editing notes inline from the health dashboard (navigation to editor is acceptable)
- Trend charts (e.g., orphan count over time) — no history stored yet
- Cluster naming by AI (cluster is named after hub note title)
- Mobile-responsive layout — desktop-first for weekly review ritual

### Future Considerations
- Track KB health metrics over time (requires a daily snapshot table)
- Tag-based cluster breakdown as a secondary view
- "Generate from this cluster" shortcut from the Richest Clusters section
- Scheduled weekly digest email/notification summarising health changes

---

## Risks & Mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Cluster computation is slow on large KBs | Medium | Medium | Run graph connected-components server-side in the health endpoint; cache result for 5 min or until note updated |
| Wikilink insertion corrupts Tiptap HTML | Low | High | Always append as a new paragraph `<p>[[Title]]</p>` — never inject mid-content. Preview modal shows full result before save. |
| Orphan list is overwhelming (hundreds of notes) | Low | Medium | Cap "New & Unconnected" to last 30 days. Full orphan list is a future scope item. |
| Suggestions are poor quality (low embeddings coverage) | Medium | Low | Show suggestion count on the list item. If 0 suggestions, guide user to embed first. |

---

## Dependencies

| Dependency | Status | Notes |
|------------|--------|-------|
| `GraphService.BuildGraphAsync` | Complete | Reuse edge computation for cluster and orphan detection |
| pgvector semantic similarity | Complete | Powers suggestion ranking |
| `UsedSeedNote` table | Complete | Powers "Never Used as Seed" list |
| Tiptap HTML content model | Complete | Wikilink appended as `<p>[[Title]]</p>` |

---

## Open Questions

- [ ] What similarity threshold should define a "suggestion" for orphan connections? (Proposed: 0.6 — lower than graph's 0.8 to surface more options)
- [ ] Should the "Never Used as Seed" list exclude notes with fewer than N connections? (Proposed: no filter — even isolated notes with good content could be seeds)
- [ ] Should accepting a link suggestion also trigger re-embedding immediately, or wait for the background worker? (Proposed: mark Stale, let background worker handle it — same as edit flow)
- [ ] Is 30 days the right window for "New & Unconnected"? Validate after first week of use.

---

## Implementation Notes (for engineering)

### Cluster Algorithm (MVP)
Use union-find (disjoint set) on the combined edge list (wikilinks + semantic edges at ≥ 0.7 similarity). The component with the most notes = Cluster 1, etc. Name each cluster by the node with the highest edge count within the component.

The GraphService already fetches all edges — the health endpoint can call `BuildGraphAsync` and post-process the result, or extract the logic into a shared method. Consider a 5-minute in-memory cache keyed to avoid repeated graph builds in the same session.

### Wikilink Insertion
The note `Content` is Tiptap-generated HTML. The safest insertion point is before the closing `</div>` or appending `<p>[[TargetTitle]]</p>` to the end of the content string. Validate that the resulting HTML still parses (basic string sanity check is sufficient).

After insertion:
1. Update `Note.Content` and `Note.UpdatedAt`
2. Set `Note.EmbedStatus = EmbedStatus.Stale`
3. Invalidate React Query cache for `notes` and `health/overview`

### Performance
The overview endpoint does several queries. Consider a single SQL query that computes:
- Total notes count
- Embedded count
- Orphan count (notes not in any edge) — this requires graph data, so compute in-memory after loading edges
- Average edge count

Load the graph once per request, compute all derived metrics in-process.
