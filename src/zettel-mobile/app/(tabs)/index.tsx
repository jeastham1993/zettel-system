import { useCallback, useEffect, useRef, useState } from 'react'
import {
  StyleSheet,
  Text,
  View,
  FlatList,
  Pressable,
  ActivityIndicator,
  RefreshControl,
  ScrollView,
} from 'react-native'
import { useRouter } from 'expo-router'
import { ChevronDown, ChevronUp, FileText, Sparkles } from 'lucide-react-native'
import { colors, themed } from '@/src/theme/colors'
import { typography } from '@/src/theme/typography'
import { useNotes, useDiscoverNotes } from '@/src/hooks/use-notes'
import { useTheme } from '@/src/hooks/use-theme'
import { NoteCard } from '@/src/components/NoteCard'
import { EmptyState } from '@/src/components/EmptyState'
import type { Note } from '@/src/api/types'

const PAGE_SIZE = 20

export default function HomeScreen() {
  const router = useRouter()
  const scheme = useTheme()

  const [skip, setSkip] = useState(0)
  const [allNotes, setAllNotes] = useState<Note[]>([])
  const [totalCount, setTotalCount] = useState(0)
  const [isRefreshing, setIsRefreshing] = useState(false)
  const [discoveryOpen, setDiscoveryOpen] = useState(true)

  const notesQuery = useNotes(skip, PAGE_SIZE)
  const discoverQuery = useDiscoverNotes(4)

  // Track which skip value we last merged so we don't double-merge
  const lastMergedSkip = useRef(-1)

  useEffect(() => {
    if (!notesQuery.data || lastMergedSkip.current === skip) return
    lastMergedSkip.current = skip

    const { items, totalCount: tc } = notesQuery.data
    setTotalCount(tc)

    if (skip === 0) {
      setAllNotes(items)
    } else {
      setAllNotes((prev) => {
        const existingIds = new Set(prev.map((n) => n.id))
        const newItems = items.filter((n) => !existingIds.has(n.id))
        return [...prev, ...newItems]
      })
    }
  }, [notesQuery.data, skip])

  const hasMore = allNotes.length < totalCount

  const loadMore = useCallback(() => {
    if (!hasMore || notesQuery.isFetching) return
    setSkip(allNotes.length)
  }, [hasMore, notesQuery.isFetching, allNotes.length])

  const onRefresh = useCallback(async () => {
    setIsRefreshing(true)
    setSkip(0)
    lastMergedSkip.current = -1
    setAllNotes([])
    await notesQuery.refetch()
    setIsRefreshing(false)
  }, [notesQuery])

  const renderItem = useCallback(
    ({ item }: { item: Note }) => <NoteCard note={item} scheme={scheme} />,
    [scheme],
  )

  const renderHeader = () => (
    <View>
      <Text style={[styles.title, { color: themed(colors.foreground, scheme) }]}>
        Zettel
      </Text>
      <Text style={[styles.sectionHeader, { color: themed(colors.foreground, scheme) }]}>
        Recent Notes
      </Text>
    </View>
  )

  const renderDiscovery = () => {
    const items = discoverQuery.data
    if (!items || items.length === 0) return null

    return (
      <View style={[styles.discoverySection, { borderTopColor: themed(colors.border, scheme) }]}>
        <Pressable
          style={styles.discoveryHeader}
          onPress={() => setDiscoveryOpen((v) => !v)}
        >
          <View style={styles.discoveryTitleRow}>
            <Sparkles size={18} color={themed(colors.primary, scheme)} />
            <Text style={[styles.sectionHeader, { color: themed(colors.foreground, scheme), marginTop: 0, marginLeft: 8 }]}>
              Discover
            </Text>
          </View>
          {discoveryOpen ? (
            <ChevronUp size={20} color={themed(colors.muted, scheme)} />
          ) : (
            <ChevronDown size={20} color={themed(colors.muted, scheme)} />
          )}
        </Pressable>

        {discoveryOpen && (
          <ScrollView
            horizontal
            showsHorizontalScrollIndicator={false}
            contentContainerStyle={styles.discoveryScroll}
          >
            {items.map((item) => (
              <Pressable
                key={item.noteId}
                style={({ pressed }) => [
                  styles.discoveryCard,
                  {
                    backgroundColor: themed(colors.card, scheme),
                    borderColor: themed(colors.border, scheme),
                    opacity: pressed ? 0.8 : 1,
                  },
                ]}
                onPress={() => router.push(`/note/${item.noteId}`)}
              >
                <Text
                  style={[styles.discoveryTitle, { color: themed(colors.foreground, scheme) }]}
                  numberOfLines={2}
                >
                  {item.title}
                </Text>
                <Text
                  style={[styles.discoverySnippet, { color: themed(colors.muted, scheme) }]}
                  numberOfLines={3}
                >
                  {item.snippet}
                </Text>
              </Pressable>
            ))}
          </ScrollView>
        )}
      </View>
    )
  }

  const renderFooter = () => {
    if (notesQuery.isFetching && skip > 0) {
      return (
        <View style={styles.footerLoader}>
          <ActivityIndicator color={themed(colors.primary, scheme)} />
        </View>
      )
    }
    return renderDiscovery()
  }

  const renderEmpty = () => {
    if (notesQuery.isLoading) {
      return (
        <View style={styles.centerLoader}>
          <ActivityIndicator size="large" color={themed(colors.primary, scheme)} />
        </View>
      )
    }
    if (notesQuery.isError) {
      return (
        <EmptyState
          icon={FileText}
          title="Could not load notes"
          description="Check your server connection and try pulling to refresh."
          scheme={scheme}
        />
      )
    }
    return (
      <EmptyState
        icon={FileText}
        title="No notes yet"
        description="Capture your first thought using the + button below."
        scheme={scheme}
      />
    )
  }

  return (
    <View style={[styles.container, { backgroundColor: themed(colors.background, scheme) }]}>
      <FlatList
        data={allNotes}
        keyExtractor={(item) => item.id}
        renderItem={renderItem}
        ListHeaderComponent={renderHeader}
        ListFooterComponent={renderFooter}
        ListEmptyComponent={renderEmpty}
        contentContainerStyle={styles.listContent}
        refreshControl={
          <RefreshControl
            refreshing={isRefreshing}
            onRefresh={onRefresh}
            tintColor={themed(colors.primary, scheme)}
          />
        }
        onEndReached={loadMore}
        onEndReachedThreshold={0.5}
      />
    </View>
  )
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
  },
  listContent: {
    padding: 20,
    paddingBottom: 80,
    flexGrow: 1,
  },
  title: {
    fontSize: typography.sizes['3xl'],
    fontFamily: typography.titleFamily,
    fontWeight: '700',
    marginTop: 12,
  },
  sectionHeader: {
    fontSize: typography.sizes.lg,
    fontWeight: '600',
    marginTop: 20,
    marginBottom: 12,
  },
  centerLoader: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'center',
    paddingVertical: 60,
  },
  footerLoader: {
    paddingVertical: 20,
    alignItems: 'center',
  },
  discoverySection: {
    borderTopWidth: 1,
    marginTop: 20,
    paddingTop: 12,
  },
  discoveryHeader: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    marginBottom: 12,
  },
  discoveryTitleRow: {
    flexDirection: 'row',
    alignItems: 'center',
  },
  discoveryScroll: {
    paddingBottom: 4,
  },
  discoveryCard: {
    width: 160,
    borderRadius: 10,
    borderWidth: 1,
    padding: 12,
    marginRight: 10,
  },
  discoveryTitle: {
    fontSize: typography.sizes.sm,
    fontWeight: '600',
    fontFamily: typography.titleFamily,
    marginBottom: 4,
  },
  discoverySnippet: {
    fontSize: typography.sizes.xs,
    lineHeight: typography.sizes.xs * typography.lineHeights.normal,
  },
})
