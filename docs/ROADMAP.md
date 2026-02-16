# Zettel-Web Roadmap

Last Updated: 2026-02-15

## Overview

Personal Zettelkasten web app. ASP.NET Core API + React SPA + PostgreSQL
with pgvector for semantic search.

## Completed Features

### Core
- Note CRUD with Zettelkasten-style IDs
- Tiptap WYSIWYG editor with autosave
- Tag system with autocomplete
- Markdown import/export (Notion-compatible)
- Health endpoint with DB and embedding checks

### Search
- PostgreSQL full-text search (`to_tsvector`/`ts_rank`)
- Semantic search via pgvector cosine similarity
- Hybrid search with configurable weighting
- Cmd+K search UI

### Embeddings
- Microsoft.Extensions.AI abstraction
- OpenAI and Ollama provider support
- Background embedding pipeline with state tracking
- Bulk re-embed endpoint

### Fleeting Notes (Batch 20)
- Quick capture from web UI (floating button, Ctrl+Shift+N)
- Email capture via webhook (POST /api/capture/email)
- Telegram capture via webhook (POST /api/capture/telegram)
- Inbox page with fleeting note management
- Background URL enrichment service

### SQS Webhook Ingestion (Batch 21)
- AWS CDK infrastructure (API Gateway + Lambda + SQS + CloudWatch)
- Thin Lambda relay (no business logic in AWS)
- SqsPollingBackgroundService for private server deployments
- Messages survive server downtime (14-day SQS retention)
- Dead letter queue for poison messages
- CloudWatch alarms -> SNS email notifications
- Deployment guide: [deployment-sqs-webhook-ingestion.md](
  deployment-sqs-webhook-ingestion.md)

### Structure Notes & Sources (Batch 22 - Backend)
- NoteType enum (Regular/Structure/Source) on permanent notes
- Source metadata fields (Author, Title, URL, Year, SourceType)
- NoteType filter on List API endpoint
- Promote fleeting notes with target type
- ALTER TABLE migration with IF NOT EXISTS guards
- 16 new tests, 423 total passing

### Observability (Batch 23)
- OpenTelemetry SDK with tracing, metrics, and log export
- Auto-instrumentation: ASP.NET Core, HttpClient, Npgsql
- Custom spans: note CRUD, search (fulltext/semantic/hybrid),
  embedding processing, enrichment processing
- Custom metrics: notes created/deleted, searches executed,
  embeddings processed/failed, enrichments processed/failed,
  embedding duration, search duration
- Configurable OTLP endpoint (`Otel:Endpoint` setting)
- Aspire Dashboard in docker-compose (port 18888)
- 13 new telemetry tests, 431 total passing

### Infrastructure
- Docker Compose full stack (Traefik + backend + frontend + pgvector)
- Multi-stage Dockerfiles for backend (.NET) and frontend (nginx)
- Dev database compose file
- Aspire Dashboard for local telemetry visualization

## Architecture Decisions

- [ADR-001](adr/ADR-001-backend-architecture.md): Simple Layered
  Architecture
- [ADR-002](adr/ADR-002-postgresql-native-search.md): PostgreSQL Native
  Search
- [ADR-003](adr/ADR-003-fleeting-notes-architecture.md): Fleeting Notes
- [ADR-004](adr/ADR-004-sqs-webhook-ingestion.md): SQS Webhook Ingestion
- [ADR-005](adr/ADR-005-structure-notes-sources.md): Structure Notes
  & Sources
- [ADR-006](adr/ADR-006-opentelemetry-observability.md): OpenTelemetry
  Observability
- [ADR-007](adr/ADR-007-mobile-app-strategy.md): Mobile App Strategy
