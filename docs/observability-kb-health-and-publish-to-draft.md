# Observability Report: KB Health Dashboard + Publish-to-Draft

Generated: 2026-02-24
Features: `feat: add Knowledge Health Dashboard` + publishing services

---

## Production Verification Checklist

> Run these queries after first real traffic to confirm the code is running correctly.

### Code Path 1: GET /api/kb-health/overview

**Signal type**: Distributed trace (business span) + structured log
**Confidence**: High — fires on every dashboard load

| Backend | Query |
|---------|-------|
| Jaeger/Tempo | `service.name="ZettelWeb" AND span.name="kb_health.get_overview"` |
| OTLP logs | `message CONTAINS "KB health overview"` |
| Prometheus | `rate(http_server_request_duration_seconds_count{http_route="/api/kb-health/overview"}[5m])` |

What to look for: span with tags `kb_health.note_count`, `kb_health.orphan_count`, `kb_health.embedded_percent`
and log line `KB health overview: {NoteCount} notes, {OrphanCount} orphans, {EmbeddedPercent}% embedded`.

---

### Code Path 2: POST /api/kb-health/orphan/{id}/link

**Signal type**: Distributed trace + structured log + metric counter
**Confidence**: High — fires on every wikilink insertion

| Backend | Query |
|---------|-------|
| Jaeger/Tempo | `service.name="ZettelWeb" AND span.name="kb_health.insert_wikilink"` |
| OTLP logs | `message CONTAINS "Wikilink inserted"` |
| Prometheus | `zettel_kb_health_wikilinks_inserted_total` |

---

### Code Path 3: POST /api/content/pieces/{id}/send-to-draft (GitHub)

**Signal type**: Distributed trace (two spans: HTTP server + business) + metrics
**Confidence**: High — business span wraps the entire GitHub interaction

| Backend | Query |
|---------|-------|
| Jaeger/Tempo | `service.name="ZettelWeb" AND span.name="publishing.github.send_to_draft"` |
| OTLP logs | `message CONTAINS "Blog post pushed to GitHub"` |
| Prometheus | `zettel_content_drafts_sent_total` / `zettel_content_publishing_duration_ms_count` |

---

### Code Path 4: POST /api/content/pieces/{id}/send-to-draft (Publer)

**Signal type**: Distributed trace + metrics
**Confidence**: High

| Backend | Query |
|---------|-------|
| Jaeger/Tempo | `service.name="ZettelWeb" AND span.name="publishing.publer.send_to_draft"` |
| OTLP logs | `message CONTAINS "Publer draft created"` |
| Prometheus | `zettel_content_drafts_sent_total` |

---

## Findings Fixed in This Session

### 🔴 Critical (now resolved)

#### 1. `KbHealthService` — no business spans on any of the three endpoints

**Before**: Three endpoints (`GetOverview`, `GetSuggestions`, `InsertWikilink`) produced no custom spans.
ASP.NET Core instrumentation created HTTP server spans, and Npgsql created DB spans, but there was
no named span to search for KB health operations specifically.

**Fix**: Added `ZettelTelemetry.ActivitySource.StartActivity("kb_health.*")` to all three methods
with relevant tags.

#### 2. `InsertWikilinkAsync` — no success logging for a write operation

**Before**: The only write operation in KB health (modifying note content + setting embed status Stale)
produced zero logs. No audit trail, no way to correlate a note change with a KB health action.

**Fix**: Added `_logger.LogInformation("Wikilink inserted: {OrphanNoteId} -> {TargetNoteId} ({TargetTitle})", ...)`.

---

### 🟡 Important (now resolved)

#### 3. Metric naming — `content.*` prefix inconsistent with codebase-wide `zettel.*`

**Before**: Four recently-added metrics used a `content.` prefix while every other metric in
`ZettelTelemetry.cs` uses `zettel.*`. Off-the-shelf dashboards querying `zettel.*` would miss these.

**Fixed names**:
| Old | New |
|-----|-----|
| `content.drafts_sent` | `zettel.content.drafts_sent` |
| `content.draft_send_failures` | `zettel.content.draft_send_failures` |
| `content.editor_feedback_generated` | `zettel.content.editor_feedback_generated` |
| `content.publishing_duration_ms` | `zettel.content.publishing_duration` (unit: `"ms"`) |

Also: `content.publishing_duration_ms` had the unit embedded in the name AND was not passing
`"ms"` as the unit parameter to `CreateHistogram` — so the OTel SDK reported no unit. Fixed both.

#### 4. Publishing service error spans — `catch` didn't record span status or error type

