# Compound Docs Index

Searchable knowledge base of solved problems, patterns, and learnings.

Last Updated: 2026-02-25 (session 3)

## Database

| ID | Title | Type | Tags |
|----|-------|------|------|
| [DB-001](database/DB-001-ef-core-ensure-created-no-alter.md) | EF Core EnsureCreated Does Not Alter Existing Tables | problem-solution | ef-core, ensureCreated, schema-migration, postgresql |
| [DB-002](database/DB-002-ef-core-ensure-created-pitfall.md) | EnsureCreated() silently skips table creation | problem-solution | ef-core, migrations, postgresql, docker |

## Patterns

| ID | Title | Type | Tags |
|----|-------|------|------|
| [PAT-001](patterns/PAT-001-ef-core-migrations-with-pgvector.md) | EF Core Migrations with pgvector and custom indexes | pattern | ef-core, pgvector, migrations, indexes |
| [PAT-002](patterns/PAT-002-full-stack-feature-completion.md) | Full-Stack Feature Completion: Backend and Frontend in One Unit | pattern | fullstack, api, frontend, workflow, checklist |

## .NET

| ID | Title | Type | Tags |
|----|-------|------|------|
| [DN-001](dotnet/DN-001-options-init-vs-set-configuration-binding.md) | .NET Options Classes: Use `set`, Not `init`, for Configuration Binding | problem-solution | dotnet, options-pattern, configuration-binding, init, set |
| [DN-002](dotnet/DN-002-double-dollar-raw-string-for-json-in-llm-prompts.md) | Use `$$"""` Raw Strings When Embedding JSON Examples in LLM Prompts | problem-solution | csharp, raw-strings, json, llm-prompts, compiler-error |
| [DN-003](dotnet/DN-003-meai-v10-chatcompletion-to-chatresponse-rename.md) | Microsoft.Extensions.AI v10.x Renamed ChatCompletion → ChatResponse | problem-solution | dotnet, microsoft-extensions-ai, ichatclient, breaking-change |

## Infrastructure

| ID | Title | Type | Tags |
|----|-------|------|------|
| [INFRA-001](infrastructure/INFRA-001-ollama-embedding-context-limit.md) | Ollama Embedding Model Context Length Exceeded | problem-solution | ollama, embedding, nomic-embed-text, truncation |
| [INFRA-002](infrastructure/INFRA-002-startup-configuration-logging.md) | Startup Configuration Logging for Deployment Debugging | pattern | configuration, startup, logging, aspnetcore, deployment |
| [INFRA-003](infrastructure/INFRA-003-ollama-httpclient-timeout-not-configurable.md) | OllamaSharp HttpClient Timeout Hardcoded at 100s | problem-solution | ollama, httpclient, timeout, ubuntu, cpu-inference |
| [INFRA-004](infrastructure/INFRA-004-embedding-similarity-thresholds-are-model-specific.md) | Embedding Similarity Thresholds Are Model-Specific | problem-solution | embedding, cosine-similarity, bedrock, titan, thresholds |
| [INFRA-005](infrastructure/INFRA-005-hnsw-index-dimension-mismatch-on-provider-switch.md) | HNSW Index Dimension Mismatch When Switching Embedding Providers | problem-solution | pgvector, hnsw, dimensions, bedrock, ollama, postgresql |

## Mobile

| ID | Title | Type | Tags |
|----|-------|------|------|
| [MOB-001](mobile/MOB-001-mmkv-nitromodules-expo-go.md) | MMKV v4 Requires Development Build (Not Expo Go) | problem-solution | react-native, expo, mmkv, nitro-modules, expo-go, development-build |

## Frontend

| ID | Title | Type | Tags |
|----|-------|------|------|
| [FE-001](frontend/FE-001-react-router-data-router-required.md) | React Router useBlocker Requires a Data Router | problem-solution | react-router, useBlocker, data-router |
| [FE-002](frontend/FE-002-api-client-http-method-mismatch.md) | Frontend API Client HTTP Method Must Match Backend Controller Attribute | problem-solution | http, 405, method-not-allowed, api-client, dotnet |
| [FE-003](frontend/FE-003-primary-action-hierarchy-in-toolbar.md) | Primary Action Hierarchy in a Flat Navigation Toolbar | pattern | navigation, hierarchy, button-variants, shadcn, visual-design |
| [FE-004](frontend/FE-004-page-heading-typography-consistency.md) | Page Heading Typography Must Follow the Established Pattern | problem-solution | typography, consistency, design-system, font-serif, page-headings |
