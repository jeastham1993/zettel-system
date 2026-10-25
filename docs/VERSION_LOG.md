# Version Log

Last Updated: 2026-10-25

## Unreleased - 2026-10-25

### Removed
- `src/zettel-mobile/` — the React Native/Expo mobile app. The web UI is
  now an installable PWA, which covers mobile use. ADR-007 is retained as
  history (superseded).
- Dead remnants of the voice assistant and Telegram/email webhook capture
  (removed from the application in commit `6901ea9`): the orphaned `Capture`
  config section in `appsettings.Development.json` and the stale
  `/api/capture/email` + `/api/capture/telegram` paths in
  `docs/openapi.json`.

### Added
- Cohere embed model support on Bedrock (e.g. `cohere.embed-v4` via
  cross-region inference-profile ARNs). The bundled
  `AWSSDK.Extensions.Bedrock.MEAI` adapter only speaks Titan's request
  format, so Cohere models get a dedicated generator with the correct
  payload (`texts`/`input_type`/`embedding_types`/`dimensions`) — queries
  are tagged `search_query`, stored documents `search_document`.
- The HNSW index now rebuilds automatically at startup when
  `Embedding:Dimensions` changes, fixing the provider-switch trap from
  INFRA-005.
- PWA support completed: the manifest previously pointed at icons that did
  not exist, making the app un-installable. Added generated icon set
  (192/512/maskable 512, apple-touch-icon, favicon), a maskable icon entry,
  iOS web-app meta tags, and an offline navigation fallback for SPA routes
  (API and health routes excluded). Service worker registration was already
  wired via vite-plugin-pwa's autoUpdate mode.

### Fixed
- Related-note matching was hit-and-miss on Titan embeddings: noise and
  genuine matches share an absolute similarity band, so results are now
  gated relative to the top match (`Search:RelatedRelativeFloor`, default
  0.6) in addition to the absolute `MinimumSimilarity` floor (BUG-009).
- Related notes now exclude non-Permanent (e.g. Telegram inbox) notes.
- Empty or near-empty notes (< `Embedding:MinContentCharacters`, default 50)
  are no longer embedded — they get the new `EmbedStatus.Skipped` state and
  a null embedding so they drop out of semantic matching.
- `KbHealthService` read `Search:SuggestionThreshold` under a misspelled
  key (`Search:_suggestionThreshold`), silently ignoring the configured
  value (BUG-010).
