# Design: Automated Content Generator

## Overview

A scheduled feature that mines a personal Zettelkasten knowledge base, discovers interesting threads of connected notes, and generates publish-ready content — blog posts and social media posts — written in the owner's authentic voice.

The system runs weekly, picks a random starting note, follows the knowledge graph to build a rich topic thread, and produces one blog post plus 3–5 social media posts for human review and approval.

---

## Problems This Solves

1. **Forgotten knowledge goes unused.** Notes accumulate in the Zettelkasten but rarely get revisited or shared. Valuable ideas stay buried.
2. **Content creation is effortful.** Turning raw notes into polished blog posts or social media content requires significant time and mental energy.
3. **Publishing cadence is hard to maintain.** Without a forcing function, weeks or months can pass without publishing — even when there's plenty of material to draw from.

---

## Target User

A single user (the Zettelkasten owner) who wants to consistently publish content derived from their own knowledge base. This is not a multi-tenant feature — it operates on one person's notes, in one person's voice.

---

## Core Workflow

```
┌─────────────┐     ┌──────────────┐     ┌───────────────┐     ┌──────────────┐
│  Weekly      │     │  Topic       │     │  Content      │     │  Review &    │
│  Scheduler   │────▶│  Discovery   │────▶│  Generation   │────▶│  Approval    │
│  (cron)      │     │  (random +   │     │  (LLM in      │     │  (human in   │
│              │     │   graph walk)│     │   user voice)  │     │   the loop)  │
└─────────────┘     └──────────────┘     └───────────────┘     └──────────────┘
```

### Step 1: Topic Discovery

- **Entry point:** Select a truly random permanent note as the seed.
- **Graph traversal:** Follow wikilinks (`[[Title]]`) and semantically related notes to build a topic cluster.
- **Depth control:** Gather enough connected material to produce substantive content (target: 3–10 linked notes forming a coherent thread).
- **Thin topic handling:** When a seed note's thread doesn't have enough substance for a full blog post, the system uses a two-tier strategy:
  1. **Combine threads:** Attempt to merge 2–3 loosely related threads from the seed's neighbourhood into a richer composite topic.
  2. **Adapt length:** If combined threads are still thin, generate shorter-form content that matches the available depth rather than padding with filler. A 400-word focused insight is better than a 1,200-word watered-down post.
- **Repetition avoidance:** Track which notes have been used as seed notes and which topic clusters have been generated. Never reuse the same seed note. Avoid generating content that substantially overlaps with previous outputs.

### Step 2: Content Generation

Using an LLM, generate two types of content from the discovered topic thread:

**Blog Post (1 per week)**
- The LLM decides the appropriate length based on available source material:
  - **Full post** (800–1,500 words) when the topic thread is rich enough
  - **Focused insight** (300–600 words) when the material is thinner
- The system prompt describes both formats and instructs the LLM to choose the best fit
- Structured with title, introduction, body sections, and conclusion
- Draws on the substance of the linked notes but synthesises them into a coherent narrative
- Written in the user's authentic voice (see Voice Profile below)

**Social Media Posts (3–5 per week)**
- Short-form posts suitable for platforms like Twitter/X, LinkedIn, or Bluesky
- Each post should stand alone but relate to the blog post's theme
- Varied formats: insights, questions, hot takes, thread starters, key takeaways
- Written in the user's authentic voice

### Step 3: Review & Approval

- All generated content is stored as drafts — **nothing is published automatically**
- The user reviews each piece in an **in-app dashboard** within the Zettel web UI
- Drafts can be edited inline, approved, or rejected
- Approved content is exported as **separate markdown files** (one for the blog post, one per social media post)
- Rejected content can be flagged for regeneration
- The generation history is tracked so the user can see which notes inspired each piece

---

## Voice Profile

The system should replicate the user's writing voice as closely as possible.

### Voice Input

The user provides examples of their existing writing — published blog posts, social media posts, or other written content. These examples are stored and used as few-shot context for the LLM during generation.

### Voice Configuration

