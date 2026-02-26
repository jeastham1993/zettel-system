/**
 * Vitest setup: provide minimal browser globals for the Node.js test environment.
 * These stubs are only active during tests — not in the real app.
 */

// sessionStorage / localStorage
function makeStorageStub(): Storage {
  const store: Record<string, string> = {}
  return {
    getItem: (k) => store[k] ?? null,
    setItem: (k, v) => { store[k] = String(v) },
    removeItem: (k) => { delete store[k] },
    clear: () => { for (const k in store) delete store[k] },
    get length() { return Object.keys(store).length },
    key: (i) => Object.keys(store)[i] ?? null,
  }
}

if (typeof sessionStorage === 'undefined') {
  ;(globalThis as unknown as Record<string, unknown>).sessionStorage = makeStorageStub()
}

if (typeof localStorage === 'undefined') {
  ;(globalThis as unknown as Record<string, unknown>).localStorage = makeStorageStub()
}

// window.location (only what auth.ts needs)
if (typeof window === 'undefined') {
  ;(globalThis as unknown as Record<string, unknown>).window = {
    location: { origin: 'http://localhost:5173' },
  }
}

// crypto.getRandomValues (used by auth.ts PKCE helpers)
if (typeof crypto === 'undefined') {
  const { webcrypto } = await import('node:crypto')
  ;(globalThis as unknown as Record<string, unknown>).crypto = webcrypto
}
