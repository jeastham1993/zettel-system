export const colors = {
  // Stone palette (warm grays)
  background: { light: '#fafaf9', dark: '#1c1917' }, // stone-50 / stone-900
  card: { light: '#f5f5f4', dark: '#292524' }, // stone-100 / stone-800
  border: { light: '#e7e5e4', dark: '#44403c' }, // stone-200 / stone-700
  muted: { light: '#a8a29e', dark: '#78716c' }, // stone-400 / stone-500
  foreground: { light: '#1c1917', dark: '#fafaf9' }, // stone-900 / stone-50

  // Amber accent
  primary: { light: '#d97706', dark: '#f59e0b' }, // amber-600 / amber-500
  primaryMuted: { light: '#fef3c7', dark: '#451a03' }, // amber-100 / amber-950

  // Status
  success: '#16a34a', // green-600
  warning: '#d97706', // amber-600
  destructive: '#dc2626', // red-600

  // Inbox age dots
  ageFresh: '#16a34a', // green-600 (< 1 day)
  ageModerate: '#d97706', // amber-600 (1-3 days)
  ageStale: '#dc2626', // red-600 (> 3 days)
} as const

export type ColorScheme = 'light' | 'dark'

export function themed(
  colorPair: { light: string; dark: string },
  scheme: ColorScheme,
): string {
  return colorPair[scheme]
}
