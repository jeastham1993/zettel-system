---
type: problem-solution
category: frontend
tags: [react, tailwind, typography, consistency, design-system, page-headings, font-serif]
created: 2026-02-24
confidence: high
languages: [typescript, react]
related: []
---

# Page Heading Typography Must Follow the Established Pattern

## Problem

A new page (`KbHealthPage`) was built with a `<h1>` that didn't match the heading style used
everywhere else:

```tsx
// kb-health.tsx — WRONG
<h1 className="text-xl font-semibold">Knowledge Health</h1>
```

Every other dashboard-style page used the serif heading pattern:

```tsx
// content-review.tsx — CORRECT
<h1 className="font-serif text-2xl font-bold tracking-tight">Content Review</h1>
```

The result: KB Health's heading is visually lighter and smaller than every other page,
breaking the sense that pages belong to the same application.

## Root Cause

When building a new page, it's tempting to write a quick heading and move on to the
interesting parts (layout, data fetching). The typography pattern isn't enforced anywhere —
no lint rule, no shared component — so the deviation compiles without warning.

## Fix

Use the established heading pattern for all top-level page headings:

```tsx
// Dashboard/list pages (Content Review pattern):
<h1 className="font-serif text-2xl font-bold tracking-tight">{title}</h1>

// With subtitle:
<div className="mb-6">
  <h1 className="font-serif text-2xl font-bold tracking-tight">Knowledge Health</h1>
  <p className="mt-1 text-sm text-muted-foreground">
    Weekly view of your KB's structure.
  </p>
</div>
```

## The Typography Scale in This Project

| Context | Classes |
|---------|---------|
| Page heading (dashboard/list) | `font-serif text-2xl font-bold tracking-tight` |
| Section heading | `font-medium` or `text-xs font-medium uppercase tracking-wide text-muted-foreground` |
| Card/item title | `font-serif text-base font-semibold` (content) or `text-sm font-medium` (list) |
| List item title | `font-serif text-lg font-medium tracking-tight` |
| Meta/label | `text-xs text-muted-foreground` |

## Prevention

When creating a new page, copy the `<h1>` structure directly from an existing page before
writing anything else. The heading should be the first thing you write — it anchors the page's
visual identity.

Checklist when reviewing a new page:
- [ ] Does the `h1` use `font-serif`?
- [ ] Is the size `text-2xl` (dashboard) or appropriate for the page type?
- [ ] Does `font-bold tracking-tight` match the heading weight of adjacent pages?
