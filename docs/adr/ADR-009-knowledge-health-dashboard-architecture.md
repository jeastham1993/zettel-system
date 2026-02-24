# ADR-009: Knowledge Health Dashboard — Dedicated Service Architecture

Date: 2026-02-24
Status: Proposed

---

## Context

The Knowledge Health Dashboard needs to surface KB structural analytics: orphan notes, cluster density, embed coverage, and unused generation seeds. Two approaches were considered — reusing `GraphService` or creating a dedicated `IKbHealthService`.

`GraphService.BuildGraphAsync` was designed for graph visualization. It returns a `GraphData` object optimised for rendering (nodes + edges with weights). It loads all note content into memory for wiki-link parsing and runs a pgvector LATERAL join. Stretching this service to also serve analytics would require:
- Adding `CreatedAt` to `GraphNode` (a visualization DTO)
- Embedding orphan/cluster logic inside a visualization-oriented service
- Making health dashboard performance dependent on graph render latency

## Decision

Implement KB health analytics in a new `IKbHealthService` / `KbHealthService` pair.

The wiki-link regex is extracted from `GraphService` into a shared `WikiLinkParser` static class, consumed by both services. This is the only shared dependency.

The pgvector LATERAL join SQL is intentionally duplicated at a different threshold (0.6 for suggestions, 0.8 for graph edges) — the different semantics justify separate queries.

## Consequences

### Positive
- `GraphService` remains single-purpose (visualization only) — no regression risk
- KB health logic is fully encapsulated and independently testable
- Easier to add caching, trend history, or tag breakdowns to health service without touching graph code
- `WikiLinkParser` extraction benefits both services

### Negative
- pgvector LATERAL SQL appears in two places — must be maintained consistently if the schema changes
- More files: new interface, implementation, controller, models file

### Neutral
- No database migration required for MVP
- Health dashboard loads on-demand (no background job) — acceptable at current scale

## Alternatives Considered

### Option A: Reuse `GraphService`
Rejected. Creates wrong coupling — analytics and visualization have different query shapes, different thresholds, and different DTO requirements. `GraphNode` is a visualization primitive, not an analytics data model.

### Option C: Background snapshot table
Rejected for MVP. No measured performance problem to solve yet. Adds migration + background job complexity before the feature is validated. Can be added later if `GetOverviewAsync` proves slow at scale.

## Related Decisions
- ADR-001: Backend service layer pattern (interface + implementation registered in DI)
- ADR-002: pgvector for semantic similarity
- ADR-006: OTel spans on all service methods

## Notes

If KB grows beyond ~2,000 notes and `GetOverviewAsync` becomes slow, the migration path is:
1. Add `IMemoryCache` with 5-minute TTL to `KbHealthService`
2. If still slow: move computation to a background job + `KbHealthSnapshot` table (Option C)
