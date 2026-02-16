# Design: Zettel-Web Mobile App (React Native / Expo)

Generated: 2026-02-15
Updated: 2026-02-15
Status: Draft
Decision: **Option C -- React Native with Expo** (chosen)

## Problem Statement

### Goal

Provide a mobile-native experience for Zettel-Web. The primary use case is
**quick capture** (fleeting notes on the go), with secondary use cases of
browsing, searching, and reading existing notes. Full note editing is a
nice-to-have but not the driving requirement.

### Constraints

- **Self-hosted**: The backend always runs on the user's own infrastructure
  (Docker Compose on a home server / NAS / VPS). There is no cloud service.
- **Same network**: The mobile app connects to the backend over the local
  network (or VPN/Tailscale for remote access). No app store backend exists.
- **Single developer**: Must be buildable and maintainable by one person.
- **Existing API**: The REST API is already complete (`/api/notes`, `/api/search`,
  `/api/capture`, `/api/notes/inbox`, etc.). No backend changes should be
  required for core mobile functionality.
- **Budget**: Zero ongoing costs beyond what already exists (self-hosted infra).

### Success Criteria

- [ ] Quick capture from mobile in < 3 seconds (open app -> type -> save)
- [ ] Search notes from mobile (fulltext + semantic)
- [ ] Browse and read notes with acceptable mobile formatting
- [ ] Inbox management (view fleeting notes, promote to permanent)
- [ ] Works offline for capture, syncs when back on network
- [ ] Installable on iOS and Android
- [ ] Share sheet integration (receive shared text/URLs as fleeting notes)

## Context

### Current State

The web app (`src/zettel-web-ui/`) is a React 19 SPA with:
- Tailwind v4 + ShadCN/ui (warm stone palette + amber accent)
- Tiptap WYSIWYG editor for rich text
- Cmd+K search, keyboard shortcuts, dark mode
- 50 source files, ~276KB gzip production build

The API surface the mobile app needs:

| Endpoint | Method | Purpose | Mobile Priority |
|----------|--------|---------|-----------------|
| `/api/notes` | POST | Create note (permanent or fleeting) | **P0** |
| `/api/notes` | GET | List notes (paginated, filterable) | **P0** |
| `/api/notes/{id}` | GET | Get single note | **P0** |
| `/api/notes/{id}` | PUT | Update note | **P1** |
| `/api/notes/{id}` | DELETE | Delete note | **P1** |
| `/api/notes/inbox` | GET | List fleeting notes | **P0** |
| `/api/notes/inbox/count` | GET | Inbox badge count | **P0** |
| `/api/notes/{id}/promote` | POST | Promote fleeting -> permanent | **P0** |
| `/api/search` | GET | Search (fulltext/semantic/hybrid) | **P0** |
| `/api/notes/search-titles` | GET | Title autocomplete | **P1** |
| `/api/notes/discover` | GET | Discovery/serendipity | **P2** |
| `/api/notes/{id}/related` | GET | Related notes | **P2** |
| `/api/tags` | GET | Tag autocomplete | **P1** |
| `/api/notes/{id}/backlinks` | GET | Backlinks | **P2** |
| `/api/notes/check-duplicate` | POST | Duplicate check | **P2** |
| `/api/notes/{id}/suggested-tags` | GET | AI-suggested tags | **P2** |
| `/api/notes/{id}/merge/{targetId}` | POST | Merge fleeting into note | **P2** |
| `/health` | GET | Health check | **P1** |

### Related Decisions

- [ADR-001](adr/ADR-001-backend-architecture.md): Simple layered backend
- [ADR-003](adr/ADR-003-fleeting-notes-architecture.md): Fleeting notes
- [ADR-007](adr/ADR-007-mobile-app-strategy.md): Mobile app strategy

### Alternatives Not Chosen

- **Option A (PWA)**: Minimal effort but no share sheet, limited offline on
  iOS, no native feel. Could serve as a fallback if RN proves too costly.
- **Option B (Capacitor)**: Wraps existing web app in native shell. Better
  than PWA but still WebView-quality scrolling/transitions. Not worth the
  native project overhead without the native UX benefit.

