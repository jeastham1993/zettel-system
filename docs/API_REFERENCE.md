# API Reference

Last Updated: 2026-02-22

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

### `GET /api/notes/{id}/backlinks`
Find notes that link to this note via `[[Title]]` wikilinks.

### `GET /api/notes/{id}/suggested-tags`
AI-suggested tags derived from embedding similarity.

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

## Capture

### `POST /api/capture/email`
Email webhook. Requires `X-Webhook-Secret` header.

### `POST /api/capture/telegram`
Telegram webhook. Requires `X-Telegram-Bot-Api-Secret-Token` header.

Rate limited: 10 requests/minute.

---

## Export / Import

### `GET /api/export`
Export all notes as a ZIP archive (markdown files with YAML front matter).

### `POST /api/import`
Bulk import markdown files.

---

## Content Generation

Endpoints for the automated content generator. Generates blog posts and social media drafts from the Zettelkasten knowledge graph.

### `POST /api/content/generate`
Trigger a manual content generation run. Selects a random seed note, traverses the graph, and generates a blog post plus social media posts via LLM.

**Response:** `201 Created` — `ContentGeneration` object
**Error:** `409 Conflict` — `{ "error": "No eligible notes available for content generation." }` when no unvisited permanent notes with embeddings remain.

---

### `GET /api/content/generations`
List all generation runs, newest first.

**Query params:** `skip` (default 0), `take` (1–200, default 50)

**Response:** `200 OK`
```json
{
  "items": [
    {
      "id": "string",
      "seedNoteId": "string",
      "clusterNoteIds": ["string"],
      "topicSummary": "string",
      "status": "Pending | Generated | Approved | Rejected",
      "generatedAt": "datetime",
      "reviewedAt": "datetime | null"
    }
  ],
  "totalCount": 0
}
```

---

### `GET /api/content/generations/{id}`
Get a generation run with all its content pieces.

**Response:** `200 OK` — generation object including `pieces[]` | `404 Not Found`

---

### `GET /api/content/pieces`
List content pieces with optional filtering, newest first.

**Query params:** `skip`, `take` (1–200, default 50), `medium` (`blog` | `social`), `status` (`Draft` | `Approved` | `Rejected`)

**Response:** `200 OK` — paged list of `ContentPiece` objects.

---

### `GET /api/content/pieces/{id}`
Get a single content piece.

**Response:** `200 OK` | `404 Not Found`

```json
{
  "id": "string",
  "generationId": "string",
  "medium": "blog | social",
  "title": "string | null",
  "body": "string (markdown)",
  "status": "Draft | Approved | Rejected",
  "sequence": 0,
  "createdAt": "datetime",
  "approvedAt": "datetime | null"
}
```

---

### `PUT /api/content/pieces/{id}/approve`
Approve a content piece. Sets `status = Approved` and records `approvedAt`.

**Response:** `204 No Content` | `404 Not Found`

---

### `PUT /api/content/pieces/{id}/reject`
Reject a content piece. Sets `status = Rejected`.

**Response:** `204 No Content` | `404 Not Found`

---

### `GET /api/content/pieces/{id}/export`
Download a content piece as a markdown file.

**Response:** `200 OK` — `text/markdown` file download
Filename: sanitized title + `.md`, or `content-{id}.md` if no title.
**Error:** `404 Not Found`

---

### `GET /api/content/schedule`
Get current generation schedule settings (read from configuration).

**Response:** `200 OK`
```json
{
  "enabled": false,
  "dayOfWeek": "Monday",
  "timeOfDay": "09:00"
}
```

---

### `PUT /api/content/schedule`
Echo back schedule settings. **Note:** This endpoint does not persist changes. To modify the schedule, update `ContentGeneration:Schedule:*` in `appsettings.json` or environment variables.

**Body:** `{ "enabled": bool, "dayOfWeek": "string", "timeOfDay": "string" }`

**Response:** `200 OK` — echoed settings.

---

## Voice Configuration

Manages user writing samples and style notes used to replicate the user's voice during content generation.

### `GET /api/voice/examples`
List all voice examples, newest first.

**Response:** `200 OK`
```json
[
  {
    "id": "string",
    "medium": "blog | social | all",
    "title": "string | null",
    "content": "string",
    "source": "string | null",
    "createdAt": "datetime"
  }
]
```

---

### `POST /api/voice/examples`
Add a new voice example.

**Body:**
```json
{
  "medium": "blog | social | all",
  "title": "string?",
  "content": "string (required)",
  "source": "string?"
}
```

**Response:** `201 Created` — voice example object | `400 Bad Request`

---

### `DELETE /api/voice/examples/{id}`
Delete a voice example.

**Response:** `204 No Content` | `404 Not Found`

---

### `GET /api/voice/config?medium=`
Get voice configuration. Optionally filter by medium.

**Query params:** `medium` (`blog` | `social` | `all`) — optional

**Response:** `200 OK` — array of config objects
```json
[
  {
    "id": "string",
    "medium": "blog | social | all",
    "styleNotes": "string | null",
    "updatedAt": "datetime"
  }
]
```

---

### `PUT /api/voice/config`
Create or update style notes for a medium (upsert).

**Body:**
```json
{
  "medium": "blog | social | all",
  "styleNotes": "string?"
}
```

**Response:** `200 OK` — updated config object | `400 Bad Request`
