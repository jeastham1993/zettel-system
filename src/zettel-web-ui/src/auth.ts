/**
 * Cognito PKCE authentication module.
 *
 * Flow:
 *   1. App loads → isAuthenticated() check
 *   2. No token → redirectToLogin() → Cognito Hosted UI
 *   3. User logs in → Cognito redirects to /callback?code=...
 *   4. handleCallback(code) exchanges code for tokens → stored in sessionStorage
 *   5. All API calls include Authorization: Bearer <access_token>
 *   6. On 401 → redirectToLogin() re-runs the flow
 *
 * Environment variables (set from terraform outputs in CI/CD):
 *   VITE_COGNITO_CLIENT_ID  — Cognito App Client ID
 *   VITE_COGNITO_DOMAIN     — https://{pool-domain}.auth.{region}.amazoncognito.com
 */

const CLIENT_ID = import.meta.env.VITE_COGNITO_CLIENT_ID as string
const COGNITO_DOMAIN = import.meta.env.VITE_COGNITO_DOMAIN as string
const REDIRECT_URI = `${window.location.origin}/callback`

// ── PKCE helpers ─────────────────────────────────────────────────────────────

function generateRandomBytes(length: number): Uint8Array {
  return crypto.getRandomValues(new Uint8Array(length))
}

function base64UrlEncode(bytes: Uint8Array): string {
  return btoa(String.fromCharCode(...bytes))
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=/g, '')
}

async function generateCodeVerifier(): Promise<string> {
  return base64UrlEncode(generateRandomBytes(32))
}

async function generateCodeChallenge(verifier: string): Promise<string> {
  const data = new TextEncoder().encode(verifier)
  const digest = await crypto.subtle.digest('SHA-256', data)
  return base64UrlEncode(new Uint8Array(digest))
}

// ── Token storage ─────────────────────────────────────────────────────────────
// sessionStorage: tokens cleared when the browser tab is closed.

const STORAGE_KEYS = {
  accessToken: 'auth_access_token',
  refreshToken: 'auth_refresh_token',
  pkceVerifier: 'auth_pkce_verifier',
} as const

export function getToken(): string | null {
  return sessionStorage.getItem(STORAGE_KEYS.accessToken)
}

export function isAuthenticated(): boolean {
  return getToken() !== null
}

function storeTokens(accessToken: string, refreshToken: string): void {
  sessionStorage.setItem(STORAGE_KEYS.accessToken, accessToken)
  sessionStorage.setItem(STORAGE_KEYS.refreshToken, refreshToken)
}

// ── Auth actions ──────────────────────────────────────────────────────────────

export async function redirectToLogin(): Promise<void> {
  const verifier = await generateCodeVerifier()
  const challenge = await generateCodeChallenge(verifier)

  sessionStorage.setItem(STORAGE_KEYS.pkceVerifier, verifier)

  const params = new URLSearchParams({
    response_type: 'code',
    client_id: CLIENT_ID,
    redirect_uri: REDIRECT_URI,
    code_challenge: challenge,
    code_challenge_method: 'S256',
    scope: 'openid email',
  })

  window.location.href = `${COGNITO_DOMAIN}/oauth2/authorize?${params}`
}

export async function handleCallback(code: string): Promise<void> {
  const verifier = sessionStorage.getItem(STORAGE_KEYS.pkceVerifier)
  if (!verifier) {
    throw new Error('PKCE verifier missing — restart the login flow')
  }

  const response = await fetch(`${COGNITO_DOMAIN}/oauth2/token`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body: new URLSearchParams({
      grant_type: 'authorization_code',
      client_id: CLIENT_ID,
      redirect_uri: REDIRECT_URI,
      code,
      code_verifier: verifier,
    }),
  })

  if (!response.ok) {
    const error = await response.text().catch(() => 'unknown error')
    throw new Error(`Token exchange failed: ${error}`)
  }

  const tokens = await response.json()
  storeTokens(tokens.access_token, tokens.refresh_token)
  sessionStorage.removeItem(STORAGE_KEYS.pkceVerifier)
}

export function logout(): void {
  sessionStorage.clear()
  const params = new URLSearchParams({
    client_id: CLIENT_ID,
    logout_uri: window.location.origin + '/',
  })
  window.location.href = `${COGNITO_DOMAIN}/logout?${params}`
}
