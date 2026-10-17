# Design: Conversational Voice Interface

Generated: 2026-03-07
Status: Removed (2026-10) — the voice assistant was removed. Historical record only.

## Problem Statement

### Goal
Add a bi-directional voice interface that lets a user navigate their knowledgebase conversationally
via spoken queries. The system should retrieve relevant notes and synthesise spoken answers in
near-real time, using Nova Sonic v2 for combined STT/reasoning/TTS in a single streaming session.

### Constraints
- The existing .NET API must remain unchanged — voice is additive
- Nova Sonic v2 is available (AWS Bedrock, us-east-1)
- Bi-directional streaming is a hard requirement (not sequential STT → LLM → TTS)
- Voice support is optional / experimental — the main UI must not regress
- TypeScript Strands SDK has **no Nova Sonic/audio support** (public preview, text-only agents)

### Success Criteria
- [ ] User can ask a spoken question and receive a spoken answer within ~2 seconds of finishing
- [ ] Agent correctly retrieves notes relevant to the question using hybrid search
- [ ] Agent fetches full note content before synthesising detailed answers
- [ ] WebSocket disconnect is handled gracefully without a page reload
- [ ] Zero changes to existing .NET controllers, services, or migrations

---

## Current State

The .NET ZettelWeb API exposes:
- `GET /api/search?q={query}&type=hybrid` — ranked snippets (NoteId, Title, 200-char Snippet, Rank)
- `GET /api/notes/{id}` — full note content

The React frontend has no voice capability. The existing `VoiceController` manages writing-style
configuration for content generation and is unrelated to this feature.

### Related Decisions
- ADR-001: Backend architecture (simple layered, no unnecessary abstraction)
- ADR-002: PostgreSQL native search (hybrid search already built)

---

## Alternatives Considered

### Option A: Python + Strands microservice (recommended)

**Summary**: A self-contained Python FastAPI service handles the Nova Sonic bi-directional session
and dispatches tool calls to the existing .NET API over HTTP.

**Architecture**:

```
Browser
  Web Audio API (16kHz PCM capture)
  Audio playback (decoded from base64 PCM chunks)
       |
       | WebSocket (raw audio frames + text events)
       |
voice-service/ (Python, FastAPI, uvicorn)
  main.py        — WebSocket endpoint, session lifecycle
  agent.py       — Strands BidiAgent wrapping Nova Sonic v2
  tools.py       — search_notes(), get_note()
       |
       | HTTP (internal / localhost in dev)
       |
.NET ZettelWeb API (unchanged)
  GET /api/search?q=...&type=hybrid
  GET /api/notes/{id}
       |
PostgreSQL + pgvector (unchanged)
```

**Session flow**:
1. Browser opens WebSocket to `ws://voice-service/ws`
2. Python service starts a Strands BidiAgent session with Nova Sonic v2
3. Browser streams PCM audio frames → Python → Nova Sonic
4. Nova Sonic detects intent and fires `search_notes` tool call
5. Python tool calls `GET /api/search?q=...` → returns snippets + IDs
6. If answer requires depth, Nova Sonic fires `get_note` tool call
7. Python tool calls `GET /api/notes/{id}` → returns full content
8. Nova Sonic synthesises spoken answer, streams PCM audio back
9. Python relays PCM chunks → WebSocket → Browser → Audio element

**Tool definitions** (in `tools.py`):
```python
@tool
def search_notes(query: str) -> list[dict]:
    """Search the knowledgebase for notes relevant to a query.
    Returns a list of {id, title, snippet, rank} objects.
    Call get_note(id) to retrieve the full content of a specific note."""
    resp = httpx.get(f"{KB_API_URL}/api/search", params={"q": query, "type": "hybrid"})
    return resp.json()

@tool
def get_note(note_id: str) -> dict:
    """Retrieve the full content of a specific note by its ID."""
    resp = httpx.get(f"{KB_API_URL}/api/notes/{note_id}")
    return resp.json()
```

**Pros**:
- Strands BidiAgent handles Nova Sonic protocol, session management, and tool dispatch
- Python SDK is the reference implementation — community examples, well-tested
- Zero coupling to .NET codebase; voice service is fully isolated
- Strands tool decorator pattern is concise (~15 lines of tool code total)
- FastAPI WebSocket is straightforward and well-documented

**Cons**:
- Introduces Python to a predominantly .NET/TypeScript codebase
- Requires managing a second runtime (Python + uvicorn) in dev
- Strands is still evolving (v0.x); BidiAgent API may change
- Nova Sonic bi-directional streaming requires specific audio format (16kHz, 16-bit, mono PCM)

**Coupling Analysis**:
| Component | Afferent (Ca) | Efferent (Ce) | Notes |
|-----------|--------------|---------------|-------|
| voice-service | 1 (browser) | 2 (Nova Sonic, .NET API) | Isolated behind WS interface |
| .NET API | +0 new | +0 new | Unchanged — voice service is just another HTTP client |
| React frontend | +1 (VoicePage) | +1 (voice-service WS) | New route only; no changes to existing pages |

