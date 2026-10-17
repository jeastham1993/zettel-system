# Design: Content Regeneration from Existing Generation

Generated: 2026-02-23
Status: Removed (2026-10, commit `bed2ed0`) — historical record only

---

## Problem Statement

### Goal

Allow a user to re-trigger content generation using the **same cluster of notes** that was used
in an existing `ContentGeneration` run.  The current output may be unsatisfying (wrong tone,
poor connections drawn, repetitive phrasing) and the user wants a fresh attempt without
re-discovering the topic from scratch.

### Constraints

- **No new note-discovery step** — the cluster is fixed from the original run.
- Voice configuration must be loaded fresh so that any updates since the original run are applied.
- The original generation record must be preserved (non-destructive).
- The seed note tracking (`UsedSeedNotes`) must not block regeneration.

### Success Criteria

- [ ] A user can trigger regeneration of a full generation run and receive a new `ContentGeneration`
      record with fresh blog + social pieces.
- [ ] A user can trigger regeneration of a single medium ("blog" or "social") on an existing
      generation run and receive new `ContentPiece` records for that medium only.
- [ ] No database migration is required for the minimal option.
- [ ] Existing tests continue to pass; new endpoints have integration-test coverage.

---

## Context

### Current State

`POST /api/content/generate`

1. `TopicDiscoveryService.DiscoverTopicAsync()` — picks a random unused seed, walks the
   knowledge graph, returns a `TopicCluster(SeedNoteId, Notes, TopicSummary)`.
2. `ContentGenerationService.GenerateContentAsync(cluster)` — calls the LLM twice (blog,
   social), persists a `ContentGeneration` + `ContentPiece` rows, marks the seed note as used.

There is no mechanism to re-run step 2 against an existing cluster.

### Key Insight

`TopicCluster` is fully reconstructible from the persisted `ContentGeneration` entity:

| `TopicCluster` field | `ContentGeneration` column |
|---|---|
| `SeedNoteId` | `SeedNoteId` |
| `Notes` | Fetch `Notes` WHERE `Id IN ClusterNoteIds` |
| `TopicSummary` | `TopicSummary` |

No schema change is needed to reconstruct the cluster.

### Related Decisions

- ADR-001: Backend Architecture (ASP.NET Core, EF Core, PostgreSQL)

---

## Alternatives Considered

---

### Option A: Full Generation Regeneration Only

**Summary**: Add one endpoint that re-runs the full LLM pipeline on an existing cluster and
creates a new `ContentGeneration` record.

**API surface**:

```
POST /api/content/generations/{id}/regenerate
→ 201 ContentGenerationResponse (new generation)
→ 404 if source generation not found
```

**Implementation**:

```
ContentController.RegenerateGeneration(id)
  1. Load ContentGeneration by id (404 if missing)
  2. Load Note[] WHERE Id IN generation.ClusterNoteIds
  3. Construct TopicCluster(generation.SeedNoteId, notes, generation.TopicSummary)
  4. Call ContentGenerationService.GenerateContentAsync(cluster)
     → voice config loaded fresh inside here
     → seed note NOT re-added to UsedSeedNotes (already present)
  5. Return 201 with new generation
```

The only modification needed: `GenerateContentAsync` should skip the `UsedSeedNotes.Add`
when a seed is already present, or the caller passes a flag.  Simplest is a guard:

```csharp
if (!await _db.UsedSeedNotes.AnyAsync(u => u.NoteId == cluster.SeedNoteId))
    _db.UsedSeedNotes.Add(new UsedSeedNote { ... });
```

**Pros**:
- Minimal surface area — one endpoint, no schema changes, reuses existing service entirely.
- Original generation preserved; user can compare old vs new.
- Fresh voice config applied automatically.

**Cons**:
- Can only regenerate both mediums together — no per-medium control.
- Each regeneration creates a full duplicate of all pieces, even if only the blog was unsatisfactory.

**Coupling Analysis**:

| Component | Change | Impact |
|---|---|---|
| `ContentController` | +1 endpoint, reconstruct cluster | Low |
| `ContentGenerationService` | +1 guard on `UsedSeedNotes` insert | Low |
| `IContentGenerationService` | No change | None |
| `TopicDiscoveryService` | No change | None |
| Database schema | No change | None |

New dependencies introduced: none
Coupling impact: **Low**

**Failure Modes**:

