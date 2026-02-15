# ADR-005: Mobile App Strategy

Date: 2026-02-15
Status: Proposed

## Context

Zettel-Web is used on both desktop and mobile. The primary mobile use case
is quick capture (fleeting notes on the go), with secondary use cases of
search, browse, and inbox management. The backend is always self-hosted and
the mobile device is on the same network (LAN or VPN).

Three approaches were evaluated:
- **Option A**: Progressive Web App (enhance existing React SPA)
- **Option B**: Capacitor wrapper (existing SPA in native WebView shell)
- **Option C**: React Native / Expo (dedicated native mobile app)

## Decision

Build a **dedicated React Native mobile app using Expo**, housed in
`src/zettel-mobile/` within the existing repository.

The mobile app is purpose-built for mobile interaction patterns -- it is NOT
a feature-complete replica of the web app. It focuses on:
- Quick capture (fleeting notes with offline queue)
- Browse and read notes (HTML rendering via react-native-render-html)
- Search (fulltext / semantic / hybrid)
- Inbox management (swipe gestures to promote/delete)
- Share sheet integration (receive text/URLs from other apps)

Desktop-only features stay on the web: knowledge graph, import/export,
re-embed, full WYSIWYG editing, wikilink editing, version history.

Key technology choices:
- **Expo SDK 53+** (managed workflow)
- **Expo Router v4** (file-based navigation with bottom tabs)
- **TanStack React Query v5** (same data layer as web)
- **MMKV** (fast local storage for settings + offline capture queue)
- **NativeWind v4** (Tailwind classes for style consistency)
- **react-native-render-html** (render Tiptap HTML output in read view)
- **Markdown-only editing** (no WYSIWYG on mobile)

## Consequences

### Positive

- True native UX: native navigation, gestures, scrolling, haptic feedback
- Purpose-built screens for mobile use cases (quick capture in < 3 seconds)
- Robust offline capture via MMKV queue (not dependent on service workers)
- Share sheet integration to receive text/URLs as fleeting notes
- Same React + TypeScript skills; API client types shareable with web
- Expo ecosystem: OTA updates, EAS Build, rich plugin library
- Clear feature boundary prevents maintenance creep

### Negative

- Separate codebase to maintain alongside the web app
- Higher initial effort (~2-4 weeks for v1 vs 1-3 days for PWA)
- Rich text editing is Markdown-only on mobile (Tiptap WYSIWYG stays web)
- Feature drift risk (RPN 210) -- mitigated by explicitly scoped feature list
- Tiptap HTML content may not render perfectly via react-native-render-html
- iOS distribution requires Apple Developer account ($99/yr)
- Build infrastructure: EAS Build or local Xcode/Android Studio

### Neutral

- The backend API requires zero changes -- mobile and web share the same
  REST endpoints, only differing in base URL configuration
- Notes created on mobile (Markdown) will be stored as-is; notes created
  on web (Tiptap HTML) need HTML rendering on mobile
- Offline support is scoped to captures only -- full bidirectional sync is
  not worth the complexity for a personal app on the same network

## Alternatives Considered

### Progressive Web App (Option A)

Not chosen because: no share sheet integration, unreliable offline on iOS
(Safari kills service workers aggressively), web-like feel rather than
native. However, PWA remains a valid fallback if React Native maintenance
burden proves too high. The effort to add a PWA manifest is < 1 day.

### Capacitor Wrapper (Option B)

Not chosen because: it provides native API access (share sheet, push) but
the UI is still a WebView -- same scrolling/animation quality as a browser.
The overhead of maintaining ios/ and android/ native projects isn't justified
without the native UX benefit. If we're managing native projects, we should
get native UI quality (React Native) not WebView quality (Capacitor).

## Related Decisions

- [ADR-001](ADR-001-backend-architecture.md): Simple layered backend (API
  serves both web and mobile unchanged)
- [ADR-003](ADR-003-fleeting-notes-architecture.md): Fleeting notes with
  quick capture (the primary mobile use case)

## Notes

- Full design document: [docs/design-mobile-app.md](../design-mobile-app.md)
- Mobile app lives at `src/zettel-mobile/` in the same repository
- API types are copied from web (`src/zettel-web-ui/src/api/types.ts`);
  extract to shared package if drift becomes a problem
- The existing Telegram bot and email capture channels complement the mobile
  app -- they provide capture without opening any app at all
- Tailscale or WireGuard VPN recommended for access outside the home network
- **CORS configuration**: Allowed origins are configurable via `Cors:AllowedOrigins`
  in appsettings or environment variables. Set to `["*"]` to allow any origin, or
  provide an explicit list (e.g., `["http://localhost:8081"]`). React Native's
  native `fetch` is not subject to CORS, so this only affects browser-based clients
  (Expo Web, the Vite web UI in non-proxied mode). In production behind Traefik,
  the web UI and API share the same origin so CORS is not exercised.