New dependencies: `fastapi`, `uvicorn`, `strands-agents`, `boto3`, `httpx`
Coupling impact: **Low** — the microservice boundary means no shared code or types

**Failure Modes**:
| Mode | Severity | Occurrence | Detection | RPN | Mitigation |
|------|----------|------------|-----------|-----|------------|
| Nova Sonic session drops mid-answer | High | Low | WebSocket close event | 4 | Auto-reconnect with exponential backoff |
| .NET API down during tool call | Medium | Low | HTTP 5xx/timeout | 3 | Agent responds "I couldn't reach the knowledgebase" |
| Browser WebSocket disconnect | Medium | Medium | onclose event | 6 | Frontend shows reconnect button; no data loss |
| Wrong audio format from browser | High | Low | Garbled/silent Nova Sonic session | 3 | Validate sample rate in AudioWorklet; log warning |
| Large note exceeds Nova Sonic context | Medium | Low | Truncated/confusing answer | 2 | Truncate `get_note` response to 4000 chars in tool |
| Strands BidiAgent API change (v0.x) | Medium | Medium | CI breakage | 4 | Pin Strands version; review changelogs on upgrade |

**Evolvability Assessment**:
- Add `create_fleeting_note` voice tool: Easy — add `@tool` function, `POST /api/notes`
- Swap Nova Sonic for another model: Medium — swap Strands model provider config
- Add conversation history / multi-turn memory: Medium — Strands session context
- Move to TypeScript once TS SDK matures: Easy — rewrite service, keep WS protocol
- Add text-chat fallback (no voice): Easy — second endpoint on same FastAPI service

**Effort Estimate**: M (3–5 days)

---

### Option B: TypeScript/Node.js service + raw AWS SDK

**Summary**: A Node.js service (Fastify or plain http) uses `@aws-sdk/client-bedrock-runtime`
to open a bi-directional stream with Nova Sonic, implementing the agent loop manually.

**Architecture**: Same topology as Option A but Node.js replaces Python.

**Pros**:
- Single language ecosystem (TypeScript matches the frontend)
- AWS SDK for JS v3 supports bi-directional streaming via `InvokeModelWithBidirectionalStreamCommand`
- Could share types/utilities with frontend build tooling in future

**Cons**:
- No Strands equivalent in TypeScript for Nova Sonic — must implement full agent loop manually:
  tool-call event detection, result injection, session continuation, audio interleaving
- AWS JS SDK bi-directional streaming for Nova Sonic has minimal documentation and no reference examples
- Significantly more code (~300–400 lines vs ~80 in Python with Strands)
- Higher risk: protocol bugs in the agent loop are subtle and hard to debug
- TypeScript Strands SDK explicitly confirmed to have no audio/Nova Sonic support

**Coupling Analysis**: Same topology as Option A.
Coupling impact: **Low** (same isolation), but **implementation risk: High**

**Failure Modes**: Same as Option A, plus:
| Mode | Severity | Occurrence | Detection | RPN | Mitigation |
|------|----------|------------|-----------|-----|------------|
| Agent loop protocol bug | High | Medium | Silent failures, wrong audio | 9 | Extensive integration tests; replay logs |
| Event ordering race in async stream | High | Low | Interleaved audio corruption | 4 | Careful async/await discipline; test harness |

**Evolvability**: Moving to Strands TS once it adds audio: Easy. But the manual loop is a dead end.

**Effort Estimate**: XL (10–15 days) — the agent loop implementation is the bottleneck

---

### Option C: Browser-direct with Cognito (no microservice)

**Summary**: The browser opens a bi-directional Bedrock stream directly using temporary AWS
credentials from Cognito. Tool calls are executed as browser callbacks calling the .NET API.

**Architecture**:
```
Browser
  Cognito identity pool → temporary AWS credentials
  Direct bi-directional stream → Nova Sonic (AWS Bedrock)
  Tool callbacks → fetch() → GET /api/search, GET /api/notes/{id}
```

**Pros**:
- No server-side microservice to maintain or deploy
- Simplest deployment story — static frontend + existing .NET API

**Cons**:
- AWS credentials in the browser require Cognito setup (not currently in the codebase)
- Tool call results must be injected back into the Bedrock stream from the browser — complex
- .NET API must allow CORS from the browser origin (currently internal); acceptable but worth noting
- Harder to add server-side logic (caching, rate limiting, auth) later
- AWS SDK bi-directional streaming from browser is even less documented than Node.js
- Couples the browser to AWS Bedrock directly — hard to swap models later

**Coupling Analysis**:
| Component | Afferent | Efferent | Notes |
|-----------|----------|----------|-------|
| Browser | users | Nova Sonic, .NET API, Cognito | Browser now couples directly to 3 external systems |

Coupling impact: **High** — the browser does too much