| Mode | Severity | Occurrence | Detection | RPN |
|---|---|---|---|---|
| One or more cluster notes deleted since original | Medium | Low | 404 on note load (graceful — proceed with remaining notes) | 4 |
| LLM API failure during regeneration | High | Low | Exception → 502 response | 6 |
| Source generation not found | Low | Low | 404 | 2 |

**Evolvability Assessment**:
- Adding per-medium control later: Medium — would need a new endpoint or request body.
- Adding version history: Easy — already creates new records by design.

**Effort Estimate**: S (half a day)

---

### Option B: Full Generation + Per-Medium Regeneration (Recommended)

**Summary**: Two new endpoints — one to regenerate the full generation (same as A), and one
to regenerate only blog or social pieces on an existing generation in-place.

**API surface**:

```
POST /api/content/generations/{id}/regenerate
→ 201 ContentGenerationResponse (new generation)
→ 404 if source generation not found

POST /api/content/generations/{id}/regenerate/{medium}
  where medium = "blog" | "social"
→ 200 List<ContentPieceResponse> (new pieces for that medium)
→ 404 if generation not found
→ 400 if medium is invalid
```

**Implementation**:

`RegenerateGeneration(id)` — identical to Option A.

`RegenerateMedium(id, medium)`:

```
1. Load ContentGeneration by id (404 if missing)
2. Validate medium ("blog" | "social") → 400 if invalid
3. Load Note[] WHERE Id IN generation.ClusterNoteIds
4. Load voice config for medium (fresh)
5. Build noteContext string
6. Call GenerateBlogPostAsync or GenerateSocialPostsAsync (make internal → internal or extract)
7. Remove existing Draft pieces for that medium on this generation
8. Insert new ContentPiece rows for that medium (sequence from max existing)
9. Return new pieces
```

To enable step 6, two options:
- **6a** Expose `GenerateBlogPostAsync` / `GenerateSocialPostsAsync` as `protected internal`
  and call from controller (breaks encapsulation).
- **6b** Add `RegenerateMediumAsync(generationId, medium)` to `IContentGenerationService` —
  the service encapsulates all LLM logic (preferred).

**New service method** (preferred):

```csharp
// IContentGenerationService
Task<List<ContentPiece>> RegenerateMediumAsync(
    ContentGeneration generation,
    IReadOnlyList<Note> notes,
    string medium,
    CancellationToken cancellationToken = default);
```

**Pros**:
- Surgical regeneration — only regenerate what you dislike.
- No unnecessary LLM calls (cost + latency).
- No schema changes required.
- Service interface remains the single point of LLM interaction.

**Cons**:
- Slightly more surface area than A (two endpoints, one new service method).
- Per-medium regeneration removes existing Draft pieces — if user had manually edited a draft
  piece (if that feature ever exists), those edits are lost.

**Coupling Analysis**:

| Component | Change | Impact |
|---|---|---|
| `ContentController` | +2 endpoints | Low |
| `IContentGenerationService` | +1 method | Low |
| `ContentGenerationService` | +1 method implementation | Low |
| `TopicDiscoveryService` | No change | None |
| Database schema | No change | None |

New dependencies introduced: none
Coupling impact: **Low–Medium**

**Failure Modes**:

| Mode | Severity | Occurrence | Detection | RPN |
|---|---|---|---|---|
| Cluster notes deleted since original | Medium | Low | Proceed with remaining notes; log warning | 4 |
| LLM failure mid-regeneration | High | Low | Exception → 502; old pieces still present (not yet deleted) | 6 |
| Partial write (pieces deleted but new ones not saved) | High | Very Low | Wrap in DB transaction | 3 |
| Invalid medium value | Low | Low | 400 validation | 1 |

Note: The delete-then-insert for medium regeneration must be wrapped in a transaction to avoid
leaving the generation in a state with no pieces for that medium.

**Evolvability Assessment**:
- Version history (keep old pieces): Easy — change "delete" to "mark as superseded".
- Regenerate a single social post: Medium — would need piece-level endpoint.
- Regeneration count tracking: Easy — add counter field to `ContentGeneration`.

**Effort Estimate**: M (1–2 days)

---

### Option C: Regeneration with Full Version History

**Summary**: All of Option B, plus a `ParentPieceId` FK on `ContentPiece` to track lineage,
and a `RegenerationOf` FK on `ContentGeneration`.  Users can see all versions and pick between them.

**API surface**: All of B, plus:

```
GET /api/content/generations/{id}/history
→ 200 List<ContentGenerationResponse>

GET /api/content/pieces/{id}/history
→ 200 List<ContentPieceResponse>
```

