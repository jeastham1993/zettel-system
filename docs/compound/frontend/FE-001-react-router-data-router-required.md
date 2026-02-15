---
type: problem-solution
category: frontend
tags: [react-router, useBlocker, data-router, createBrowserRouter]
created: 2026-02-14
confidence: high
languages: [typescript, react]
related: []
---

# React Router useBlocker Requires a Data Router

## Problem

Creating or editing notes crashed with:

```
useBlocker must be used within a data router.
See https://reactrouter.com/en/main/routers/picking-a-router.
```

## Root Cause

The note editor uses `useBlocker(isDirty)` to show an "unsaved changes"
dialog when navigating away. `useBlocker` is a React Router v7 hook that
**only works with data routers** (`createBrowserRouter` + `RouterProvider`).

The app was using the legacy `<BrowserRouter>` + `<Routes>` pattern, which
is not a data router.

## Solution

Convert from legacy router to data router:

**Before (`main.tsx`):**
```tsx
import { BrowserRouter } from 'react-router'
<BrowserRouter>
  <App />
</BrowserRouter>
```

**Before (`app.tsx`):**
```tsx
import { Routes, Route } from 'react-router'
export function App() {
  return (
    <Routes>
      <Route element={<AppShell />}>
        <Route path="/" element={<HomePage />} />
        ...
      </Route>
    </Routes>
  )
}
```

**After (`app.tsx`):**
```tsx
import { createBrowserRouter } from 'react-router'
export const router = createBrowserRouter([
  {
    element: <AppShell />,
    children: [
      { path: '/', element: <HomePage /> },
      ...
    ],
  },
])
```

**After (`main.tsx`):**
```tsx
import { RouterProvider } from 'react-router'
import { router } from '@/app'
<RouterProvider router={router} />
```

## Key Insight

In React Router v7, these hooks require a data router:
- `useBlocker`
- `useLoaderData` / `useActionData`
- `useFetcher`
- `useNavigation` (some features)

If you use any of these, you must use `createBrowserRouter` (or
`createHashRouter` / `createMemoryRouter`), not `<BrowserRouter>`.
