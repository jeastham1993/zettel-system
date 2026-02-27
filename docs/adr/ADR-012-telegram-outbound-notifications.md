# ADR-012: Outbound Telegram Notifications via `ITelegramNotifier`

Date: 2026-02-27
Status: Proposed

## Context

The system already receives messages from Telegram (capture channel, ADR-003).
The bot token and allowlisted chat IDs are stored in `CaptureConfig`. Users want
to receive Telegram notifications when scheduled content generation completes
(blog weekly, social daily), so they know to review drafts without checking the UI.

Three delivery approaches were evaluated:
1. A focused `ITelegramNotifier` service (best-effort, fire-and-forget)
2. An in-memory event channel decoupling the notification send from generation
3. A DB-backed outbox for reliable at-least-once delivery

## Decision

Implement outbound notifications as a focused `ITelegramNotifier` service
(Option A). A concrete `TelegramNotifier` wraps the Telegram Bot API
`sendMessage` endpoint using a named `HttpClient`. All failure paths swallow
exceptions and log at `Warning` — notifications are best-effort and must never
interrupt the primary flow.

A `NullTelegramNotifier` is registered when `TelegramBotToken` is absent,
ensuring the app degrades gracefully without configuration.

Notification targets are the existing `CaptureConfig.AllowedTelegramChatIds`
list — no new config keys are needed.

## Consequences

### Positive
- Reuses the existing bot token and chat ID list — no new Telegram app setup
- Consistent with the `IPublishingService` / named-`HttpClient` pattern already
  in the codebase
- `NullTelegramNotifier` makes unit tests trivial (inject the no-op, no mocking)
- Adding future notification triggers (research complete, errors) is a one-line
  injection at each call site

### Negative
- Notifications are best-effort: a process restart mid-generation will not send
  the notification for that run
- `ContentSchedulerBase` gains a new dependency on `ITelegramNotifier`

### Neutral
- The same Telegram bot is used for both capture (inbound) and notification
  (outbound) — this is intentional and reduces operational complexity

## Alternatives Considered

### In-memory event channel (Option B)
Decouples generation from notification send via `System.Threading.Channels`.
Rejected because Telegram `sendMessage` is <500 ms and the overhead of a
background worker adds complexity without meaningful benefit at this scale.

### Outbox-backed reliable delivery (Option C)
Persists notification intents to a DB table for at-least-once delivery.
Rejected as over-engineering for a personal tool — a missed notification is
trivially handled by opening the app.

## Related Decisions
- ADR-003: Fleeting notes architecture — Telegram as capture channel
- ADR-004: SQS/webhook ingestion — inbound Telegram flow

## Notes
See `docs/design-telegram-notifications.md` for the full alternatives analysis,
failure mode table, and coupling breakdown.
