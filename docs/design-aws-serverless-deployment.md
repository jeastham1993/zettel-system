# Design: AWS Serverless Deployment with Terraform

Generated: 2026-02-26
Status: Removed (2026-10) — the AWS deployment was removed. Historical record only.

---

## Problem Statement

### Goal

Deploy the Zettel System (ASP.NET Core backend + React frontend + PostgreSQL + pgvector)
to AWS using serverless-first technologies managed by Terraform, eliminating the need for
a self-hosted private server. The solution should scale to zero when unused (this is a
single-user personal application) and remain cost-effective at low traffic volumes.

### Constraints

- Must preserve the existing application code with minimal changes (no re-architecture
  of the service layer or EF Core data model)
- Must support the SQS capture pipeline (ADR-004) — email and Telegram relay already uses AWS
- Must keep the `IEmbeddingProvider` and `ISearchService` interfaces as extension points
- Must run EF Core migrations reliably (currently in startup: `db.Database.Migrate()`)
- Must support the AWS Bedrock embedding provider (already a NuGet dependency)
- Background services (`EmbeddingBackgroundService`, `SqsPollingBackgroundService`,
  `ContentScheduleBackgroundService`) currently run in-process; these require special
  treatment in a Lambda environment
- All infrastructure must be Terraform-managed (not CDK, which is used for the existing
  SQS capture relay in ADR-004)
- **All API endpoints must be authentication-protected** — the application will have a
  public URL; unauthenticated access must be rejected. No self-registration: a single
  admin-created user account is sufficient.

### Success Criteria

- [ ] Application deploys end-to-end with `terraform apply`
- [ ] Frontend accessible via HTTPS at a CloudFront distribution
- [ ] API accessible via HTTPS at an API Gateway URL
- [ ] Semantic search returns results in < 2 seconds (including cold start budget)
- [ ] Infrastructure costs < $50/month at single-user personal usage
- [ ] Scale to zero when no traffic (no idle EC2/container costs)
- [ ] Zero-downtime migrations during deployments
- [ ] All API endpoints reject unauthenticated requests with 401
- [ ] Frontend redirects to login when no valid session exists

---

## Context

### Current Deployment

The application runs on a private server via Docker Compose + Traefik:

```
Browser → Traefik (port 9010)
  ├── /api/* → ASP.NET Core backend (port 8080)
  └── /* → React frontend (nginx)
PostgreSQL + pgvector (local container)
```

The private server means no public endpoint, which is why ADR-004 introduced the
SQS-based webhook relay for email/Telegram capture. Moving to AWS removes this
limitation entirely — the application can have a public endpoint, making the SQS
relay optional (though still useful for resilience).

### Key Application Characteristics

- **~11 REST controllers**, ~25 services — medium-sized ASP.NET Core app
- **3 BackgroundService instances** running in-process:
  1. `EmbeddingBackgroundService` — polls for un-embedded notes, calls Bedrock, updates DB
  2. `SqsPollingBackgroundService` — polls SQS capture queue, delegates to CaptureService
  3. `ContentScheduleBackgroundService` — periodic content generation
- **EF Core migrations at startup** (`db.Database.Migrate()`) — runs every startup
- **HNSW index creation at startup** — DDL that depends on `Embedding:Dimensions` config
- **500–2000 notes**, vector dimensions 1536 (OpenAI) or 768 (Bedrock/Ollama)
- **Infrequent read pattern**: semantic searches happen a few times per hour, not per second

### Related Decisions

- [ADR-001](adr/ADR-001-backend-architecture.md): Simple Layered Architecture
- [ADR-004](adr/ADR-004-sqs-webhook-ingestion.md): SQS Webhook Ingestion (existing AWS infra)
- [ADR-006](adr/ADR-006-opentelemetry-observability.md): OpenTelemetry Observability

---

## Alternatives Considered

### Option A: ECS Fargate + Aurora Serverless v2 (Serverless Containers)

**Summary**: Run the existing Docker containers unchanged on ECS Fargate with Aurora
Serverless v2 replacing the local PostgreSQL + pgvector.

**Architecture**:
```
CloudFront → S3 (frontend static)
CloudFront → ALB → ECS Fargate (backend container, 1 task)
ECS Fargate task → Aurora Serverless v2 (PostgreSQL + pgvector)
ECS Fargate task → SQS (capture queue, existing)
ECS Fargate task → Bedrock (embeddings)
```

**Terraform modules needed**:
- `networking` — VPC, public/private subnets, NAT gateway
- `database` — `aws_rds_cluster` (Aurora Serverless v2), `aws_rds_cluster_instance`
- `compute` — ECS cluster, task definition, service, IAM roles
- `loadbalancer` — ALB, target group, listener
- `frontend` — S3 bucket, CloudFront distribution, Route53 (optional)
- `secrets` — AWS Secrets Manager for DB credentials, API keys
- `monitoring` — CloudWatch alarms, log groups

