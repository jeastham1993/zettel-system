# ADR-013: Voice Interface Architecture

Date: 2026-03-07
Status: Removed (2026-10) — the voice assistant was removed. Kept as historical record.

## Context

We want to add a bi-directional voice interface to the knowledgebase — a user speaks a question,
the system retrieves relevant notes, and responds in speech. The key requirement is bi-directional
streaming: audio flows in both directions simultaneously with no blocking pause between input and
output, using Amazon Nova Sonic v2 via AWS Bedrock.

Three implementation paths were evaluated:
1. Python microservice with the Strands Agents Python SDK
2. Node.js microservice with the raw AWS SDK for JavaScript v3
3. Browser-direct connection to Bedrock via Cognito temporary credentials

The TypeScript version of the Strands Agents SDK was the preferred language choice, but it was
confirmed to have no Nova Sonic or audio streaming support as of 2026-03-07 (public preview,
text-only agent workflows only).

## Decision

We will implement the voice interface as a **Python microservice** using **FastAPI** for the
WebSocket server and the **Strands Agents Python SDK** (`BidiAgent`) for Nova Sonic bi-directional
session management.

The service exposes a single WebSocket endpoint. The browser streams PCM audio frames to the
service; the service relays them to a Nova Sonic v2 session. Nova Sonic fires tool calls when it
needs to retrieve information; the service executes those tools by calling the existing .NET API
over HTTP. Audio responses from Nova Sonic are relayed back to the browser.

Two tools are registered:
- `search_notes(query)` — calls `GET /api/search?q={query}&type=hybrid` on the .NET API
- `get_note(id)` — calls `GET /api/notes/{id}` on the .NET API

The React frontend gains a new `/voice` route. No changes are made to the .NET API, its
controllers, services, or database schema.

## Consequences

### Positive
- The Strands Python SDK is the reference implementation for Nova Sonic bi-directional streaming;
  community examples, documentation, and the Strands BidiAgent abstraction are all Python-first.
- The existing hybrid search endpoint is consumed as-is — the voice service is just another HTTP
  client of the .NET API, requiring zero backend changes.
- Complete isolation: the Python service is hidden behind a WebSocket interface. Its implementation
  language does not affect the .NET codebase, the React frontend, or deployment of either.
- If the TypeScript Strands SDK adds Nova Sonic support in future, migration is a self-contained
  rewrite of one service with no changes elsewhere.
- The `/voice` route is additive — existing UI pages and flows are unaffected.

### Negative
- Python is introduced as a third language in a repo that is otherwise .NET and TypeScript.
  Developers unfamiliar with Python will need to context-switch to work on the voice service.
- The Strands Agents SDK is v0.x; the BidiAgent API may have breaking changes. The version must
  be pinned and changelogs reviewed on upgrade.
- Local development requires running a second process (uvicorn) in addition to the .NET API and
  Vite dev server.
- Nova Sonic v2 requires audio in a specific format: 16kHz, 16-bit, mono PCM. The browser
  AudioWorklet must produce exactly this format; format mismatches cause silent or garbled sessions.

### Neutral
- `get_note` responses should be truncated to ~4000 characters before being passed to Nova Sonic
  to avoid context overflow on very long notes. This is tool-layer logic in the Python service.
- The voice feature is experimental. It lives behind a dedicated `/voice` route and does not
  affect existing functionality.

## Alternatives Considered

### Node.js + raw AWS SDK for JavaScript v3
The `@aws-sdk/client-bedrock-runtime` supports bi-directional streaming via
`InvokeModelWithBidirectionalStreamCommand`, but there is no Strands equivalent for the agent loop.
The tool-call detection, result injection, session continuation, and audio interleaving would all
need to be implemented manually. This is effectively re-implementing what Strands BidiAgent
provides, with higher implementation risk, no reference examples, and an estimated 10–15 days of
effort vs. 3–5 days for the Python approach. Not selected due to effort and risk.

### Browser-direct with Cognito
The browser connects to Bedrock directly using temporary credentials from an AWS Cognito identity
pool. Tool calls are executed as `fetch()` callbacks in the browser.
This approach requires Cognito setup (not currently in the codebase), couples the browser to three
separate systems (Nova Sonic, Cognito, .NET API), and makes it hard to add server-side logic
(caching, rate limiting, session persistence) later. Not selected due to high coupling and
Cognito complexity.

## Related Decisions
- ADR-001: Backend architecture (simple layered, no abstraction without need — voice service follows same principle)
- ADR-002: PostgreSQL native search (the hybrid search endpoint consumed by voice tools)
- ADR-011: AWS serverless deployment (Nova Sonic is also an AWS service; same credential chain)

## Notes
Full design document with coupling analysis, failure modes, and implementation plan:
`docs/design-voice-interface.md`

Strands Python SDK: https://github.com/strands-agents/sdk-python
TypeScript SDK (no audio support as of 2026-03-07): https://github.com/strands-agents/sdk-typescript
Nova Sonic v2 reference implementation: https://darryl-ruggles.cloud/bi-directional-voice-controlled-recipe-assistant-with-nova-sonic-2/
