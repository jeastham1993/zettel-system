# Zettel-Web

A personal Zettelkasten web app with semantic search. ASP.NET Core API
backed by PostgreSQL + pgvector, with an AI-powered embedding pipeline
for finding related notes by meaning, not just keywords.

## Prerequisites

- [.NET 10 SDK](https://dot.net/download)
- [Docker](https://www.docker.com/) (for PostgreSQL + pgvector)
- An [OpenAI API key](https://platform.openai.com/api-keys) (for embeddings)

## Getting Started

### 1. Start the database

```bash
docker compose up -d
```

This starts PostgreSQL 17 with pgvector on `localhost:5432`.

### 2. Configure the API key

Set your OpenAI API key in `src/ZettelWeb/appsettings.json`:

```json
{
  "Embedding": {
    "Model": "text-embedding-3-large",
    "ApiKey": "sk-..."
  }
}
```

Or via environment variable:

```bash
export Embedding__ApiKey="sk-..."
```

### 3. Run the API

```bash
dotnet run --project src/ZettelWeb
```

The API starts on `http://localhost:5000` by default.

### 4. Run tests

```bash
dotnet test
```

All tests use an in-memory database and require no external services.

## API Endpoints

### Notes

| Method | Endpoint | Description |
|--------|----------|-------------|
| `POST` | `/api/notes` | Create a note |
| `GET` | `/api/notes` | List all notes |
| `GET` | `/api/notes/{id}` | Get a note by ID |
| `PUT` | `/api/notes/{id}` | Update a note |
| `DELETE` | `/api/notes/{id}` | Delete a note |
| `POST` | `/api/notes/re-embed` | Re-embed all notes |

**Create/Update request body:**

```json
{
  "title": "My Note",
  "content": "Note content in markdown...",
  "tags": ["rust", "programming"]
}
```

Note IDs are Zettelkasten-style timestamps (`YYYYMMDDHHmmss`),
generated automatically on create.

### Search

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/search?q={query}` | Hybrid search (default) |
| `GET` | `/api/search?q={query}&type=fulltext` | Full-text only |
| `GET` | `/api/search?q={query}&type=semantic` | Semantic only |
| `GET` | `/api/search?q={query}&type=hybrid` | Combined ranking |

Hybrid search combines full-text and semantic results with configurable
weights. If the embedding API is unavailable, it falls back to
full-text automatically.

### Import

| Method | Endpoint | Description |
|--------|----------|-------------|
| `POST` | `/api/import` | Import markdown files |

**Request body:**

```json
[
  { "fileName": "my-note.md", "content": "Note content..." },
  { "fileName": "another.md", "content": "More content..." }
]
```

Non-`.md` files are skipped. Titles are derived from filenames (minus
the `.md` extension). All imported notes are automatically enqueued for
embedding.

### Export

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/export` | Download all notes as zip |

Returns a `zettel-export.zip` file containing each note as a markdown
file (`{title}.md`) with YAML front matter including id, timestamps,
and tags.

### Tags

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/tags?q={prefix}` | Autocomplete tags |

### Health

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/health` | Service health status |

Returns `Healthy`, `Degraded`, or `Unhealthy`. Checks database
connectivity (with note/embedding stats) and embedding API
configuration.

## Configuration

All config is in `src/ZettelWeb/appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=zettelweb;Username=zettel;Password=zettel_dev"
  },
  "Embedding": {
    "Model": "text-embedding-3-large",
    "ApiKey": ""
  },
  "Search": {
    "SemanticWeight": 0.7,
    "FullTextWeight": 0.3
  }
}
```

| Key | Default | Description |
|-----|---------|-------------|
| `Embedding:Model` | `text-embedding-3-large` | OpenAI embedding model |
| `Embedding:ApiKey` | (empty) | OpenAI API key |
| `Search:SemanticWeight` | `0.7` | Weight for semantic scores in hybrid search |
| `Search:FullTextWeight` | `0.3` | Weight for full-text scores in hybrid search |

## Architecture

Simple layered architecture: Controllers -> Services -> EF Core.

```
src/ZettelWeb/
  Controllers/     # HTTP endpoints
  Services/        # Business logic
  Background/      # Embedding background worker
  Providers/       # External API integrations
  Data/            # EF Core DbContext
  Models/          # Domain entities
```

Embeddings are processed asynchronously via a background service using
`Channel<T>` for immediate processing and DB polling as a fallback.
Notes track their embedding state (`pending` -> `processing` ->
`completed`/`failed`/`stale`) so embeddings are never silently lost.

See [docs/design-zettel-web-architecture.md](docs/design-zettel-web-architecture.md)
for the full design document.

## Project Structure

```
zettel-web/
  src/
    ZettelWeb/           # ASP.NET Core Web API
    ZettelWeb.Tests/     # xUnit test project
  docs/                  # Design docs, ADRs, specs
  docker-compose.yml     # PostgreSQL + pgvector
```