**Pros**:
- Zero application code changes — exact same container images from GHCR
- Background services run as they do today (in-process, persistent)
- EF Core migrations at startup work unchanged
- Battle-tested pattern; extensive Terraform provider support
- Aurora Serverless v2 supports pgvector; HNSW indexes work unchanged
- Fargate can be set to 0.5 vCPU / 512 MB — very cheap

**Cons**:
- Does not scale to zero: Fargate task must be running to serve requests
  (minimum ~$15/month for 0.5 vCPU / 512 MB task, plus ALB ~$16/month)
- NAT gateway required for private subnet ($32/month) — or use Fargate in
  public subnet with security groups (viable but less ideal)
- ALB is overkill for a single-user app; API Gateway HTTP API would be cheaper
- "Serverless containers" is a marketing term — there's still a container
  lifecycle to manage; cold starts are 10–60 seconds when scaling from 0

**Coupling Analysis**:
| Component | Afferent Ca | Efferent Ce | Instability I |
|-----------|-------------|-------------|---------------|
| ECS Task | 0 | 4 (Aurora, SQS, Bedrock, ECR) | 1.0 |
| Aurora | 1 (ECS) | 0 | 0.0 |
| ALB | 1 (CloudFront) | 1 (ECS) | 0.5 |

New AWS dependencies introduced: ECR (image registry), ECS, ALB, Aurora, Secrets Manager
Coupling impact: **Low** — existing application interfaces unchanged

**Failure Modes**:
| Mode | Severity (1-10) | Occurrence (1-10) | Detection (1-10) | RPN |
|------|-----------------|-------------------|------------------|-----|
| Fargate task OOM crash | 6 | 3 | 7 | 126 |
| Aurora cold start (0 ACUs → warm) | 4 | 5 | 8 | 160 |
| NAT gateway outage (Bedrock/SQS unreachable) | 7 | 2 | 6 | 84 |
| Container image pull failure | 7 | 2 | 8 | 112 |

**Evolvability Assessment**:
- Add new background service: Easy (existing in-process pattern)
- Switch embedding provider: Easy (IEmbeddingProvider interface unchanged)
- Add authentication: Easy (ASP.NET Core middleware)
- Scale to multiple users: Medium (ECS auto-scaling, Aurora scaling)
- Move to microservices: Hard (entire container approach changes)

**Cost Estimate** (eu-west-1, single-user):
- Fargate: 0.5 vCPU × 730h × $0.04048/vCPU-h + 0.5 GB × $0.004445/GB-h = **~$16/month**
- ALB: $16/month
- Aurora Serverless v2 (0.5–2 ACU, mostly paused): ~$8/month
- NAT gateway: $32/month (or ~$0 if Fargate in public subnet)
- S3 + CloudFront: ~$1/month
- **Total: ~$40–73/month** (depending on NAT gateway choice)

**Effort Estimate**: M (3–5 days)

---

### Option B: Lambda Web Adapter + Aurora Serverless v2 (True Serverless)

**Summary**: Host the ASP.NET Core application on AWS Lambda using the Lambda Web Adapter
(no code changes), with background services extracted to separate Lambda functions
triggered by EventBridge Scheduler and SQS event source mappings.

**Architecture**:
```
Browser
  │ (unauthenticated) → Cognito Hosted UI → login → redirect with auth code
  │ (authenticated)   ↓
CloudFront → API Gateway HTTP API
                  │
           JWT Authorizer ← Cognito User Pool (rejects 401 before Lambda)
                  │
              Lambda (ASP.NET Core via Lambda Web Adapter)
                  │
          Aurora Serverless v2 (PostgreSQL + pgvector, min 0.5 ACU)
          SQS → Lambda (EmbeddingWorker)
          EventBridge Scheduler → Lambda (ContentSchedule)
Secrets Manager → all Lambdas
```

**AWS Lambda Web Adapter**: A layer that translates Lambda invocation events
(API Gateway HTTP API payloads) into HTTP requests that ASP.NET Core can handle.
The application code is **unchanged** — only the container entrypoint changes.
See: https://github.com/awslabs/aws-lambda-web-adapter

**Background Service extraction**:

The three in-process BackgroundServices must become separate Lambda functions:

1. **EmbeddingWorker Lambda**: EventBridge Scheduler rule every 60 seconds →
   Lambda invokes `EmbeddingBackgroundService.ExecuteOnceAsync()` (refactored from
   `ExecuteAsync` loop). Alternatively, use SQS event source: note creation pushes
   to an embed queue, Lambda processes batches. The SQS event source approach is
   more event-driven and avoids polling overhead.