See earlier version of this document for full analysis of all three options.

---

## Chosen Architecture: React Native with Expo

### Project Structure

```
src/zettel-mobile/                   (NEW Expo project)
  app/                                (Expo Router -- file-based routing)
    _layout.tsx                       (Root layout: providers, theme, auth)
    (tabs)/
      _layout.tsx                     (Tab bar layout)
      index.tsx                       (Home: recent notes + discovery)
      search.tsx                      (Search: fulltext/semantic/hybrid)
      inbox.tsx                       (Inbox: fleeting notes list)
      settings.tsx                    (Server URL, theme, about)
    note/
      [id].tsx                        (Note detail: read view)
      [id]/edit.tsx                   (Note edit: title + markdown)
    capture.tsx                       (Quick capture modal -- presented modally)

  src/
    api/
      client.ts                       (HTTP client: baseURL from settings)
      notes.ts                        (Note CRUD, inbox, promote, merge)
      search.ts                       (Search endpoint)
      tags.ts                         (Tag autocomplete)
      health.ts                       (Health check)
      types.ts                        (Shared with web -- copy or package)

    components/
      NoteCard.tsx                    (Note list item: title, snippet, tags, age)
      NoteContent.tsx                 (Render note HTML/markdown to native)
      SearchBar.tsx                   (Search input with type picker)
      TagBadge.tsx                    (Tag display chip)
      TagInput.tsx                    (Tag entry with autocomplete)
      InboxItem.tsx                   (Inbox note with swipe actions)
      ConnectionStatus.tsx            (Online/offline indicator)
      EmptyState.tsx                  (Empty list placeholders)

    hooks/
      use-notes.ts                    (TanStack Query hooks for notes)
      use-search.ts                   (Search query hook)
      use-inbox.ts                    (Inbox query + count hook)
      use-server.ts                   (Server URL + connectivity)
      use-offline-queue.ts            (Offline queue management)

    stores/
      server-store.ts                 (MMKV: backend URL, connection state)
      offline-queue.ts                (MMKV: pending offline captures)
      preferences.ts                  (MMKV: theme, default search type)

    theme/
      colors.ts                       (Warm stone palette + amber accent)
      typography.ts                   (Font sizes, serif for titles)

    lib/
      markdown.ts                     (Markdown <-> HTML conversion)
      date.ts                         (Relative date formatting)

  app.json                            (Expo config)
  eas.json                            (EAS Build config)
  package.json
  tsconfig.json
```

### Key Technology Choices

| Concern | Choice | Rationale |
|---------|--------|-----------|
| Framework | **Expo SDK 53+** (managed workflow) | No native code needed initially; EAS Build handles compilation |
| Navigation | **Expo Router v4** (file-based) | Consistent with web mental model; deep linking for free |
| Data fetching | **TanStack React Query v5** | Same as web app; caching, refetching, optimistic updates |
| Local storage | **react-native-mmkv** | Synchronous, fast KV store; 30x faster than AsyncStorage |
| Offline queue | **MMKV** (serialised JSON array) | Simple, no ORM overhead; queue is small (captures only) |
| Styling | **NativeWind v4** (Tailwind for RN) | Same Tailwind classes as web; consistent design language |
| Note rendering | **react-native-markdown-display** | Render note content as Markdown in read view |
| Note editing | **Plain TextInput** (Markdown) | No WYSIWYG on mobile; Markdown with preview toggle |
| Icons | **lucide-react-native** | Same icon set as web app |
| Haptics | **expo-haptics** | Tactile feedback on capture, promote, delete |
| Share receive | **expo-sharing + Intent Filters** | Receive text/URLs from other apps |
| Gestures | **react-native-gesture-handler** | Swipe-to-delete, swipe-to-promote on inbox items |
| HTTP client | **fetch** (built-in) | Same pattern as web; no axios needed |
| Build | **EAS Build** (free tier) | Cloud builds for iOS/Android; no local Xcode/Android Studio needed for CI |

