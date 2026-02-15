import React, { useRef } from 'react'
import {
  StyleSheet,
  Text,
  View,
  TouchableOpacity,
  Animated,
} from 'react-native'
import { Swipeable } from 'react-native-gesture-handler'
import * as Haptics from 'expo-haptics'
import { ArrowUpRight, Trash2 } from 'lucide-react-native'
import { colors, themed } from '@/src/theme/colors'
import { typography } from '@/src/theme/typography'
import { relativeDate, truncateContent } from '@/src/lib/date'
import type { Note } from '@/src/api/types'
import type { ColorScheme } from '@/src/theme/colors'

const DAY_MS = 24 * 60 * 60 * 1000

function getAgeDotColor(createdAt: string): string {
  const age = Date.now() - new Date(createdAt).getTime()
  if (age < DAY_MS) return colors.ageFresh
  if (age < 3 * DAY_MS) return colors.ageModerate
  return colors.ageStale
}

interface InboxItemProps {
  note: Note
  scheme: ColorScheme
  onPromote: (id: string) => void
  onDelete: (id: string) => void
  onPress: (id: string) => void
}

function renderRightActions(
  _progress: Animated.AnimatedInterpolation<number>,
  dragX: Animated.AnimatedInterpolation<number>,
) {
  const scale = dragX.interpolate({
    inputRange: [-80, 0],
    outputRange: [1, 0.5],
    extrapolate: 'clamp',
  })

  return (
    <View style={styles.deleteAction}>
      <Animated.View style={{ transform: [{ scale }], alignItems: 'center' }}>
        <Trash2 size={20} color="#fff" />
        <Text style={styles.actionText}>Delete</Text>
      </Animated.View>
    </View>
  )
}

function renderLeftActions(
  _progress: Animated.AnimatedInterpolation<number>,
  dragX: Animated.AnimatedInterpolation<number>,
) {
  const scale = dragX.interpolate({
    inputRange: [0, 80],
    outputRange: [0.5, 1],
    extrapolate: 'clamp',
  })

  return (
    <View style={styles.promoteAction}>
      <Animated.View style={{ transform: [{ scale }], alignItems: 'center' }}>
        <ArrowUpRight size={20} color="#fff" />
        <Text style={styles.actionText}>Promote</Text>
      </Animated.View>
    </View>
  )
}

export function InboxItem({ note, scheme, onPromote, onDelete, onPress }: InboxItemProps) {
  const swipeableRef = useRef<Swipeable>(null)
  const ageDotColor = getAgeDotColor(note.createdAt)
  const preview = truncateContent(note.content)
  const sourceLabel = note.source ? `via: ${note.source}` : null

  const handleSwipeRight = () => {
    Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success)
    swipeableRef.current?.close()
    onPromote(note.id)
  }

  const handleSwipeLeft = () => {
    Haptics.notificationAsync(Haptics.NotificationFeedbackType.Warning)
    swipeableRef.current?.close()
    onDelete(note.id)
  }

  const handleSwipeableWillOpen = (direction: 'left' | 'right') => {
    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium)
    if (direction === 'right') {
      // Swiped right -> promote action (left actions rendered)
    } else {
      // Swiped left -> delete action (right actions rendered)
    }
  }

  return (
    <Swipeable
      ref={swipeableRef}
      renderLeftActions={renderLeftActions}
      renderRightActions={renderRightActions}
      onSwipeableOpen={(direction) => {
        if (direction === 'left') handleSwipeRight()
        else handleSwipeLeft()
      }}
      onSwipeableWillOpen={handleSwipeableWillOpen}
      overshootLeft={false}
      overshootRight={false}
    >
      <TouchableOpacity
        activeOpacity={0.7}
        onPress={() => onPress(note.id)}
        style={[
          styles.container,
          {
            backgroundColor: themed(colors.card, scheme),
            borderColor: themed(colors.border, scheme),
          },
        ]}
      >
        <View style={styles.header}>
          <View style={styles.headerLeft}>
            <View style={[styles.ageDot, { backgroundColor: ageDotColor }]} />
            <Text
              style={[styles.preview, { color: themed(colors.foreground, scheme) }]}
              numberOfLines={2}
            >
              {preview}
            </Text>
          </View>
          <TouchableOpacity
            onPress={() => {
              Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success)
              onPromote(note.id)
            }}
            hitSlop={{ top: 8, bottom: 8, left: 8, right: 8 }}
            style={styles.promoteButton}
            accessibilityLabel="Promote this note"
            accessibilityRole="button"
          >
            <ArrowUpRight size={16} color={colors.success} />
            <Text style={[styles.promoteLabel, { color: colors.success }]}>Promote</Text>
          </TouchableOpacity>
        </View>

        <View style={styles.footer}>
          {sourceLabel && (
            <View
              style={[
                styles.sourceBadge,
                { backgroundColor: themed(colors.primaryMuted, scheme) },
              ]}
            >
              <Text
                style={[styles.sourceText, { color: themed(colors.primary, scheme) }]}
              >
                {sourceLabel}
              </Text>
            </View>
          )}
          <Text style={[styles.age, { color: themed(colors.muted, scheme) }]}>
            {relativeDate(note.createdAt)}
          </Text>
        </View>
      </TouchableOpacity>
    </Swipeable>
  )
}

const styles = StyleSheet.create({
  container: {
    padding: 16,
    borderBottomWidth: StyleSheet.hairlineWidth,
  },
  header: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'flex-start',
    gap: 12,
  },
  headerLeft: {
    flex: 1,
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: 10,
  },
  ageDot: {
    width: 10,
    height: 10,
    borderRadius: 5,
    marginTop: 5,
    flexShrink: 0,
  },
  preview: {
    flex: 1,
    fontSize: typography.sizes.base,
    lineHeight: typography.sizes.base * typography.lineHeights.normal,
  },
  promoteButton: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 4,
    paddingVertical: 4,
    paddingHorizontal: 8,
  },
  promoteLabel: {
    fontSize: typography.sizes.sm,
    fontWeight: '600',
  },
  footer: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 8,
    marginTop: 8,
    marginLeft: 20,
  },
  sourceBadge: {
    paddingHorizontal: 8,
    paddingVertical: 2,
    borderRadius: 4,
  },
  sourceText: {
    fontSize: typography.sizes.xs,
    fontWeight: '500',
  },
  age: {
    fontSize: typography.sizes.xs,
  },
  promoteAction: {
    backgroundColor: '#16a34a',
    justifyContent: 'center',
    alignItems: 'center',
    width: 80,
  },
  deleteAction: {
    backgroundColor: '#dc2626',
    justifyContent: 'center',
    alignItems: 'center',
    width: 80,
  },
  actionText: {
    color: '#fff',
    fontSize: typography.sizes.xs,
    fontWeight: '600',
    marginTop: 4,
  },
})
