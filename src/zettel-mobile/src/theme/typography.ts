import { Platform } from 'react-native'

export const typography = {
  // Serif for note titles -- use system serif on each platform
  titleFamily: Platform.select({
    ios: 'Georgia',
    android: 'serif',
    default: 'serif',
  }),

  // System default for body text
  bodyFamily: Platform.select({
    ios: 'System',
    android: 'Roboto',
    default: 'System',
  }),

  // Monospace for markdown editing
  monoFamily: Platform.select({
    ios: 'Menlo',
    android: 'monospace',
    default: 'monospace',
  }),

  sizes: {
    xs: 12,
    sm: 14,
    base: 16,
    lg: 18,
    xl: 20,
    '2xl': 24,
    '3xl': 30,
  },

  lineHeights: {
    tight: 1.25,
    normal: 1.5,
    relaxed: 1.75,
  },
} as const