**Before**: The `catch` block in both `GitHubPublishingService` and `PublerPublishingService`
recorded failure metrics then re-threw, but the OTel span ended with `Unset` status. Trace
queries for failed publishes would show spans that appeared to succeed.

**Fix**: Changed `catch` to `catch (Exception ex)` and added:
```csharp
activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
activity?.SetTag("error.type", ex.GetType().FullName);
```

---

### 🟢 Suggestions (not yet addressed)

#### 5. `zettel.kb_health.wikilinks_inserted` — new metric added in this session

Counter added to `ZettelTelemetry`. Provides a simple rate signal for how frequently the
KB health feature is being used to connect orphan notes.

#### 6. `KbHealthService.GetOverviewAsync` — pgvector query has no duration metric

The `CROSS JOIN LATERAL` similarity query is the most expensive operation in the service.
There's no timing signal for it. If KB health overview becomes slow, there's no metric to
diff against. A `Stopwatch` + `ZettelTelemetry.Meter.CreateHistogram("zettel.kb_health.overview_duration", "ms")` would be the fix.

#### 7. Publer `PollForPostUrlAsync` — no child span

The polling loop (up to 10 iterations × 1s delay) is visible via the auto-generated
`AddHttpClientInstrumentation()` spans on each `_http.SendAsync` call, but there's no
wrapping span named `publishing.publer.poll_for_url` to see the aggregate duration and retry count.

---

## Observability Stack Config (from Program.cs)

```csharp
.WithTracing(tracing => tracing
    .AddSource("ZettelWeb")    // custom business spans
    .AddSource("Npgsql")       // PostgreSQL query spans
    .AddAspNetCoreInstrumentation()  // HTTP server spans
    .AddHttpClientInstrumentation()) // outbound HTTP CLIENT spans

.WithMetrics(metrics => metrics
    .AddMeter("ZettelWeb")
    .AddAspNetCoreInstrumentation()
    .AddHttpClientInstrumentation())
```

OTel logging: `IncludeScopes = true`, `IncludeFormattedMessage = true`.
All three signals (traces, metrics, logs) export to OTLP when `OTEL_EXPORTER_OTLP_ENDPOINT` is set.

---

## Span Inventory (post-fix)

| Span name | Service | Kind | Key tags |
|-----------|---------|------|----------|
| `kb_health.get_overview` | KbHealthService | Internal | `kb_health.note_count`, `kb_health.orphan_count`, `kb_health.embedded_percent` |
| `kb_health.get_suggestions` | KbHealthService | Internal | `kb_health.note_id`, `kb_health.limit` |
| `kb_health.insert_wikilink` | KbHealthService | Internal | `kb_health.orphan_note_id`, `kb_health.target_note_id` |
| `publishing.github.send_to_draft` | GitHubPublishingService | Internal | `content.piece_id`, `content.medium` |
| `publishing.publer.send_to_draft` | PublerPublishingService | Internal | `content.piece_id`, `content.medium` |
| `content.generate` | ContentGenerationService | Internal | `content.seed_id`, `content.cluster_size` |
| `content.regenerate_medium` | ContentGenerationService | Internal | `content.generation_id`, `content.medium` |
| `content.editor_feedback` | ContentGenerationService | Internal | `content.piece_title` |

Plus auto-instrumented spans for every HTTP request (`ASP.NET Core`) and every DB query (`Npgsql`).

---

## Metric Inventory (post-fix)

| Metric | Type | Unit | Description |
|--------|------|------|-------------|
| `zettel.notes.created` | Counter | — | Notes created |
| `zettel.notes.deleted` | Counter | — | Notes deleted |
| `zettel.searches.executed` | Counter | — | Search runs |
| `zettel.embeddings.processed` | Counter | — | Embeddings completed |
| `zettel.embeddings.failed` | Counter | — | Embedding failures |
| `zettel.enrichments.processed` | Counter | — | Enrichment runs |
| `zettel.enrichments.failed` | Counter | — | Enrichment failures |
| `zettel.embeddings.duration` | Histogram | ms | Embedding latency |
| `zettel.searches.duration` | Histogram | ms | Search latency |
| `zettel.content.generated` | Counter | — | Content generation runs |
| `zettel.content.scheduled_generations` | Counter | — | Scheduled generation runs |
| `zettel.content.drafts_sent` | Counter | — | Pieces successfully sent to draft |
| `zettel.content.draft_send_failures` | Counter | — | Failed draft send attempts |
| `zettel.content.editor_feedback_generated` | Counter | — | Editor feedback passes |
| `zettel.content.publishing_duration` | Histogram | ms | External publish call latency |
| `zettel.kb_health.wikilinks_inserted` | Counter | — | Wikilinks inserted via KB health |