2. **SqsPollingWorker**: Replaced entirely by **SQS event source mapping** on the
   existing capture queue. Lambda receives SQS batch events and calls CaptureService
   directly. This is actually a *better* pattern than polling — AWS manages the
   long-poll loop.

3. **ContentScheduleWorker Lambda**: EventBridge Scheduler rule (cron) → Lambda
   invokes ContentGenerationService. Pure scheduled task.

**EF Core Migration problem**:
`db.Database.Migrate()` at startup is problematic in Lambda:
- Runs on every cold start (wasteful: typically 50–200ms)
- Can have race conditions if multiple instances cold-start simultaneously
- Solution: Add a `MigrationLambda` invoked once by Terraform `aws_lambda_invocation`
  during `terraform apply`, before the API lambda is updated. Remove `Migrate()` from
  startup. The Lambda Web Adapter's initialization phase (before first request) can
  also run migrations safely since Lambda only processes one request at a time on
  initial container start.

**Terraform modules needed**:
- `networking` — VPC, private subnets for Lambda + RDS, VPC endpoints (saves NAT cost)
- `database` — Aurora Serverless v2 with pgvector, min 0.5 ACU (no RDS Proxy — single user concurrency is too low to exhaust connections)
- `auth` — Cognito User Pool, App Client (PKCE, no client secret), Cognito domain for Hosted UI, API Gateway JWT authorizer
- `api` — Lambda (container image), Lambda IAM role, API Gateway HTTP API with JWT authorizer applied to `$default` route
- `workers` — Embedding Lambda, ContentSchedule Lambda, EventBridge rules
- `frontend` — S3 + CloudFront OAC
- `secrets` — Secrets Manager (DB password, API keys), SSM Parameter Store (config)
- `monitoring` — CloudWatch Log Groups, alarms (DLQ depth, Lambda errors, p95 latency)

**Pros**:
- True scale to zero — no idle compute costs
- Pay-per-invocation: personal use likely < $1/month Lambda costs
- SQS event source mapping is better than long-polling for capture workload
- No ALB needed (API Gateway HTTP API at $1/million requests)
- VPC endpoints eliminate NAT gateway cost (~$32/month savings)
- **Cognito JWT authorizer means the ASP.NET Core application requires zero auth
  changes** — API Gateway rejects unauthenticated requests before Lambda is invoked
- Cognito Hosted UI provides a login page with no frontend form to build
- Cognito free tier covers 50,000 MAUs — effectively free for personal use

**Cons**:
- .NET cold starts: 1–3 seconds on first request (mitigated by provisioned
  concurrency, but that costs money)
- Migration extraction requires touching `Program.cs` (small but non-trivial)
- Background services require refactoring to extract the "do once" logic from the
  "loop forever" hosting pattern
- Aurora Serverless v2 must be in a VPC; Lambda must also be in the same VPC →
  adds networking complexity
- Lambda has a 15-minute timeout; content generation LLM calls may approach this
  for large note sets
- Container image cold starts are slower than zip deployment; consider using
  Lambda Snap Start (Java-style) — not yet available for .NET on ARM
- **Aurora `min_capacity=0` (true scale-to-zero) was considered but rejected**:
  resume latency is ~15 seconds (< 24h idle) or 30+ seconds (> 24h idle), which
  is unacceptable for an interactive web app. `min_capacity=0.5` keeps Aurora warm
  at ~$4/month — the correct tradeoff. See Aurora scale-to-zero analysis.

**Coupling Analysis**:
| Component | Afferent Ca | Efferent Ce | Instability I |
|-----------|-------------|-------------|---------------|
| API Lambda | 1 (API GW) | 3 (Aurora, Bedrock, Secrets) | 0.75 |
| Embedding Lambda | 1 (SQS/EventBridge) | 2 (Aurora, Bedrock) | 0.67 |
| Aurora | 2 (API Lambda, Workers) | 0 | 0.0 |
| API Gateway | 1 (CloudFront) | 2 (Lambda, Cognito) | 0.67 |
| Cognito | 2 (API GW, React) | 0 | 0.0 |

New AWS dependencies: Lambda, API Gateway HTTP API, EventBridge Scheduler, Cognito User Pool
Coupling impact: **Medium** — background service refactoring required; Cognito is a
  stable managed service with no code coupling into the Lambda/ASP.NET Core layer

