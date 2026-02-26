import { useEffect, useState } from 'react'
import { isAuthenticated, redirectToLogin } from '@/auth'

interface AuthGuardProps {
  children: React.ReactNode
}

/**
 * Wraps the entire app. If no valid access token exists in sessionStorage,
 * redirects to the Cognito Hosted UI login page.
 *
 * The /callback route is rendered before this component (in the router),
 * so it is never blocked by the auth guard.
 */
export function AuthGuard({ children }: AuthGuardProps) {
  const [checked, setChecked] = useState(false)

  useEffect(() => {
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
