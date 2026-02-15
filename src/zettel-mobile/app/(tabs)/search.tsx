import { useState } from 'react'
import {
  ActivityIndicator,
  FlatList,
  Pressable,
  StyleSheet,
  Text,
  View,
} from 'react-native'
import { useRouter } from 'expo-router'
import { Search as SearchIcon } from 'lucide-react-native'
import { colors, themed } from '@/src/theme/colors'
import { typography } from '@/src/theme/typography'
import { useTheme } from '@/src/hooks/use-theme'
import { preferences } from '@/src/stores/preferences'
import { useSearch } from '@/src/hooks/use-search'
import { SearchBar } from '@/src/components/SearchBar'
import type { SearchResult, SearchType } from '@/src/api/types'

const SEARCH_TYPES: { label: string; value: SearchType }[] = [
  { label: 'Hybrid', value: 'hybrid' },
  { label: 'Full-text', value: 'fulltext' },
  { label: 'Semantic', value: 'semantic' },
]

function formatRank(rank: number): string {
  if (rank >= 0 && rank <= 1) {
    return `${Math.round(rank * 100)}%`
  }
  return rank.toFixed(1)
}

export default function SearchScreen() {
  const router = useRouter()
  const scheme = useTheme()

  const [query, setQuery] = useState('')
  const [searchType, setSearchType] = useState<SearchType>(
    preferences.getDefaultSearchType(),
  )

  const { data: results, isLoading, isError, error, refetch } = useSearch(query, searchType)

  const hasQuery = query.trim().length > 0

  return (
    <View
      style={[
        styles.container,
        { backgroundColor: themed(colors.background, scheme) },
      ]}
    >
      <SearchBar value={query} onChange={setQuery} scheme={scheme} />

      {/* Search type segmented control */}
      <View
        style={[
          styles.segmentedControl,
          {
            backgroundColor: themed(colors.card, scheme),
            borderColor: themed(colors.border, scheme),
          },
        ]}
      >
        {SEARCH_TYPES.map(({ label, value }) => {
          const active = searchType === value
          return (
            <Pressable
              key={value}
              style={[
                styles.segment,
                active && {
                  backgroundColor: themed(colors.primary, scheme),
                },
              ]}
              onPress={() => setSearchType(value)}
              accessibilityRole="tab"
              accessibilityState={{ selected: active }}
            >
              <Text
                style={[
                  styles.segmentText,
                  {
                    color: active
                      ? '#fff'
                      : themed(colors.foreground, scheme),
                  },
                ]}
              >
                {label}
              </Text>
            </Pressable>
          )
        })}
      </View>

      {/* Content area */}
      {!hasQuery ? (
        <View style={styles.emptyState}>
          <SearchIcon
            size={48}
            color={themed(colors.muted, scheme)}
            style={styles.emptyIcon}
          />
          <Text
            style={[styles.emptyText, { color: themed(colors.muted, scheme) }]}
          >
            Search your notes
          </Text>
        </View>
      ) : isLoading ? (
        <View style={styles.emptyState}>
          <ActivityIndicator
            size="large"
            color={themed(colors.primary, scheme)}
          />
        </View>
      ) : isError ? (
        <View style={styles.emptyState}>
          <Text
            style={[
              styles.errorText,
              { color: colors.destructive },
            ]}
          >
            {error?.message ?? 'Something went wrong'}
          </Text>
          <Pressable
            style={[
              styles.retryButton,
              { backgroundColor: themed(colors.primary, scheme) },
            ]}
            onPress={() => refetch()}
          >
            <Text style={styles.retryButtonText}>Retry</Text>
          </Pressable>
        </View>
      ) : results && results.length === 0 ? (
        <View style={styles.emptyState}>
          <Text
            style={[styles.emptyText, { color: themed(colors.muted, scheme) }]}
          >
            No notes found
          </Text>
        </View>
      ) : (
        <FlatList<SearchResult>
          data={results}
          keyExtractor={(item) => item.noteId}
          contentContainerStyle={styles.listContent}
          renderItem={({ item }) => (
            <Pressable
              style={[
                styles.resultCard,
                {
                  backgroundColor: themed(colors.card, scheme),
                  borderColor: themed(colors.border, scheme),
                },
              ]}
              onPress={() => router.push(`/note/${item.noteId}`)}
            >
              <View style={styles.resultHeader}>
                <Text
                  style={[
                    styles.resultTitle,
                    { color: themed(colors.foreground, scheme) },
                  ]}
                  numberOfLines={1}
                >
                  {item.title}
                </Text>
                <Text
                  style={[
                    styles.resultRank,
                    { color: themed(colors.primary, scheme) },
                  ]}
                >
                  {formatRank(item.rank)}
                </Text>
              </View>
              <Text
                style={[
                  styles.resultSnippet,
                  { color: themed(colors.muted, scheme) },
                ]}
                numberOfLines={2}
              >
                {item.snippet}
              </Text>
            </Pressable>
          )}
        />
      )}
    </View>
  )
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    padding: 16,
  },
  segmentedControl: {
    flexDirection: 'row',
    borderWidth: 1,
    borderRadius: 8,
    marginTop: 12,
    overflow: 'hidden',
  },
  segment: {
    flex: 1,
    paddingVertical: 8,
    alignItems: 'center',
  },
  segmentText: {
    fontSize: typography.sizes.sm,
    fontWeight: '600',
  },
  emptyState: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'center',
  },
  emptyIcon: {
    marginBottom: 16,
  },
  emptyText: {
    fontSize: typography.sizes.base,
    textAlign: 'center',
  },
  errorText: {
    fontSize: typography.sizes.base,
    textAlign: 'center',
    marginBottom: 16,
  },
  retryButton: {
    paddingHorizontal: 24,
    paddingVertical: 10,
    borderRadius: 8,
  },
  retryButtonText: {
    color: '#fff',
    fontSize: typography.sizes.sm,
    fontWeight: '600',
  },
  listContent: {
    paddingTop: 12,
    paddingBottom: 24,
  },
  resultCard: {
    borderWidth: 1,
    borderRadius: 10,
    padding: 14,
    marginBottom: 10,
  },
  resultHeader: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: 6,
  },
  resultTitle: {
    fontSize: typography.sizes.base,
    fontFamily: typography.titleFamily,
    fontWeight: '600',
    flex: 1,
    marginRight: 8,
  },
  resultRank: {
    fontSize: typography.sizes.sm,
    fontWeight: '700',
  },
  resultSnippet: {
    fontSize: typography.sizes.sm,
    lineHeight: typography.sizes.sm * typography.lineHeights.normal,
  },
})
