---
type: problem-solution
category: dotnet
tags: [microsoft-extensions-ai, ichatclient, chatresponse, chatcompletion, breaking-change, tests, fakes]
created: 2026-02-25
updated: 2026-02-25
confidence: high
languages: [dotnet]
related: []
---

# Microsoft.Extensions.AI v10.x Renamed ChatCompletion → ChatResponse

## Problem

After referencing `Microsoft.Extensions.AI.Abstractions` v10.3.0, test code that
implements a fake `IChatClient` fails to compile:

```
error CS0246: The type or namespace name 'ChatCompletion' could not be found
error CS0246: The type or namespace name 'StreamingChatCompletionUpdate' could not be found
error CS0738: 'FakeChatClient' does not implement interface member
  'IChatClient.GetResponseAsync'. Cannot implement because return type
  does not match 'Task<ChatResponse>'.
```

## Root Cause

Microsoft renamed core types between pre-v10 and v10.x of `Microsoft.Extensions.AI`:

| Old name (pre-v10) | New name (v10.x) |
|---|---|
| `ChatCompletion` | `ChatResponse` |
| `StreamingChatCompletionUpdate` | `ChatResponseUpdate` |

The production code only calls `response.Text` on the result of `GetResponseAsync`,
so it compiles cleanly against v10. Fake/mock implementations that explicitly name
the return type are where the break surfaces.

## Solution

Update fake implementations to use the new type names:

```csharp
// Before (pre-v10)
public Task<ChatCompletion> GetResponseAsync(
    IEnumerable<ChatMessage> messages,
    ChatOptions? options = null,
    CancellationToken cancellationToken = default)
    => Task.FromResult(new ChatCompletion(new ChatMessage(ChatRole.Assistant, Response)));

public IAsyncEnumerable<StreamingChatCompletionUpdate> GetStreamingResponseAsync(...)
    => throw new NotSupportedException();
```

```csharp
// After (v10.x)
public Task<ChatResponse> GetResponseAsync(
    IEnumerable<ChatMessage> messages,
    ChatOptions? options = null,
    CancellationToken cancellationToken = default)
    => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Response)));

public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(...)
    => throw new NotSupportedException();
```

## Notes

- `ChatResponse.Text` is still the correct property to read the response text
- The `GetService(Type, object?)` method signature is unchanged
- `ChatOptions`, `ChatMessage`, `ChatRole` are all unchanged
- This only affects code that explicitly names the return types (fakes, decorators, etc.)
