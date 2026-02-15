# Design: SQS Webhook Ingestion for Private Server

Generated: 2026-02-14
Status: Draft

## Problem Statement

### Goal
Enable email and Telegram webhook capture for a Zettel-Web instance running
on a private server with no public endpoint. Webhook providers (Amazon SES,
Telegram Bot API) require a publicly-reachable URL to deliver callbacks.

### Constraints
- Private server has no public IP or domain
- Single-user personal app (James only)
- Existing capture system (CaptureController + CaptureService) is proven
  with 293 tests
- Minimize AWS footprint and cost (target: < $1/month)
- Must not break existing web capture path (floating button in UI)
- Docker Compose deployment model on private server
- Infrastructure as code via AWS CDK (TypeScript)
- Lambda in TypeScript

### Success Criteria
- [ ] Email webhooks (via SES) captured via AWS relay within 60 seconds
- [ ] Telegram messages captured via AWS relay within 60 seconds
- [ ] Messages survive private server downtime for up to 14 days
- [ ] All existing 293 tests continue to pass
- [ ] AWS cost < $1/month at personal usage levels
- [ ] No business logic in AWS components

## Context

### Current State
The app currently exposes two webhook endpoints directly:
- `POST /api/capture/email` - receives email provider webhooks
- `POST /api/capture/telegram` - receives Telegram Bot API updates

Both endpoints are behind rate limiting (10 req/min) and validate webhook
secrets via HTTP headers. `CaptureService` parses raw JSON payloads,
validates sender allowlists, creates fleeting notes, and enqueues URL
enrichment. All logic lives in the .NET backend.

Since the server is private, these endpoints are unreachable from the
internet, making email and Telegram capture non-functional.

### Related Decisions
- [ADR-003](adr/ADR-003-fleeting-notes-architecture.md): Fleeting Notes
  Architecture (Capture Service + Background Enrichment pattern)
- [ADR-001](adr/ADR-001-backend-architecture.md): Simple Layered
  Architecture

## Alternatives Considered

### Option A: Thin Lambda Relay + SQS (Recommended)

**Summary**: AWS Lambda receives raw webhook payloads and forwards them
unmodified to SQS. The .NET app polls SQS and processes messages through
the existing CaptureService unchanged.

**Architecture**:
```
  Email path:
  Amazon SES -> SNS Topic -> SQS (raw delivery, no Lambda)

  Telegram path:
  Telegram Bot API
           |
  +------------------+
  | API Gateway      |
  | - 10 req/s limit |
  +------------------+
           |
  +------------------+
  | Lambda (TS ~20L) |
  | - Add source attr|
  | - Forward raw    |
  +------------------+
           |
           v
  +------------------+
  | SQS Standard     |  <-- both paths converge here
  | - 14-day retain  |
  | - DLQ (max 3)    |
  +------------------+
           |
      (HTTPS poll)
           |
  Private Server (Docker)
  +------------------+
  | SqsPollingBgSvc  |
  | - Long poll 20s  |
  | - Delete after   |
  |   persist        |
  +------------------+
           |
  +------------------+
  | CaptureService   |
  | - ParseSesNotif  |
  | - ParseTelegram  |
  +------------------+
```

**Pros**:
- All business logic stays in .NET (parsing, validation, allowlists)
- Zero cross-boundary knowledge coupling
- All 293 existing tests remain valid
- Lambda is ~20 lines of TypeScript, nearly zero maintenance
- Messages survive server downtime (14 days in SQS)
- Deployment coordination is trivial (Lambda rarely changes)
- CDK makes infrastructure reproducible and version-controlled

**Cons**:
- No validation at the edge (spam reaches SQS before rejection)
- New AWSSDK.SQS dependency in .NET project
- Requires AWS account and IAM configuration
- Monitoring requires CloudWatch setup

**Coupling Analysis**:

