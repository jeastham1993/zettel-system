---
type: problem-solution
category: infrastructure
tags: [ollama, embedding, nomic-embed-text, context-length, truncation]
created: 2026-02-14
updated: 2026-02-14
confidence: high
languages: [dotnet]
related: []
---

# Ollama Embedding Model Context Length Exceeded

## Problem

The EmbeddingBackgroundService failed with:

```
OllamaSharp.Models.Exceptions.OllamaException:
  the input length exceeds the context length
```

Notes with long content (e.g., imported Notion pages at 26,000+ chars)
exceeded the Ollama embedding model's token context window.

## Root Cause

**Key insight**: Ollama's `nomic-embed-text` has a *maximum* context of
8192 tokens, but the **default `num_ctx` is 2048 tokens**. At ~3-4
chars/token, 8,000 characters produces ~2,000-2,667 tokens — right at
or above the default limit.

The original fix of truncating to 8,000 characters was based on the
model's maximum, not its default runtime configuration.

## Solution

Configurable truncation limit defaulting to 4,000 characters:

```csharp
// Read from config, default 4000 (safe for num_ctx=2048)
_maxInputCharacters = configuration
    .GetValue("Embedding:MaxInputCharacters", 4_000);

var text = $"{note.Title}\n\n{note.Content}";
if (text.Length > _maxInputCharacters)
{
    text = text[.._maxInputCharacters];
}
```

Config in `appsettings.json`:
```json
"Embedding": {
    "MaxInputCharacters": 4000,
    "MaxRetries": 3
}
```

## Gotcha: Max vs Default Context

| Limit | Tokens | Safe chars (3 c/t) |
|-------|--------|---------------------|
| nomic-embed-text max | 8192 | ~24,000 |
| Ollama default num_ctx | 2048 | ~6,000 |
| **Our safe default** | ~1,000-1,300 | **4,000** |

If you increase `num_ctx` in Ollama (via Modelfile or API), you can
raise `MaxInputCharacters` accordingly.

## Also Fixed: Infinite Retry Loop

The original implementation had no max retry limit. Failed notes were
retried every 30 seconds forever. Added `Embedding:MaxRetries` (default
3) matching the pattern in `EnrichmentBackgroundService`.

## Notes

- Failed notes are marked `EmbedStatus.Failed` and retried up to
  `MaxRetries` times, then skipped permanently.
- For most Zettelkasten notes (short atomic ideas), this limit is
  never hit. It mainly affects long imported documents.
- Truncation loses tail content but preserves the title and opening,
  which typically carry the most semantic signal.