### Screen-by-Screen Design

#### Tab 1: Home (`app/(tabs)/index.tsx`)

```
┌─────────────────────────┐
│  Zettel            [⚡]  │  ← Connection status indicator
├─────────────────────────┤
│                         │
│  Recent Notes           │
│  ┌─────────────────┐   │
│  │ Note Title       │   │  ← NoteCard: title, first line, tags, age
│  │ First line of... │   │
│  │ #tag1  #tag2     │   │
│  └─────────────────┘   │
│  ┌─────────────────┐   │
│  │ Another Note     │   │
│  │ Content previ... │   │
│  └─────────────────┘   │
│        ...              │
│                         │
│  Discover               │  ← Collapsible section, serendipity
│  ┌────────┐ ┌────────┐ │
│  │ Random │ │ Random │ │
│  │ Note 1 │ │ Note 2 │ │
│  └────────┘ └────────┘ │
│                         │
│  [🏠] [🔍] [📥] [⚙️]   │  ← Bottom tab bar
│         ┌─┐             │
│         │+│             │  ← FAB: Quick capture
│         └─┘             │
└─────────────────────────┘
```

- **Pull-to-refresh** reloads recent notes
- **FAB (floating action button)** opens quick capture modal
- **Tap note** navigates to `note/[id].tsx` detail view
- **Discovery section** shows 4 serendipity notes from `/api/notes/discover`
- **Infinite scroll** pagination via `skip` + `take` params

#### Tab 2: Search (`app/(tabs)/search.tsx`)

```
┌─────────────────────────┐
│  ┌───────────────┐ [≡]  │  ← Search input + type picker (hybrid/fulltext/semantic)
│  │ Search notes...│     │
│  └───────────────┘      │
│                         │
│  Results                │
│  ┌─────────────────┐   │
│  │ Note Title  92% │   │  ← SearchResult: title, snippet, rank
│  │ ...matching...  │   │
│  └─────────────────┘   │
│  ┌─────────────────┐   │
│  │ Another One 87% │   │
│  │ ...relevant...  │   │
│  └─────────────────┘   │
│                         │
│  [🏠] [🔍] [📥] [⚙️]   │
└─────────────────────────┘
```

- **Debounced search** (300ms) triggers on text input
- **Search type segmented control** (Hybrid | Full-text | Semantic)
- **Tap result** navigates to note detail
- **Empty state** shows recent searches or suggestions

#### Tab 3: Inbox (`app/(tabs)/inbox.tsx`)

```
┌─────────────────────────┐
│  Inbox (3)              │  ← Badge count from /api/notes/inbox/count
├─────────────────────────┤
│                         │
│  ┌─────────────────┐   │
│  │ 🟠 Fleeting note │   │  ← Age dot (green < 1d, yellow < 3d, red > 3d)
│  │ content preview  │   │
│  │ via: telegram    │   │  ← Source badge
│  │ 2h ago           │   │
│  │ [Process] [Promote]│ │
│  └─────────────────┘   │
│                         │
│  ← Swipe left: Delete  │  ← Swipe gestures
│  → Swipe right: Promote │
│                         │
│  [🏠] [🔍] [📥] [⚙️]   │
└─────────────────────────┘
```

- **Swipe right** to promote (with haptic feedback)
- **Swipe left** to delete (with confirmation)
- **Tap "Process"** navigates to edit screen for full editing
- **Tap "Promote"** promotes and shows toast
- **Pull-to-refresh** reloads inbox
- **Empty state**: "All caught up!" with link to capture

#### Tab 4: Settings (`app/(tabs)/settings.tsx`)

```
┌─────────────────────────┐
│  Settings               │
├─────────────────────────┤
│                         │
│  Server                 │
│  ┌─────────────────┐   │
│  │ URL              │   │
│  │ http://192.168.. │   │  ← Editable server URL
│  └─────────────────┘   │
│  Status: Connected ✅   │  ← Health check result
│  Notes: 847 | Embedded: │
│  832 | Pending: 15      │
│                         │
│  Appearance             │
│  Theme: [System ▼]      │  ← System / Light / Dark
│                         │
│  Search                 │
│  Default type: [Hybrid] │
│                         │
│  Offline                │
│  Pending captures: 2    │
│  [Sync Now]             │
│                         │
│  About                  │
│  Version: 1.0.0         │
│                         │
│  [🏠] [🔍] [📥] [⚙️]   │
└─────────────────────────┘
```