- Health check now reports a `skipped` embedding count.
- KB health clusters now use **mutual top-5 k-NN** semantic edges (an edge
  counts only if each note is in the other's five nearest neighbours) in
  addition to wiki-links. One-directional edges on Titan's compressed score
  distribution had merged 315 of 379 notes into a single mega-cluster.
- Scorecard now breaks connectivity down: `averageWikiLinks` and
  `averageSemanticLinks` alongside the combined `averageConnections`.
- Orphan detection now counts only **wiki-link** connections — semantic
  similarity is an insight, not a connection.
- New duplicate detection: permanent note pairs with cosine similarity above
  `Search:DuplicateThreshold` (default 0.95) are listed in the overview
  (`duplicates`) and on the Knowledge Health page for manual merge review.

### Migration note
Run `POST /api/notes/re-embed` once after deploying so existing thin
notes are marked `Skipped` and their stale embeddings cleared.

## 1.2.3 - 2026-10-17

### Fixed
- Chat sent the raw prompt template to the LLM with unsubstituted
  `{{contextText}}` / history / user-message placeholders — the feature
  had never worked. `BuildPrompt` used a `$$$` raw string where the
  template's double-brace placeholders are literal text; changed to
  `$$` so they interpolate. Added regression tests (BUG-008).

## 1.2.2 - 2026-10-17

### Added
- Restored Amazon Bedrock as an embedding provider (`Embedding:Provider
  = bedrock`, region via `Embedding:BedrockRegion`, ambient AWS
  credentials) and as a chat LLM provider (`ContentGeneration:Provider
  = bedrock`, region via `ContentGeneration:Region`). These provider
  branches were removed in 1.2.0 along with the AWS deployment stack —
  they are independent of it and remain useful for self-hosted
  deployments that prefer Bedrock. Default providers remain Ollama.

## 1.2.1 - 2026-10-17

### Fixed
- Backend no longer crashes at startup when no OpenAI API key is
  configured. The chat LLM now defaults to Ollama (OpenAI-compatible
  endpoint), reusing `Embedding:OllamaUrl` unless
  `ContentGeneration:OllamaUrl` is set. Default model `llama3.1`.
- Explicit startup error (with guidance) instead of a raw
  `ArgumentException` when `ContentGeneration:ApiKey` is empty with
  provider `openai`, or when the removed `bedrock` provider is
  requested.

## 1.2.0 - 2026-10-17

### Removed
- Removed the AWS deployment stack entirely — Docker Compose on a local
  server is the only supported deployment:
  - `terraform/` (API Gateway, Cognito, Aurora Serverless, CloudFront,
    ECR, worker/migration Lambdas) and `infra/` (CDK SQS webhook relay)
  - All .NET Lambda handlers (`EmbeddingWorker`, `CaptureWorker`,
    `MigrationRunner`, `LambdaServiceProvider`) and the Lambda Web
    Adapter from the backend Docker image (the cause of the CI ECR
    rate-limit failures)
  - SQS webhook ingestion and email/Telegram webhook capture
    (`CaptureController`, `CaptureService`, `SqsPollingBackgroundService`,
    `CaptureConfig`), Telegram notifications, and the Cognito PKCE
    frontend auth (only used by the AWS deployment)
  - The voice assistant (`voice-service/`, `/voice` page, voice session
    hook, CI job, compose profile)
  - Bedrock provider support for embeddings and chat LLM (OpenAI and
    Ollama remain)
- Backend now has zero AWS SDK dependencies

### Changes
- Enrichment config moved from the `Capture:` section to
  `Enrichment:TimeoutSeconds` / `Enrichment:MaxRetries`
- Chat LLM provider is OpenAI-only (`ContentGeneration:` keys unchanged)
- EF migrations run unconditionally at startup (no Lambda migration step)

## 1.1.0 - 2026-10-17

### Removed
- Removed the automated content generation feature in its entirety:
  - Blog/social content schedulers (`BlogContentScheduler`, `SocialContentScheduler`, `ContentSchedulerBase`)
  - `TopicDiscoveryService`, `ContentGenerationService`, and publishing integrations (GitHub, Publer)
  - `ContentController`, `ContentScheduleController`, `VoiceController` API endpoints (`/api/content/*`, `/api/voice/*`)
  - Content review and voice config pages in the web UI
  - Lambda `ContentScheduleHandler` and its EventBridge schedule (Terraform)
  - Database tables: `ContentGenerations`, `ContentPieces`, `VoiceExamples`, `VoiceConfigs`, `UsedSeedNotes`
    (dropped via the `20261017143749_RemoveContentGenerator` migration)
  - KB Health "Never Used as Seeds" section and related telemetry counters
- The `ContentGeneration:Provider/Model/Region/ApiKey` config keys remain —
  they now configure the shared chat LLM (`IChatClient`) used by Chat and KB Health

### Changes
- Database size reduction: content generation tables dropped and no longer written to

## 1.0.0 - 2026-07-09

### New Features
- Added source references feature: Users can now view all notes that reference a specific source
- New API endpoint: `GET /api/notes/sources/{sourceId}/references`
- New UI component: SourceReferences.tsx for displaying notes that reference a source
- Updated NoteView.tsx to show source references for source-type notes
- Updated note page to include source references in both desktop and mobile views

### Changes
- Enhanced INoteService interface with GetNotesBySourceAsync method
- Added getSourceReferences function to the API client
- Updated API reference documentation

### Bug Fixes
- None in this release

## Previous Versions

No previous versions documented.