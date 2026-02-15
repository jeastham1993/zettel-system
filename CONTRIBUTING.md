# Contributing to Zettel System

## Prerequisites

| Tool | Version | Required For |
|------|---------|-------------|
| Docker & Docker Compose | 20.10+ / 2.0+ | Database, full stack |
| .NET SDK | 10.0 | Backend |
| Node.js | 18+ (22 recommended) | Frontend, mobile |
| npm | Latest | Frontend, mobile |
| Xcode | Latest (macOS only) | iOS mobile builds |
| Android Studio or SDK | Latest | Android mobile builds |
| Ollama | Latest | Local embeddings (optional) |

## Architecture Overview

```
                          ┌──────────────┐
                          │   Traefik    │ :8080
                          │   (proxy)    │
                          └──────┬───────┘
                     ┌───────────┴───────────┐
                     │                       │
              ┌──────▼──────┐        ┌───────▼───────┐
              │  ASP.NET    │        │  React SPA    │
              │  Core API   │        │  (Vite)       │
              │  :8080      │        │  :80          │
              └──────┬──────┘        └───────────────┘
                     │
          ┌──────────┼──────────┐
          │          │          │
   ┌──────▼───┐ ┌───▼────┐ ┌──▼───────┐
   │PostgreSQL│ │Ollama /│ │ AWS SQS  │
   │+pgvector │ │OpenAI  │ │(optional)│
   └──────────┘ └────────┘ └──────────┘

   ┌────────────────┐
   │ React Native   │  Connects to API
   │ (Expo) Mobile  │  over LAN/VPN
   └────────────────┘
```

**Services:**

- **Backend** (`src/ZettelWeb/`) -- ASP.NET Core 10 Web API
- **Frontend** (`src/zettel-web-ui/`) -- React 19 SPA with Vite, Tailwind v4, Tiptap editor
- **Mobile** (`src/zettel-mobile/`) -- React Native with Expo SDK 54, Expo Router v4
- **Database** -- PostgreSQL 17 with pgvector extension
- **Infrastructure** (`infra/`) -- Optional AWS CDK stack for SQS/Telegram ingestion

## Quick Start: Full Stack (Docker)

The fastest way to get everything running:

```bash
# 1. Clone and configure
git clone <repo-url>
cd zettel-system
cp .env.example .env

# 2. (Optional) Start Ollama for local embeddings
ollama pull nomic-embed-text
ollama serve

# 3. Start all services
docker compose up -d

# 4. Verify
curl http://localhost:8080/health
```

This starts Traefik (:8080), the backend, frontend, PostgreSQL, and the Aspire dashboard (:18888).

Open http://localhost:8080 in your browser.

## Service-by-Service Setup

### Database

All development paths need PostgreSQL. The dev compose file starts just the database:

```bash
docker compose -f docker-compose.dev.yml up -d
```

**Connection details:**

| Setting | Value |
|---------|-------|
| Host | `localhost` |
| Port | `5432` |
| Database | `zettelweb` |
| User | `zettel` |
| Password | `zettel_dev` |

EF Core migrations run automatically on backend startup.

### Backend

```bash
# Start the database first
docker compose -f docker-compose.dev.yml up -d

# Run the API
dotnet run --project src/ZettelWeb

# API available at http://localhost:5000
# Swagger UI at http://localhost:5000/swagger
```

**Environment variables** (set in shell or `appsettings.Development.json`):

```bash
# Required -- embedding provider
export Embedding__Provider=ollama
export Embedding__Model=nomic-embed-text
export Embedding__OllamaUrl=http://localhost:11434

# Connection string (matches docker-compose.dev.yml)
export ConnectionStrings__DefaultConnection="Host=localhost;Database=zettelweb;Username=zettel;Password=zettel_dev"
```

To use OpenAI instead of Ollama:

```bash
export Embedding__Provider=openai
export Embedding__Model=text-embedding-3-large
export Embedding__ApiKey=sk-your-key-here
```

### Frontend

```bash
cd src/zettel-web-ui
npm install
npm run dev
```

The dev server runs on http://localhost:5173 with hot module replacement. It proxies `/api/*` and `/health` to `http://localhost:5000` automatically (configured in `vite.config.ts`).

**Ensure the backend is running** before starting the frontend.

### Mobile App

```bash
cd src/zettel-mobile
npm install
npx expo start
```

The interactive menu gives you options:

| Key | Action |
|-----|--------|
| `i` | Open in iOS Simulator (requires Xcode) |
| `a` | Open in Android Emulator (requires Android Studio) |
| `w` | Open in web browser |
| `r` | Reload the app |