**Failure Modes**:
| Mode | Severity | Occurrence | Detection | RPN |
|------|----------|------------|-----------|-----|
| Lambda cold start timeout (user-facing) | 5 | 4 | 7 | 140 |
| EmbeddingLambda timeout (large batch) | 5 | 3 | 8 | 120 |
| Aurora scale-up latency (cold → warm) | 4 | 4 | 7 | 112 |
| Lambda concurrency limit reached | 6 | 2 | 8 | 96 |
| Cognito Hosted UI unavailable (AWS regional outage) | 6 | 1 | 9 | 54 |
| Cognito refresh token expired (30-day idle) | 2 | 3 | 10 | 60 |

**Evolvability Assessment**:
- Add new background service: Easy (new Lambda + EventBridge rule)
- Switch embedding provider: Easy (IEmbeddingProvider unchanged)
- Add authentication: Easy (API Gateway authorizer or middleware)
- Scale to multiple users: Medium (Lambda scales automatically; Aurora needs ACU review)
- Add API versioning: Easy (API Gateway stage variables)

**Cost Estimate** (eu-west-1, single-user, ~100 requests/day):
- Lambda: ~1M free requests/month → effectively **$0/month** at personal use
- API Gateway HTTP API: $1/million requests → **~$0/month**
- Aurora Serverless v2 (min 0.5 ACU, no RDS Proxy): ~$4/month
- VPC endpoints (S3, Bedrock, SQS, Secrets Manager): ~$8/month
- S3 + CloudFront: ~$1/month
- Cognito User Pool: **$0/month** (free tier: 50,000 MAUs; personal use ~1 MAU)
- **Total: ~$13/month**

Note: RDS Proxy (~$15/month) was evaluated and rejected for this single-user deployment.
At ≤ 3 concurrent Lambda instances, each holding `Max Pool Size=10`, Aurora at 0.5 ACU
can handle the connection load without a proxy. This is the largest single cost saving
vs the original estimate. Revisit if concurrency grows.

**Effort Estimate**: L (1–2 weeks, including background service refactoring + auth)

---

### Option C: Lambda Web Adapter + Aurora Serverless v2 + S3 Vectors (Recommended)

**Summary**: Same as Option B, but replaces pgvector in Aurora with Amazon S3 Vectors
for semantic search. Aurora Serverless v2 retains relational note storage and
full-text search; S3 Vectors handles the embedding storage and ANN (approximate
nearest neighbor) queries.

**Architecture**:
```
CloudFront → API Gateway HTTP API → Lambda (ASP.NET Core)
                                 ↓
                    ┌────────────────────────────┐
                    │ Aurora Serverless v2        │
                    │  - Notes (all columns)      │
                    │  - Full-text search (GIN)   │
                    │  - embed_status column      │
                    │  - NO vector column         │
                    └────────────────────────────┘
                    ┌────────────────────────────┐
                    │ S3 Vectors Bucket           │
                    │  - Vector index             │
                    │  - Embeddings + note IDs    │
                    │  - Cosine similarity ANN    │
                    └────────────────────────────┘
SQS → Lambda (EmbeddingWorker) → dual-write: Aurora (embed_status) + S3 Vectors
EventBridge → Lambda (ContentSchedule)
```

**The S3 Vectors proposition**:

Amazon S3 Vectors (announced 2025) is purpose-built for storing and querying vectors
at low cost with no infrastructure to provision. For Zettel's 500–2000 notes:
- Up to 2 billion vectors/index — no scale concern
- 100ms minimum warm query latency — acceptable for semantic search in a personal app
- ~90% lower storage cost vs pgvector on RDS
- "Designed for infrequent query workloads" — exact match for personal Zettelkasten use

**Implementation approach**:

Create a new `IS3VectorSearchService` implementing the existing `ISearchService` interface.
The current `SearchService` (pgvector) becomes the fallback. Configuration-gated:
if `VectorStore:Provider = "s3vectors"`, use S3 Vectors; otherwise use pgvector in Aurora.
This follows the same pattern as the `IEmbeddingProvider` switch (OpenAI / Bedrock / Ollama).

The `Embedding` column can be removed from Aurora entirely (saving storage + no HNSW index),
or retained as a cache to avoid S3 Vectors reads during re-embedding. The simpler path
is to remove it from Aurora and make S3 Vectors the source of truth for vectors.

**Dual-write in EmbeddingWorker Lambda**:
```
EmbeddingWorker:
  1. Fetch note content from Aurora
  2. Call Bedrock embedding API
  3. Write vector to S3 Vectors (key = note ID)
  4. Update embed_status = Embedded in Aurora
  5. Delete from embed queue
```

**S3 Vectors Terraform resource**:
As of 2026, the AWS Terraform provider supports `aws_s3vectors_vector_bucket` and
`aws_s3vectors_index`. The index configuration specifies:
- `data_type: float32`
- `dimensions: 1536` (or 768 for Bedrock Titan Embeddings V2)
- `distance_metric: cosine`

