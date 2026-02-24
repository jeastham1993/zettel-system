---
type: problem-solution
category: dotnet
tags: [dotnet, options-pattern, configuration-binding, init, set, aspnetcore]
created: 2026-02-24
confidence: high
languages: [csharp, dotnet]
related: []
---

# .NET Options Classes: Use `set`, Not `init`, for Configuration Binding

## Problem

An options class is written with `init`-only properties to make it feel immutable:

```csharp
public class PublerOptions
{
    public string? ApiKey { get; init; }
    public List<PublerAccount> Accounts { get; init; } = [];

    public bool IsConfigured =>
        !string.IsNullOrEmpty(ApiKey) && Accounts.Count > 0;
}

public class PublerAccount
{
    public string Id { get; init; } = "";
    public string Platform { get; init; } = "linkedin";
}
```

The environment variables are set correctly (e.g. `Publishing__Publer__ApiKey`,
`Publishing__Publer__Accounts__0__Id`), and the service starts without error. But at
runtime, `IsConfigured` returns `false` because `Accounts.Count` is 0 — the list binding
silently produced no items.

### Why it happens

`init` is a **C# compiler** restriction only. The CLR does not enforce it at runtime, so
`PropertyInfo.SetValue()` can bypass it. The .NET configuration binder does use reflection
to set properties.

However, for collection properties the binder's code path varies by .NET version:

- It may **get the existing list** and call `.Add()` (no setter needed — works regardless
  of `init`/`set`)
- It may **create a new list** and assign it back via the setter (requires a working
  setter path — this is where `init` can silently fail in some versions)

The result is version-dependent and non-obvious: simple `string?` properties bind fine, but
`List<T>` properties with `init` may silently stay empty.

## Solution

Use `set` on all properties in options classes. This is the canonical pattern in every
.NET/ASP.NET Core sample and is guaranteed to work across all runtime versions:

```csharp
public class PublerOptions
{
    public string? ApiKey { get; set; }
    public List<PublerAccount> Accounts { get; set; } = [];

    public bool IsConfigured =>
        !string.IsNullOrEmpty(ApiKey) && Accounts.Count > 0;
}

public class PublerAccount
{
    public string Id { get; set; } = "";
    public string Platform { get; set; } = "linkedin";
}
```

If you want the options to be immutable after construction, apply
`[ValidateOnStart]` + `IValidateOptions<T>` rather than trying to enforce immutability
through property accessors.

## Symptoms

- Publishing/feature guard (`IsConfigured`, `IsEnabled`, etc.) always returns `false`
  even when environment variables are set
- Simple `string?` properties bind correctly; `List<T>` or nested object properties do not
- No error or warning is logged — the binding silently yields the default value

## Verification

Add a startup log immediately after `app.Build()` to confirm what the binder resolved:

```csharp
var opts = app.Services.GetRequiredService<IOptions<PublerOptions>>().Value;
var log = app.Services.GetRequiredService<ILogger<Program>>();
log.LogInformation(
    "Publer — key present: {Key}, accounts: {Count}",
    !string.IsNullOrEmpty(opts.ApiKey),
    opts.Accounts.Count);
```

If `accounts: 0` appears with the env vars set, the `init` → `set` change will fix it.
See also: **INFRA-002** for the general startup configuration logging pattern.

## Rule of Thumb

> Options classes are data bags for the configuration binder. Use `set`. Immutability is
> not the goal here — correctness is.