**On a physical device:** Install [Expo Go](https://expo.dev/go) and scan the QR code from the terminal.

**Connecting to the backend:** On first launch, the app shows a "Connect to Server" screen. Enter the backend URL:

| Scenario | URL |
|----------|-----|
| Backend on same machine (simulator) | `http://localhost:5000` |
| Backend on same machine (physical device) | `http://<your-lan-ip>:5000` |
| Backend via Docker Compose | `http://<your-lan-ip>:8080` |
| Backend via Tailscale | `http://<tailscale-hostname>:8080` |

The app validates the URL with a `/health` check before saving.

**Building for distribution:**

```bash
# Install EAS CLI
npm install -g eas-cli

# Cloud builds (no local native tooling needed)
eas build --platform android
eas build --platform ios   # Requires Apple Developer account ($99/yr)

# Local builds (requires Xcode / Android Studio)
eas build --local --platform android
eas build --local --platform ios
```

For Android, you can sideload the APK directly. For iOS, use TestFlight.

## Running Tests

### Backend (xUnit)

```bash
# Run all tests
dotnet test

# Run a specific test class
dotnet test --filter "ClassName=NotesControllerTests"

# Run tests matching a pattern
dotnet test --filter "Name~SearchService"
```

Tests use WebApplicationFactory with in-memory databases and Testcontainers for PostgreSQL integration tests. No external services required.

### Frontend (Vitest)

```bash
cd src/zettel-web-ui

# Run once
npm run test

# Watch mode
npm run test:watch
```

### Frontend Linting

```bash
cd src/zettel-web-ui
npm run lint
```

### Mobile

No automated test suite yet. Test manually on real devices:

```bash
cd src/zettel-mobile
npx expo start
```

Key flows to verify:
- Quick capture (online and offline)
- Search (hybrid, fulltext, semantic)
- Inbox management (promote, delete via swipe)
- Note detail rendering (HTML from Tiptap)
- Settings (server URL change, theme toggle)
- Offline queue sync on reconnect

## Project Structure

```
zettel-system/
├── src/
│   ├── ZettelWeb/                 # ASP.NET Core backend
│   │   ├── Controllers/           # API endpoints
│   │   ├── Services/              # Business logic
│   │   ├── Data/                  # EF Core context + migrations
│   │   ├── Background/            # Embedding, enrichment, SQS workers
│   │   ├── Health/                # Health check endpoints
│   │   └── Program.cs             # DI setup, middleware, startup
│   │
│   ├── ZettelWeb.Tests/           # xUnit backend tests
│   │   ├── Controllers/           # Integration tests
│   │   ├── Services/              # Unit tests
│   │   └── Fakes/                 # Test doubles
│   │
│   ├── zettel-web-ui/             # React frontend
│   │   ├── src/
│   │   │   ├── pages/             # Route pages
│   │   │   ├── components/        # UI components (shadcn/ui, Tiptap)
│   │   │   ├── hooks/             # React Query hooks
│   │   │   └── api/               # TypeScript API client
│   │   └── vite.config.ts
│   │
│   └── zettel-mobile/             # React Native mobile app
│       ├── app/                   # Expo Router screens
│       │   ├── (tabs)/            # Tab screens (Home, Search, Inbox, Settings)
│       │   ├── capture.tsx        # Quick capture modal
│       │   ├── connect.tsx        # Server connection setup
│       │   └── note/[id]/         # Note detail + edit
│       └── src/
│           ├── api/               # HTTP client + endpoint functions
│           ├── components/        # Native UI components
│           ├── hooks/             # TanStack Query hooks + utilities
│           ├── stores/            # MMKV persistence (offline queue, prefs)
│           ├── theme/             # Colors + typography
│           └── lib/               # Markdown, date utilities
│
├── infra/                         # AWS CDK (optional SQS ingestion)
├── docs/
│   ├── adr/                       # Architecture Decision Records
│   └── design-mobile-app.md       # Mobile app design document
│
├── docker-compose.yml             # Full stack (Traefik + all services)
├── docker-compose.dev.yml         # Database only (for local dev)
├── .env.example                   # Environment variable template
└── .github/workflows/             # CI/CD (build + push Docker images)
```

## Environment Configuration

Copy `.env.example` to `.env` and configure:

```bash
cp .env.example .env
```

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `EMBEDDING_PROVIDER` | Yes | `ollama` | `ollama` or `openai` |
| `EMBEDDING_MODEL` | Yes | `nomic-embed-text` | Model name for embeddings |
| `EMBEDDING_OLLAMA_URL` | If Ollama | `http://host.docker.internal:11434` | Ollama server URL |
| `EMBEDDING_API_KEY` | If OpenAI | -- | OpenAI API key |
| `CAPTURE_SQS_QUEUE_URL` | No | -- | AWS SQS queue for email/Telegram capture |
| `TELEGRAM_BOT_TOKEN` | No | -- | Telegram bot token for capture |
| `Otel__Endpoint` | No | -- | OpenTelemetry collector endpoint |

## Architecture Decisions

Design decisions are documented as ADRs in `docs/adr/`:

| ADR | Decision |
|-----|----------|
| [001](docs/adr/ADR-001-backend-architecture.md) | Simple layered architecture (Controllers -> Services -> Data) |
| [002](docs/adr/ADR-002-postgresql-native-search.md) | PostgreSQL for fulltext + vector search (no Elasticsearch) |
| [003](docs/adr/ADR-003-fleeting-notes-architecture.md) | Fleeting notes with inbox workflow |
| [004](docs/adr/ADR-004-sqs-webhook-ingestion.md) | SQS for email/Telegram webhook ingestion |
| [005](docs/adr/ADR-005-mobile-app-strategy.md) | React Native with Expo (not PWA or Capacitor) |
| [006](docs/adr/ADR-006-opentelemetry-observability.md) | OpenTelemetry for tracing, metrics, and logs |

## Observability

When running via `docker compose up`, the Aspire Dashboard is available at http://localhost:18888 with:

- **Traces** -- ASP.NET Core, HTTP client, PostgreSQL (auto-instrumented) + custom spans for note CRUD, search, embedding
- **Metrics** -- Notes created/deleted, searches executed, embeddings processed
- **Logs** -- Structured logs from all backend services

## CI/CD

GitHub Actions (`.github/workflows/build-and-push.yml`) runs on pushes to `main` and version tags:

1. Builds Docker images for backend and frontend
2. Pushes to GitHub Container Registry (`ghcr.io/jameseastham/zettel-system/`)
3. Tags: `latest` (main), `sha-<commit>`, `v*` (version tags)