**SDK Note**: The AWS SDK for .NET will require `AWSSDK.S3Vectors` (check NuGet for
the latest version). If the SDK is not yet stable, Option B with pgvector in Aurora
is the safe fallback while the S3 Vectors SDK matures.

**Terraform modules needed** (extends Option B):
- All Option B modules, plus:
- `vector-store` — `aws_s3vectors_vector_bucket`, `aws_s3vectors_index`, IAM policies
- Modified `workers` — EmbeddingWorker writes to S3 Vectors instead of pgvector column
- Modified `database` — Aurora schema without `Embedding real[]` column (new migration)

**Schema change**:
This requires an EF Core migration to drop the `Embedding` column and update
`embed_status` semantics. The vector data is now in S3 Vectors. This is a breaking
schema change — requires the migration Lambda approach from Option B.

**Pros**:
- S3 Vectors is genuinely serverless (no infrastructure, no pgvector extension management)
- No HNSW index creation DDL hack at startup (`Program.cs` simplifies)
- ~90% lower vector storage cost (though at 2000 notes, the absolute saving is tiny)
- Aurora Serverless v2 can use standard PostgreSQL (no pgvector extension) → wider
  compatibility (e.g., Neon, Supabase, RDS PostgreSQL free tier)
- Separates concerns: relational data in Aurora, vector data in S3 Vectors
- Follows the existing IEmbeddingProvider interface pattern (pluggable)

**Cons**:
- S3 Vectors is a new service (2025) — SDK maturity risk, limited community examples
- Dual-write adds consistency complexity: if S3 Vectors write succeeds but Aurora
  embed_status update fails, the note appears un-embedded but the vector exists
- Hybrid search (full-text + semantic) requires scatter-gather: query both stores,
  then merge/re-rank results in the Lambda
- Cannot use pgvector's SQL-level hybrid search (`ts_rank + <=>` combined query)
- Similarity metrics supported by S3 Vectors need verification (cosine confirmed;
  dot product and Euclidean TBC from the API docs)
- Requires new `AWSSDK.S3Vectors` dependency and `IS3VectorSearchService` implementation
- More moving parts = more Terraform resources = more operational surface area

**Coupling Analysis**:
| Component | Afferent Ca | Efferent Ce | Instability I |
|-----------|-------------|-------------|---------------|
| API Lambda | 1 (API GW) | 4 (Aurora, S3 Vectors, Bedrock, Secrets) | 0.80 |
| Embedding Lambda | 1 (SQS) | 3 (Aurora, S3 Vectors, Bedrock) | 0.75 |
| Aurora | 2 (API Lambda, EmbedLambda) | 0 | 0.0 |
| S3 Vectors | 2 (API Lambda, EmbedLambda) | 0 | 0.0 |

New AWS dependencies vs Option B: S3 Vectors bucket + index, AWSSDK.S3Vectors NuGet
Coupling impact: **Medium-High** — new storage tier, dual-write consistency concern

**Failure Modes**:
| Mode | Severity | Occurrence | Detection | RPN |
|------|----------|------------|-----------|-----|
| S3 Vectors partial write (vector written, embed_status not updated) | 6 | 3 | 7 | 126 |
| S3 Vectors API unavailability | 7 | 2 | 7 | 98 |
| Scatter-gather merge divergence | 5 | 3 | 6 | 90 |
| SDK breaking change (new service, v0.x) | 6 | 4 | 9 | 216 |
| Lambda cold start with dual-store query | 5 | 4 | 7 | 140 |

**Evolvability Assessment**:
- Switch vector store: Easy (ISearchService interface; config-gated)
- Add hybrid search: Medium (scatter-gather merge in SearchService; more code)
- Return to pgvector: Easy (keep IS3VectorSearchService behind feature flag)
- Scale vector size: Easy (S3 Vectors auto-scales; no HNSW index recreation)

**Cost Estimate** (eu-west-1, single-user):
- All Option B costs (~$5–15/month)
- S3 Vectors: pricing based on storage + query operations; at 2000 vectors ~1536 dims,
  cost is negligible (< $1/month at personal query volumes)
- **Total: ~$6–16/month** — marginally cheaper than Option B at this scale

**Effort Estimate**: XL (2–3 weeks, includes S3 Vectors SDK integration + migration)

---

## Comparison Matrix