- **Examples:** A collection of the user's past writing samples (blog posts, tweets, articles)
- **Style notes (optional):** Free-text guidance on tone, vocabulary preferences, topics to avoid, formatting conventions, etc.
- **Per-medium configuration:** Voice may differ between blog posts (more formal, structured) and social media (more casual, punchy). Allow separate example sets per output medium.

---

## Content Architecture

Blog posts and social media posts are **separate features** sharing a common pipeline but producing independent outputs.

### Extensibility

The system should be designed so that new output mediums can be added in the future without restructuring the core pipeline. Examples of future mediums:

- Newsletter digests
- Podcast show notes or scripts
- Conference talk outlines
- YouTube video scripts

Each medium is defined by:
- A **content template** (structure, length, format constraints)
- A **voice profile** (example set and style notes specific to the medium)
- A **generation prompt** (LLM instructions for producing that medium)

---

## LLM Configuration

Content generation requires a **chat/completion model** (not an embedding model), so it needs its own dedicated LLM configuration, separate from the existing embedding provider.

### Configuration Settings

| Setting | Description | Example |
|---------|-------------|---------|
| `ContentGeneration:Provider` | LLM provider | `bedrock`, `openai`, `anthropic` |
| `ContentGeneration:Model` | Model identifier | `anthropic.claude-3-5-sonnet-20241022-v2:0` |
| `ContentGeneration:Region` | AWS region (Bedrock only) | `us-east-1` |
| `ContentGeneration:ApiKey` | API key (OpenAI/Anthropic direct) | `sk-...` |
| `ContentGeneration:MaxTokens` | Max output tokens per generation | `4096` |
| `ContentGeneration:Temperature` | Sampling temperature | `0.7` |

The primary target is **Amazon Bedrock**, but the provider abstraction should support OpenAI and Anthropic direct API as alternatives. This aligns with the existing embedding provider pattern in the codebase.

---

## Repetition Tracking

### What to Track

| Entity | Purpose |
|--------|---------|
| **Seed notes used** | Prevent the same note from being picked as a starting point twice |
| **Note clusters used** | Track which combinations of notes have been woven into content |
| **Generated content** | Store all outputs (drafts, approved, rejected) with metadata |
| **Topic fingerprints** | A semantic summary/embedding of each generated piece to detect topical overlap |

### Rules

- A note that has been used as a **seed** is permanently excluded from future random selection.
- Individual notes can still appear in **supporting roles** across multiple content pieces (a foundational concept note might be relevant to many topics).
- If a candidate topic cluster overlaps significantly with a previously generated piece (e.g. cosine similarity > 0.85 between topic embeddings), skip and retry with a different seed.

---

## Schedule & Cadence

| Setting | Default | Configurable |
|---------|---------|-------------|
| Frequency | Weekly | Yes |
| Day of week | Monday | Yes |
| Blog posts per run | 1 | Yes |
| Social media posts per run | 3–5 | Yes (min/max range) |
| Max retries for thin topics | 3 | Yes |

The scheduler should be implemented as a background service or cron job that triggers the discovery → generation pipeline.

---

## Data Model (New Entities)

### ContentGeneration

Represents a single run of the content generation pipeline.

| Field | Type | Description |
|-------|------|-------------|
| Id | string | Unique generation run ID |
| SeedNoteId | string | The randomly selected starting note |
| ClusterNoteIds | string[] | All notes included in the topic thread |
| TopicSummary | string | Brief description of the topic |
| TopicEmbedding | float[] | Semantic embedding for overlap detection |
| Status | enum | Pending, Generated, Approved, Rejected |
| GeneratedAt | DateTime | When the content was generated |
| ReviewedAt | DateTime? | When the user reviewed the content |

### ContentPiece

An individual piece of generated content.

| Field | Type | Description |
|-------|------|-------------|
| Id | string | Unique content piece ID |
| GenerationId | string | FK to ContentGeneration |
| Medium | string | "blog" or "social" (extensible) |
| Title | string? | Title (for blog posts) |
| Body | string | The generated content (markdown) |
| Status | enum | Draft, Approved, Rejected |
| Sequence | int | Ordering (e.g. social post 1 of 5) |
| CreatedAt | DateTime | When generated |
| ApprovedAt | DateTime? | When approved by user |