- **Server URL** persisted in MMKV; validated with `/health` on change
- **Health data** shows note counts, embedding status from health endpoint
- **Offline queue** shows count of pending captures with manual sync button
- **QR code scan** option: web app could display a QR with the server URL
  for easy first-time setup (future enhancement)

#### Quick Capture Modal (`app/capture.tsx`)

```
┌─────────────────────────┐
│  ×  Quick Capture       │  ← Modal, presented over current screen
├─────────────────────────┤
│                         │
│  ┌─────────────────┐   │
│  │                 │   │
│  │ Type your       │   │  ← Auto-focused TextInput, multiline
│  │ thought...      │   │
│  │                 │   │
│  │                 │   │
│  └─────────────────┘   │
│                         │
│  Tags: [+ Add tag]      │  ← Optional tag input
│                         │
│  [Cancel]    [Capture]  │  ← Capture saves as fleeting note
│                         │
└─────────────────────────┘
```

- **Auto-focus** on TextInput when modal opens
- **Capture** creates a fleeting note via `POST /api/notes` with
  `status: "fleeting"`, `source: "mobile"`
- **Offline**: If no network, save to MMKV offline queue, show
  "Saved offline" toast with sync indicator
- **Haptic feedback** on successful capture
- **Keyboard avoidance** so the input stays visible above keyboard
- **Minimal UI** -- open to capture in < 3 seconds

#### Note Detail (`app/note/[id].tsx`)

```
┌─────────────────────────┐
│  ←  Note Title    [✏️]  │  ← Back button + edit button
├─────────────────────────┤
│                         │
│  Note Title             │  ← Serif font, large
│  Feb 15, 2026           │  ← Created date
│  #tag1  #tag2           │  ← Tag badges
│                         │
│  Rendered markdown      │  ← react-native-markdown-display
│  content of the note    │
│  with **bold**, links,  │
│  and other formatting.  │
│                         │
│  ─────────────────────  │
│  Related Notes          │  ← /api/notes/{id}/related
│  • Similar Note 1 (92%) │
│  • Similar Note 2 (87%) │
│                         │
│  Backlinks              │  ← /api/notes/{id}/backlinks
│  • Linking Note 1       │
│  • Linking Note 2       │
│                         │
└─────────────────────────┘
```

- **Markdown rendering** using react-native-markdown-display
- **Edit button** navigates to edit screen
- **Related notes** and **backlinks** shown below content
- **Share button** in header to share note text via system share sheet

#### Note Edit (`app/note/[id]/edit.tsx`)

```
┌─────────────────────────┐
│  ×  Edit         [Save] │  ← Cancel + Save buttons
├─────────────────────────┤
│                         │
│  ┌─────────────────┐   │
│  │ Note Title      │   │  ← TextInput for title
│  └─────────────────┘   │
│                         │
│  ┌─────────────────┐   │
│  │                 │   │
│  │ Markdown        │   │  ← Multiline TextInput
│  │ content here    │   │     Monospace font for editing
│  │                 │   │
│  │ **bold**        │   │
│  │ - list item     │   │
│  │                 │   │
│  └─────────────────┘   │
│                         │
│  Tags: #tag1 #tag2 [+]  │
│                         │
│  [Preview]              │  ← Toggle to rendered markdown view
│                         │
└─────────────────────────┘
```

- **Markdown-only editing** -- no WYSIWYG toolbar on mobile
- **Preview toggle** to see rendered output
- **Auto-save draft to MMKV** every 5 seconds (local only)
- **Unsaved changes warning** on back navigation
- **Title required** for permanent notes; optional for fleeting

---

### Shared Code Strategy