| Criterion | Option A (Fargate) | Option B (Lambda + pgvector) | Option C (Lambda + S3 Vectors) |
|-----------|-------------------|------------------------------|--------------------------------|
| Scale to zero | 🔴 No | 🟢 Yes | 🟢 Yes |
| Code changes required | 🟢 None | 🟡 BackgroundService refactor | 🔴 New ISearchService + migration |
| Monthly cost (personal use) | 🔴 $40–73 | 🟢 ~$13 | 🟢 ~$14 |
| Cold start penalty | 🟢 Low (warm container) | 🟡 1–3s (mitigable) | 🟡 1–3s (mitigable) |
| Terraform complexity | 🟡 Medium | 🟡 Medium-High | 🔴 High |
| Vector storage solution | 🟢 Proven (pgvector) | 🟢 Proven (pgvector) | 🟡 New (S3 Vectors, 2025) |
| Operational simplicity | 🟢 High | 🟡 Medium | 🔴 Lower (two stores) |
| SDK maturity risk | 🟢 None | 🟢 None | 🔴 S3 Vectors SDK risk |
| Hybrid search quality | 🟢 Native SQL | 🟢 Native SQL | 🟡 Scatter-gather merge |
| Authentication | 🟡 Middleware (code change) | 🟢 Cognito JWT (zero backend change) | 🟢 Cognito JWT (zero backend change) |
| Effort to implement | 🟢 M (3–5d) | 🟡 L (1–2w) | 🔴 XL (2–3w) |
| Evolvability | 🟡 Medium | 🟢 High | 🟢 High |
| Resilience score (FMEA total RPN) | 482 | 542 | 616 |

---

## Recommendation

**Recommended Option**: Option B — Lambda Web Adapter + Aurora Serverless v2

**Rationale**:

Option B delivers true serverless scale-to-zero at minimal cost (~$5–15/month), with
moderate application changes (BackgroundService refactoring + migration extraction) that
pay long-term dividends regardless of deployment target. The background service extraction
makes the application more testable and cloud-portable by separating "business logic" from
"hosting concern".

Option C (S3 Vectors) is architecturally elegant and aligns with the IEmbeddingProvider
pattern already in the codebase, but carries meaningful risk from SDK immaturity and
dual-write consistency complexity. The cost savings at 2000 notes are negligible in
absolute terms (~$1/month). The right approach is to build Option B cleanly, then add
an `IS3VectorSearchService` implementation as a future enhancement once the S3 Vectors
.NET SDK stabilizes (tracked separately as a low-priority enhancement).

Option A is rejected because it doesn't scale to zero and costs 3–5× more than Option B
at single-user traffic levels.

**Tradeoffs Accepted**:
- **Cold start latency (1–3s)**: Acceptable for a personal app. The first request after
  idle will be slow; subsequent requests within the same Lambda instance are normal speed.
  If this becomes painful, add provisioned concurrency for the API Lambda (~$5/month).
- **Background service refactoring**: ~2–3 days of work. The refactoring improves
  testability anyway (BackgroundService unit testing is awkward with `ExecuteAsync` loops).
- **No RDS Proxy**: At single-user concurrency (≤ 3 Lambda instances), direct Npgsql
  connections with `Max Pool Size=10;Connection Idle Lifetime=300` are safe. Aurora at
  0.5 ACU supports ~90 connections; 3 Lambda instances × 10 = 30 connections maximum.
  Saves $15/month. Revisit if usage grows.
- **Aurora min_capacity=0.5, not 0**: Scaling Aurora to 0 ACU was evaluated and
  rejected. Resume latency from 0 ACU is ~15 seconds (or 30+ seconds after 24h idle),
  which produces an unacceptable blank-screen pause for an interactive web app. The
  $4/month for 0.5 ACU keeping Aurora warm is the correct tradeoff.
- **Cognito for auth, not custom middleware**: The API Gateway JWT authorizer pattern
  means zero auth code in the ASP.NET Core application. Cognito handles token issuance,
  expiry, and the Hosted UI login page. The React frontend adds ~150 lines for the PKCE
  flow and token injection into `client.ts`.

**Risks to Monitor**:
- **Cognito refresh token expiry (30-day default)**: After 30 days without opening the
  app, the user will need to re-authenticate via the Hosted UI. This is expected and
  correct behaviour, but worth communicating as "the app will ask you to log in again
  after a month of inactivity." Can be extended to 3650 days in Cognito config if
  re-auth is annoying.
- **Lambda function URL as Cognito alternative**: If API Gateway is removed in favour
  of Lambda function URLs (free, simpler), the JWT authorizer pattern no longer applies.
  In that case, auth would move to ASP.NET Core middleware (AddAuthentication + JWT
  Bearer). This is still straightforward but requires a small code change.
- **S3 Vectors SDK maturity**: Check `AWSSDK.S3Vectors` on NuGet in Q3 2026. If stable
  and has good cosine similarity + metadata filtering support, implement Option C.

---

## Implementation Plan

### Phase 0: Prerequisites (before any Terraform)

