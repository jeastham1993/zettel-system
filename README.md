# Zettel-Web

A self-hostable Zettelkasten knowledge management app with AI-powered
semantic search. Capture notes from the web, email, or Telegram, then
find connections between ideas using meaning-based search rather than
just keywords.

Built with ASP.NET Core, React, PostgreSQL + pgvector, and your choice
of OpenAI or Ollama for embeddings.

![Alt text](./img/ui-screenshot.png "UI screenshot")

## Features

- **Semantic search** - Find notes by meaning using vector embeddings,
  with hybrid mode that combines full-text and semantic ranking
- **Zettelkasten workflow** - Permanent notes, fleeting notes (quick
  capture), structure notes (for organizing), and source notes (with
  bibliography metadata)
- **Multiple capture methods** - Web UI, email (via AWS SES), or
  Telegram bot
- **Knowledge graph** - Visual graph of note relationships and
  backlinks
- **Backlinks** - Wiki-style `[[Title]]` linking between notes with
  automatic backlink detection
- **Related notes** - AI-powered discovery of semantically similar
  notes and duplicate detection
- **URL enrichment** - Automatically fetches and extracts metadata from
  URLs found in notes
- **Import/export** - Import Notion-compatible markdown; export all
  notes as a ZIP with YAML front matter
- **Version history** - Tracks snapshots of note changes
- **Tag system** - Full-text autocomplete and filtering
- **Health monitoring** - `/health` endpoint with database and
  embedding API status
- **OpenTelemetry** - Built-in tracing and metrics instrumentation
- **Graceful degradation** - Falls back to full-text search if the
  embedding API is unavailable

## Self-Hosting

### Quick Start with Docker Compose (recommended)

The fastest way to run the full stack. Requires only Docker.

**1. Clone and configure**

```bash
git clone https://github.com/jameseastham/zettel-system.git
cd zettel-system
cp .env.example .env
```

