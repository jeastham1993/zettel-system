---
type: problem-solution
category: dotnet
tags: [csharp, raw-strings, interpolation, json, llm-prompts, compiler-error]
created: 2026-02-25
updated: 2026-02-25
confidence: high
languages: [dotnet]
related: []
---

# Use `$$"""` Raw Strings When Embedding JSON Examples in LLM Prompts

## Problem

Compiler errors when a `$"""` interpolated raw string contains JSON format examples:

```
error CS9006: The interpolated raw string literal does not start with enough
'$' characters to allow this many consecutive opening braces as content.
```

This occurs when the prompt includes literal JSON to show the LLM the expected
output format, e.g.:

```csharp
var prompt = $"""
    Return ONLY valid JSON:
    {"notes":[{"title":"...", "content":"..."}]}  // ← compiler error

    --- NOTE: {note.Title} ---
    {note.Content}
    """;
```

## Root Cause

In a `$"""` raw string, `{expr}` is an interpolation and `{{` is a literal `{`.
When JSON contains `[{"title"` the parser sees `[{` as the start of an interpolation
attempt and throws because `{` alone is not valid interpolation syntax.

## Solution

Use `$$"""` (double-dollar raw string literal). With two `$` characters:
- A **single** `{` is a literal brace
- `{{expr}}` is the interpolation syntax

```csharp
var prompt = $$"""
    Return ONLY valid JSON:
    {"notes":[{"title":"...", "content":"..."}]}  // ← now literal JSON, no error

    --- NOTE: {{note.Title}} ---
    {{note.Content}}
    """;
```

## Rule of Thumb

| Scenario | Use |
|---|---|
| No JSON / curly braces in template | `$"""..."""` — interpolate with `{expr}` |
| JSON examples or format strings in template | `$$"""..."""` — interpolate with `{{expr}}` |
| Many nested braces | `$$$"""..."""` — interpolate with `{{{expr}}}` |

The number of `$` characters sets the number of `{` required to open an interpolation.
Single braces are always literal when using `$$` or more.
