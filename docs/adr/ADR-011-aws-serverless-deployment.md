# ADR-011: AWS Serverless Deployment Strategy

Date: 2026-02-26
Status: Proposed

## Context

Zettel-Web currently runs on a private server via Docker Compose + Traefik with a
self-hosted PostgreSQL + pgvector instance. This requires maintaining a physical server,
prevents direct webhook delivery (worked around in ADR-004 via SQS relay), and has a
fixed cost regardless of usage. The application is a single-user personal Zettelkasten
with infrequent, bursty access patterns — an ideal fit for serverless pricing.

Three deployment strategies were evaluated:
- **Option A**: ECS Fargate + Aurora Serverless v2 (serverless containers)
- **Option B**: Lambda Web Adapter + Aurora Serverless v2 (true serverless, pgvector)
- **Option C**: Lambda Web Adapter + Aurora Serverless v2 + S3 Vectors (true serverless,
  S3-native vector store)

The application has four constraints that affect serverless migration:
1. Three `BackgroundService` instances run in-process (assume persistent hosting)
2. EF Core migrations execute at startup via `db.Database.Migrate()`
3. HNSW vector index creation runs at startup (depends on runtime config)
4. Moving to a public URL requires authentication — the current deployment is private
   with no auth; a public AWS endpoint without auth would expose all notes and AI
   operations to the internet

## Decision

Deploy using **Option B: AWS Lambda with Lambda Web Adapter + Aurora Serverless v2**,
managed by Terraform.

The ASP.NET Core application is hosted on Lambda using the AWS Lambda Web Adapter
(a sidecar that translates API Gateway HTTP API events to standard HTTP). No application
code changes are required for the API layer.

The three BackgroundService instances are extracted to separate Lambda functions:
- `EmbeddingWorkerLambda` — EventBridge Scheduler (60s) or SQS event source
- `ContentScheduleLambda` — EventBridge Scheduler (daily cron)
- SQS polling — replaced by native SQS event source mapping (better than long-polling)

EF Core migrations are extracted from startup to a dedicated `MigrationLambda` invoked
once by Terraform (`aws_lambda_invocation`) before the API Lambda is updated. The
startup migration call is removed from `Program.cs`.

The frontend (React/Vite) is deployed as static assets to S3 + CloudFront.

**Authentication** is provided by Amazon Cognito User Pool + API Gateway JWT Authorizer:
- A single Cognito User Pool with `allow_admin_create_user_only=true` (no self-registration)
- One admin-created user account provisioned via AWS CLI after first deploy
- API Gateway HTTP API validates Cognito JWTs before any Lambda invocation — the ASP.NET
  Core application has zero authentication code
- Cognito Hosted UI provides the login page; no login form is built in React
- The React frontend adds `Authorization: Bearer <token>` to all API calls via a central
  `auth.ts` module + a one-line change in the existing `client.ts`
- The `/health` endpoint is excluded from the JWT authorizer (public)

**Aurora Serverless v2** runs at `min_capacity=0.5 ACU`. Scale-to-zero (`min_capacity=0`)
was evaluated and rejected: resume latency from 0 ACU is 15–30+ seconds (unacceptable
for an interactive web app). At 0.5 ACU, Aurora stays warm at ~$4/month.

**RDS Proxy** is not used. At single-user concurrency (≤ 3 concurrent Lambda instances),
direct Npgsql connections with `Max Pool Size=10;Connection Idle Lifetime=300` are safe.
Aurora at 0.5 ACU supports ~90 connections; this configuration uses at most 30.

All infrastructure is managed by Terraform in a `terraform/` directory at the repo root,
structured as modules: `networking`, `database`, `auth`, `secrets`, `api`, `workers`,
`frontend`, `monitoring`.

## Consequences

### Positive

- Scale to zero: Lambda charges only on invocation; personal-use cost ~$13/month
  (vs $40–73/month for Fargate with ALB + NAT gateway)
- Eliminates the private server entirely; the application gets a public API Gateway URL,
  making the SQS capture relay (ADR-004) optional rather than mandatory
- SQS event source mapping for the capture queue is a better pattern than the current
  long-polling BackgroundService — AWS manages the polling loop, Lambda scales
  with queue depth
- BackgroundService extraction improves testability (the worker logic becomes a
  pure method callable from tests, no hosted service scaffolding required)
- VPC endpoints for Bedrock, SQS, Secrets Manager eliminate the NAT gateway cost
  (~$32/month savings vs NAT gateway approach)
- Cognito JWT authorizer enforces authentication at the API Gateway layer — zero
  auth code in the ASP.NET Core application; Lambda is only invoked for authenticated
  requests
- Cognito Hosted UI means no login form to build or maintain in the React frontend
- No RDS Proxy required: saving $15/month vs original estimate at single-user concurrency

### Negative