Edit `.env` with your settings (see [Configuration](#configuration)
below). At minimum, choose an embedding provider.

**2. Start everything**

```bash
docker compose up -d
```

This starts four services behind Traefik on port 80:

| Service    | Description                                                 |
| ---------- | ----------------------------------------------------------- |
| `traefik`  | Reverse proxy routing `/api/*` to backend, `/*` to frontend |
| `backend`  | ASP.NET Core API (port 8080 internal)                       |
| `frontend` | React SPA                                                   |
| `db`       | PostgreSQL 17 with pgvector                                 |

The app is available at `http://localhost`. The database is persisted
in a Docker volume (`pgdata`).

**3. Verify**

```bash
curl http://localhost/health
```

Should return `{"status":"Healthy",...}`.

### Pre-Built Container Images

Container images are published to GitHub Container Registry on every
push to `main`:

```
ghcr.io/jameseastham/zettel-system/backend:latest
ghcr.io/jameseastham/zettel-system/frontend:latest
```

Tags available: `latest` (main branch), `sha-<commit>`, and semantic
versions (`v1.0.0`).

You can reference these in your own `docker-compose.yml` instead of
building from source:

```yaml
services:
  backend:
    image: ghcr.io/jameseastham/zettel-system/backend:latest
    environment:
      ConnectionStrings__DefaultConnection: "Host=db;..."
      # ... see Configuration section
  frontend:
    image: ghcr.io/jameseastham/zettel-system/frontend:latest
```

### Building from Source

Requires [.NET 10 SDK](https://dot.net/download) and
[Node.js](https://nodejs.org/).

```bash
# Backend
dotnet run --project src/ZettelWeb

# Frontend
cd src/zettel-web-ui
npm install && npm run build
```

For local development with just the database:

```bash
docker compose -f docker-compose.dev.yml up -d
dotnet run --project src/ZettelWeb
```

This starts only PostgreSQL on `localhost:5432`.

## Configuration

All configuration is via environment variables (or `appsettings.json`
for local development). The docker-compose file reads from a `.env`
file in the project root.

### Required

| Variable                               | Description                                                        |
| -------------------------------------- | ------------------------------------------------------------------ |
| `ConnectionStrings__DefaultConnection` | PostgreSQL connection string. Set automatically by docker-compose. |

### Embedding Provider

Choose between OpenAI (cloud) or Ollama (local, free).

#### Option A: Ollama (default, no API key needed)

Run [Ollama](https://ollama.ai) locally and pull a model:

```bash
ollama pull nomic-embed-text
```

```bash
EMBEDDING_PROVIDER=ollama
EMBEDDING_MODEL=nomic-embed-text
EMBEDDING_OLLAMA_URL=http://host.docker.internal:11434
```

If running Ollama on the same machine as Docker, the default
`host.docker.internal` URL works on macOS/Windows. On Linux, use
`http://172.17.0.1:11434` or add `--network=host`.

#### Option B: OpenAI

```bash
EMBEDDING_PROVIDER=openai
EMBEDDING_MODEL=text-embedding-3-large
EMBEDDING_API_KEY=sk-...
```

### Optional: Fleeting Note Capture

Capture quick notes from email or Telegram. Requires AWS
infrastructure (see [Webhook Ingestion](#webhook-ingestion-aws)).

| Variable                              | Description                     |
| ------------------------------------- | ------------------------------- |
| `CAPTURE_SQS_QUEUE_URL`               | AWS SQS queue URL               |
| `AWS_ACCESS_KEY_ID`                   | AWS credentials for SQS polling |
| `AWS_SECRET_ACCESS_KEY`               | AWS credentials for SQS polling |
| `CAPTURE_ALLOWED_EMAIL_SENDERS_0`     | Whitelisted email sender        |
| `CAPTURE_ALLOWED_TELEGRAM_CHAT_IDS_0` | Whitelisted Telegram chat ID    |
| `TELEGRAM_BOT_TOKEN`                  | Telegram bot API token          |

### Optional: Observability

| Variable         | Description                                                          |
| ---------------- | -------------------------------------------------------------------- |
| `Otel__Endpoint` | OpenTelemetry collector endpoint (e.g. `http://otel-collector:4317`) |

### Search Tuning

| Variable                    | Default | Description                                  |
| --------------------------- | ------- | -------------------------------------------- |
| `Search__SemanticWeight`    | `0.7`   | Weight for semantic scores in hybrid search  |
| `Search__FullTextWeight`    | `0.3`   | Weight for full-text scores in hybrid search |
| `Search__MinimumSimilarity` | `0.5`   | Minimum cosine similarity threshold          |

### All Embedding Options

| Variable                        | Default                  | Description                          |
| ------------------------------- | ------------------------ | ------------------------------------ |
| `Embedding__Provider`           | `ollama`                 | `openai` or `ollama`                 |
| `Embedding__Model`              | `nomic-embed-text`       | Model name                           |
| `Embedding__Dimensions`         | `768`                    | Vector dimensions (must match model) |
| `Embedding__ApiKey`             | (empty)                  | OpenAI API key                       |
| `Embedding__OllamaUrl`          | `http://localhost:11434` | Ollama endpoint                      |
| `Embedding__MaxInputCharacters` | `4000`                   | Max text length before truncation    |
| `Embedding__MaxRetries`         | `3`                      | Retry count for failed embeddings    |

## Deployment Options

### Docker Compose (single server)

The included `docker-compose.yml` runs the full stack on a single
machine. Suitable for personal use. Add a TLS-terminating reverse proxy
(Caddy, nginx, or Traefik with Let's Encrypt) in front for HTTPS.

### Container orchestration (Kubernetes, ECS, etc.)

Use the pre-built GHCR images. You need:

- A PostgreSQL 17 instance with the `pgvector` extension
- The backend container with environment variables configured
- The frontend container served behind a reverse proxy
- Route `/api/*` and `/health` to backend, everything else to frontend

### Cloud VM / VPS

Run `docker compose up -d` on any Linux VPS with Docker installed.
Minimum recommended: 1 vCPU, 1 GB RAM (without Ollama). If running
Ollama on the same machine for local embeddings, 4 GB+ RAM recommended.

### Webhook Ingestion (AWS)

The `infra/` directory contains an AWS CDK stack for capturing notes
from email and Telegram:

- **SQS** queues for reliable message ingestion
- **API Gateway** HTTP endpoint for Telegram webhooks
- **Lambda** relay function (Telegram -> SQS)
- **SNS** topic for SES inbound email
- **CloudWatch** alarms for queue depth and errors

Deploy with:

```bash
cd infra
npm install
npx cdk deploy
```

This is optional. The core app works without it - you just won't
have email/Telegram capture.

## Architecture

```
┌─────────┐     ┌──────────┐     ┌──────────────────┐
│ React   │────▶│ Traefik  │────▶│ ASP.NET Core API │
│ Frontend│     │ (proxy)  │     │                  │
└─────────┘     └──────────┘     │ Controllers      │
                                 │ Services         │
                                 │ Background       │
                                 │  Workers         │
                                 └────────┬─────────┘
                                          │
                    ┌─────────────────────┼──────────────┐
                    │                     │              │
               ┌────▼─────┐    ┌─────────▼──┐   ┌──────▼──────┐
               │PostgreSQL │    │ OpenAI /   │   │ AWS SQS     │
               │+ pgvector │    │ Ollama     │   │ (optional)  │
               └───────────┘    └────────────┘   └─────────────┘
```

The backend runs three background services:

- **Embedding pipeline** - Generates vector embeddings for notes
  asynchronously using `Channel<T>` with database polling as fallback
- **Enrichment pipeline** - Fetches URL metadata from links in notes
- **SQS poller** - Reads email/Telegram messages from AWS SQS
  (only active when configured)

Notes track their embedding state (`pending` -> `processing` ->
`completed`/`failed`) so no embeddings are silently lost. If the
embedding API is down, notes queue up and process when it recovers.

## API Reference

### Notes

| Method   | Endpoint                        | Description                                 |
| -------- | ------------------------------- | ------------------------------------------- |
| `POST`   | `/api/notes`                    | Create a note                               |
| `GET`    | `/api/notes`                    | List notes (supports pagination, filtering) |
| `GET`    | `/api/notes/{id}`               | Get a note by ID                            |
| `PUT`    | `/api/notes/{id}`               | Update a note                               |
| `DELETE` | `/api/notes/{id}`               | Delete a note                               |
| `POST`   | `/api/notes/check-duplicate`    | Check for duplicate content                 |
| `POST`   | `/api/notes/re-embed`           | Re-embed all notes                          |
| `GET`    | `/api/notes/{id}/backlinks`     | Get wiki-style backlinks                    |
| `GET`    | `/api/notes/{id}/versions`      | Get version history                         |
| `POST`   | `/api/notes/{id}/promote`       | Convert fleeting to permanent               |
| `POST`   | `/api/notes/{fleetingId}/merge` | Merge fleeting into permanent               |

### Search

| Method | Endpoint                              | Description              |
| ------ | ------------------------------------- | ------------------------ |
| `GET`  | `/api/search?q={query}`               | Hybrid search (default)  |
| `GET`  | `/api/search?q={query}&type=fulltext` | Full-text only           |
| `GET`  | `/api/search?q={query}&type=semantic` | Semantic only            |
| `GET`  | `/api/search/{noteId}/related`        | Find related notes       |
| `GET`  | `/api/search/discover`                | Discover unrelated notes |

### Import / Export

| Method | Endpoint      | Description                        |
| ------ | ------------- | ---------------------------------- |
| `POST` | `/api/import` | Import markdown files (JSON array) |
| `GET`  | `/api/export` | Download all notes as ZIP          |

### Other

| Method | Endpoint                | Description          |
| ------ | ----------------------- | -------------------- |
| `GET`  | `/api/tags?q={prefix}`  | Autocomplete tags    |
| `GET`  | `/api/graph`            | Knowledge graph data |
| `GET`  | `/api/discovery/random` | Random notes         |
| `GET`  | `/health`               | Service health check |

## Running Tests

```bash
dotnet test
```

Tests use in-memory databases and require no external services.

## Project Structure

```
zettel-system/
  src/
    ZettelWeb/           # ASP.NET Core Web API
    ZettelWeb.Tests/     # xUnit test project
    zettel-web-ui/       # React frontend (Vite + Tailwind)
  infra/                 # AWS CDK (optional webhook ingestion)
  docs/                  # Design docs, ADRs, specs
  docker-compose.yml     # Full stack (Traefik + backend + frontend + DB)
  docker-compose.dev.yml # Dev database only
```

## License

MIT
