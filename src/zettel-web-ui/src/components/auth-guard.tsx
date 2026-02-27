import { useEffect, useState } from 'react'
import { isAuthenticated, redirectToLogin } from '@/auth'

interface AuthGuardProps {
  children: React.ReactNode
}

// Auth is only active when Cognito is configured (AWS deployment).
// Docker Compose deployments omit these env vars and bypass auth entirely.
const authEnabled = !!import.meta.env.VITE_COGNITO_CLIENT_ID

/**
 * Wraps the entire app. If no valid access token exists in sessionStorage,
 * redirects to the Cognito Hosted UI login page.
 *
 * When VITE_COGNITO_CLIENT_ID is not set (e.g. Docker Compose), auth is
 * skipped and children are rendered immediately.
 *
 * The /callback route is rendered before this component (in the router),
 * so it is never blocked by the auth guard.
 */
export function AuthGuard({ children }: AuthGuardProps) {
  const [checked, setChecked] = useState(!authEnabled)

  useEffect(() => {
    if (!authEnabled) return
    if (isAuthenticated()) {
      setChecked(true)
    } else {
      // Fire and forget — browser navigates away
      redirectToLogin().catch(console.error)
    }
  }, [])

  if (!checked) {
    return (
      <div className="flex h-screen items-center justify-center">
        <div className="h-8 w-8 animate-spin rounded-full border-4 border-muted border-t-foreground" />
      </div>
    )
  }

  return <>{children}</>
}
