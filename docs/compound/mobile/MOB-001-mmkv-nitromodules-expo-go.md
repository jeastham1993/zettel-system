---
type: problem-solution
category: mobile
tags: [react-native, expo, mmkv, nitro-modules, expo-go, development-build, native-modules]
created: 2026-02-15
confidence: high
languages: [typescript, react-native]
related: [ADR-007]
---

# MMKV v4 Requires Development Build (Not Expo Go)

## Problem

After adding `react-native-mmkv` v4 to an Expo project and running on the
Android emulator via Expo Go, the app crashes at startup with:

```
ERROR  Failed to get NitroModules: The native "NitroModules" Turbo/Native-Module
could not be found.
```

Every route file also warns "missing the required default export" — but this is
a cascade effect, not the root cause.

## Symptoms

1. **Primary error:** `NitroModules` native module not found, stack trace
   pointing at `react-native-mmkv` import in `preferences.ts`
2. **Cascade warnings:** Every route file that transitively imports the
   preferences store (via hooks like `use-theme.ts`) fails to export its
   default component, producing Expo Router "missing default export" warnings
3. The app renders a blank screen or error overlay

## Root Cause

`react-native-mmkv` v3+ switched from the old bridge architecture to
**NitroModules** — a JSI-based native bridge. NitroModules requires custom
native code compiled into the app binary.

**Expo Go** is a pre-built app with a fixed set of native modules. It does not
include NitroModules or MMKV's native code. Any library that ships its own
native C++/Java/ObjC code cannot run in Expo Go.

## Solution

Use a **custom development build** instead of Expo Go:

```bash
# 1. Install expo-dev-client (replaces Expo Go's JS launcher)
npx expo install expo-dev-client

# 2. Generate the native Android project
npx expo prebuild --platform android

# 3. Build and run on the emulator
npx expo run:android
```

This compiles all native modules (MMKV, NitroModules, Reanimated, etc.) into
a custom APK that still connects to your local Metro dev server for hot reload.

### Key points

- `expo-dev-client` gives you the same DX as Expo Go (hot reload, dev menu)
  but with your custom native modules included
- The generated `android/` and `ios/` directories should be gitignored (they
  are regenerable via `npx expo prebuild --clean`)
- After prebuild, use `expo run:android` instead of `expo start --android`

## How to Recognise This

If you see the NitroModules error **and** you're running via Expo Go (no
`android/` or `ios/` directory in the project), this is almost certainly the
cause. The tell-tale sign is:

- **No `android/` directory** = Expo Go = no custom native modules
- Any library using NitroModules, JSI, or TurboModules will fail the same way

## Affected Libraries

Any library that requires NitroModules or custom native code, including:
- `react-native-mmkv` v3+
- `react-native-worklets`
- Libraries using TurboModules directly

## Alternative

If you need to stay on Expo Go (e.g., for rapid prototyping), replace MMKV
with `@react-native-async-storage/async-storage` which is included in Expo Go.
The trade-off: AsyncStorage has an async API (MMKV is synchronous) and is
slower for frequent reads.
