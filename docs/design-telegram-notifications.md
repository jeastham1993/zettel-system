# Design: Outbound Telegram Notifications

Generated: 2026-02-27
Status: Draft

## Problem Statement

### Goal
Use the existing Telegram bot to send notifications back to the user — primarily
when scheduled content generation completes, so they know drafts are waiting for
review without polling the UI.

### Constraints
- Reuse the bot token and chat IDs already stored in `CaptureConfig`
- Notifications must be **non-blocking** — a Telegram outage must never prevent
  content generation from completing
- No new secrets or Telegram app registration required
- Keep the design consistent with the existing `GitHubPublishingService` /
  named-`HttpClient` pattern

### Success Criteria
- [ ] User receives a Telegram message when the blog scheduler finishes (weekly)
- [ ] User receives a Telegram message when the social scheduler finishes (daily)
- [ ] Telegram API failure produces a warning log but does not surface as an error
- [ ] When `TelegramBotToken` is not configured, the notifier is a no-op (no exceptions)

## Context

### Current State
The system already **receives** messages from Telegram via a webhook
(`POST /api/capture/telegram`). The bot token and allowlist of chat IDs are
stored in `CaptureConfig`:

```json
"Capture": {
  "TelegramBotToken": "<bot-token>",
  "AllowedTelegramChatIds": [123456789]
}
```

There is no outbound messaging. The Telegram Bot API for sending is a single
REST call:

```
POST https://api.telegram.org/bot{token}/sendMessage
Body: { "chat_id": 123456789, "text": "Hello!" }
```

### Related Decisions
- ADR-003: Fleeting notes architecture — Telegram established as first-class
  capture channel alongside email and web UI
- ADR-004: SQS/webhook ingestion — inbound Telegram flow documented

---

## Alternatives Considered

### Option A: Focused `ITelegramNotifier` service (recommended)

**Summary**: A thin `ITelegramNotifier` interface backed by a concrete HTTP
implementation, injected wherever notifications should fire. A no-op
`NullTelegramNotifier` is registered when the token is absent.

**Architecture**:

```
ContentSchedulerBase ──────→ ITelegramNotifier.BroadcastAsync(msg)
                                      │
CaptureService (optional) ───→ ITelegramNotifier.SendAsync(chatId, msg)
                                      │
                          ┌───────────┴──────────────┐
                          │  TelegramNotifier         │
                          │  - HttpClient "Telegram"  │
                          │  - CaptureConfig          │
                          │  POST /sendMessage        │
                          └───────────────────────────┘

Token absent → NullTelegramNotifier (no-op, no exception)
```

**Interface**:

```csharp
public interface ITelegramNotifier
{
    // Send to every chat ID in AllowedTelegramChatIds
    Task BroadcastAsync(string message, CancellationToken ct = default);

    // Send to a specific chat (e.g. reply to the sender on capture)
    Task SendAsync(long chatId, string message, CancellationToken ct = default);
}
```

**Notification events (Phase 1)**:

| Trigger | Message |
|---|---|
| Blog scheduler completes | `📝 Blog draft ready: "{title}" — {n} pieces generated` |
| Social scheduler completes | `📱 {n} social posts drafted and ready for review` |
| Generation skipped (no notes) | `⚠️ Scheduled generation skipped: no eligible notes` |
| Capture acknowledge (optional) | `✅ Note saved` |

**Coupling Analysis**:

| Component | New dependency | Direction | Impact |
|---|---|---|---|
| `ContentSchedulerBase` | `ITelegramNotifier` | Efferent (weak, interface) | Low |
| `CaptureService` | `ITelegramNotifier` | Efferent (weak, interface) | Low |
| `TelegramNotifier` | `CaptureConfig` | Efferent (reads token + IDs) | Minimal — reuses existing config |
| `TelegramNotifier` | `HttpClient "Telegram"` | Efferent | Isolated via factory |

No circular dependencies introduced. `ITelegramNotifier` lives in `ZettelWeb.Services`,
keeping it alongside `IPublishingService` and similar abstractions.

**Failure Modes**:

| Mode | Severity | Detection | Strategy |
|---|---|---|---|
| Telegram API unreachable | Low | `HttpRequestException` | Log warning, swallow — notification is best-effort |
| Bot token invalid / 401 | Low | Non-2xx response | Log warning, swallow |
| Chat ID removed from bot | Low | 400 Bad Request | Log warning, skip that chat ID |
| Token not configured | None | Resolved at startup (NullTelegramNotifier) | Silent no-op |
| Telegram rate limit (30/sec) | Negligible | 429 response | Log warning — personal tool sends <5/day |

All failure paths log at `Warning` and do not propagate exceptions.
The primary flow (content generation, note capture) is never interrupted.