- [ ] Verify `AWSSDK.S3Vectors` NuGet package exists (for Option C tracking)
- [ ] Create AWS account / profile with required IAM permissions
- [ ] Set up Terraform state backend (S3 bucket + DynamoDB lock table)
- [ ] Create ECR repositories for backend + frontend images

### Phase 1: Foundation Infrastructure (Terraform)

- [ ] `terraform/modules/networking/` — VPC, 2 private subnets (Lambda + RDS),
      VPC endpoints (Bedrock, SQS, Secrets Manager, S3) — no NAT gateway
- [ ] `terraform/modules/secrets/` — Secrets Manager secrets for DB password,
      Bedrock API key, SQS queue URL, CORS origins
- [ ] `terraform/modules/database/` — Aurora Serverless v2 cluster (min 0.5 ACU,
      max 4 ACU), pgvector extension, security groups. No RDS Proxy.
      Connection string: `Max Pool Size=10;Connection Idle Lifetime=300`
- [ ] `terraform/modules/auth/` — Cognito User Pool (`allow_admin_create_user_only=true`),
      App Client (PKCE, no secret, `code` flow), Cognito domain for Hosted UI
- [ ] `terraform/modules/monitoring/` — CloudWatch log groups, basic alarms

### Phase 2: Backend Lambda + Authentication

- [ ] Create Dockerfile variant for Lambda Web Adapter
      (`FROM public.ecr.aws/awsguru/aws-lambda-web-adapter:0.9.0 AS adapter` layer)
- [ ] `terraform/modules/api/` — Lambda function (container image), Lambda IAM role,
      API Gateway HTTP API with JWT authorizer pointing at the Cognito User Pool.
      Apply authorizer to `$default` route; leave `GET /health` unauthenticated.
- [ ] Extract `db.Database.Migrate()` to a `MigrationLambda` invoked by Terraform
      `aws_lambda_invocation` during apply
- [ ] Remove `Migrate()` from `Program.cs` startup (or guard with env var)
- [ ] Remove HNSW index creation from startup; move to migration Lambda
- [ ] Create the single admin user via AWS CLI after first `terraform apply`:
      ```bash
      aws cognito-idp admin-create-user \
        --user-pool-id <pool-id> \
        --username you@example.com \
        --temporary-password "TempPass123!" \
        --message-action SUPPRESS

      aws cognito-idp admin-set-user-password \
        --user-pool-id <pool-id> \
        --username you@example.com \
        --password "YourRealPassword123!" \
        --permanent
      ```

### Phase 3: Background Worker Lambdas

- [ ] Refactor `EmbeddingBackgroundService` — extract `ProcessPendingEmbeddingsAsync()`
      callable from both the BackgroundService (local dev) and a Lambda handler
- [ ] `EmbeddingWorkerLambda` — triggered by EventBridge Scheduler (every 60s) or
      SQS event source on a new `embedding-queue`
- [ ] Refactor `SqsPollingBackgroundService` — replace with SQS event source mapping
      on the existing capture queue (Lambda receives SQS batch events)
- [ ] Refactor `ContentScheduleBackgroundService` — extract to `ContentScheduleLambda`
      triggered by EventBridge Scheduler (daily cron)
- [ ] `terraform/modules/workers/` — all three worker Lambdas + EventBridge rules +
      SQS event source mappings

### Phase 4: Frontend + Auth Integration

- [ ] `terraform/modules/frontend/` — S3 bucket (private), CloudFront OAC, invalidation
- [ ] Add `terraform/deploy-frontend.sh` script — `npm run build`, `aws s3 sync`, CloudFront invalidation
- [ ] Update Vite environment variables (via Terraform outputs):
      - `VITE_API_URL` — API Gateway URL
      - `VITE_COGNITO_CLIENT_ID` — Cognito App Client ID
      - `VITE_COGNITO_DOMAIN` — `https://{cognito-domain}.auth.{region}.amazoncognito.com`
- [ ] Add `src/auth.ts` — PKCE helpers, `getToken()`, `isAuthenticated()`,
      `redirectToLogin()`, `handleCallback()`, `logout()`
- [ ] Modify `src/api/client.ts` — inject `Authorization: Bearer ${getToken()}` header
      into all four request helpers (`get`, `post`, `put`, `del`); on 401 response,
      call `redirectToLogin()`
- [ ] Add `/callback` route handler in `App.tsx` — exchanges auth code for tokens,
      stores in `sessionStorage`, redirects to `/`
- [ ] Add unauthenticated guard in `App.tsx` — redirects to Cognito Hosted UI if no
      valid token exists
- [ ] Update CloudFront callback URL in Cognito App Client to match the deployed domain

### Phase 5: Migration Cutover

