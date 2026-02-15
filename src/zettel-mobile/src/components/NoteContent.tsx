import { useMemo } from 'react'
import { ScrollView, StyleSheet, Text, useWindowDimensions } from 'react-native'
import RenderHtml, { type MixedStyleDeclaration } from 'react-native-render-html'
import { colors, themed, type ColorScheme } from '../theme/colors'
import { typography } from '../theme/typography'
import { htmlToPlainText } from '../lib/markdown'
import { ErrorBoundary } from './ErrorBoundary'

const IGNORED_DOM_TAGS = ['iframe', 'script', 'form', 'input', 'object', 'embed', 'style']

interface NoteContentProps {
  html: string
  scheme: ColorScheme
}

function PlainTextFallback({ html, scheme }: NoteContentProps) {
  const fg = themed(colors.foreground, scheme)
  const muted = themed(colors.muted, scheme)

  return (
    <ScrollView style={styles.fallbackContainer}>
      <Text style={[styles.fallbackTitle, { color: muted }]}>
        Could not render this note
      </Text>
      <Text style={[styles.fallbackBody, { color: fg }]}>
        {htmlToPlainText(html)}
      </Text>
    </ScrollView>
  )
}

export function NoteContent({ html, scheme }: NoteContentProps) {
  const { width } = useWindowDimensions()
  const contentWidth = width - 40 // 20px padding each side

  const fg = themed(colors.foreground, scheme)
  const muted = themed(colors.muted, scheme)
  const primary = themed(colors.primary, scheme)
  const codeBg = themed(colors.card, scheme)
  const borderColor = themed(colors.border, scheme)

  const baseStyle: MixedStyleDeclaration = useMemo(() => ({
    color: fg,
    fontSize: typography.sizes.base,
    lineHeight: typography.sizes.base * typography.lineHeights.relaxed,
    fontFamily: typography.bodyFamily,
  }), [fg])

  const tagsStyles: Record<string, MixedStyleDeclaration> = useMemo(() => ({
    h1: {
      fontSize: typography.sizes['2xl'],
      fontWeight: '700',
      fontFamily: typography.titleFamily,
      color: fg,
      marginBottom: 8,
      marginTop: 20,
    },
    h2: {
      fontSize: typography.sizes.xl,
      fontWeight: '700',
      fontFamily: typography.titleFamily,
      color: fg,
      marginBottom: 6,
      marginTop: 16,
    },
    h3: {
      fontSize: typography.sizes.lg,
      fontWeight: '600',
      fontFamily: typography.titleFamily,
      color: fg,
      marginBottom: 4,
      marginTop: 12,
    },
    a: {
      color: primary,
      textDecorationLine: 'underline',
    },
    strong: {
      fontWeight: '700',
    },
    em: {
      fontStyle: 'italic',
    },
    code: {
      fontFamily: typography.monoFamily,
      fontSize: typography.sizes.sm,
      backgroundColor: codeBg,
      paddingHorizontal: 4,
      paddingVertical: 2,
      borderRadius: 4,
    },
    pre: {
      backgroundColor: codeBg,
      padding: 12,
      borderRadius: 8,
      borderWidth: 1,
      borderColor: borderColor,
      overflow: 'hidden' as const,
    },
    blockquote: {
      borderLeftWidth: 3,
      borderLeftColor: primary,
      paddingLeft: 12,
      marginLeft: 0,
      color: muted,
      fontStyle: 'italic',
    },
    ul: {
      paddingLeft: 8,
    },
    ol: {
      paddingLeft: 8,
    },
    li: {
      marginBottom: 4,
    },
  }), [fg, muted, primary, codeBg, borderColor])

  return (
    <ErrorBoundary fallback={<PlainTextFallback html={html} scheme={scheme} />}>
      <RenderHtml
        contentWidth={contentWidth}
        source={{ html }}
        baseStyle={baseStyle}
        tagsStyles={tagsStyles}
        ignoredDomTags={IGNORED_DOM_TAGS}
      />
    </ErrorBoundary>
  )
}

const styles = StyleSheet.create({
  fallbackContainer: {
    padding: 4,
  },
  fallbackTitle: {
    fontSize: typography.sizes.sm,
    fontStyle: 'italic',
    marginBottom: 12,
  },
  fallbackBody: {
    fontSize: typography.sizes.base,
    fontFamily: typography.bodyFamily,
    lineHeight: typography.sizes.base * typography.lineHeights.relaxed,
  },
})
