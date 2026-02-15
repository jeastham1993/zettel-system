import { StyleSheet, Text, View } from 'react-native'
import type { LucideIcon } from 'lucide-react-native'
import { colors, themed, type ColorScheme } from '../theme/colors'
import { typography } from '../theme/typography'

interface EmptyStateProps {
  icon: LucideIcon
  title: string
  description: string
  scheme: ColorScheme
}

export function EmptyState({ icon: Icon, title, description, scheme }: EmptyStateProps) {
  return (
    <View style={styles.container}>
      <Icon size={48} color={themed(colors.muted, scheme)} strokeWidth={1.5} />
      <Text style={[styles.title, { color: themed(colors.foreground, scheme) }]}>
        {title}
      </Text>
      <Text style={[styles.description, { color: themed(colors.muted, scheme) }]}>
        {description}
      </Text>
    </View>
  )
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'center',
    paddingHorizontal: 32,
    paddingVertical: 60,
  },
  title: {
    fontSize: typography.sizes.lg,
    fontWeight: '600',
    marginTop: 16,
    textAlign: 'center',
  },
  description: {
    fontSize: typography.sizes.base,
    marginTop: 8,
    textAlign: 'center',
    lineHeight: typography.sizes.base * typography.lineHeights.relaxed,
  },
})
