import React, { useState, useRef, useEffect } from 'react'
import {
  StyleSheet,
  Text,
  View,
  TextInput,
  TouchableOpacity,
  KeyboardAvoidingView,
  Platform,
  Alert,
} from 'react-native'
import { useRouter } from 'expo-router'
import * as Haptics from 'expo-haptics'
import { X, Plus } from 'lucide-react-native'
import { colors, themed } from '@/src/theme/colors'
import { typography } from '@/src/theme/typography'
import { useTheme } from '@/src/hooks/use-theme'
import { serverStore } from '@/src/stores/server-store'
import { useOfflineQueue } from '@/src/hooks/use-offline-queue'
import { useCaptureNote } from '@/src/hooks/use-inbox'

export default function CaptureScreen() {
  const router = useRouter()
  const scheme = useTheme()
  const { enqueue } = useOfflineQueue()
  const captureNote = useCaptureNote()

  const [content, setContent] = useState('')
  const [tags, setTags] = useState<string[]>([])
  const [tagInput, setTagInput] = useState('')
  const [showTagInput, setShowTagInput] = useState(false)
  const [isSaving, setIsSaving] = useState(false)
  const [toast, setToast] = useState<string | null>(null)

  const contentRef = useRef<TextInput>(null)
  const navTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)

  // Auto-focus the content input when modal opens
  useEffect(() => {
    const timer = setTimeout(() => {
      contentRef.current?.focus()
    }, 300)
    return () => clearTimeout(timer)
  }, [])

  // Auto-dismiss toast
  useEffect(() => {
    if (toast) {
      const timer = setTimeout(() => setToast(null), 2000)
      return () => clearTimeout(timer)
    }
  }, [toast])

  // Clean up navigation timer on unmount
  useEffect(() => {
    return () => {
      if (navTimerRef.current) clearTimeout(navTimerRef.current)
    }
  }, [])

  const handleAddTag = () => {
    const trimmed = tagInput.trim().replace(/^#/, '')
    if (trimmed && !tags.includes(trimmed)) {
      setTags([...tags, trimmed])
    }
    setTagInput('')
    setShowTagInput(false)
  }

  const handleRemoveTag = (tag: string) => {
    setTags(tags.filter((t) => t !== tag))
  }

  const handleCapture = async () => {
    const trimmed = content.trim()
    if (!trimmed) return

    setIsSaving(true)

    const isOnline = serverStore.getConnectionState() === 'connected'

    if (!isOnline) {
      // Save to offline queue
      enqueue(trimmed, tags)
      await Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success)
      setToast('Saved offline')
      // Small delay so user sees the toast
      navTimerRef.current = setTimeout(() => router.back(), 600)
      setIsSaving(false)
      return
    }

    try {
      await captureNote.mutateAsync({ content: trimmed, tags })
      await Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success)
      router.back()
    } catch (err) {
      console.warn('Online capture failed, falling back to offline queue:', err)
      // Network failed mid-request -- fall back to offline queue
      enqueue(trimmed, tags)
      await Haptics.notificationAsync(Haptics.NotificationFeedbackType.Warning)
      setToast('Saved offline')
      navTimerRef.current = setTimeout(() => router.back(), 600)
    } finally {
      setIsSaving(false)
    }
  }

  const handleCancel = () => {
    if (content.trim()) {
      Alert.alert('Discard capture?', 'Your unsaved thought will be lost.', [
        { text: 'Keep Editing', style: 'cancel' },
        { text: 'Discard', style: 'destructive', onPress: () => router.back() },
      ])
    } else {
      router.back()
    }
  }

  const canCapture = content.trim().length > 0 && !isSaving

  return (
    <KeyboardAvoidingView
      style={[styles.root, { backgroundColor: themed(colors.background, scheme) }]}
      behavior={Platform.OS === 'ios' ? 'padding' : 'height'}
      keyboardVerticalOffset={0}
    >
      {/* Header */}
      <View style={[styles.header, { borderBottomColor: themed(colors.border, scheme) }]}>
        <TouchableOpacity onPress={handleCancel} hitSlop={12}>
          <X size={24} color={themed(colors.muted, scheme)} />
        </TouchableOpacity>
        <Text style={[styles.headerTitle, { color: themed(colors.foreground, scheme) }]}>
          Quick Capture
        </Text>
        <TouchableOpacity
          onPress={handleCapture}
          disabled={!canCapture}
          style={[
            styles.captureButton,
            {
              backgroundColor: canCapture
                ? themed(colors.primary, scheme)
                : themed(colors.border, scheme),
            },
          ]}
        >
          <Text
            style={[
              styles.captureButtonText,
              {
                color: canCapture ? '#fff' : themed(colors.muted, scheme),
              },
            ]}
          >
            {isSaving ? 'Saving...' : 'Capture'}
          </Text>
        </TouchableOpacity>
      </View>

      {/* Content input */}
      <View style={styles.body}>
        <TextInput
          ref={contentRef}
          style={[
            styles.contentInput,
            {
              color: themed(colors.foreground, scheme),
            },
          ]}
          placeholder="Type your thought..."
          placeholderTextColor={themed(colors.muted, scheme)}
          value={content}
          onChangeText={setContent}
          multiline
          textAlignVertical="top"
          autoCapitalize="sentences"
          autoCorrect
        />

        {/* Tags section */}
        <View style={[styles.tagsSection, { borderTopColor: themed(colors.border, scheme) }]}>
          <View style={styles.tagsList}>
            {tags.map((tag) => (
              <TouchableOpacity
                key={tag}
                onPress={() => handleRemoveTag(tag)}
                style={[
                  styles.tagBadge,
                  { backgroundColor: themed(colors.primaryMuted, scheme) },
                ]}
              >
                <Text style={[styles.tagText, { color: themed(colors.primary, scheme) }]}>
                  #{tag}
                </Text>
                <X size={12} color={themed(colors.primary, scheme)} />
              </TouchableOpacity>
            ))}

            {showTagInput ? (
              <TextInput
                style={[
                  styles.tagInputField,
                  {
                    color: themed(colors.foreground, scheme),
                    borderColor: themed(colors.border, scheme),
                  },
                ]}
                placeholder="tag name"
                placeholderTextColor={themed(colors.muted, scheme)}
                value={tagInput}
                onChangeText={setTagInput}
                onSubmitEditing={handleAddTag}
                onBlur={handleAddTag}
                autoCapitalize="none"
                autoCorrect={false}
                autoFocus
                returnKeyType="done"
              />
            ) : (
              <TouchableOpacity
                onPress={() => setShowTagInput(true)}
                style={[
                  styles.addTagButton,
                  { borderColor: themed(colors.border, scheme) },
                ]}
              >
                <Plus size={14} color={themed(colors.muted, scheme)} />
                <Text style={[styles.addTagText, { color: themed(colors.muted, scheme) }]}>
                  Add tag
                </Text>
              </TouchableOpacity>
            )}
          </View>
        </View>
      </View>

      {/* Toast overlay */}
      {toast && (
        <View style={styles.toastContainer}>
          <View style={[styles.toast, { backgroundColor: themed(colors.foreground, scheme) }]}>
            <Text style={[styles.toastText, { color: themed(colors.background, scheme) }]}>
              {toast}
            </Text>
          </View>
        </View>
      )}
    </KeyboardAvoidingView>
  )
}