**Schema changes**:

```sql
ALTER TABLE "ContentGenerations" ADD "RegenerationOf" text REFERENCES "ContentGenerations"("Id");
ALTER TABLE "ContentPieces" ADD "ParentPieceId" text REFERENCES "ContentPieces"("Id");
```

**Pros**:
- Complete audit trail — users can compare and revert to any version.
- Unlocks future "pick the best version" UI.

**Cons**:
- Requires EF Core migration.
- Significantly more complexity for a feature that may not be needed immediately.
- History endpoints add more API surface to maintain.
- Self-referential FKs complicate queries (recursive CTEs or eager-load depth).

**Coupling Analysis**:

| Component | Change | Impact |
|---|---|---|
| `ContentGeneration` model | +1 nullable FK | Medium |
| `ContentPiece` model | +1 nullable FK | Medium |
| `ZettelDbContext` | +2 relationship configs | Medium |
| `ContentController` | +2 endpoints + existing updates | Medium |
| EF Core migration | New migration required | Medium |

Coupling impact: **Medium–High**

**Failure Modes**: All of B, plus:
- Self-referential FK cycles if not handled carefully.
- Recursive history queries could be slow without index tuning.

**Evolvability Assessment**: High — everything in B is easy, and history gives maximum future flexibility.

**Effort Estimate**: L (3–5 days)

---

## Comparison Matrix

| Criterion | Option A | Option B | Option C |
|---|---|---|---|
| Complexity | 🟢 Very Low | 🟢 Low | 🔴 High |
| Evolvability | 🟡 Medium | 🟡 Medium | 🟢 High |
| Schema change required | 🟢 No | 🟢 No | 🔴 Yes |
| Effort | 🟢 S | 🟡 M | 🔴 L |
| Granularity | 🔴 Full only | 🟢 Full + per-medium | 🟢 Full + per-medium |
| Coupling impact | 🟢 Low | 🟢 Low | 🟡 Medium |
| Failure resilience | 🟡 Medium | 🟡 Medium | 🟡 Medium |

---

## Recommendation

**Recommended Option**: **Option B — Full Generation + Per-Medium Regeneration**

### Rationale

Option A is too coarse — a user who only dislikes the blog post should not have to regenerate
five social posts as well (incurring extra LLM cost and latency).  Option C's version-history
requirement adds schema migration risk and significant complexity for a feature whose primary
value is "try again".  Option B delivers the right granularity with no schema changes and
minimal coupling.

### Tradeoffs Accepted

- **No version history**: Accepted — if the user regenerates a medium, the old Draft pieces are
  replaced.  Approved/Published pieces would never be regenerated in-place (guard against this).
- **No single-post regeneration**: Accepted — social posts are generated as a cohesive set;
  regenerating one in isolation would likely produce an inconsistent tone.

### Risks to Monitor

- **LLM cost**: Each regeneration call costs tokens.  If this becomes expensive, add a simple
  rate-limit (e.g., max 5 regenerations per generation per day).
- **Notes deleted from cluster**: Monitor for warnings about shrinking clusters.  If a cluster
  drops below `MinClusterSize`, reject the regeneration with 409 Conflict.

---

## Implementation Plan

### Phase 1: Full Generation Regeneration

- [ ] Add `UsedSeedNotes` guard in `ContentGenerationService.GenerateContentAsync` (skip insert
      if seed already tracked).
- [ ] Add `POST /api/content/generations/{id}/regenerate` to `ContentController`.
- [ ] Integration test: regenerate → verify new generation created, original preserved.

### Phase 2: Per-Medium Regeneration

- [ ] Add `RegenerateMediumAsync(generation, notes, medium)` to `IContentGenerationService` and implement.
- [ ] Wrap delete-then-insert in a DB transaction.
- [ ] Add `POST /api/content/generations/{id}/regenerate/{medium}` to `ContentController`.
- [ ] Guard: return 409 if all pieces for that medium are already Approved (don't clobber
      approved content).
- [ ] Integration test: regenerate blog → verify old blog piece removed, new one created,
      social pieces untouched.

### Phase 3: Documentation

- [ ] Update `docs/API_REFERENCE.md` with new endpoints.
- [ ] Update `docs/ROADMAP.md` to mark feature complete.

---

## Decisions

- **Blocked on Approved**: Yes — both endpoints return 409 Conflict if `generation.Status == Approved`.
- **TopicSummary**: Reused verbatim from the original generation.  Only the LLM-generated content changes.
