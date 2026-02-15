# ADR-006: OpenTelemetry for Application Observability

Date: 2026-02-15
Status: Accepted

## Context

The ZettelWeb backend runs background services (embedding, enrichment, SQS
polling) alongside the HTTP API. When issues occur in production -- slow
searches, failed embeddings, enrichment timeouts -- the only signals are
log messages. There is no distributed tracing, no metrics dashboards, and
no way to correlate a slow API response with the database query or
embedding call that caused it.

## Decision

Instrument the backend with OpenTelemetry using the standard .NET SDK
packages. The implementation covers three pillars:

1. **Tracing** -- automatic instrumentation for ASP.NET Core, HttpClient,
   and Npgsql, plus custom spans for business operations (note CRUD,
   search, embedding, enrichment).
2. **Metrics** -- custom counters and histograms for notes created/deleted,
   searches executed, embeddings processed/failed, enrichments
   processed/failed, and duration measurements.
3. **Logging** -- existing `ILogger` output forwarded to OTLP when an
   endpoint is configured.

All telemetry exports to a configurable OTLP endpoint (`Otel:Endpoint`).
When no endpoint is configured, the OTel SDK is still registered but
produces no network traffic -- the custom `ActivitySource` and `Meter`
still emit data for any in-process listeners (tests, diagnostics).

For local development, the .NET Aspire Dashboard runs as a Docker
container alongside the stack, providing trace, metric, and log
visualization at `http://localhost:18888`.

## Alternatives Considered

- **Application Insights SDK** -- vendor lock-in to Azure, heavier SDK.
- **Prometheus + Grafana** -- good for metrics but requires separate
  tracing solution; OTel can export to Prometheus later if needed.
- **No instrumentation** -- status quo; debugging production issues
  remains difficult.

## Consequences

- Four new NuGet packages added to ZettelWeb.csproj.
- `ZettelTelemetry` static class centralises `ActivitySource` and `Meter`
  definitions, keeping instrumentation code minimal in services.
- Aspire Dashboard added to docker-compose (port 18888).
- 13 new tests verify that custom activities are emitted by instrumented
  services.
- Future: can switch OTLP endpoint to any compatible backend (Jaeger,
  Grafana Tempo, Datadog, AWS X-Ray) without code changes.
