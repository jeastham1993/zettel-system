import {
  Mic,
  WifiOff,
  AlertCircle,
  Loader2,
} from 'lucide-react'
import { Button } from '@/components/ui/button'
import { useVoiceSession } from '@/hooks/use-voice-session'
import type { TranscriptEntry } from '@/hooks/use-voice-session'
import { CitationsSidebar } from '@/components/CitationsSidebar'

// ── Status indicator ──────────────────────────────────────────────────────────

function StatusIndicator({ status }: { status: 'listening' | 'thinking' | 'speaking' }) {
  if (status === 'listening') {
    return (
      <div className="relative flex h-12 w-12 items-center justify-center">
        <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-primary/30" />
        <span className="relative inline-flex h-8 w-8 rounded-full bg-primary/60" />
      </div>
    )
  }

  if (status === 'thinking') {
    return (
      <div className="flex h-12 w-12 items-center justify-center">
        <Loader2 className="h-8 w-8 animate-spin text-primary" />
      </div>
    )
  }

  // speaking — animated wave bars
  return (
    <div className="flex h-12 w-12 items-center justify-center gap-0.5">
      {[1, 2, 3, 4, 5].map((i) => (
        <span
          key={i}
          className="w-1 rounded-full bg-primary"
          style={{
            height: `${20 + i * 6}px`,
            animationName: 'voice-wave',
            animationDuration: `${0.6 + i * 0.1}s`,
            animationTimingFunction: 'ease-in-out',
            animationIterationCount: 'infinite',
            animationDirection: 'alternate',
          }}
        />
      ))}
    </div>
  )
}

// ── Status label map ──────────────────────────────────────────────────────────

const STATUS_LABELS: Record<string, string> = {
  listening: 'Listening...',
  thinking: 'Thinking...',
  speaking: 'Speaking...',
}

// ── Transcript bubble ─────────────────────────────────────────────────────────

function TranscriptBubble({ entry }: { entry: TranscriptEntry }) {
  const isUser = entry.role === 'user'
  return (
    <div className={`flex ${isUser ? 'justify-end' : 'justify-start'}`}>
      <div
        className={`max-w-[80%] rounded-xl px-3 py-2 text-sm ${
          isUser
            ? 'bg-primary text-primary-foreground'
            : 'bg-muted text-foreground border border-border/50'
        }`}
      >
        {entry.text}
      </div>
    </div>
  )
}

// ── Main page ─────────────────────────────────────────────────────────────────