- [ ] Test end-to-end in AWS staging environment
- [ ] Export data from self-hosted PostgreSQL: `pg_dump`
- [ ] Import to Aurora Serverless v2: `psql` via bastion Lambda or RDS Data API
- [ ] Update DNS (if applicable) to point to CloudFront
- [ ] Decommission self-hosted Docker Compose setup

---

## Terraform Structure

```
terraform/
├── main.tf                    # Root module: provider config, backend
├── variables.tf               # Environment-specific variables
├── outputs.tf                 # API URL, CloudFront domain, Cognito outputs
├── modules/
│   ├── networking/
│   │   ├── main.tf            # VPC, subnets, VPC endpoints (S3, Bedrock, SQS, Secrets)
│   │   └── outputs.tf
│   ├── database/
│   │   ├── main.tf            # aws_rds_cluster (Aurora Serverless v2, min 0.5 ACU)
│   │   ├── variables.tf       # min_capacity=0.5, max_capacity=4, db_name
│   │   └── outputs.tf
│   ├── auth/
│   │   ├── main.tf            # aws_cognito_user_pool, aws_cognito_user_pool_client,
│   │   │                      # aws_cognito_user_pool_domain,
│   │   │                      # aws_apigatewayv2_authorizer (JWT),
│   │   │                      # aws_apigatewayv2_route (with authorization_type=JWT)
│   │   ├── variables.tf       # cloudfront_domain, api_gateway_id, aws_region
│   │   └── outputs.tf         # user_pool_id, client_id, cognito_domain
│   ├── secrets/
│   │   ├── main.tf            # aws_secretsmanager_secret for all credentials
│   │   └── outputs.tf
│   ├── api/
│   │   ├── main.tf            # Lambda function, IAM role, API Gateway HTTP API
│   │   ├── variables.tf       # image_uri, memory_size, environment variables
│   │   └── outputs.tf         # api_endpoint, api_gateway_id, lambda_integration_id
│   ├── workers/
│   │   ├── main.tf            # EmbeddingLambda, ContentScheduleLambda, EventBridge rules
│   │   └── outputs.tf
│   ├── frontend/
│   │   ├── main.tf            # S3 bucket, CloudFront OAC distribution
│   │   └── outputs.tf         # cloudfront_domain
│   └── monitoring/
│       ├── main.tf            # CloudWatch alarms, SNS for alerts
│       └── variables.tf
└── environments/
    ├── prod/
    │   ├── main.tf            # Calls all modules with prod variables
    │   └── terraform.tfvars   # GITIGNORED: actual secret values
    └── dev/
        └── main.tf            # Calls modules with dev/cheap settings
```

> **Module dependency order**: `networking` → `database` → `secrets` → `api` (outputs `api_gateway_id`) → `auth` (consumes `api_gateway_id` + `cloudfront_domain`) → `frontend` → `workers` → `monitoring`
>
> The `auth` module must be applied after `api` (needs the API Gateway ID and integration ID) and after the first `frontend` apply (needs the CloudFront domain for the Cognito callback URL). In practice, a single `terraform apply` handles this via the dependency graph.

---

## Open Questions

### Resolved

- ~~**Aurora Serverless v2 min ACU = 0**~~: **Decided against.** Resume latency from
  0 ACU is ~15 seconds (< 24h idle) or 30+ seconds (> 24h idle), unacceptable for an
  interactive web app. `min_capacity=0.5` is the correct tradeoff at ~$4/month.
- ~~**RDS Proxy vs direct connection**~~: **Decided against RDS Proxy.** At single-user
  concurrency, direct Npgsql connections with `Max Pool Size=10` are safe. Saves
  $15/month. See Recommendation section.
- ~~**Authentication approach**~~: **Decided: Cognito User Pool + API Gateway JWT
  Authorizer.** Zero backend code changes. Cognito Hosted UI. Single admin-created user.

### Outstanding

- [ ] **S3 Vectors .NET SDK status**: Does `AWSSDK.S3Vectors` have stable NuGet release?
      What similarity metrics are supported? Can metadata filters (note tags) be applied
      at query time? (Relevant for upgrading to Option C — re-evaluate Q3 2026)
- [ ] **Lambda Snap Start for .NET**: As of 2026, is Snap Start supported for .NET 10
      on ARM64? This would eliminate the cold start penalty entirely.
- [ ] **Existing SQS capture relay (ADR-004)**: The CDK-managed Lambda relay becomes
      redundant once the backend has a public API Gateway URL. Keep both paths during
      cutover, then optionally deprecate the CDK relay.
- [ ] **Cognito refresh token validity**: Default is 30 days. After 30 idle days, the
      user must re-authenticate. Consider extending to 365 days for a personal app where
      re-login is more annoying than it is a security concern.
