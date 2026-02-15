import React, { useCallback } from 'react'
import {
  StyleSheet,
  Text,
  View,
  FlatList,
  ActivityIndicator,
  Alert,
  TouchableOpacity,
} from 'react-native'
import { useRouter } from 'expo-router'
import { Inbox as InboxIcon, Plus } from 'lucide-react-native'
import { useQueryClient } from '@tanstack/react-query'
import * as Haptics from 'expo-haptics'
import { colors, themed } from '@/src/theme/colors'
import { typography } from '@/src/theme/typography'
import { useTheme } from '@/src/hooks/use-theme'
import { useInbox, useInboxCount, usePromoteNote } from '@/src/hooks/use-inbox'
import { useDeleteNote } from '@/src/hooks/use-notes'
import { queryKeys } from '@/src/hooks/query-keys'
import { InboxItem } from '@/src/components/InboxItem'
import type { Note } from '@/src/api/types'

export default function InboxScreen() {
  const scheme = useTheme()
  const router = useRouter()
  const queryClient = useQueryClient()
  const { data: notes, isLoading, refetch, isRefetching } = useInbox()
  const { data: countData } = useInboxCount()
  const promoteMutation = usePromoteNote()
  const deleteMutation = useDeleteNote()

  const handlePromote = useCallback(
    (id: string) => {
      // Snapshot previous data before optimistic update
      const previousInbox = queryClient.getQueryData<Note[]>(queryKeys.inbox.list())
      const previousCount = queryClient.getQueryData<{ count: number }>(queryKeys.inbox.count())

      // Optimistic update: remove from list immediately
      queryClient.setQueryData<Note[]>(queryKeys.inbox.list(), (old) =>
        old ? old.filter((n) => n.id !== id) : old,
      )
      queryClient.setQueryData<{ count: number }>(queryKeys.inbox.count(), (old) =>
        old ? { count: Math.max(0, old.count - 1) } : old,
      )

      promoteMutation.mutate(id, {
        onError: () => {
          // Restore from snapshot — works even when offline
          queryClient.setQueryData(queryKeys.inbox.list(), previousInbox)
          queryClient.setQueryData(queryKeys.inbox.count(), previousCount)
          Alert.alert('Error', 'Failed to promote note. Please try again.')
        },
        onSettled: () => {
          // Always refetch to ensure server/client sync
          queryClient.invalidateQueries({ queryKey: queryKeys.inbox.all })
        },
      })
    },
    [promoteMutation, queryClient],
  )

  const handleDelete = useCallback(
    (id: string) => {
      Alert.alert('Delete Note', 'Are you sure you want to delete this fleeting note?', [
        { text: 'Cancel', style: 'cancel' },
        {
          text: 'Delete',
          style: 'destructive',
          onPress: () => {
            Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success)

            // Snapshot previous data before optimistic update
            const previousInbox = queryClient.getQueryData<Note[]>(queryKeys.inbox.list())
            const previousCount = queryClient.getQueryData<{ count: number }>(
              queryKeys.inbox.count(),
            )

            // Optimistic update
            queryClient.setQueryData<Note[]>(queryKeys.inbox.list(), (old) =>
              old ? old.filter((n) => n.id !== id) : old,
            )
            queryClient.setQueryData<{ count: number }>(queryKeys.inbox.count(), (old) =>
              old ? { count: Math.max(0, old.count - 1) } : old,
            )

            deleteMutation.mutate(id, {
              onError: () => {
                // Restore from snapshot — works even when offline
                queryClient.setQueryData(queryKeys.inbox.list(), previousInbox)
                queryClient.setQueryData(queryKeys.inbox.count(), previousCount)
                Alert.alert('Error', 'Failed to delete note. Please try again.')
              },
              onSettled: () => {
                // Always refetch to ensure server/client sync
                queryClient.invalidateQueries({ queryKey: queryKeys.inbox.all })
              },
            })
          },
        },
      ])
    },
    [deleteMutation, queryClient],
  )

  const handlePress = useCallback(
    (id: string) => {
      router.push(`/note/${id}`)
    },
    [router],
  )

  const renderItem = useCallback(
    ({ item }: { item: Note }) => (
      <InboxItem
        note={item}
        scheme={scheme}
        onPromote={handlePromote}
        onDelete={handleDelete}
        onPress={handlePress}
      />
    ),
    [scheme, handlePromote, handleDelete, handlePress],
  )

  const headerCount = countData?.count ?? notes?.length ?? 0

  if (isLoading) {
    return (
      <View
        style={[styles.centered, { backgroundColor: themed(colors.background, scheme) }]}
      >
        <ActivityIndicator size="large" color={themed(colors.primary, scheme)} />
      </View>
    )
  }

  if (!notes || notes.length === 0) {
    return (
      <View
        style={[styles.centered, { backgroundColor: themed(colors.background, scheme) }]}
      >
        <InboxIcon size={48} color={themed(colors.muted, scheme)} style={styles.emptyIcon} />
        <Text style={[styles.emptyTitle, { color: themed(colors.foreground, scheme) }]}>
          All caught up!
        </Text>
        <Text style={[styles.emptySubtitle, { color: themed(colors.muted, scheme) }]}>
          No fleeting notes in your inbox.
        </Text>
        <TouchableOpacity
          style={[styles.captureButton, { backgroundColor: themed(colors.primary, scheme) }]}
          onPress={() => router.push('/capture')}
          activeOpacity={0.8}
        >
          <Plus size={18} color="#fff" />
          <Text style={styles.captureButtonText}>Capture a thought</Text>
        </TouchableOpacity>
      </View>
    )
  }

  return (
    <View style={[styles.container, { backgroundColor: themed(colors.background, scheme) }]}>
      <View style={[styles.headerBar, { borderBottomColor: themed(colors.border, scheme) }]}>
        <Text style={[styles.headerTitle, { color: themed(colors.foreground, scheme) }]}>
          Inbox
        </Text>
        <View style={[styles.countBadge, { backgroundColor: themed(colors.primary, scheme) }]}>
          <Text style={styles.countText}>{headerCount}</Text>
        </View>
      </View>
      <FlatList
        data={notes}
        keyExtractor={(item) => item.id}
        renderItem={renderItem}
        refreshing={isRefetching}
        onRefresh={refetch}
        contentContainerStyle={styles.list}
      />
    </View>
  )
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
  },
  centered: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'center',
    padding: 24,
  },
  headerBar: {
    flexDirection: 'row',
    alignItems: 'center',
    paddingHorizontal: 20,
    paddingVertical: 12,
    borderBottomWidth: StyleSheet.hairlineWidth,
    gap: 8,
  },
  headerTitle: {
    fontSize: typography.sizes['2xl'],
    fontWeight: '700',
  },
  countBadge: {
    minWidth: 24,
    height: 24,
    borderRadius: 12,
    alignItems: 'center',
    justifyContent: 'center',
    paddingHorizontal: 8,
  },
  countText: {
    color: '#fff',
    fontSize: typography.sizes.xs,
    fontWeight: '700',
  },
  list: {
    flexGrow: 1,
  },
  emptyIcon: {
    marginBottom: 16,
  },
  emptyTitle: {
    fontSize: typography.sizes.xl,
    fontWeight: '600',
    marginBottom: 8,
  },
  emptySubtitle: {
    fontSize: typography.sizes.base,
    textAlign: 'center',
    marginBottom: 24,
  },
  captureButton: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 8,
    paddingVertical: 12,
    paddingHorizontal: 20,
    borderRadius: 24,
  },
  captureButtonText: {
    color: '#fff',
    fontSize: typography.sizes.base,
    fontWeight: '600',
  },
})