export function VoicePage() {
  const { state, citations, transcript, errorMessage, devices, selectedDeviceId, setSelectedDeviceId, connect, disconnect } = useVoiceSession()

  return (
    <div className="mx-auto max-w-2xl px-4 py-8">
      <div className="mb-6">
        <h1 className="font-serif text-2xl font-bold tracking-tight">Voice Assistant</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Ask questions about your notes using your voice.
        </p>
      </div>

      {/* ── Checking ── */}
      {state === 'checking' && (
        <div className="flex flex-col items-center gap-4 py-16">
          <div className="h-8 w-8 animate-spin rounded-full border-4 border-muted border-t-foreground" />
          <p className="text-sm text-muted-foreground">Checking voice service...</p>
        </div>
      )}

      {/* ── Unavailable ── */}
      {state === 'unavailable' && (
        <div className="flex flex-col items-center gap-4 rounded-lg border border-border/50 bg-card px-6 py-10 text-center">
          <WifiOff className="h-10 w-10 text-muted-foreground" />
          <div>
            <h2 className="text-lg font-semibold">Voice service unavailable</h2>
            <p className="mt-1 text-sm text-muted-foreground">
              The voice assistant service isn&apos;t running. Start it with:
            </p>
            <code className="mt-2 inline-block rounded border border-border/50 bg-muted px-3 py-1.5 font-mono text-sm">
              uvicorn main:app
            </code>
            <p className="mt-1 text-xs text-muted-foreground">
              in the <code className="font-mono">voice-service/</code> directory.
            </p>
          </div>
          <Button variant="outline" size="sm" onClick={connect}>
            Retry
          </Button>
        </div>
      )}

      {/* ── Idle / Connecting ── */}
      {(state === 'idle' || state === 'connecting') && (
        <div className="flex flex-col items-center gap-6 py-16">
          {devices.length > 1 && (
            <div className="flex flex-col items-center gap-1.5">
              <label htmlFor="mic-select" className="text-xs text-muted-foreground">
                Microphone
              </label>
              <select
                id="mic-select"
                value={selectedDeviceId ?? ''}
                onChange={(e) => setSelectedDeviceId(e.target.value || null)}
                className="rounded-md border border-border bg-background px-3 py-1.5 text-sm text-foreground focus:outline-none focus:ring-2 focus:ring-ring"
              >
                <option value="">Default</option>
                {devices.map((d) => (
                  <option key={d.deviceId} value={d.deviceId}>
                    {d.label || `Microphone ${d.deviceId.slice(0, 6)}`}
                  </option>
                ))}
              </select>
            </div>
          )}
          <button
            onClick={connect}
            disabled={state === 'connecting'}
            className="flex h-24 w-24 items-center justify-center rounded-full bg-primary text-primary-foreground shadow-lg transition-transform hover:scale-105 disabled:cursor-not-allowed disabled:opacity-60"
            aria-label="Start voice session"
            aria-busy={state === 'connecting'}
          >
            {state === 'connecting' ? (
              <Loader2 className="h-10 w-10 animate-spin" />
            ) : (
              <Mic className="h-10 w-10" />
            )}
          </button>
          <p className="text-sm text-muted-foreground">
            {state === 'connecting' ? 'Connecting...' : 'Click to start'}
          </p>
        </div>
      )}

      {/* ── Active session ── */}
      {(state === 'listening' || state === 'thinking' || state === 'speaking') && (
        <div className="flex gap-6">
          {/* Left: status + transcript + stop */}
          <div className="min-w-0 flex-1 flex flex-col gap-4">
            {/* Status indicator */}
            <div
              role="status"
              aria-label={STATUS_LABELS[state] ?? state}
              className="flex items-center gap-3 rounded-lg border border-border/50 bg-card px-4 py-3"
            >
              <StatusIndicator status={state} />
              <span className="text-sm font-medium text-muted-foreground">
                {STATUS_LABELS[state]}
              </span>
            </div>

            {/* Transcript */}
            {transcript.length > 0 && (
              <div className="flex flex-col gap-2 rounded-lg border border-border/50 bg-card px-4 py-3">
                <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground mb-1">
                  Transcript
                </p>
                <div
                  role="log"
                  aria-live="polite"
                  aria-label="Conversation transcript"
                  className="space-y-2"
                >
                  {transcript.map((entry, i) => (
                    <TranscriptBubble key={i} entry={entry} />
                  ))}
                </div>
              </div>
            )}

            {/* Stop button */}
            <Button
              variant="outline"
              size="sm"
              className="self-start gap-1.5"
              aria-label="Stop voice session"
              onClick={disconnect}
            >
              Stop
            </Button>
          </div>

          {/* Right: citations */}
          {citations.length > 0 && <CitationsSidebar citations={citations} />}
        </div>
      )}

      {/* ── Error ── */}
      {state === 'error' && (
        <div className="flex flex-col items-center gap-4 rounded-lg border border-destructive/20 bg-destructive/5 px-6 py-10 text-center">
          <AlertCircle className="h-10 w-10 text-destructive" />
          <div>
            <h2 className="text-lg font-semibold">Something went wrong</h2>
            {errorMessage && (
              <p className="mt-1 text-sm text-muted-foreground">{errorMessage}</p>
            )}
          </div>
          <Button variant="outline" size="sm" onClick={connect}>
            Try again
          </Button>
        </div>
      )}
    </div>
  )
}
