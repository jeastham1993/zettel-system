# ADR-008: Content Regeneration Strategy

Date: 2026-02-23
Status: Proposed

---

## Context

The content generation pipeline (ADR-001) discovers a cluster of notes and passes them to an
LLM to produce a blog post and 3–5 social media posts.  A user reviewing the output may find the
entire generation or a specific medium (blog/social) unsatisfactory and want to re-run the LLM
on the same cluster of notes.

Currently there is no mechanism to do this — every `POST /api/content/generate` call discovers
a brand-new cluster.

The key insight is that `ContentGeneration` already persists everything needed to reconstruct
a `TopicCluster`: `SeedNoteId`, `ClusterNoteIds`, and `TopicSummary`.  No schema migration is
required to enable regeneration.

Full analysis and alternatives: `docs/design-content-regeneration.md`

---

## Decision

Implement **two new endpoints** providing full-generation and per-medium regeneration:

```
POST /api/content/generations/{id}/regenerate
POST /api/content/generations/{id}/regenerate/{medium}
```

Both endpoints reconstruct a `TopicCluster` from the persisted `ContentGeneration` record and
call into `ContentGenerationService`, which already encapsulates all LLM interaction.

A new service method `RegenerateMediumAsync` is added to `IContentGenerationService` to support
per-medium regeneration without exposing private LLM helpers.

Per-medium regeneration replaces only **Draft** pieces for that medium.  Approved pieces are
never overwritten.

---

## Consequences

### Positive

- Users can iterate on unsatisfying output without re-discovering a new topic cluster.
- No database migration required — all data needed for cluster reconstruction already exists.
- Per-medium regeneration avoids unnecessary LLM calls when only one medium is unsatisfactory.
- Fresh voice configuration is applied on each regeneration, so style changes take effect
  immediately.
- Original generation records are preserved; the user can compare old and new output.

### Negative

- Full-generation regeneration creates a duplicate `ContentGeneration` record for the same
  cluster.  Without version history (see Option C in design doc), there is no formal lineage link.
- Per-medium regeneration deletes and replaces Draft pieces — any in-progress manual edits
  (if that feature is added later) would be lost.
- Each regeneration incurs LLM API cost.  No rate-limiting is implemented in this iteration.

### Neutral

- The `UsedSeedNotes` table is unaffected — the seed note remains tracked and the guard
  added to `GenerateContentAsync` simply skips a duplicate insert.
- Social posts are always regenerated as a complete set.  Regenerating a single social post
  in isolation is out of scope for this decision.

---

## Alternatives Considered

### Option A: Full Generation Regeneration Only
One endpoint, no per-medium control.  Rejected because it forces unnecessary regeneration of
all content when only one medium is unsatisfactory, incurring avoidable cost and latency.

### Option C: Regeneration with Version History
Adds `RegenerationOf` FK on `ContentGeneration` and `ParentPieceId` FK on `ContentPiece` to
maintain full lineage.  Rejected as premature — it requires a schema migration and significant
implementation effort for a "nice to have" history UI that can be added later.  The chosen
approach (new records, original preserved) already provides enough context for a future
migration to add linkage.

---

## Related Decisions

- ADR-001: Backend Architecture (ASP.NET Core, EF Core, PostgreSQL)

---

## Notes

If version history becomes a requirement in future, Option C from the design document describes
the migration path.  The `ContentGeneration.Id` of the source generation can be stored as an
optional field at that point without restructuring any existing data.
