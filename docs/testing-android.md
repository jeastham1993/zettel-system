# Testing Zettel Mobile on a Physical Android Device

Last Updated: 2026-02-24

---

## Prerequisites

### On your development machine

- **Node.js 20+** — check with `node -v`
- **Android Studio** (for the local build path) — download from developer.android.com/studio
  - During setup, install: Android SDK, Android SDK Platform-Tools, Android Emulator
  - Set `ANDROID_HOME` env var: `export ANDROID_HOME=$HOME/Library/Android/sdk`
  - Add platform tools to PATH: `export PATH=$PATH:$ANDROID_HOME/platform-tools`
- **Java 17** — `java -version` should show 17.x. Install via `brew install openjdk@17` if needed.
- **Expo CLI** — `npm install -g expo-cli`

### On your Android phone

- Go to **Settings → About Phone**
- Tap **Build Number** 7 times to unlock Developer Options
- Go to **Settings → Developer Options**
- Enable **USB Debugging**
- Enable **Install via USB** (or "Allow installs from unknown sources" depending on Android version)

---

## Option A: Local Build (Recommended for Development)

This builds a dev APK on your machine and pushes it directly to the phone over USB.
No internet or EAS account required.

### Step 1: Connect your phone

```bash
# Connect via USB cable, then verify the device appears
adb devices
# Should show something like: R58M39XXXXX  device
```

If you see `unauthorized`, unlock your phone and accept the USB debugging prompt.

### Step 2: Install dependencies and prebuild

```bash
cd src/zettel-mobile
npm install

# Generate the native android/ directory
npx expo prebuild --platform android --clean
```

> `--clean` wipes and regenerates `android/` from scratch. Run this any time you change
> `app.json` (e.g. after adding intent filters — which you just did).

### Step 3: Build and install

```bash
# Build the debug APK and push it to your connected device
npx expo run:android --device
```

This will:
1. Run a Gradle build (~2-5 minutes first time, faster after)
2. Install the APK on your phone automatically
3. Start the Metro bundler
4. Launch the app on your phone

The app will hot-reload whenever you save a file — the build only needs to run again if
you change `app.json` or add/remove native modules.

### Step 4: Set your server URL

On first launch you'll see the "Connect to Server" screen.

- Make sure your phone and server are on the same network (home Wi-Fi or Tailscale)
- Find your server's local IP: on macOS run `ipconfig getifaddr en0`
- Enter the URL as: `http://192.168.x.x:PORT` (whatever port your Zettel server runs on)
- Tap **Connect** — the app calls `/health` to verify

---

## Option B: EAS Cloud Build (No Android Studio Required)

EAS builds the APK in the cloud and gives you a download link. No local toolchain needed.

### Step 1: Set up EAS

```bash
npm install -g eas-cli
eas login          # sign in to your Expo account (free)
```

### Step 2: Configure your project (one-time)

```bash
cd src/zettel-mobile
eas build:configure
```

If prompted for an Android package name, keep `com.anonymous.zettelmobile` or change it
to something like `com.yourname.zettelmobile`.

### Step 3: Build the APK

```bash
# Development build (includes dev client, larger file, hot reload works)
eas build --platform android --profile development

# Preview build (release APK, smaller, no hot reload — closer to production)
eas build --platform android --profile preview
```

This takes 5-15 minutes. EAS will print a URL to track progress and download the APK.

### Step 4: Install on your phone

1. Download the `.apk` file to your phone (email it to yourself, or download via the EAS URL)
2. Open the APK on your phone — Android will prompt to allow "Install from unknown sources"
3. Accept and install

---

## Testing Checklist

Once the app is running, work through this:

### Core flows

- [ ] First launch shows "Connect to Server" screen
- [ ] Enter server URL → taps Connect → validates and navigates to Home
- [ ] Home tab shows your recent notes (pull down to refresh)
- [ ] Discovery section shows serendipity notes (collapsible)
- [ ] Infinite scroll loads more notes as you scroll down
- [ ] Tap a note → Note detail screen with content, related notes, backlinks
- [ ] Tap edit (pencil icon) → Edit screen
- [ ] Edit title/content, preview toggle works, tags can be added/removed
- [ ] Save → navigates back, changes persisted
- [ ] Search tab → type a query → results appear with relevance %
- [ ] Switch search type (Hybrid / Full-text / Semantic) and re-search
- [ ] Inbox tab → shows fleeting notes with age dots (green/yellow/red)
- [ ] Swipe right on inbox item → promote (note disappears from inbox)
- [ ] Swipe left on inbox item → delete confirmation → deletes
- [ ] FAB (+) button → Quick Capture modal opens
- [ ] Type content → tap Capture → note saved, modal closes
- [ ] Settings → enter different server URL → Test Connection verifies

### Offline capture

1. Enable Airplane Mode on the phone
2. Open the app (it will show disconnected)
3. Tap FAB → type a note → Capture
4. You should see "Saved offline" toast
5. Disable Airplane Mode
6. Bring the app to foreground (lock + unlock the phone or switch away and back)
7. The offline note should auto-sync (check your server or the Inbox)
8. Settings → Offline → "Pending captures: 0" after sync

### Share sheet (URL sharing from browser)

1. Open Chrome on your phone
2. Navigate to any page
3. Tap the share icon → look for **Zettel** in the share sheet
4. Tap Zettel → the app opens and the Quick Capture modal appears pre-filled with the URL
5. Tap Capture to save it as a fleeting note

> **Note on plain text sharing**: If you share text (not a URL) from a notes app or
> clipboard manager, the app WILL appear in the share sheet and WILL open when selected,
> but the text won't be pre-filled in the capture box. This is a known limitation of how
> Android's SEND intent extras are exposed to JavaScript. Copy-paste the text manually.
> A future enhancement using a native module can fix this.

---

## Troubleshooting

### `adb devices` shows nothing

- Try a different USB cable (some cables are charge-only)
- Check Developer Options → USB Debugging is still enabled
- Run `adb kill-server && adb start-server` and reconnect

### Gradle build fails with Java version error

```bash
# Check Java version
java -version

# Install Java 17 if needed
brew install openjdk@17
export JAVA_HOME=/opt/homebrew/opt/openjdk@17
```

### App crashes immediately on launch

Most likely the MMKV native module isn't compiled in. Make sure you're using
`expo run:android` (development build), not `expo start --android` (Expo Go).
Always run `npx expo prebuild` first.

### Server connection fails

- Confirm the server is running: `curl http://192.168.x.x:PORT/health` from your laptop
- Make sure phone and server are on the same Wi-Fi network
- If using Tailscale, enter the Tailscale hostname (e.g. `http://server.tail12345.ts.net:PORT`)
- Temporarily set `Cors:AllowedOrigins: ["*"]` in your server config if you see CORS errors

### Hot reload isn't working after changing native code / app.json

Re-run the full prebuild + build cycle:
```bash
npx expo prebuild --platform android --clean
npx expo run:android --device
```

### Zettel doesn't appear in Android share sheet

The share intent filters are baked into the APK at build time. After adding them to
`app.json`, you must rebuild — an existing APK won't pick up the change.
Run `npx expo prebuild --clean && npx expo run:android --device` again.
