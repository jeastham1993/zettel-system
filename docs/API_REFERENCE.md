# API Reference

Last Updated: 2026-10-17

---

## Base URL

```
http://localhost:5000
```

All endpoints return `application/json` unless stated otherwise. All `DateTime` values are UTC ISO 8601 strings.

---

## Notes

### `POST /api/notes`
Create a new note.

**Body:**
```json
{
  "title": "string",
  "content": "string (HTML, max 500,000 chars)",
  "tags": ["string"],
  "status": "Permanent | Fleeting",
  "noteType": "Regular | Structure | Source",
  "sourceAuthor": "string?",
  "sourceTitle": "string?",
  "sourceUrl": "string?",
  "sourceYear": "string?",
  "sourceType": "string?"
}
```

**Response:** `201 Created` — note object.

---

### `GET /api/notes`
List notes with filtering and pagination.

**Query params:** `skip`, `take` (1–200, default 50), `status`, `tag`, `noteType`

**Response:** `200 OK` — `{ items: Note[], totalCount: int }`

---

### `GET /api/notes/{id}`
Get a note by ID.

**Response:** `200 OK` | `404 Not Found`

---

### `PUT /api/notes/{id}`
Update a note. Queues re-embedding if content changed.

**Response:** `200 OK` | `404 Not Found`

---

### `DELETE /api/notes/{id}`
Delete a note and its versions and tags.

**Response:** `204 No Content` | `404 Not Found`

---

### `GET /api/notes/inbox`
List fleeting notes.

### `GET /api/notes/inbox/count`
Count of fleeting notes.

### `POST /api/notes/{id}/promote`
Promote a fleeting note to permanent.

### `POST /api/notes/{fleetingId}/merge/{targetId}`
Merge a fleeting note into an existing permanent note.

### `GET /api/notes/{id}/related`
Find semantically related notes using pgvector cosine similarity.
Permanent notes only; candidates must clear `Search:MinimumSimilarity`,
and results below `Search:RelatedRelativeFloor` (default 0.6) × the top
match's similarity are dropped as noise (see BUG-009 / INFRA-004).
Notes with less than `Embedding:MinContentCharacters` (default 50) of
content are never returned — they are marked `Skipped` and carry no
embedding.

### `GET /api/notes/{id}/backlinks`
Find notes that link to this note via `[[Title]]` wikilinks.

### `GET /api/notes/{id}/suggested-tags`
AI-suggested tags derived from embedding similarity.

### `GET /api/notes/sources/{sourceId}/references`
Get all notes that reference a specific source.

**Response:** `200 OK` — array of `Note` objects

**Example:**
```bash
GET /api/notes/sources/20230101120000000/references
```

Returns all notes that have the specified source ID in their source field.

### `GET /api/notes/{id}/versions`
List version history for a note.

### `GET /api/notes/{id}/versions/{versionId}`
Get a specific historical version.

### `POST /api/notes/re-embed`
Queue all notes for re-embedding (use after changing embedding model).

### `POST /api/notes/check-duplicate`
Check if similar content already exists.

### `GET /api/notes/discover`
Discover semantically unrelated notes for broadening the knowledge graph.

### `GET /api/notes/search-titles`
Autocomplete note titles (for wikilink suggestions).

---

## Tags

### `GET /api/tags?prefix=`
Search tags by prefix.

---

## Search

### `GET /api/search?q=&type=`
Search notes.

**Query params:**
- `q` — search query
- `type` — `hybrid` (default), `fulltext`, `semantic`

Hybrid mode: weighted combination of PostgreSQL full-text (`tsvector`) and pgvector cosine similarity.

---

## Graph

### `GET /api/graph?threshold=`
Build the knowledge graph.

**Query params:** `threshold` (semantic edge similarity floor, default 0.8)

**Response:**
```json
{
  "nodes": [{ "id": "string", "title": "string", "edgeCount": 0 }],
  "edges": [{ "source": "string", "target": "string", "type": "wikilink|semantic", "weight": 0.0 }]
}
```


---

## Export / Import

### `GET /api/export`
Export all notes as a ZIP archive (markdown files with YAML front matter).

### `POST /api/import`
Bulk import markdown files.

---

## KB Health

### `GET /api/kb-health/overview`
Full knowledge base health overview: scorecard metrics, recent orphans, richest clusters, and likely duplicates.

**Response:** `200 OK`
```json
{
  "scorecard": {
    "totalNotes": 342,
    "embeddedPercent": 87,
    "orphanCount": 23,
    "averageConnections": 4.2,
    "averageWikiLinks": 1.4,
    "averageSemanticLinks": 2.8
  },
  "newAndUnconnected": [
    {
      "id": "string",
      "title": "string",
      "createdAt": "datetime",
      "suggestionCount": 5
    }
  ],
  "richestClusters": [
    {
      "hubNoteId": "string",
      "hubTitle": "string",
      "noteCount": 42
    }
  ],
  "duplicates": [
    {
      "firstNoteId": "string",
      "firstTitle": "string",
      "secondNoteId": "string",
      "secondTitle": "string",
      "similarity": 0.98
    }
  ]
}
```

