---
type: pattern
category: frontend
tags: [react, shadcn, tailwind, navigation, hierarchy, visual-design, button-variants]
created: 2026-02-24
confidence: high
languages: [typescript, react]
related: []
---

# Primary Action Hierarchy in a Flat Navigation Toolbar

## Problem

A navigation bar accumulated 8 icon buttons over time as features were added one by one.
Each button used `variant="ghost" size="sm"` with `text-muted-foreground`. The result: every
action looked identical — the primary creation action ("New note") was visually
indistinguishable from a rarely-visited config page ("Voice & Style").

Users had to read each icon tooltip to evaluate importance rather than scanning by weight.
The header communicated no hierarchy.

## Root Cause

Feature-by-feature additions default to the same safe, unobtrusive style. No single addition
looks wrong in isolation. The problem only becomes visible when you step back and see all
actions as a group.

## Solution

Two changes, applied together:

### 1. Promote the primary action to `variant="default"`

The one thing users come to do most — in this case, creating a note — should be visually
distinct. `variant="default"` (filled, `bg-primary text-primary-foreground`) creates
immediate hierarchy without restructuring the nav.

```tsx
// Before — blends in with everything else
<Button variant="ghost" size="sm" asChild>
  <Link to="/new" className="gap-1.5 text-muted-foreground">
    <Plus className="h-4 w-4" />
    New
  </Link>
</Button>

// After — stands out as the primary action
<Button variant="default" size="sm" asChild>
  <Link to="/new" className="gap-1.5">
    <Plus className="h-4 w-4" />
    New
  </Link>
</Button>
```

Note: remove `text-muted-foreground` from the child `Link` when using `variant="default"` —
it overrides the button's own foreground color and produces washed-out text.

### 2. Add a `Separator` between action groups

Separate "things you do to content" (Search, New, Inbox) from "places you navigate to"
(Content, Voice, Graph, KB Health). A single vertical separator communicates the grouping
without any labels.

```tsx
import { Separator } from '@/components/ui/separator'

// Between Inbox and the secondary nav group
</Tooltip>  {/* end Inbox */}

<Separator orientation="vertical" className="h-4" />

<Tooltip>  {/* start secondary nav */}
```

## When to Apply

Audit the header whenever a new navigation destination is added. Ask:
- Is this a **primary action** (something users do constantly)? → `variant="default"`
- Is this a **frequent destination** (Inbox)? → `variant="ghost"` but in the primary group
- Is this a **utility/config destination** (settings, voice config)? → `variant="ghost"` in
  the secondary group, separated from primary actions

If all buttons have the same variant after adding a new one, stop and re-evaluate.

## Why It Works

Variant choice is information. `variant="default"` says "this matters most." Ghost says
"this is available but not urgent." A separator says "these are different kinds of things."
Users read these signals before reading any labels.
