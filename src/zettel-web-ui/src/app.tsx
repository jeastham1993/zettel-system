import { lazy, Suspense } from 'react'
import { createBrowserRouter } from 'react-router'
import { AppShell } from '@/components/app-shell'
import { HomePage } from '@/pages/home'
import { NotePage } from '@/pages/note'
import { EditorPage } from '@/pages/editor'
import { NotFoundPage } from '@/pages/not-found'
import { CallbackPage } from '@/pages/callback'

const GraphPage = lazy(() =>
  import('./pages/graph').then((m) => ({ default: m.GraphPage })),
)
const InboxPage = lazy(() =>
  import('./pages/inbox').then((m) => ({ default: m.InboxPage })),
)
const SettingsPage = lazy(() =>
  import('./pages/settings').then((m) => ({ default: m.SettingsPage })),
)
const ContentReviewPage = lazy(() =>
  import('./pages/content-review').then((m) => ({ default: m.ContentReviewPage })),
)
const VoiceConfigPage = lazy(() =>
  import('./pages/voice-config').then((m) => ({ default: m.VoiceConfigPage })),
)
const KbHealthPage = lazy(() =>
  import('./pages/kb-health').then((m) => ({ default: m.KbHealthPage })),
)
const ResearchPage = lazy(() =>
  import('./pages/research').then((m) => ({ default: m.ResearchPage })),
)

function LazyFallback() {
  return (
    <div className="flex h-[calc(100vh-4rem)] items-center justify-center">
      <div className="h-8 w-8 animate-spin rounded-full border-4 border-muted border-t-foreground" />
    </div>
  )
}

export const router = createBrowserRouter([
  // Auth callback — outside AppShell, no auth required
  { path: '/callback', element: <CallbackPage /> },
  {
    element: <AppShell />,
    children: [
      { path: '/', element: <HomePage /> },
      { path: '/notes/:id', element: <NotePage /> },
      { path: '/notes/:id/edit', element: <EditorPage /> },
      { path: '/new', element: <EditorPage /> },
      {
        path: '/inbox',
        element: (
          <Suspense fallback={<LazyFallback />}>
            <InboxPage />
          </Suspense>
        ),
      },
      {
        path: '/settings',
        element: (
          <Suspense fallback={<LazyFallback />}>
            <SettingsPage />
          </Suspense>
        ),
      },
      {
        path: '/content',
        element: (
          <Suspense fallback={<LazyFallback />}>
            <ContentReviewPage />
          </Suspense>
        ),
      },
      {
        path: '/voice',
        element: (
          <Suspense fallback={<LazyFallback />}>
            <VoiceConfigPage />
          </Suspense>
        ),
      },
      {
        path: '/graph',
        element: (
          <Suspense fallback={<LazyFallback />}>
            <GraphPage />
          </Suspense>
        ),
      },
      {
        path: '/kb-health',
        element: (
          <Suspense fallback={<LazyFallback />}>
            <KbHealthPage />
          </Suspense>
        ),
      },
      {
        path: '/research',
        element: (
          <Suspense fallback={<LazyFallback />}>
            <ResearchPage />
          </Suspense>
        ),
      },
      { path: '*', element: <NotFoundPage /> },
    ],
  },
])
