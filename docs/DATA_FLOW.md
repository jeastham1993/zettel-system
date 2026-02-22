# Data Flow

Last Updated: 2026-02-22

## Overview

ZettelWeb is an ASP.NET Core API + React SPA backed by PostgreSQL with
pgvector. Data flows through several pipelines: note management,
embedding generation, search, capture/enrichment, and content generation.

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

## Content Generation Pipeline

```
Trigger (manual API call or scheduler)
        │
        ▼
┌──────────────────┐
│ TopicDiscovery   │
│  Service         │
│                  │
│  1. Select random│
│     seed note    │
│     (excludes    │
│     used seeds)  │
│                  │
│  2. Build cluster│
│     - wikilinks  │
│     - semantic   │
│       neighbours │
│     - second-hop │
│       wikilinks  │
│                  │
│  3. Generate     │
│     topic        │
│     summary      │
└────────┬─────────┘
         │ TopicCluster
         ▼
┌──────────────────┐
│ ContentGeneration│
│  Service         │
│                  │
│  1. Build LLM    │
│     prompt with  │
│     note content │
│                  │
│  2. Call IChatClient
│     (Bedrock or  │
│      OpenAI)     │
│                  │
│  3. Parse LLM    │
│     response     │
│     into blog +  │
│     social posts │
│                  │
│  4. Persist      │
│     ContentGen + │
│     ContentPiece │
│     rows         │
│                  │
│  5. Record seed  │
│     in UsedSeed  │
│     Notes        │
└────────┬─────────┘
         │ ContentGeneration (with Pieces)
         ▼
┌──────────────────┐
│ Review & Approval│
│ (human in loop)  │
│                  │
│  - Review in     │
│    web dashboard │
│  - Approve or    │
│    reject pieces │
│  - Export as     │
│    markdown      │
└──────────────────┘
```

### Components

**TopicDiscoveryService** selects a random permanent note that has not
been used as a seed before. It builds a cluster of related notes by:
1. Following `[[wikilinks]]` from the seed note's content
2. Finding semantically similar notes via pgvector cosine similarity
   (threshold configurable via `SemanticSimilarityThreshold`)
3. Following wikilinks from first-level related notes (second hop)

The cluster is capped at `MaxClusterSize` (default 10) and requires at
least `MinClusterSize` (default 3) notes. If a seed produces too few
related notes, the service retries with a new seed up to
`MaxSeedRetries` times.

**ContentGenerationService** takes a `TopicCluster` and calls the LLM
(`IChatClient`) to generate one blog post and 3-5 social media posts.
Results are persisted as `ContentGeneration` and `ContentPiece` entities
in the database. The seed note ID is recorded in `UsedSeedNotes` to
prevent reuse.

### LLM Provider Abstraction

Content generation uses `IChatClient` from `Microsoft.Extensions.AI`,
registered separately from the embedding provider
(`IEmbeddingGenerator`). This allows using different models and
providers for embeddings vs. content generation.

Supported providers:
- **Bedrock**: `AmazonBedrockRuntimeClient.AsIChatClient(modelId)` via
  `AWSSDK.Extensions.Bedrock.MEAI`
- **OpenAI**: `OpenAIClient.GetChatClient(model).AsIChatClient()` via
  `Microsoft.Extensions.AI.OpenAI`

Configuration is in the `ContentGeneration` section of appsettings:
`Provider`, `Model`, `Region`, `ApiKey`, `MaxTokens`, `Temperature`.

### Entities

| Entity | Purpose |
|--------|---------|
| `ContentGeneration` | A pipeline run: seed note, cluster, topic summary, status |
| `ContentPiece` | Individual output: blog or social post with review status |
| `UsedSeedNote` | Tracks seed notes to prevent reuse |

### Data Relationships

```
Note (seed)
  │
  └──▶ ContentGeneration (1 per pipeline run)
           │
           ├──▶ ContentPiece (1 blog post)
           ├──▶ ContentPiece (social post 1)
           ├──▶ ContentPiece (social post 2)
           └──▶ ContentPiece (social post N)

  └──▶ UsedSeedNote (prevents reuse)
```

---

## Embedding Providers

The application uses two separate LLM integrations:

| Purpose | Interface | Providers |
|---------|-----------|-----------|
| Note embeddings | `IEmbeddingGenerator<string, Embedding<float>>` | Ollama, OpenAI, Bedrock |
| Content generation | `IChatClient` | Bedrock, OpenAI |

Both are configured independently in appsettings (`Embedding:*` and
`ContentGeneration:*` sections respectively).