**Failure Modes**: Superset of Option A, plus:
| Mode | Severity | Occurrence | Detection | RPN | Mitigation |
|------|----------|------------|-----------|-----|------------|
| Expired Cognito credentials mid-session | High | Medium | Stream auth failure | 8 | Refresh token flow; complex in browser |
| CORS misconfiguration | Medium | Low | Console errors | 3 | Explicit CORS headers on .NET API |

**Effort Estimate**: XL (8–12 days) — Cognito setup + browser stream complexity

---

## Comparison Matrix

| Criterion | A: Python + Strands | B: TS + raw SDK | C: Browser-direct |
|-----------|--------------------|-----------------|--------------------|
| Implementation complexity | Low | High | High |
| Proven Nova Sonic support | Yes (reference impl) | Minimal docs | Minimal docs |
| Coupling impact on .NET | None | None | None (CORS add) |
| Coupling impact on browser | Low (WS only) | Low (WS only) | High (3 systems) |
| Evolvability | High | Medium | Low |
| Time to working state | M (3–5d) | XL (10–15d) | XL (8–12d) |
| New language in repo | Yes (Python) | No | No |
| Failure resilience | High | Medium | Low |
| Aligns with user's preference | Close (TS preferred but unavailable) | Best language fit | No |

---

## Recommendation

**Recommended Option: A — Python + Strands microservice**

### Rationale

The TypeScript Strands SDK has no Nova Sonic/audio support. Implementing the bi-directional agent
loop manually in Node.js (Option B) is the equivalent of building Strands yourself — 10x the effort
with higher protocol risk. That's not a tradeoff worth making for a new, experimental feature.

The microservice boundary is what matters here, not the language. The Python service is hidden
entirely behind a WebSocket interface. The React frontend, .NET API, and all existing code are
completely unaffected by the implementation language of the voice service. If the TypeScript Strands
SDK gains Nova Sonic support in future, migrating is a self-contained rewrite of one service.

Option C (browser-direct) introduces too much coupling and Cognito complexity for an experimental feature.

### Tradeoffs Accepted
- **Python in a .NET/TS repo**: Acceptable because the microservice boundary isolates it completely.
  It's a separate `voice-service/` directory with its own `requirements.txt` and startup command.
- **Strands v0.x instability**: Acceptable because the feature is experimental. Pin the version.
- **Second dev process**: Minor friction. Documented in README; single `uvicorn` command.

### Risks to Monitor
- **Strands BidiAgent API changes**: Pin `strands-agents==x.y.z` and review changelog on bump
- **Nova Sonic audio format**: Browser must produce exactly 16kHz 16-bit mono PCM — validate early
- **Note content truncation**: `get_note` should cap at ~4000 chars to stay within Nova Sonic context

---

## Implementation Plan

### Phase 1: Voice service skeleton (no audio yet)
- [ ] Create `voice-service/` with `requirements.txt`, `main.py`, `tools.py`
- [ ] Implement `search_notes` and `get_note` tools calling the .NET API
- [ ] Text-only WebSocket endpoint to validate tool execution end-to-end
- [ ] Confirm Nova Sonic credentials and region config work

### Phase 2: Nova Sonic bi-directional session
- [ ] Integrate Strands `BidiAgent` with Nova Sonic v2 model
- [ ] Wire browser audio (Web Audio API + AudioWorklet → WebSocket → Python)
- [ ] Wire Python audio output → WebSocket → browser → AudioContext playback
- [ ] Test round-trip: speak query → get spoken answer

### Phase 3: React `/voice` route
- [ ] Add `/voice` route to React router
- [ ] `useVoiceSession` hook: WebSocket connection, audio capture, audio playback
- [ ] `VoicePage` component: push-to-talk or VAD, status indicator, transcript display
- [ ] Handle disconnect/reconnect gracefully

### Phase 4: Polish
- [ ] Show cited note titles alongside spoken answer
- [ ] Truncate `get_note` response to 4000 chars in tool
- [ ] Add to `docker-compose.dev.yml` as optional service (profile-gated)
- [ ] Document startup in README

---

## Resolved Design Decisions

| Question | Decision |
|----------|----------|
| Turn detection | Voice Activity Detection (VAD) — no push-to-talk button required |
| .NET API offline | `/voice` route checks a health endpoint on the voice service; degrades gracefully with a clear "voice service unavailable" state rather than a broken UI |
| Cited notes in UI | Yes — note titles returned by tool calls are displayed as clickable links in a sidebar panel while the spoken answer plays |
| .NET API base URL env var | `ZETTEL_API_URL` |

### Implications for Phase 3 (React `/voice` route)
- `useVoiceSession` hook must also poll or check `GET /health` on the voice service before
  attempting a WebSocket connection; surface a degraded state if the service is unreachable
- The WebSocket protocol should include a structured event type for tool results so the frontend
  knows which note IDs/titles to display: `{ type: "citations", notes: [{id, title}] }`
- VAD removes the need for a push-to-talk button; a visual indicator of "listening" vs "speaking"
  states is sufficient
