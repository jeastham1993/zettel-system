---
type: problem-solution
category: frontend
tags: [react, dotnet, http, 405, method-not-allowed, api-client, fetch]
created: 2026-02-23
confidence: high
languages: [typescript, react, csharp, dotnet]
related: [PAT-002]
---

# Frontend API Client HTTP Method Must Match Backend Controller Attribute

## Problem

Clicking "Approve" or "Reject" on a content piece silently failed in the browser with:

```
Failed to load resource: the server responded with a status of 405 (Method Not Allowed)
api/content/pieces/202602222103302849295/approve
```

The action appeared to do nothing — no toast, no state change.

## Root Cause

The frontend API client called the endpoints using `post()` (HTTP POST):

```typescript
// content.ts — WRONG
export function approvePiece(id: string): Promise<void> {
  return post<void>(`/api/content/pieces/${encodeURIComponent(id)}/approve`)
}
```

But the backend controller declared them as `[HttpPut]`:

```csharp
// ContentController.cs
[HttpPut("pieces/{id}/approve")]
public async Task<IActionResult> ApprovePiece(string id) { ... }

[HttpPut("pieces/{id}/reject")]
public async Task<IActionResult> RejectPiece(string id) { ... }
```

ASP.NET Core returns `405 Method Not Allowed` when the route matches but the HTTP verb does
not. React Query treats 405 as an error but the `onError` handler was not wired up on the
approve/reject mutations at the time of writing, so the failure was silent.

## Fix

Change the client to use `put()` and pass an empty body (the endpoint has no `[FromBody]`
parameter, so the body is ignored by the server):

```typescript
// content.ts — CORRECT
export function approvePiece(id: string): Promise<void> {
  return put<void>(`/api/content/pieces/${encodeURIComponent(id)}/approve`, {})
}

export function rejectPiece(id: string): Promise<void> {
  return put<void>(`/api/content/pieces/${encodeURIComponent(id)}/reject`, {})
}
```

## Why It Was Missed

The frontend client was written during a separate pass from the backend implementation.
When writing client functions without the controller open side-by-side, it is easy to
default to `post()` for any "action" endpoint regardless of the actual HTTP verb.

The integration tests only test the backend in isolation (via `HttpClient` with the correct
verb), so the mismatch was not caught until the UI was manually exercised.

## Prevention

1. **Always read the controller attribute** (`[HttpGet/Post/Put/Delete]`) before writing the
   client function. Never assume.
2. **Complete frontend and backend together** (see PAT-002) so the controller is open while
   writing the client.
3. **Wire up `onError`** on all React Query mutations so that 4xx/5xx responses produce a
   visible toast rather than silently failing.