const styles = StyleSheet.create({
  root: {
    flex: 1,
  },
  header: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingHorizontal: 16,
    paddingTop: Platform.OS === 'ios' ? 56 : 16,
    paddingBottom: 12,
    borderBottomWidth: StyleSheet.hairlineWidth,
  },
  headerTitle: {
    fontSize: typography.sizes.lg,
    fontWeight: '600',
  },
  captureButton: {
    paddingHorizontal: 16,
    paddingVertical: 8,
    borderRadius: 8,
  },
  captureButtonText: {
    fontSize: typography.sizes.sm,
    fontWeight: '600',
  },
  body: {
    flex: 1,
    justifyContent: 'space-between',
  },
  contentInput: {
    flex: 1,
    padding: 16,
    fontSize: typography.sizes.base,
    lineHeight: typography.sizes.base * typography.lineHeights.relaxed,
  },
  tagsSection: {
    paddingHorizontal: 16,
    paddingVertical: 12,
    borderTopWidth: StyleSheet.hairlineWidth,
  },
  tagsList: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    alignItems: 'center',
    gap: 8,
  },
  tagBadge: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 4,
    paddingHorizontal: 10,
    paddingVertical: 4,
    borderRadius: 12,
  },
  tagText: {
    fontSize: typography.sizes.sm,
    fontWeight: '500',
  },
  addTagButton: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 4,
    paddingHorizontal: 10,
    paddingVertical: 4,
    borderRadius: 12,
    borderWidth: 1,
    borderStyle: 'dashed',
  },
  addTagText: {
    fontSize: typography.sizes.sm,
  },
  tagInputField: {
    fontSize: typography.sizes.sm,
    paddingHorizontal: 10,
    paddingVertical: 4,
    borderRadius: 12,
    borderWidth: 1,
    minWidth: 80,
  },
  toastContainer: {
    position: 'absolute',
    bottom: 40,
    left: 0,
    right: 0,
    alignItems: 'center',
  },
  toast: {
    paddingHorizontal: 20,
    paddingVertical: 10,
    borderRadius: 20,
  },
  toastText: {
    fontSize: typography.sizes.sm,
    fontWeight: '500',
  },
})
