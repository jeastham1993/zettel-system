import React from 'react'
import { StyleSheet, View, Text } from 'react-native'
import { colors, themed } from '@/src/theme/colors'
import { typography } from '@/src/theme/typography'
import { useTheme } from '@/src/hooks/use-theme'
import { useServer } from '@/src/hooks/use-server'

export function ConnectionStatus() {
  const scheme = useTheme()
  const { connectionState } = useServer()

  const isConnected = connectionState === 'connected'
  const isChecking = connectionState === 'checking'
  const dotColor = isConnected
    ? colors.success
    : isChecking
      ? colors.warning
      : colors.destructive
  const label = isConnected ? 'Online' : isChecking ? 'Checking...' : 'Offline'

  return (
    <View
      style={styles.container}
      accessibilityLabel={`Server status: ${label}`}
    >
      <View style={[styles.dot, { backgroundColor: dotColor }]} />
      <Text style={[styles.label, { color: themed(colors.muted, scheme) }]}>
        {label}
      </Text>
    </View>
  )
}

const styles = StyleSheet.create({
  container: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 6,
  },
  dot: {
    width: 8,
    height: 8,
    borderRadius: 4,
  },
  label: {
    fontSize: typography.sizes.xs,
  },
})
