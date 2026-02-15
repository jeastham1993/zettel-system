# ADR-001: Backend Architecture Pattern

Date: 2026-02-13
Status: Proposed

## Context

Zettel-Web is a personal Zettelkasten web application for a single user with
500-2000 Markdown notes. The backend is an ASP.NET Core Web API with PostgreSQL
+ pgvector for storage and vector search, and a pluggable embedding provider
(OpenAI now, Ollama later).

We need to decide the internal architecture pattern for the .NET backend. The
key tension is between architectural purity (which optimises for large teams
and complex domains) and pragmatism (which optimises for speed and simplicity
in a single-developer, thin-domain project).

Three options were evaluated:
- **Option A**: Simple Layered (Controllers -> Services -> EF Core)
- **Option B**: Vertical Slice (MediatR handlers per feature)
- **Option C**: Clean Architecture (Domain -> Application -> Infrastructure
  -> API)

A comprehensive analysis was performed covering coupling metrics, failure modes
(FMEA), and evolvability against 9 anticipated changes.

## Decision

Use **Simple Layered Architecture** (Option A) with two structural guardrails:

1. **No business logic in controllers.** Controllers validate input, call a
   service, and return the result. Services are the testable, reusable unit of
   behaviour.

2. **All external dependencies behind interfaces.** `IEmbeddingProvider` is
   the first. Apply the same pattern to any future external dependency. Internal
   services do NOT need interfaces.

Additionally, use the **outbox pattern** for the embedding pipeline: an
`embed_status` column on the notes table tracks embedding state durably, with
`Channel<T>` as a responsiveness optimisation on top.

## Consequences

### Positive

- Fastest path to a working application (~1-2 weeks to first CRUD endpoint)
- Lowest cognitive overhead: every file has an obvious place
- ~20 files for the full v1 backend (vs ~40-50 for Clean Architecture)
- All 9 anticipated changes (provider swap, AI features, v2 discovery, auth,
  scaling, search tuning) are rated "Easy" in effort
- Direct method calls make debugging straightforward
- No third-party architecture dependencies
- The outbox pattern eliminates the two highest-risk failure modes (RPN 240)
  by making embedding state durable and observable

### Negative

- No enforced architectural boundaries -- relies on developer discipline
- Services layer sits at I=0.50 (hub position); needs proactive splitting if
  any service exceeds ~300 lines
- Data layer in Zone of Pain (D=1.00) -- schema changes ripple through all
  layers (mitigated by low layer count)
- If the project unexpectedly grows to 30+ endpoints or multiple developers,
  the architecture may need to evolve toward Clean Architecture (estimated
  cost: 2-4 hour refactor)

### Neutral

- The embedding pipeline design (outbox pattern) is the most important
  architectural decision and is independent of which layer pattern is chosen
- The `IEmbeddingProvider` interface provides the key extension point
  regardless of architecture
- Option C has the best theoretical coupling metrics (Application layer
  D=0.12) but fails the proportionality test for this project's context

## Alternatives Considered

### Vertical Slice Architecture (Option B)

Not chosen because Zettel-Web's features are heavily cross-cutting: embedding
is shared across create, update, import, and re-embed; search ranking is shared
across search, related notes, and digest. You either duplicate logic across
handlers or extract shared services, which recreates Option A with added
MediatR indirection. Evolvability score: 3/5.

### Clean Architecture (Option C)

Not chosen because the domain is thin (notes are documents with metadata, no
complex invariants). The Domain layer becomes anemic models in their own project.
The mapping tax (5 files changed to add a query parameter) and the architecture
astronautics risk (30-50% timeline increase) outweigh the theoretical coupling
benefits. Evolvability score: 2/5.

### When to Reconsider

Revisit this decision if:
- The project grows beyond 30 endpoints
- A second developer joins
- The domain logic becomes genuinely complex (approval workflows,
  multi-user permissions with role-based access)
- Testing requires mocking internal services (not just external providers)

None of these are anticipated in the current spec.

## Related Decisions

- Database schema design (embedding outbox pattern): documented in the
  design document

## Notes

- Full analysis: [docs/design-zettel-web-architecture.md](../design-zettel-web-architecture.md)
- Coupling analysis performed with afferent/efferent/instability metrics
- Failure mode analysis (FMEA) with 10 failure modes scored
- Evolvability assessed against 9 anticipated changes from the spec