Notes:
- `newAndUnconnected` — permanent notes added in the last 30 days with zero
  **wiki-link** connections, newest first. Semantic similarity is an insight,
  not a connection — a note nobody links to is still an orphan.
- `richestClusters` — top 5 connected components by note count (minimum 2
  notes). Edges are wiki-links **plus mutual top-5 semantic neighbours**
  (an edge counts only if each note is in the other's five nearest). Hub is
  the most-connected note in the component.
- `duplicates` — permanent note pairs with cosine similarity above
  `Search:DuplicateThreshold` (default 0.95); candidates for manual merge.

---

### `GET /api/kb-health/orphan/{id}/suggestions`
Top semantically similar notes for a given orphan note. Powered by pgvector cosine similarity above `Search:SuggestionThreshold` (default 0.3).

**Query params:** `limit` (default 5)

**Response:** `200 OK`
```json
[
  {
    "noteId": "string",
    "title": "string",
    "similarity": 0.87
  }
]
```

Returns empty array if the note has no embedding or no similar notes above the threshold.

---

### `POST /api/kb-health/orphan/{id}/link`
Insert a `[[TargetTitle]]` wikilink at the end of an orphan note's content. Sets the orphan note's `EmbedStatus` to `Stale` for re-processing.

**Body:**
```json
{ "targetNoteId": "string" }
```

**Response:** `200 OK` — updated orphan note | `404 Not Found` — if orphan or target note not found

---

## Chat

Conversational interface to interact with your knowledge base.

### `POST /api/chat/sessions`
Create a new chat session.

**Body:**
```json
{
  "title": "string?",
  "contextNoteIds": ["string"]
}
```

**Response:** `201 Created` — chat session object.

### `GET /api/chat/sessions`
List all chat sessions.

**Query params:** `status` (`Active` | `Archived`), `skip` (default 0), `take` (1–200, default 50)

**Response:** `200 OK` — array of chat session objects.

### `GET /api/chat/sessions/{sessionId}`
Get a specific chat session.

**Response:** `200 OK` — chat session object | `404 Not Found`

### `PUT /api/chat/sessions/{sessionId}`
Update a chat session.

**Body:**
```json
{
  "title": "string?",
  "status": "Active" | "Archived"
}
```

**Response:** `200 OK` — updated chat session | `404 Not Found`

### `DELETE /api/chat/sessions/{sessionId}`
Delete a chat session and all its messages.

**Response:** `204 No Content` | `404 Not Found`

### `POST /api/chat/sessions/{sessionId}/messages`
Send a message to a chat session.

**Body:**
```json
{
  "content": "string",
  "contextNoteIds": ["string"]
}
```

**Response:** `200 OK` — assistant's response message with references.

### `GET /api/chat/sessions/{sessionId}/messages`
Get messages for a chat session.

**Query params:** `skip` (default 0), `take` (1–200, default 50)

**Response:** `200 OK` — array of chat messages with references.

### `POST /api/chat/sessions/{sessionId}/regenerate`
Regenerate the last assistant response.

**Response:** `200 OK` — new assistant response message.

## Research

Autonomous research agent that analyses KB health, generates search queries, and produces findings for human review.

### `POST /api/research/trigger`
Trigger a research run. Analyses the KB, generates a research agenda with search queries.

**Body:**
```json
{ "sourceNoteId": "string | null" }
```

**Response:** `201 Created` — `ResearchAgenda` object with tasks.

---

### `POST /api/research/agenda/{agendaId}/approve`
Approve a research agenda and start async execution. Returns immediately (fire-and-forget).

**Body:**
```json
{ "blockedTaskIds": ["string"] }
```

**Response:** `202 Accepted`

---

### `GET /api/research/findings`
Get all pending research findings awaiting review.

**Response:** `200 OK`
```json
[
  {
    "id": "string",
    "taskId": "string",
    "title": "string",
    "synthesis": "string",
    "sourceUrl": "string",
    "sourceType": "WebSearch | Arxiv",
    "status": "Pending | Accepted | Dismissed",
    "createdAt": "datetime",
    "reviewedAt": "datetime | null"
  }
]
```

---

### `POST /api/research/findings/{findingId}/accept`
Accept a finding — creates a fleeting note from the synthesis.

**Response:** `201 Created` — Note object | `404 Not Found`

---

### `POST /api/research/findings/{findingId}/dismiss`
Dismiss a finding.

**Response:** `204 No Content`
