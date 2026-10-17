# Data Flow

Last Updated: 2026-10-17

## Overview

ZettelWeb is an ASP.NET Core API + React SPA backed by PostgreSQL with
pgvector. Data flows through several pipelines: note management,
embedding generation, search, capture/enrichment, and chat.

---

## Note Management

```
User (Web UI) ──▶ API Controller ──▶ NoteService ──▶ ZettelDbContext ──▶ PostgreSQL
                                          │
                                          ▼
                                   EmbeddingQueue (Channel)
                                          │
                                          ▼
                              EmbeddingBackgroundService
                                          │
                                          ▼
                              IEmbeddingGenerator (LLM)
                                          │
                                          ▼
                              Note.Embedding updated in DB
```

Notes are created/updated through the API. On save, the note is enqueued
for background embedding generation. The `EmbeddingBackgroundService`
reads from a `Channel<T>` queue, calls the configured embedding provider
(`IEmbeddingGenerator<string, Embedding<float>>`), and writes the vector
back to the note row.

---

## Search

```
Query ──▶ SearchService
              │
              ├── Full-text search (PostgreSQL to_tsvector / ts_rank)
              │
              ├── Semantic search (pgvector cosine similarity)
              │
              └── Hybrid merge (configurable weights)
                      │
                      ▼
                 SearchResult[]
```

Search supports three modes: full-text, semantic, and hybrid. Hybrid
search runs both queries and merges results using configurable
`SemanticWeight` / `FullTextWeight` scores.

---

## Capture & Enrichment

```
External source ──▶ Webhook / SQS ──▶ CaptureService ──▶ Fleeting Note
(email, Telegram)       │                                      │
                        ▼                                      ▼
              SqsPollingBackgroundService            EnrichmentQueue (Channel)
                                                           │
                                                           ▼
                                              EnrichmentBackgroundService
                                                           │
                                                           ▼
                                              URL metadata fetch + update
```

External messages arrive via webhooks or SQS polling. The
`CaptureService` creates fleeting notes. URLs in captured content are
enqueued for background enrichment (title/description extraction).

---

## Chat Pipeline

```
User Message ──▶ ChatController ──▶ ChatService
                                      │
                                      ▼
                               BuildContextNotesAsync
                                      │
                                      ▼
                              SearchService.SemanticSearch
                                      │
                                      ▼
                              BuildPrompt + ConversationHistory
                                      │
                                      ▼
                                  IChatClient
                                      │
                                      ▼
                              GenerateAssistantResponse
                                      │
                                      ▼
                              Save ChatMessage + References
```

The chat service:
1. Receives user messages via the API
2. Builds context by finding relevant notes using semantic search
3. Constructs a prompt with conversation history and knowledge base context
4. Calls the LLM via `IChatClient`
5. Saves the assistant's response with references to source notes
6. Updates the session context and embeddings for future conversations

## Embedding Providers

The application uses two separate LLM integrations:

| Purpose | Interface | Providers |
|---------|-----------|-----------|
| Note embeddings | `IEmbeddingGenerator<string, Embedding<float>>` | Ollama, OpenAI, Bedrock |
| Chat / KB Health summaries | `IChatClient` | Bedrock, OpenAI |

Both are configured independently in appsettings (`Embedding:*` and
`ContentGeneration:*` sections respectively; the `ContentGeneration:*` keys
now configure the shared chat LLM only).
