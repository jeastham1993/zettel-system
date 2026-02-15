import React from 'react'
import { StyleSheet, Text, View, Pressable } from 'react-native'
import { useRouter } from 'expo-router'
import { colors, themed, type ColorScheme } from '../theme/colors'
import { typography } from '../theme/typography'
import { htmlToPlainText } from '../lib/markdown'
import { relativeDate } from '../lib/date'
import { TagBadge } from './TagBadge'
import type { Note } from '../api/types'

interface NoteCardProps {
  note: Note
  scheme: ColorScheme
}

export const NoteCard = React.memo(function NoteCard({ note, scheme }: NoteCardProps) {
  const router = useRouter()

  const snippet = htmlToPlainText(note.content).slice(0, 120)
  const age = relativeDate(note.createdAt)

  return (
    <Pressable
      style={({ pressed }) => [
        styles.card,
        {
          backgroundColor: themed(colors.card, scheme),
          borderColor: themed(colors.border, scheme),
          opacity: pressed ? 0.8 : 1,
        },
      ]}
      onPress={() => router.push(`/note/${note.id}`)}
      accessibilityLabel={`Open note: ${note.title}`}
      accessibilityRole="button"
    >
      <View style={styles.header}>
        <Text
          style={[styles.title, { color: themed(colors.foreground, scheme) }]}
          numberOfLines={1}
        >
          {note.title}
        </Text>
        <Text style={[styles.age, { color: themed(colors.muted, scheme) }]}>
          {age}
        </Text>
      </View>

      {snippet.length > 0 && (
        <Text
          style={[styles.snippet, { color: themed(colors.muted, scheme) }]}
          numberOfLines={2}
        >
          {snippet}
        </Text>
      )}

      {note.tags.length > 0 && (
        <View style={styles.tags}>
          {note.tags.map((t) => (
            <TagBadge key={t.tag} tag={t.tag} scheme={scheme} />
          ))}
        </View>
      )}
    </Pressable>
  )
})

const styles = StyleSheet.create({
  card: {
    borderRadius: 10,
    borderWidth: 1,
    padding: 14,
    marginBottom: 10,
  },
  header: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
  title: {
    fontSize: typography.sizes.base,
    fontWeight: '600',
    fontFamily: typography.titleFamily,
    flex: 1,
    marginRight: 8,
  },
  age: {
    fontSize: typography.sizes.xs,
  },
  snippet: {
    fontSize: typography.sizes.sm,
    marginTop: 6,
    lineHeight: typography.sizes.sm * typography.lineHeights.normal,
  },
  tags: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    marginTop: 8,
  },
})
