---
type: pattern
category: patterns
tags: [fullstack, api, frontend, workflow, react, dotnet, checklist]
created: 2026-02-23
confidence: high
languages: [typescript, react, csharp, dotnet]
related: [ADR-001, ADR-008, FE-002]
---

# Full-Stack Feature Completion: Backend and Frontend in One Unit

## Problem

A new API endpoint is designed, implemented, tested, and committed on the backend.
The frontend client function and UI are treated as a follow-up task. This produces:

- A period where the feature is unreachable from the UI
- A second context-switch back into the codebase
- Risk of HTTP method mismatches (see FE-002) going undetected until runtime
- Design docs and ADRs that describe "new endpoints" without capturing the user-facing
  experience

This pattern was observed during the content regeneration feature (ADR-008): the backend
endpoints were fully implemented and committed before any frontend work was started, requiring
a separate pass to add the API client functions, UI buttons, and to fix a pre-existing
method mismatch on the approve/reject endpoints discovered during that pass.

## Solution

Treat each new API endpoint as a 3-layer unit: **controller → API client function → UI**.
All three should be included in the same commit (or at minimum the same PR).

### Completion Checklist for a New API Endpoint

When adding any `[HttpPost/Put/Delete]` action to a controller:

- [ ] Backend endpoint implemented and passing tests
- [ ] API client function added to `src/zettel-web-ui/src/api/*.ts`
  - [ ] HTTP verb in client function matches controller attribute (`post` for `[HttpPost]`,
        `put` for `[HttpPut]`, etc.)
  - [ ] URL path matches the controller route exactly
- [ ] UI wired up: button, action, loading state, toast on success/error
- [ ] React Query cache invalidation correct (invalidate the right query key)
- [ ] `docs/API_REFERENCE.md` updated with the new endpoint

### HTTP Verb Cross-Check (critical)

Before writing the client function, confirm the verb from the controller:

| Controller attribute | Client function to use |
|---|---|
| `[HttpGet]` | `get<T>(url)` |
| `[HttpPost]` | `post<T>(url, body?)` |
| `[HttpPut]` | `put<T>(url, body)` |
| `[HttpDelete]` | `del(url)` |

Never assume — always read the attribute. The TypeScript compiler will not catch a wrong
HTTP verb; the only signal is a `405 Method Not Allowed` at runtime.

## Key Insight

The backend is done when the **user can use it**, not when the server accepts the request.
Designing, implementing, and reviewing the full stack together also catches API surface
issues (wrong verb, ambiguous response shape) before they're baked in.

## When to Split Across Commits

Splitting is acceptable when:
- The UI work is genuinely blocked on a design decision not yet made
- The feature is internal/background (no user-facing surface, e.g. a scheduler job)
- The PR is already large and reviewers have asked to split it

Even then, open a tracking issue or TODO before closing the backend PR.