| Component | Ca | Ce | I | Change |
|---|---|---|---|---|
| Lambda | 0 | 1 (SQS) | 1.00 | NEW, minimal coupling |
| SqsPollingBgSvc | 0 | 3 (SQS SDK, CaptureService, Config) | 1.00 | NEW, mirrors CaptureController |
| CaptureService | 2 | 4 | 0.67 | Ca +1 (second consumer) |
| CaptureConfig | 3 | 0 | 0.00 | Ca +1 |

New dependencies: 1 (AWSSDK.SQS). Cross-boundary knowledge coupling: 0.
Coupling cycles: 0.

**Failure Modes**:

| Mode | S | O | D | RPN |
|---|---|---|---|---|
| Monitoring blind spots | 5 | 6 | 8 | 240 |
| SQS credential compromise | 6 | 2 | 7 | 84 |
| Unauthorized submissions | 3 | 4 | 6 | 72 |
| Poison messages | 4 | 3 | 5 | 60 |
| Partial failure (delete before persist) | 5 | 2 | 6 | 60 |
| Network to AWS | 3 | 4 | 5 | 60 |
| **Total RPN** | | | | **942** |

Top mitigation: DLQ + delete-after-persist + CloudWatch alarms.

**Evolvability Assessment**:
- Add new capture channel (e.g., Slack): Easy. Add API Gateway route,
  Lambda passes through, add parser in CaptureService.
- Switch from SES to another email provider: Easy. Only CaptureService
  parser changes.
- Move to public server later: Easy. Re-enable direct webhooks,
  disable SQS polling.
- Replace SQS with another queue: Medium. SqsPollingBgSvc is the
  only AWS-coupled component.

**Effort Estimate**: Small (1-2 days)

---

### Option B: Validating Lambda + SQS

**Summary**: Lambda validates webhook secrets, checks sender allowlists,
and normalizes payloads before enqueuing. SQS contains pre-parsed messages.

**Pros**:
- Spam filtered at the edge (lower SQS costs, lower .NET processing)
- Unauthorized submissions blocked before queue (RPN 24 vs 72)

