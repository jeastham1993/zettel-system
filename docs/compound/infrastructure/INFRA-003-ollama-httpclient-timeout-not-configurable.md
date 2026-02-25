---
type: problem-solution
category: infrastructure
tags: [ollama, httpclient, timeout, ollamasharp, ubuntu, cpu-inference]
created: 2026-02-25
updated: 2026-02-25
confidence: high
languages: [dotnet]
related: [INFRA-001]
---

# OllamaSharp HttpClient Timeout Hardcoded at 100s

## Problem

`embedding.process` spans timed out for **all** notes on an Ubuntu server running
Ollama, regardless of note size:

```
The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.
```

## Root Cause

`OllamaApiClient` has two constructor overloads:

| Constructor | HttpClient behaviour |
|---|---|
| `new OllamaApiClient(Uri uri, string model)` | Creates an internal `HttpClient` with a **hardcoded 100-second timeout** |
| `new OllamaApiClient(HttpClient client, string model)` | Uses the provided client's timeout |

The original code used the `Uri` overload:

```csharp
// Program.cs — ORIGINAL (100s hardcoded, not configurable)
builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
    new OllamaApiClient(new Uri(ollamaUri), embeddingModel));
```

On a **CPU-only Ubuntu server**, Ollama cold-loads the embedding model from disk on each
request when it's been evicted from memory. Model load time alone can exceed 100 seconds,
causing every embedding to fail before the actual inference even runs.

This is distinct from INFRA-001 (context length exceeded) — the failure mode is timeout,
not a payload error, and it affects all notes regardless of size.

## Solution

Pass a pre-configured `HttpClient` with a generous, configurable timeout:

```csharp
// Program.cs — FIXED
var ollamaUri = builder.Configuration["Embedding:OllamaUrl"] ?? "http://localhost:11434";
var ollamaTimeoutSeconds = builder.Configuration.GetValue("Embedding:HttpTimeoutSeconds", 300);
var ollamaHttpClient = new HttpClient
{
    BaseAddress = new Uri(ollamaUri),
    Timeout = TimeSpan.FromSeconds(ollamaTimeoutSeconds)
};
builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
    new OllamaApiClient(ollamaHttpClient, embeddingModel));
```

`appsettings.json` default (300s — accommodates model-load latency on slow hardware):

```json
"Embedding": {
    "HttpTimeoutSeconds": 300
}
```

## Why All Notes Fail (Not Just Large Ones)

Content size is irrelevant when the bottleneck is model loading. On a server without a
GPU, or under memory pressure that evicts the model between requests, the cold-start
penalty applies to every request. Once the model stays resident in memory (warm),
subsequent requests complete in seconds.

## Notes

- The `Enrichment`, `GitHub`, and `Publer` HTTP clients in `Program.cs` all use named
  `AddHttpClient` registrations with explicit timeouts. The Ollama client was the only
  one bypassing `IHttpClientFactory`.
- OllamaSharp v5+ (used here at v5.4.16) supports the `HttpClient` constructor overload.
  Verify this is still valid if upgrading OllamaSharp major versions.
- After fixing the timeout, any notes stuck in `EmbedStatus.Failed` with exhausted
  retry counts will need manual requeue via the kb-health endpoint.
