# ADR-004: SQS Webhook Ingestion for Private Server

Date: 2026-02-14
Status: Proposed

## Context

Zettel-Web runs on a private server with no public endpoint. The fleeting
notes capture system (ADR-003) includes email and Telegram webhook
endpoints, but webhook providers require a publicly-reachable URL to
deliver callbacks. Without a public endpoint, email and Telegram capture
channels are non-functional.

We need a way to bridge the public internet (where webhook providers POST)
to the private server (where capture processing happens). The solution must
be low-cost, low-maintenance, and preserve the existing CaptureService
architecture which is proven with 293 tests.

Email will be captured via Amazon SES. Infrastructure will be managed with
AWS CDK (TypeScript). Lambda code will be TypeScript.

## Decision

Use a **thin AWS Lambda relay** behind API Gateway to receive webhook
payloads and forward them unmodified to an SQS queue. The .NET backend
polls SQS via a new `SqsPollingBackgroundService` and processes messages
through the existing `CaptureService` pipeline unchanged.

Key design principles:
- **Lambda is a dumb pipe**: receives raw JSON, adds a `source` message
  attribute (email/telegram), writes to SQS. ~20 lines of TypeScript.
  No parsing, no validation, no business logic.
- **All intelligence stays in .NET**: sender validation, payload parsing,
  allowlists, note creation -- all handled by existing CaptureService.
- **Delete-after-persist**: SQS messages are only deleted after successful
  database persistence, preventing data loss on partial failures.
- **DLQ for poison messages**: Messages that fail processing 3 times move
  to a dead letter queue for manual inspection.
- **Config-gated**: SQS polling only activates when `Capture:SqsQueueUrl`
  is configured. Without it, the app works exactly as before.

### AWS Components (CDK TypeScript)
- API Gateway: public HTTPS endpoint with API key auth + 10 req/s throttle
- Lambda: thin relay (~20 LOC TypeScript), forwards raw payload to SQS
- SQS Standard Queue: 14-day retention, visibility timeout 120s
- SQS Dead Letter Queue: maxReceiveCount 3
- CloudWatch Alarms: queue age > 1hr, DLQ depth > 0, Lambda errors
- SNS topic -> personal email for alarm notifications
- SES receipt rule to POST inbound email to API Gateway

### .NET Components
- `SqsPollingBackgroundService`: long-polls SQS, delegates to CaptureService
- SQS polling health check (follows EmbeddingHealthCheck pattern)
- `Capture:SqsQueueUrl` config key (empty = SQS polling disabled)
- AWSSDK.SQS NuGet dependency

## Consequences

### Positive
- Email (via SES) and Telegram capture works from a private server
- Messages survive server downtime for up to 14 days (SQS retention)
- Zero business logic in AWS -- all parsing/validation stays in .NET
  where it is tested (293 tests unchanged)
- Zero cross-boundary knowledge coupling (Lambda doesn't know about
  note formats, sender allowlists, or parsing logic)
- Deployment independence: Lambda changes only if SQS API changes
- Follows established BackgroundService patterns (EmbeddingBgSvc,
  EnrichmentBgSvc)
- Cheap: estimated < $1/month at personal usage levels
- Infrastructure is reproducible via CDK

### Negative
- New external dependency: AWSSDK.SQS in .NET project
- Requires AWS account and IAM configuration
- No validation at the edge: spam reaches SQS before .NET rejects it
  (mitigated by API Gateway API key + throttling)
- CloudWatch monitoring setup required for visibility into AWS-side
  failures
- SQS Standard queue delivers at-least-once: rare duplicate notes
  possible

### Neutral
- Existing direct webhook endpoints (POST /api/capture/email,
  /api/capture/telegram) remain functional for local development or
  future public deployment
- SqsPollingBackgroundService can be disabled via empty config, making
  AWSSDK.SQS a dormant dependency when not needed

## Alternatives Considered

### Validating Lambda + SQS
Lambda validates webhook secrets and sender allowlists before enqueuing,
producing normalized messages.

Not chosen because:
- Duplicates validation logic across Lambda (TypeScript) and .NET (C#)
- Creates configuration synchronization dependency (allowlists in 2 places)
- Requires deployment coordination for schema changes
- Cross-boundary knowledge coupling: 3 (config, email parsing, Telegram
  parsing)
- Total FMEA RPN: 1193 vs 942 for thin relay
- The edge-security benefit is marginal for a single-user app with API
  Gateway throttling

### API Gateway Direct to SQS (No Lambda)
API Gateway VTL templates map HTTP requests directly to SQS SendMessage.

Not chosen because:
- VTL templates cannot be unit-tested locally
- VTL errors are opaque and hard to debug
- No webhook secret validation possible (VTL has no crypto functions)
- Total FMEA RPN: 1152 vs 942 for thin relay
- Cost savings (~$0.20/month) do not justify maintainability tradeoff

## Related Decisions
- [ADR-003](ADR-003-fleeting-notes-architecture.md): Fleeting Notes
  Architecture -- defines the CaptureService + Background Enrichment
  pattern that this decision extends
- [ADR-001](ADR-001-backend-architecture.md): Simple Layered Architecture
  -- SqsPollingBackgroundService follows the established service layer
  patterns

## Notes
- Design document: [design-sqs-webhook-ingestion.md](../design-sqs-webhook-ingestion.md)
- The Lambda function is intentionally minimal to avoid the "two brains"
  problem where business logic lives in multiple runtimes
- If the server eventually gets a public endpoint, the SQS path can be
  disabled and direct webhooks re-enabled with zero code changes
- FMEA analysis details in the design document