**Cons**:
- Validation logic duplicated between Lambda (TS) and .NET (C#)
- Configuration sync required (allowlists in two places)
- Deployment coordination required (schema versioning)
- Cross-boundary knowledge coupling: 3 (config, email parsing, telegram
  parsing)
- Implicit coupling cycle through shared configuration

**Failure Modes**:

| Mode | S | O | D | RPN |
|---|---|---|---|---|
| Validation logic split/drift | 5 | 5 | 7 | 175 |
| Deployment coordination | 5 | 4 | 5 | 100 |
| Lambda normalization bugs | 5 | 3 | 6 | 90 |
| **Total RPN** | | | | **1193** |

**Evolvability Assessment**:
- Add new capture channel: Hard. Lambda parser + .NET parser both change.
- Switch email provider: Hard. Two parsers to update.
- Config change (new allowed sender): Medium. Two deployments required.

**Effort Estimate**: Medium (3-5 days)

---

### Option C: API Gateway Direct Integration to SQS (No Lambda)

**Summary**: API Gateway VTL templates map HTTP requests directly to SQS
SendMessage calls. No Lambda compute involved.

**Pros**:
- Fewest AWS components (no Lambda)
- Lowest possible cost

**Cons**:
- VTL templates are untestable locally and hard to debug
- No request validation possible (no crypto in VTL for secret checking)
- Opaque error messages

**Failure Modes**:

| Mode | S | O | D | RPN |
|---|---|---|---|---|
| VTL template bugs | 6 | 5 | 7 | 210 |
| No request validation | 3 | 5 | 6 | 90 |
| **Total RPN** | | | | **1152** |

**Effort Estimate**: Small (1-2 days initial), higher ongoing maintenance.

---

## Comparison Matrix

| Criterion | Option A | Option B | Option C |
|---|---|---|---|
| Complexity | Low | High | Medium |
| Evolvability | High | Low | Medium |
| Time to Implement | 1-2 days | 3-5 days | 1-2 days |
| Coupling Impact | Low (0 cross-boundary) | High (3 cross-boundary) | Low (0 cross-boundary) |
| Total FMEA RPN | 942 (best) | 1193 (worst) | 1152 |
| HIGH priority risks | 1 | 3 | 2 |
| Edge Security | Weak | Strong | Weakest |
| Testability | High | Medium | Low |
| AWS Cost | ~$0.50/mo | ~$0.50/mo | ~$0.30/mo |

## Recommendation

**Recommended Option**: Option A (Thin Lambda Relay + SQS)

### Rationale

For a single-user personal app, Option A wins decisively:

1. **Zero knowledge coupling across boundaries.** The Lambda is a dumb
   pipe. All parsing, validation, and business logic stays in the .NET app
   where it is covered by 293 tests.

2. **Lowest total risk.** Total RPN of 942 vs 1193 (Option B) and 1152
   (Option C). Only 1 HIGH-priority risk (monitoring gaps, shared across
   all options).

3. **Deployment independence.** The Lambda changes only if SQS changes
   (effectively never). The .NET app evolves freely without touching AWS.

4. **Preserves existing patterns.** SqsPollingBackgroundService follows
   the established dual-trigger pattern from EmbeddingBackgroundService
   and EnrichmentBackgroundService.

### Tradeoffs Accepted
- **Spam reaches SQS**: Mitigated by API Gateway API key + throttling.
  At personal scale, the cost of processing a few spam messages is
  negligible.
- **New AWS dependency**: AWSSDK.SQS added to .NET project. Contained
  to a single background service class.

### Risks to Monitor
- **Monitoring gaps (RPN 240)**: Mitigate with CloudWatch alarms on SQS
  age, DLQ depth, and Lambda errors. Route to personal email via SNS.
- **SQS credential security**: Use least-privilege IAM with only
  ReceiveMessage + DeleteMessage on the specific queue ARN.

## Implementation Plan

### Phase 1: AWS Infrastructure (CDK TypeScript)
- [ ] Create CDK project in `infra/` directory
- [ ] SQS standard queue (14-day retention, 120s visibility timeout)
- [ ] SQS dead letter queue (maxReceiveCount: 3)
- [ ] Lambda function (TypeScript, ~20 LOC)
- [ ] API Gateway with two routes (/email, /telegram)
- [ ] API key + usage plan (10 req/s throttle)
- [ ] IAM roles (Lambda: sqs:SendMessage; Poller: sqs:ReceiveMessage
  + sqs:DeleteMessage + sqs:GetQueueAttributes)
- [ ] CloudWatch alarms (queue age > 1hr, DLQ depth > 0, Lambda errors)
- [ ] SNS topic for alarm notifications to personal email

### Phase 2: .NET SQS Polling Service
- [ ] Add AWSSDK.SQS NuGet package
- [ ] Create SqsPollingBackgroundService following existing BgSvc pattern
- [ ] Implement delete-after-persist message handling
- [ ] Add SQS polling health check
- [ ] Add `Capture:SqsQueueUrl` config key (empty = disabled)
- [ ] Gate service registration on config presence
- [ ] Write tests with mocked SQS client

### Phase 3: Integration & Monitoring
- [ ] Configure SES to POST to API Gateway URL
- [ ] Configure Telegram Bot webhook to API Gateway URL
- [ ] End-to-end test: email -> SES -> API Gateway -> SQS -> .NET -> DB
- [ ] End-to-end test: Telegram -> API Gateway -> SQS -> .NET -> DB
- [ ] Verify existing direct-capture path still works (web UI)
- [ ] Verify all 293+ tests pass

### Phase 4: SES Email Configuration
- [ ] Verify SES domain/email identity
- [ ] Configure SES receipt rule to POST to API Gateway
- [ ] Test with real email send
