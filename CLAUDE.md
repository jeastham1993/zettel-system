# CLAUDE.md — Zettel System

Project-level instructions that override or extend global defaults.

---

## Stack

- **Backend**: ASP.NET Core 10, EF Core, PostgreSQL + pgvector
- **Frontend**: React, TypeScript, Vite, TanStack Query, Tailwind, shadcn/ui
- **Tests**: xUnit + Testcontainers (real PostgreSQL, fake LLM/embedding clients)
- **Docs**: `docs/` — see `docs/compound/index.md` for searchable learnings

---

## Full-Stack Feature Completion Checklist

**Every new API endpoint must include all three layers in the same commit.**
Backend-only PRs are incomplete. See `docs/compound/patterns/PAT-002-full-stack-feature-completion.md`.

When adding any controller action:

- [ ] Backend endpoint implemented and tests passing
- [ ] API client function added to `src/zettel-web-ui/src/api/*.ts`
  - [ ] HTTP verb matches the controller attribute exactly (`[HttpPost]` → `post()`, `[HttpPut]` → `put()`, etc.)
  - [ ] URL path matches the controller route exactly
- [ ] UI wired up: button/trigger, loading state, success toast, error toast
- [ ] React Query cache invalidated correctly (right query key)
- [ ] `docs/API_REFERENCE.md` updated

### HTTP Verb Reference

| Controller attribute | Client helper |
|---|---|
| `[HttpGet]` | `get<T>(url)` |
| `[HttpPost]` | `post<T>(url, body?)` |
| `[HttpPut]` | `put<T>(url, body)` |
| `[HttpDelete]` | `del(url)` |

Never assume — read the attribute first. A wrong verb produces a silent `405` at runtime
that no test will catch. See `docs/compound/frontend/FE-002-api-client-http-method-mismatch.md`.

---

## Compound Docs

Before solving a problem, check `docs/compound/index.md` — it may already be documented.
After solving a non-trivial problem, run `/workflows:evolve` to capture the learning.

<!-- claude-reliability:binary-instructions managed section - DO NOT EDIT -->
## claude-reliability Binary

The `claude-reliability` binary for this project is located at:

    .claude-reliability/bin/claude-reliability

Always use this path when running commands. Do NOT use bare `claude-reliability`,
do NOT use paths containing `~/.claude-reliability/`, and do NOT use `$PLUGIN_ROOT_DIR`
or any other variable to construct the path.

Example usage:

    .claude-reliability/bin/claude-reliability work list
    .claude-reliability/bin/claude-reliability work next
    .claude-reliability/bin/claude-reliability work on <id>
    .claude-reliability/bin/claude-reliability work update <id> --status complete
<!-- end claude-reliability:binary-instructions -->