The web and mobile apps are **separate codebases** coupled only through
the HTTP API. Code sharing is limited to **types and API client logic**.

```
Shared (copy or npm workspace):
  - api/types.ts         → Exact copy, same interfaces
  - api/client.ts        → Rewritten: same fetch pattern, different baseURL logic
  - api/notes.ts         → Exact copy (uses fetch, no DOM APIs)
  - api/search.ts        → Exact copy
  - api/tags.ts          → Exact copy
  - api/health.ts        → Exact copy

NOT shared (platform-specific):
  - Components           → Native components, not web components
  - Routing              → Expo Router, not React Router
  - Styling              → NativeWind, not Tailwind CSS directly
  - Storage              → MMKV, not localStorage
  - Editor               → TextInput, not Tiptap
```

**Sharing mechanism**: For v1, **copy the files**. The API surface is stable
and changes infrequently. If drift becomes a problem, extract to an npm
workspace package later (`packages/zettel-api/`).

The key difference in the API client is `baseURL`:

```typescript
// Web: relative URLs (proxied by Vite/Traefik)
fetch('/api/notes')

// Mobile: absolute URL from user settings
fetch(`${serverUrl}/api/notes`)
```

The mobile `client.ts` wraps each call to prepend the configured server URL
from MMKV storage.

---

### Offline Strategy

**Scope**: Offline support for **captures only**. Browsing and search require
network connectivity. This keeps the complexity manageable for a solo developer.

```
Online:
  User captures → POST /api/notes → Success toast

Offline:
  User captures → Save to MMKV queue → "Saved offline" toast
  ...later, network returns...
  App foreground → Check MMKV queue → Replay POSTs → Clear queue → "Synced" toast
```

**MMKV Offline Queue Schema**:

```typescript
interface OfflineCapture {
  id: string              // UUID, generated on device
  content: string         // Note content
  tags: string[]          // Optional tags
  source: 'mobile'
  capturedAt: string      // ISO timestamp
}

// Stored in MMKV as JSON string under key 'offline-queue'
```

**Sync logic**:
1. On app foreground: check queue length
2. If queue > 0 and network available: replay each capture as
   `POST /api/notes` with `status: "fleeting"`
3. On success: remove from queue, show toast
4. On failure: leave in queue, retry on next foreground
5. Show pending count in Settings and as badge on tab bar

**Why not full offline sync?**
Full offline-first with bidirectional sync (e.g., WatermelonDB) adds massive
complexity: conflict resolution, schema versioning, storage migration. The
value proposition doesn't justify it for a personal app where the server is
always nearby. Captures are the only truly mobile-first action; everything
else (search, browse, edit) can wait for connectivity.

---

### Backend URL Discovery

The mobile app needs to know where the backend is. Options:

| Method | Complexity | UX |
|--------|------------|-----|
| Manual entry | Low | User types `http://192.168.1.x:80` |
| QR code | Low | Web app shows QR code in Settings, mobile scans |
| mDNS/Bonjour | Medium | Auto-discover `zettel.local` on LAN |
| Tailscale | Low | User enters Tailscale hostname |

**Recommendation**: Start with **manual entry** + **QR code scan**. Add
mDNS discovery later as an enhancement.

**First-launch flow**:
1. App opens to a "Connect to Server" screen
2. User enters URL or scans QR code
3. App calls `/health` to validate connection
4. On success: save URL to MMKV, navigate to home tab
5. On failure: show error, let user retry

---

### Design Language

Match the web app's warm stone palette with amber accent, adapted for
native components.