**Evolvability**:
- Adding WhatsApp or email notifications → add `IWhatsAppNotifier` following same pattern
- Changing notification content → only `ContentSchedulerBase.RunGenerationAsync` changes
- Adding more trigger points (e.g. research complete) → inject `ITelegramNotifier` where needed
- Moving to webhook-reply (reply directly to the user's message) → add `ReplyAsync(messageId, text)` to interface

**Pros**:
- Minimal new code (~100 LOC total)
- Consistent with `GitHubPublishingService` / `PublerPublishingService` pattern
- Easy to test: `NullTelegramNotifier` in unit tests, fake in integration tests
- No new config keys — reuses `TelegramBotToken` and `AllowedTelegramChatIds`

**Cons**:
- Notifications are best-effort only — if the process restarts mid-generation, no notification is sent
- `ContentSchedulerBase` gains a new dependency (minor)

**Effort**: Small (S)

---

### Option B: In-memory event channel

**Summary**: Content generation raises a `ContentGeneratedEvent` onto a
`System.Threading.Channels.Channel<T>`. A background service dequeues events
and sends Telegram messages, fully decoupled from the generation path.

**Architecture**:

```
ContentSchedulerBase → Channel<ContentGeneratedEvent>.Writer.TryWrite(event)
                            │
                 TelegramNotificationWorker (BackgroundService)
                            │
                    ITelegramNotifier.SendAsync(...)
```

**Pros**:
- Generation path is never delayed by Telegram API latency
- Retry logic can be added in the worker

**Cons**:
- Significantly more infrastructure for marginal gain
- Events lost on process restart (in-memory channel)
- Over-engineered: Telegram `sendMessage` P99 latency is <500 ms — not a
  meaningful bottleneck for a scheduler that runs once daily

**Effort**: Medium (M)

---

### Option C: Outbox-backed reliable notifications

**Summary**: Notification intents are persisted to a `TelegramNotifications`
table, processed by a background poller, and marked delivered. Guarantees
at-least-once delivery across restarts.

**Architecture**:

```
ContentSchedulerBase → db.TelegramNotifications.Add(pending)
                            │
               TelegramNotificationPoller (BackgroundService)
                            │
               POST /sendMessage → mark delivered
```

**Pros**:
- Survives process restarts
- True at-least-once delivery

**Cons**:
- Requires a new DB migration and table
- Background poller adds operational complexity
- Severe over-engineering for notifications in a personal tool
- At-least-once delivery is meaningless when the user is also the only
  recipient: a duplicate "3 posts ready" message is more annoying than helpful

**Effort**: Large (L)

---

## Comparison Matrix

| Criterion | Option A (focused service) | Option B (channel) | Option C (outbox) |
|---|---|---|---|
| Complexity | 🟢 Low | 🟡 Medium | 🔴 High |
| Evolvability | 🟡 Good | 🟡 Good | 🟢 Excellent |
| Delivery guarantee | 🟡 Best-effort | 🟡 Best-effort | 🟢 At-least-once |
| Non-blocking | 🟢 Yes (log+swallow) | 🟢 Yes (channel) | 🟢 Yes (async) |
| New infrastructure | 🟢 None | 🟡 Channel worker | 🔴 DB table + migration |
| Consistent with codebase | 🟢 Yes | 🟡 Partial | 🔴 No |
| Effort | 🟢 S | 🟡 M | 🔴 L |

## Recommendation

**Recommended Option**: **Option A — Focused `ITelegramNotifier` service**

**Rationale**: This is a personal tool with a single user. Notifications are
a quality-of-life feature, not a business-critical workflow. Best-effort
delivery is entirely acceptable. The `NullTelegramNotifier` pattern ensures
the app degrades gracefully when unconfigured. Option A follows the exact same
pattern already used for GitHub and Publer publishing, keeping the codebase
consistent and predictable.

**Tradeoffs Accepted**:
- **No delivery guarantee across restarts**: Acceptable — if a notification is
  missed, the user will see the drafts when they next open the app
- **ContentSchedulerBase gains a dependency**: Mitigated by using an interface;
  unit tests use `NullTelegramNotifier` (no mock needed)

**Risks to Monitor**:
- Telegram Bot API rate limiting (30 msg/sec global) — negligible at current volume
- Bot token expiry — handled by `CaptureConfig` (shared with inbound path)

---

## Implementation Plan

### Phase 1: Core notifier
- [ ] Create `ITelegramNotifier` interface in `Services/`
- [ ] Implement `TelegramNotifier` using named HttpClient `"Telegram"`
- [ ] Implement `NullTelegramNotifier` (no-op for when token is absent)
- [ ] Register in `Program.cs` (conditional on token presence)
- [ ] Add `"Telegram"` named HttpClient in `Program.cs`

### Phase 2: Scheduler integration
- [ ] Inject `ITelegramNotifier` into `ContentSchedulerBase`
- [ ] Call `BroadcastAsync` at end of `RunGenerationAsync` with generation summary
- [ ] Call `BroadcastAsync` on the "no eligible notes" warning path

### Phase 3: Capture acknowledgment (optional)
- [ ] Inject `ITelegramNotifier` into `CaptureService`
- [ ] After successful `CaptureAsync` from Telegram source, call `SendAsync(chatId, "✅ Note saved")`

### Tests
- [ ] `TelegramNotifierTests`: verify correct URL + payload construction
- [ ] `TelegramNotifierTests`: verify non-2xx → log warning, no throw
- [ ] `ContentSchedulerTests`: verify `BroadcastAsync` called after generation
- [ ] `CaptureServiceTests`: verify `SendAsync` called on Telegram capture

## Open Questions
- [ ] Should the capture acknowledgment reply include the generated note title, or just "✅ Note saved"?
- [ ] Should failed generation (`catch` in scheduler) also send a Telegram alert? (useful for monitoring)
