---
type: problem-solution
category: infrastructure
tags: [embedding, semantic-search, cosine-similarity, bedrock, titan, ollama, thresholds, configuration]
created: 2026-02-25
updated: 2026-02-25
confidence: high
languages: [dotnet]
related: [INFRA-003, INFRA-005]
---

# Embedding Similarity Thresholds Are Model-Specific

## Problem

After switching the embedding provider from Ollama (`nomic-embed-text`) to Amazon
Bedrock (`amazon.titan-embed-text-v2:0`), semantic search quality dramatically dropped:

- Semantic search returned far fewer results
- KB health showed no semantic edges, breaking cluster detection and orphan suggestions
- "Related notes" panel on note pages returned nothing

No errors were logged. The system appeared healthy.

## Root Cause

Cosine similarity scores are **not comparable across embedding models**. Each model
produces a different absolute score distribution for the same content pairs:

| Model | Related content similarity range |
|---|---|
| `nomic-embed-text` | ~0.6–0.9 |
| `amazon.titan-embed-text-v2:0` | ~0.3–0.5 |
| `text-embedding-3-large` (OpenAI) | ~0.5–0.85 |

The thresholds in this codebase were calibrated for nomic-embed-text. After switching
to Titan, those same thresholds silently filtered out all legitimate matches before
they reached the application layer.

There were **two** hardcoded/configured thresholds, in different places:

### 1. `Search:MinimumSimilarity` (configurable, `appsettings.json`)

Applied in `SearchService.cs` as a `WHERE` clause in three query paths:
- `SemanticSearchAsync` — semantic search tab
- `FindRelatedAsync` — "related notes" panel
- `DiscoverAsync` — discovery feature

Default was `0.5`. With Titan, genuine matches score 0.3–0.5, so most were excluded.

### 2. `SuggestionThreshold` (was hardcoded, now configurable)

Was `private const double SuggestionThreshold = 0.6` in `KbHealthService.cs`.
Used in:
- Semantic edge detection for KB overview / cluster analysis
- Orphan note connection suggestions

At 0.6 against Titan's output, essentially no semantic edges were found.

## Solution

### Config changes (no code change needed for MinimumSimilarity)

```json
"Search": {
    "MinimumSimilarity": 0.2,
    "SuggestionThreshold": 0.3
}
```

### Code change: make SuggestionThreshold configurable

The hardcoded constant was replaced with a configurable field:

```csharp
// KbHealthService.cs
_suggestionThreshold = configuration.GetValue("Search:SuggestionThreshold", 0.3);
```

The default was changed from `0.6` to `0.3` to work out-of-the-box with Bedrock.

## Threshold Reference by Provider

| Setting | nomic-embed-text | Titan v2 | OpenAI text-3-large |
|---|---|---|---|
| `MinimumSimilarity` | 0.5 | 0.2 | 0.4 |
| `SuggestionThreshold` | 0.6 | 0.3 | 0.45 |

Start conservative (low threshold) and increase if you see too many irrelevant results.
The ranking quality of all these models is good — only the absolute score range differs.

## Key Insight

The models rank related content correctly — a low absolute cosine score from Titan
does not mean poor quality. The threshold gates exist to avoid noise, not to guarantee
relevance. When switching models, always recalibrate thresholds empirically.

## Checklist for Provider Switches

- [ ] Lower `Search:MinimumSimilarity` to match new model's range
- [ ] Lower `Search:SuggestionThreshold` to match new model's range
- [ ] Drop and recreate HNSW index at new dimensions (see INFRA-005)
- [ ] Re-embed all existing notes (`POST /api/notes/re-embed` or Settings page button)

## Update 2026-10-25: Absolute Thresholds Proved Insufficient for Titan

Even with correctly calibrated thresholds (`MinimumSimilarity` 0.2,
`SuggestionThreshold` 0.3), related-note results stayed hit-and-miss against
Titan v2. Live analysis of 379 notes showed Titan's **score compression**:
genuine matches and unrelated noise overlap in the same 0.2–0.45 absolute band,
so no single global threshold can separate signal from noise.

However, genuine matches still score dramatically higher *relative to the
noise band* (e.g. 0.70 for a true sibling note vs ≤0.24 for noise). Fixes:

1. **Relative gating** for related notes — new `Search:RelatedRelativeFloor`
   (default 0.6): results scoring below 60% of the top match's similarity are
   dropped. The absolute `MinimumSimilarity` floor still applies underneath.
2. **`Status = 'Permanent'` filter** in `FindRelatedAsync` — fleeting
   inbox/Telegram captures were polluting related results.
3. **Thin-note skip** — notes with < `Embedding:MinContentCharacters` (default 50)
   of stripped content get `EmbedStatus.Skipped`, a null embedding, and are
   excluded from semantic queries. Title-only embeddings of empty Source
   stubs were matching nothing/everything arbitrarily.
4. **Config key typo fixed** — `KbHealthService` read `Search:_suggestionThreshold`
   (leading underscore), so the configured `SuggestionThreshold` was silently
   ignored and only the code default ever applied.

After deploying, re-embed once so existing thin notes are marked `Skipped` and
their stale embeddings cleared (`POST /api/notes/re-embed`).