```typescript
// theme/colors.ts
export const colors = {
  // Stone palette (warm grays)
  background:      { light: '#fafaf9', dark: '#1c1917' },  // stone-50 / stone-900
  card:            { light: '#f5f5f4', dark: '#292524' },  // stone-100 / stone-800
  border:          { light: '#e7e5e4', dark: '#44403c' },  // stone-200 / stone-700
  muted:           { light: '#a8a29e', dark: '#78716c' },  // stone-400 / stone-500
  foreground:      { light: '#1c1917', dark: '#fafaf9' },  // stone-900 / stone-50

  // Amber accent
  primary:         { light: '#d97706', dark: '#f59e0b' },  // amber-600 / amber-500
  primaryMuted:    { light: '#fef3c7', dark: '#451a03' },  // amber-100 / amber-950

  // Status
  success:         '#16a34a',  // green-600
  warning:         '#d97706',  // amber-600
  destructive:     '#dc2626',  // red-600

  // Inbox age dots
  ageFresh:        '#16a34a',  // green-600 (< 1 day)
  ageModerate:     '#d97706',  // amber-600 (1-3 days)
  ageStale:        '#dc2626',  // red-600 (> 3 days)
}
```

**Typography**:
- Note titles: serif font (system serif or bundled)
- Body text: system default (San Francisco on iOS, Roboto on Android)
- Monospace for markdown editing

---

### Distribution Strategy

| Platform | Method | Cost | Friction |
|----------|--------|------|----------|
| **Android** | Direct APK sideload | Free | Low -- enable "Unknown Sources" |
| **Android** | Google Play Internal Testing | Free (25 devices) | Medium -- one-time setup |
| **iOS** | TestFlight | $99/yr Apple Developer | Medium -- requires Apple account |
| **iOS** | Ad-hoc provisioning | $99/yr Apple Developer | High -- device registration |
| **Both** | EAS Build (cloud) | Free tier (30 builds/mo) | Low -- `eas build` CLI |

**Recommendation for v1**: Android APK sideload (free, easy). iOS via
TestFlight if Apple Developer account is available. EAS Build for both
platforms to avoid needing local Xcode/Android Studio.

---

### Backend Changes Required

**None for v1.** The existing API is complete.

**Future enhancements** (if needed):

| Feature | Backend Change | Effort |
|---------|---------------|--------|
| Push notifications | `/api/push/subscribe` endpoint + notification service | ~100 LoC |
| Share extension | None (uses existing `POST /api/notes`) | 0 |
| QR code setup | Add QR code display to web Settings page | ~30 LoC frontend |
| Server-side search suggestions | `/api/search/suggest` endpoint | ~50 LoC |

---

### Testing Strategy

| Layer | Tool | What to Test |
|-------|------|-------------|
| API client | Jest | Request/response mapping, error handling |
| Hooks | React Native Testing Library | Query hook behavior, cache invalidation |
| Offline queue | Jest | Queue/dequeue, sync replay, persistence |
| Screens | Expo E2E (Maestro) | Full flow: capture -> inbox -> promote |
| Manual | Real devices | iOS + Android, offline mode, share sheet |

**Priority**: API client tests + offline queue tests first. These are the
riskiest pieces and the most testable in isolation.

---

### Failure Modes

| Mode | Severity | Occurrence | Detection | RPN | Mitigation |
|------|----------|------------|-----------|-----|------------|
| Feature drift between web and mobile | 6 | 7 | 5 | 210 | Mobile is capture/browse focused; don't replicate all web features |
| Rich text content renders poorly as markdown | 7 | 6 | 2 | 84 | Notes created on web use HTML (Tiptap); need HTML→markdown fallback |
| Offline captures lost on app uninstall | 8 | 2 | 3 | 48 | Show pending count prominently; warn before uninstall |
| User can't configure backend URL | 5 | 5 | 2 | 50 | QR code option; validate with /health; clear error messages |
| React Native upgrade breaks build | 5 | 3 | 3 | 45 | Pin Expo SDK version; upgrade deliberately |
| MMKV data corruption | 7 | 1 | 5 | 35 | MMKV is battle-tested; add queue integrity check on startup |

**Highest-RPN risk: Feature drift (210).** Mitigation: the mobile app is
explicitly NOT a feature-complete replica of the web app. It's optimised for
capture, browse, search, and inbox management. Full editing, graph view,
import/export, re-embed, and settings-heavy operations stay on the web.