- .NET Lambda cold starts: 1–3 seconds on first request after idle. Acceptable for
  personal use; provisioned concurrency (~$5/month) can eliminate this if needed
- Background service refactoring is required: each service's `ExecuteAsync` loop must
  be split into a "do-once" method and a Lambda handler. Estimated 2–3 days of work
- Migration Lambda adds deployment complexity: `terraform apply` must invoke the
  migration Lambda before updating the API Lambda container image
- Aurora Serverless v2 `min_capacity=0.5` means DB cost is ~$4/month even with zero
  requests (scale-to-zero was evaluated and rejected due to 15–30s resume latency)
- React frontend requires ~150 lines of new code for the PKCE auth flow and token
  injection, even though no backend code changes
- Cognito refresh tokens expire after 30 days; users must re-authenticate after a
  month of inactivity (configurable up to 3650 days)

### Neutral

- The Lambda Web Adapter requires a Dockerfile variant with the adapter layer added
  as a sidecar — but the application code and existing GHCR CI build are unchanged
- pgvector and the HNSW index remain in Aurora; no search behaviour changes
- The existing CDK-managed SQS relay Lambda (ADR-004) can be kept as a redundant
  capture path or deprecated once the API Gateway URL is public
- Cognito User Pool is in a separate `auth` Terraform module; it can be replaced with
  a different identity provider (e.g., Auth0, Entra ID) without touching the Lambda
  or ASP.NET Core code — only the API Gateway authorizer and React `auth.ts` change

## Alternatives Considered

### ECS Fargate + Aurora Serverless v2 (Option A)

Not chosen because it does not scale to zero and costs $40–73/month at personal usage.
The persistent container requires an ALB ($16/month) and either a NAT gateway ($32/month)
or accepting Fargate in a public subnet. The application runs unchanged, which is
appealing, but the cost differential ($30–60/month more than Option B) is not justified
for a single-user application.

If the application grows to multiple users requiring consistent sub-100ms latency
(no cold start tolerance), Option A should be reconsidered at that point.

### Lambda Web Adapter + Aurora Serverless v2 + S3 Vectors (Option C)

Not chosen at this time due to S3 Vectors SDK maturity risk. Amazon S3 Vectors was
announced in 2025 and the AWS SDK for .NET (`AWSSDK.S3Vectors`) may not be stable.
The dual-write pattern (Aurora for relational data + S3 Vectors for embeddings)
introduces consistency complexity and eliminates the ability to use pgvector's
native hybrid SQL search (`ts_rank + <=>` combined query).

The cost savings at 2000 notes are negligible in absolute terms (~$1/month). The
architectural pattern is sound — it follows the existing `IEmbeddingProvider`
interface design and the `ISearchService` abstraction already exists. This should
be revisited in Q3 2026 once the S3 Vectors .NET SDK stabilizes, implementing
`IS3VectorSearchService` as a config-gated alternative to pgvector.

### When to Reconsider

Revisit Option A (containers) if:
- Traffic grows beyond single-user and cold start latency becomes user-visible
- Lambda 15-minute timeout is insufficient for content generation batch jobs
- Multiple concurrent users exhaust Lambda concurrency limits

Revisit Option C (S3 Vectors) if:
- `AWSSDK.S3Vectors` reaches stable release with cosine similarity + metadata filtering
- Aurora vector storage costs become material (unlikely at < 100k notes)
- A use case emerges requiring more than 4096 vector dimensions (S3 Vectors supports
  up to the service maximum; pgvector's practical limit is ~2000 for HNSW)

## Related Decisions

- [ADR-001](ADR-001-backend-architecture.md): Simple Layered Architecture — the
  Lambda extraction keeps Controllers → Services → EF Core intact; only the hosting
  layer changes
- [ADR-004](ADR-004-sqs-webhook-ingestion.md): SQS Webhook Ingestion — the capture
  queue and DLQ are reused; the Lambda relay becomes optional once API Gateway is public
- [ADR-006](ADR-006-opentelemetry-observability.md): OpenTelemetry — Lambda supports
  OTLP export; the existing OTEL configuration works unchanged

## Notes

- Full analysis: [docs/design-aws-serverless-deployment.md](../design-aws-serverless-deployment.md)
- Aurora scale-to-zero analysis: [docs/design-aws-serverless-dynamodb-exploration.md](../design-aws-serverless-dynamodb-exploration.md)
- Lambda Web Adapter: https://github.com/awslabs/aws-lambda-web-adapter
- S3 Vectors: https://aws.amazon.com/s3/features/vectors/
- Aurora Serverless v2 auto-pause docs: https://docs.aws.amazon.com/AmazonRDS/latest/AuroraUserGuide/aurora-serverless-v2-auto-pause.html
- Terraform modules structure documented in the design document
- Connection string: `Max Pool Size=10;Connection Idle Lifetime=300` — no RDS Proxy needed
  at single-user concurrency; add RDS Proxy only if `max_connections` errors appear
