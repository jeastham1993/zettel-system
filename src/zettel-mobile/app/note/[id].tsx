import {
  StyleSheet,
  Text,
  View,
  ScrollView,
  Pressable,
  ActivityIndicator,
} from 'react-native'
import { useLocalSearchParams, useRouter, Stack } from 'expo-router'
import { Pencil, ArrowLeft, Link, FileText } from 'lucide-react-native'
import { colors, themed } from '@/src/theme/colors'
import { typography } from '@/src/theme/typography'
import { useNote, useRelatedNotes, useBacklinks } from '@/src/hooks/use-notes'
import { useTheme } from '@/src/hooks/use-theme'
import { fullDate } from '@/src/lib/date'
import { TagBadge } from '@/src/components/TagBadge'
import { NoteContent } from '@/src/components/NoteContent'

export default function NoteDetailScreen() {
  const { id } = useLocalSearchParams<{ id: string }>()
  const router = useRouter()
  const scheme = useTheme()

  const noteQuery = useNote(id)
  const relatedQuery = useRelatedNotes(id)
  const backlinksQuery = useBacklinks(id)

  const note = noteQuery.data
  const related = relatedQuery.data ?? []
  const backlinks = backlinksQuery.data ?? []

  if (noteQuery.isLoading) {
    return (
      <View style={[styles.container, styles.center, { backgroundColor: themed(colors.background, scheme) }]}>
        <ActivityIndicator size="large" color={themed(colors.primary, scheme)} />
      </View>
    )
  }

  if (noteQuery.isError || !note) {
    return (
      <View style={[styles.container, styles.center, { backgroundColor: themed(colors.background, scheme) }]}>
        <FileText size={48} color={themed(colors.muted, scheme)} strokeWidth={1.5} />
        <Text style={[styles.errorText, { color: themed(colors.muted, scheme) }]}>
          Could not load this note.
        </Text>
      </View>
    )
  }

  return (
    <>
      <Stack.Screen
        options={{
          title: note.title,
          headerRight: () => (
            <Pressable
              onPress={() => router.push(`/note/${id}/edit`)}
              hitSlop={8}
              accessibilityLabel="Edit note"
              accessibilityRole="button"
            >
              <Pencil size={20} color={themed(colors.foreground, scheme)} />
            </Pressable>
          ),
        }}
      />

      <ScrollView
        style={[styles.container, { backgroundColor: themed(colors.background, scheme) }]}
        contentContainerStyle={styles.content}
      >
        {/* Title */}
        <Text style={[styles.title, { color: themed(colors.foreground, scheme) }]}>
          {note.title}
        </Text>

        {/* Created date */}
        <Text style={[styles.date, { color: themed(colors.muted, scheme) }]}>
          {fullDate(note.createdAt)}
        </Text>

        {/* Tags */}
        {note.tags.length > 0 && (
          <View style={styles.tags}>
            {note.tags.map((t) => (
              <TagBadge key={t.tag} tag={t.tag} scheme={scheme} />
            ))}
          </View>
        )}

        {/* Note content */}
        <View style={styles.contentBody}>
          <NoteContent html={note.content} scheme={scheme} />
        </View>

        {/* Divider */}
        {(related.length > 0 || backlinks.length > 0) && (
          <View style={[styles.divider, { backgroundColor: themed(colors.border, scheme) }]} />
        )}

        {/* Related Notes */}
        {related.length > 0 && (
          <View style={styles.section}>
            <Text style={[styles.sectionTitle, { color: themed(colors.foreground, scheme) }]}>
              Related Notes
            </Text>
            {related.map((r) => (
              <Pressable
                key={r.noteId}
                style={({ pressed }) => [
                  styles.relatedItem,
                  {
                    backgroundColor: pressed
                      ? themed(colors.card, scheme)
                      : 'transparent',
                  },
                ]}
                onPress={() => router.push(`/note/${r.noteId}`)}
              >
                <Link size={14} color={themed(colors.primary, scheme)} />
                <Text
                  style={[styles.relatedTitle, { color: themed(colors.foreground, scheme) }]}
                  numberOfLines={1}
                >
                  {r.title}
                </Text>
                <Text style={[styles.relevance, { color: themed(colors.muted, scheme) }]}>
                  {Math.round(r.rank * 100)}%
                </Text>
              </Pressable>
            ))}
          </View>
        )}

        {/* Backlinks */}
        {backlinks.length > 0 && (
          <View style={styles.section}>
            <Text style={[styles.sectionTitle, { color: themed(colors.foreground, scheme) }]}>
              Backlinks
            </Text>
            {backlinks.map((b) => (
              <Pressable
                key={b.id}
                style={({ pressed }) => [
                  styles.relatedItem,
                  {
                    backgroundColor: pressed
                      ? themed(colors.card, scheme)
                      : 'transparent',
                  },
                ]}
                onPress={() => router.push(`/note/${b.id}`)}
              >
                <ArrowLeft size={14} color={themed(colors.primary, scheme)} />
                <Text
                  style={[styles.relatedTitle, { color: themed(colors.foreground, scheme) }]}
                  numberOfLines={1}
                >
                  {b.title}
                </Text>
              </Pressable>
            ))}
          </View>
        )}
      </ScrollView>
    </>
  )
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
  },
  center: {
    alignItems: 'center',
    justifyContent: 'center',
  },
  content: {
    padding: 20,
    paddingBottom: 40,
  },
  title: {
    fontSize: typography.sizes['2xl'],
    fontFamily: typography.titleFamily,
    fontWeight: '700',
    lineHeight: typography.sizes['2xl'] * typography.lineHeights.tight,
  },
  date: {
    fontSize: typography.sizes.sm,
    marginTop: 6,
  },
  tags: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    marginTop: 12,
  },
  contentBody: {
    marginTop: 20,
  },
  errorText: {
    fontSize: typography.sizes.base,
    marginTop: 12,
    textAlign: 'center',
  },
  divider: {
    height: 1,
    marginVertical: 24,
  },
  section: {
    marginBottom: 20,
  },
  sectionTitle: {
    fontSize: typography.sizes.lg,
    fontWeight: '600',
    marginBottom: 10,
  },
  relatedItem: {
    flexDirection: 'row',
    alignItems: 'center',
    paddingVertical: 10,
    paddingHorizontal: 8,
    borderRadius: 6,
  },
  relatedTitle: {
    fontSize: typography.sizes.base,
    marginLeft: 10,
    flex: 1,
  },
  relevance: {
    fontSize: typography.sizes.sm,
    marginLeft: 8,
  },
})