### VoiceExample

Examples of the user's writing for voice replication.

| Field | Type | Description |
|-------|------|-------------|
| Id | string | Unique example ID |
| Medium | string | Which medium this example is for ("blog", "social", "all") |
| Title | string? | Optional title of the example piece |
| Content | string | The example text |
| Source | string? | Where this came from (URL, file, manual) |
| CreatedAt | DateTime | When added |

### VoiceConfig

Global voice configuration.

| Field | Type | Description |
|-------|------|-------------|
| Id | string | Unique config ID |
| Medium | string | Which medium ("blog", "social", "all") |
| StyleNotes | string? | Free-text style guidance |
| UpdatedAt | DateTime | Last modified |

---

## API Endpoints (New)

### Content Generation

| Method | Route | Description |
|--------|-------|-------------|
| POST | `/api/content/generate` | Trigger a manual generation run |
| GET | `/api/content/generations` | List all generation runs |
| GET | `/api/content/generations/{id}` | Get a generation run with its content pieces |
| PUT | `/api/content/pieces/{id}/approve` | Approve a content piece |
| PUT | `/api/content/pieces/{id}/reject` | Reject a content piece |
| GET | `/api/content/pieces` | List content pieces (filterable by medium, status) |
| GET | `/api/content/pieces/{id}` | Get a single content piece |

### Voice Configuration

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/voice/examples` | List voice examples |
| POST | `/api/voice/examples` | Add a voice example |
| DELETE | `/api/voice/examples/{id}` | Remove a voice example |
| GET | `/api/voice/config` | Get voice configuration |
| PUT | `/api/voice/config` | Update voice configuration/style notes |

### Schedule Configuration

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/content/schedule` | Get current schedule settings |
| PUT | `/api/content/schedule` | Update schedule settings |

---

## Resolved Decisions

1. **Thin topics:** Two-tier approach — first try combining 2–3 loosely related threads, then adapt content length to match available depth. Shorter focused content is preferred over padded long-form.

2. **Voice input method:** Dedicated voice configuration page in the web UI where the user pastes examples and defines tone/style guidelines. Per-medium example sets supported (blog vs. social).

3. **Review experience:** In-app dashboard within the Zettel web UI. Dedicated page to review, edit, approve, or reject generated drafts.

4. **Publishing integration:** Export-ready markdown files. Blog posts and social media posts are exported as separate markdown files. No direct publishing integration in v1 — the user downloads and publishes manually.

5. **LLM provider:** Dedicated LLM configuration, separate from the embedding provider. Content generation will likely use Amazon Bedrock (chat model), while embeddings continue to use Ollama or OpenAI. This requires its own provider, model, and API key settings.

6. **Content length heuristics:** Let the LLM decide. The system prompt describes two output modes — full blog post (800–1,500 words) and shorter focused insight (300–600 words) — and the LLM judges which format best fits the available source material. No hard-coded note count or word count thresholds.

---

## Implementation Phases

### Phase 1: Core Pipeline
- Topic discovery (random seed + graph walk)
- Content generation via Amazon Bedrock (blog + social media)
- Dedicated LLM configuration (provider, model, API key, region)
- Repetition tracking (seed history + topic embeddings)
- New data model and migrations (ContentGeneration, ContentPiece)
- API endpoints for generation and content management
- Markdown export (separate files for blog post and each social post)

### Phase 2: Voice Profile
- Voice example storage and management (VoiceExample, VoiceConfig entities)
- Dedicated voice configuration page in web UI
- Per-medium voice configuration (blog vs. social)
- Voice-aware prompt engineering with few-shot examples
- API endpoints for voice management

### Phase 3: Scheduling
- Background service for weekly execution
- Schedule configuration API
- Generation history and audit trail

### Phase 4: Review Dashboard
- In-app review/approval dashboard in the Zettel web UI
- View generated drafts with source note context
- Edit capabilities for generated drafts
- Approve/reject workflow
- Regeneration for rejected content
- Markdown download for approved content

### Phase 5: Extensibility
- Medium plugin architecture
- Additional output formats (newsletter, podcast notes, etc.)
- Publishing integrations (if desired)
