import { useEffect, useRef } from 'react'
import { useNavigate } from 'react-router'
import { handleCallback } from '@/auth'

/**
 * Handles the redirect back from Cognito Hosted UI after successful login.
 * Exchanges the authorization code for tokens, then navigates to the app.
 */
export function CallbackPage() {
  const navigate = useNavigate()
  const handled = useRef(false)

  useEffect(() => {
    // Strict mode double-invocation guard — the code can only be exchanged once
    if (handled.current) return
    handled.current = true

    const code = new URLSearchParams(window.location.search).get('code')
    if (!code) {
      navigate('/', { replace: true })
      return
    }

    handleCallback(code)
      .then(() => navigate('/', { replace: true }))
      .catch((err) => {
        console.error('Auth callback failed:', err)
        // Clear any partial state and restart the login flow
        sessionStorage.clear()
        navigate('/', { replace: true })
      })
  }, [navigate])

  return (
    <div className="flex h-screen items-center justify-center">
      <div className="h-8 w-8 animate-spin rounded-full border-4 border-muted border-t-foreground" />
    </div>
  )
}
