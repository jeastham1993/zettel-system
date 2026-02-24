---
type: pattern
category: infrastructure
tags: [configuration, startup, logging, aspnetcore, dotnet, debugging, deployment]
created: 2026-02-24
confidence: high
languages: [csharp, dotnet]
related: [DN-001]
---

# Startup Configuration Logging for Deployment Debugging

## Problem

A feature that depends on external configuration (API keys, connection strings, account
IDs) silently fails at request time with a vague user-facing error. The error is hard to
diagnose because:

- You can't tell from the UI which config value is missing
- You can't easily inspect live container environment variables
- The failure happens on the first request, not on startup

In this project, publishing returned HTTP 422 ("Publishing is not configured for this
medium") even when the operator believed the environment variables were set. Because the
`IsConfigured` check happens inside a request handler, the first sign of the problem was a
user-visible error — not a log line during deploy.

## Solution

After `app.Build()`, resolve the options and log their effective state before handling any
requests. Log **derived state** (booleans, counts), not the secret values themselves:

```csharp
var publishingOpts = app.Services.GetRequiredService<IOptions<PublishingOptions>>().Value;
var log = app.Services.GetRequiredService<ILogger<Program>>();

log.LogInformation(
    "Publishing — GitHub: {GitHub} (token: {Token}, owner: '{Owner}', repo: '{Repo}'), " +
    "Publer: {Publer} (key: {Key}, accounts: {Accounts})",
    publishingOpts.GitHub.IsConfigured,
    !string.IsNullOrEmpty(publishingOpts.GitHub.Token),   // bool, not the token
    publishingOpts.GitHub.Owner,
    publishingOpts.GitHub.Repo,
    publishingOpts.Publer.IsConfigured,
    !string.IsNullOrEmpty(publishingOpts.Publer.ApiKey),  // bool, not the key
    publishingOpts.Publer.Accounts.Count);
```

On a correct deployment this produces:

```
Publishing — GitHub: True (token: True, owner: 'jeastham1993', repo: 'James-Eastham-Blog'),
Publer: True (key: True, accounts: 1)
```

On a misconfigured deployment:

```
Publishing — GitHub: False (token: False, owner: '', repo: ''),
Publer: False (key: True, accounts: 0)
```

The second line immediately shows that `Accounts` is 0 despite the key being present —
pointing directly at a collection binding issue (see **DN-001**).

## When to Apply

Add a startup log whenever a service has an `IsConfigured`/`IsEnabled` guard:

- External API integrations (Publer, GitHub, Stripe, etc.)
- Optional features toggled by config (schedulers, background workers)
- Any `IOptions<T>` whose misconfiguration causes a silent or confusing runtime failure

## What NOT to log

Never log secret values directly:

```csharp
// ❌ Do not do this
log.LogInformation("GitHub token: {Token}", opts.Token);

// ✅ Log presence only
log.LogInformation("GitHub token present: {Token}", !string.IsNullOrEmpty(opts.Token));
```

## Placement

Log after `app.Build()` but before `app.Run()`. At this point the DI container is fully
built and all `IOptions<T>` are resolvable, but no requests have been served yet — so the
log appears in the startup output that operators check after every deploy.