**Content rendering risk (RPN 84)**: Notes created via the web use Tiptap
(HTML output). When displaying on mobile, we need to render HTML content.
Options:
1. **react-native-render-html** -- render Tiptap's HTML output directly
2. **HTML→markdown conversion** -- convert on display (lossy)
3. **Backend provides markdown** -- add `?format=markdown` query param

Recommendation: Use **react-native-render-html** for the read view. It
handles Tiptap's output well (headings, bold, italic, lists, links, code
blocks). For the edit view, show the raw HTML or convert to markdown with
a "best effort" converter.

---

## Implementation Plan

### Phase 1: Project Scaffold + Core Navigation (2-3 days)

- [ ] `npx create-expo-app src/zettel-mobile --template tabs`
- [ ] Configure Expo Router with tab layout (Home, Search, Inbox, Settings)
- [ ] Set up NativeWind v4 with stone + amber theme
- [ ] Add MMKV for settings storage
- [ ] Build Settings screen with server URL input + `/health` validation
- [ ] Build first-launch "Connect to Server" flow
- [ ] Configure EAS Build (`eas.json`) for development builds

### Phase 2: Quick Capture + Offline Queue (2-3 days)

- [ ] Build capture modal screen with auto-focused TextInput
- [ ] Wire capture to `POST /api/notes` with `status: "fleeting"`
- [ ] Build MMKV offline queue (save when no network, replay on reconnect)
- [ ] Add ConnectionStatus component (online/offline indicator)
- [ ] Add expo-haptics for capture feedback
- [ ] Add FAB button to tab layout
- [ ] Test offline capture → reconnect → sync flow

### Phase 3: Note List + Detail (2-3 days)

- [ ] Build NoteCard component
- [ ] Build Home screen with paginated note list (infinite scroll)
- [ ] Build Note detail screen with react-native-render-html
- [ ] Add related notes section to detail screen
- [ ] Add backlinks section to detail screen
- [ ] Add pull-to-refresh on note list
- [ ] Wire up TanStack React Query for caching

### Phase 4: Search (1-2 days)

- [ ] Build Search screen with debounced input
- [ ] Add search type segmented control (hybrid/fulltext/semantic)
- [ ] Build search result list with rank display
- [ ] Add empty state and loading states

### Phase 5: Inbox Management (2-3 days)

- [ ] Build Inbox screen with fleeting note list
- [ ] Add swipe-to-promote (right) and swipe-to-delete (left) gestures
- [ ] Add Process button (navigate to edit)
- [ ] Add Promote button with optimistic update
- [ ] Add inbox count badge on tab bar icon
- [ ] Add age-based dot colors (green/yellow/red)

### Phase 6: Note Editing (2-3 days)

- [ ] Build edit screen with title + markdown TextInput
- [ ] Add tag input with autocomplete
- [ ] Add markdown preview toggle
- [ ] Add auto-save draft to MMKV
- [ ] Add unsaved changes warning on back navigation
- [ ] Handle both new permanent notes and editing existing

### Phase 7: Share Sheet + Polish (2-3 days)

- [ ] Configure iOS/Android intent filters to receive text shares
- [ ] Route shared text to capture flow (create fleeting note)
- [ ] Add Discovery section to home screen
- [ ] Add dark mode support (system preference or manual toggle)
- [ ] Add app icon and splash screen
- [ ] Build APK for Android sideload
- [ ] Build via EAS for TestFlight (if Apple account available)

### Phase 8: Testing + Hardening (2-3 days)

- [ ] Write API client unit tests
- [ ] Write offline queue unit tests
- [ ] Manual testing on real iOS + Android devices
- [ ] Test with Tailscale for remote access
- [ ] Test edge cases: very long notes, no tags, empty inbox, rapid captures
- [ ] Performance test: list with 500+ notes

**Total estimated effort**: 2-4 weeks for full v1.

---

## Feature Scope: Mobile vs Web

Explicitly defining what the mobile app **does** and **does not** do
prevents feature drift:

