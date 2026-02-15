import { StyleSheet, Text, View } from 'react-native'
import { colors, themed, type ColorScheme } from '../theme/colors'
import { typography } from '../theme/typography'

interface TagBadgeProps {
  tag: string
  scheme: ColorScheme
}

export function TagBadge({ tag, scheme }: TagBadgeProps) {
  return (
    <View
      style={[
        styles.badge,
        { backgroundColor: themed(colors.primaryMuted, scheme) },
      ]}
    >
      <Text
        style={[styles.text, { color: themed(colors.primary, scheme) }]}
        numberOfLines={1}
      >
        {tag}
      </Text>
    </View>
  )
}

const styles = StyleSheet.create({
  badge: {
    paddingHorizontal: 8,
    paddingVertical: 3,
    borderRadius: 12,
    marginRight: 6,
    marginBottom: 4,
  },
  text: {
    fontSize: typography.sizes.xs,
    fontWeight: '600',
  },
})