| Feature | Web | Mobile | Notes |
|---------|-----|--------|-------|
| Quick capture | Yes | **Yes (primary)** | Mobile-optimised capture flow |
| Note list (browse) | Yes | **Yes** | Paginated, pull-to-refresh |
| Note detail (read) | Yes | **Yes** | HTML rendering |
| Note editing | Full WYSIWYG | **Markdown only** | Simplified for mobile |
| Search (all types) | Yes | **Yes** | Same API, mobile UI |
| Inbox management | Yes | **Yes** | Swipe gestures, promote/delete |
| Tags | Yes | **Yes** | Display + autocomplete input |
| Discovery | Yes | **Yes (P2)** | Serendipity section on home |
| Related notes | Yes | **Yes (P2)** | Below note content |
| Backlinks | Yes | **Yes (P2)** | Below note content |
| Dark mode | Yes | **Yes** | System preference |
| Offline capture | No | **Yes** | MMKV queue, sync on reconnect |
| Share sheet receive | No | **Yes** | Text/URLs → fleeting note |
| Knowledge graph | Yes | **No** | Desktop-only feature |
| Import/export | Yes | **No** | Desktop-only feature |
| Re-embed | Yes | **No** | Admin action, use web |
| Health dashboard | Full | **Minimal** | Connection status only |
| Keyboard shortcuts | Yes | **No** | Touch-native interactions |
| Wikilink editing | Yes | **No** | Complex editor feature |
| Version history | Yes | **No** | Desktop-only feature |

---

## Open Questions

### Decided

- [x] **Which mobile framework?** → React Native with Expo
- [x] **Rich text editing on mobile?** → No. Markdown-only editing with
      preview toggle. Notes created on web (Tiptap HTML) are rendered via
      react-native-render-html in read view.
- [x] **Full offline sync?** → No. Offline capture queue only. Browse/search
      require connectivity. Full sync is excessive complexity for a personal
      app on the same network as the server.

### To Decide

- [ ] **Expo managed vs bare workflow?** Managed is simpler but limits native
      module options. Start managed; eject to bare only if a critical native
      module requires it. Share extensions may require bare workflow or an
      Expo config plugin.
- [ ] **NativeWind v4 vs StyleSheet?** NativeWind gives Tailwind consistency
      with the web app but adds a build-time dependency. StyleSheet is zero
      overhead but means rewriting all styles. Leaning NativeWind for
      consistency, but need to verify Expo SDK 53 compatibility.
- [ ] **How to render Tiptap HTML on mobile?** react-native-render-html is
      the leading option. Need to verify it handles Tiptap's specific HTML
      output (task lists, code blocks with language hints, wikilinks).
      Alternative: add a `?format=markdown` query param to the backend to
      serve markdown instead of HTML.
- [ ] **Monorepo or separate repo?** Options:
      - Same repo (`src/zettel-mobile/`) -- simpler, can share files directly
      - Separate repo -- cleaner separation, independent versioning
      Leaning: **same repo** for now. It's a personal project; the overhead
      of a second repo isn't worth it. Can extract later if needed.
- [ ] **Android-first or both platforms from day one?** Android APK sideload
      is free and frictionless. iOS requires $99/yr Apple Developer account.
      Build both via EAS from day one, but only distribute Android APK until
      iOS TestFlight is set up.
- [ ] **Tab order and icons?** Current proposal:
      Home (house) | Search (magnifying glass) | Inbox (inbox with badge) |
      Settings (gear). Should capture be a 5th tab instead of a FAB?
      Leaning FAB -- capture should be accessible from any screen, not just
      when the capture tab is selected.
- [ ] **What happens when server URL changes?** (e.g., DHCP assigns new IP)
      Clear TanStack Query cache and MMKV offline queue? Keep offline queue
      (captures are still valid)? Show a "reconnect" prompt?
      Leaning: clear query cache, keep offline queue, prompt user.
- [ ] **Tailscale / VPN setup?** If using Tailscale, the server URL is a
      stable hostname (e.g., `http://server.tail12345.ts.net`). Should the
      app have a Tailscale-specific onboarding flow? Or just let the user
      enter the Tailscale URL manually?
      Leaning: manual entry is fine. Tailscale URLs are stable and easy to
      type. No special integration needed.
