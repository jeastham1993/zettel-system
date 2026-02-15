import { useCallback, useEffect, useRef, useState } from 'react'
import {
  StyleSheet,
  Text,
  TextInput,
  View,
  Pressable,
  ScrollView,
  Alert,
  KeyboardAvoidingView,
  Platform,
  ActivityIndicator,
} from 'react-native'
import { useLocalSearchParams, useRouter, Stack } from 'expo-router'
import { useNavigation } from '@react-navigation/native'
import { Eye, EyeOff } from 'lucide-react-native'
import { colors, themed } from '@/src/theme/colors'
import { typography } from '@/src/theme/typography'
import { useNote, useUpdateNote, useCreateNote } from '@/src/hooks/use-notes'
import { useTheme } from '@/src/hooks/use-theme'
import { htmlToMarkdown, markdownToHtml } from '@/src/lib/markdown'
import { NoteContent } from '@/src/components/NoteContent'
import { TagInput } from '@/src/components/TagInput'
import { draftStore } from '@/src/stores/draft-store'

export default function NoteEditScreen() {
  const { id } = useLocalSearchParams<{ id: string }>()
  const router = useRouter()
  const navigation = useNavigation()
  const scheme = useTheme()

  const isNew = id === 'new'
  const noteId = isNew ? 'new' : id!

  const noteQuery = useNote(isNew ? undefined : id)
  const updateNote = useUpdateNote()
  const createNote = useCreateNote()
  const note = noteQuery.data

  const [title, setTitle] = useState('')
  const [content, setContent] = useState('')
  const [tags, setTags] = useState<string[]>([])
  const [preview, setPreview] = useState(false)
  const [initialized, setInitialized] = useState(isNew)
  const [hasChanges, setHasChanges] = useState(false)

  // Refs for auto-save interval to avoid re-creating interval on every keystroke
  const titleRef = useRef(title)
  const contentRef = useRef(content)
  const tagsRef = useRef(tags)
  const hasChangesRef = useRef(hasChanges)
  titleRef.current = title
  contentRef.current = content
  tagsRef.current = tags
  hasChangesRef.current = hasChanges

  // Initialize form from note data (or restore draft)
  useEffect(() => {
    if (isNew || !note || initialized) return

    const draft = draftStore.getDraft(noteId)
    if (draft) {
      Alert.alert(
        'Restore draft?',
        'You have an unsaved draft from a previous session. Would you like to restore it?',
        [
          {
            text: 'Discard',
            style: 'destructive',
            onPress: () => {
              draftStore.clearDraft(noteId)
              setTitle(note.title)
              setContent(htmlToMarkdown(note.content))
              setTags(note.tags.map((t) => t.tag))
              setInitialized(true)
            },
          },
          {
            text: 'Restore',
            onPress: () => {
              setTitle(draft.title)
              setContent(draft.content)
              setTags(draft.tags)
              setHasChanges(true)
              setInitialized(true)
            },
          },
        ],
      )
    } else {
      setTitle(note.title)
      setContent(htmlToMarkdown(note.content))
      setTags(note.tags.map((t) => t.tag))
      setInitialized(true)
    }
  }, [note, initialized, noteId, isNew])

  // Auto-save draft every 5 seconds using refs to avoid interval churn
  useEffect(() => {
    if (!initialized) return

    const interval = setInterval(() => {
      if (hasChangesRef.current) {
        draftStore.saveDraft(noteId, {
          title: titleRef.current,
          content: contentRef.current,
          tags: tagsRef.current,
          savedAt: new Date().toISOString(),
        })
      }
    }, 5000)

    return () => {
      clearInterval(interval)
      if (hasChangesRef.current) {
        draftStore.saveDraft(noteId, {
          title: titleRef.current,
          content: contentRef.current,
          tags: tagsRef.current,
          savedAt: new Date().toISOString(),
        })
      }
    }
  }, [initialized, noteId])

  // Prevent back navigation when there are unsaved changes
  useEffect(() => {
    if (!initialized) return

    const unsubscribe = navigation.addListener('beforeRemove', (e) => {
      if (!hasChangesRef.current) return

      e.preventDefault()

      Alert.alert(
        'Discard changes?',
        'You have unsaved changes. Are you sure you want to discard them?',
        [
          { text: 'Keep Editing', style: 'cancel' },
          {
            text: 'Discard',
            style: 'destructive',
            onPress: () => {
              draftStore.clearDraft(noteId)
              navigation.dispatch(e.data.action)
            },
          },
        ],
      )
    })

    return unsubscribe
  }, [initialized, noteId, navigation])

  const markChanged = useCallback(() => setHasChanges(true), [])

  const handleTitleChange = useCallback(
    (text: string) => {
      setTitle(text)
      markChanged()
    },
    [markChanged],
  )

  const handleContentChange = useCallback(
    (text: string) => {
      setContent(text)
      markChanged()
    },
    [markChanged],
  )

  const handleTagsChange = useCallback(
    (newTags: string[]) => {
      setTags(newTags)
      markChanged()
    },
    [markChanged],
  )

  const isSaving = updateNote.isPending || createNote.isPending

  const handleSave = useCallback(async () => {
    if (isSaving) return

    const trimmedTitle = title.trim()
    const trimmedContent = content.trim()

    // Title is required for permanent notes; new notes are permanent
    if (!trimmedTitle && !isNew) {
      Alert.alert('Title required', 'Please enter a title for this note.')
      return
    }

    if (isNew && !trimmedTitle && !trimmedContent) {
      Alert.alert('Empty note', 'Please enter a title or content.')
      return
    }

    try {
      if (isNew) {
        await createNote.mutateAsync({
          title: trimmedTitle || 'Untitled',
          content: markdownToHtml(trimmedContent),
          tags,
          status: 'Permanent',
        })
      } else {
        await updateNote.mutateAsync({
          id: noteId,
          title: trimmedTitle,
          content: markdownToHtml(trimmedContent),
          tags,
        })
      }

      draftStore.clearDraft(noteId)
      setHasChanges(false)
      router.back()
    } catch {
      Alert.alert('Save failed', 'Could not save the note. Please try again.')
    }
  }, [isSaving, title, content, tags, isNew, noteId, createNote, updateNote, router])

  const handleCancel = useCallback(() => {
    if (hasChanges) {
      Alert.alert(
        'Discard changes?',
        'You have unsaved changes. Are you sure you want to discard them?',
        [
          { text: 'Keep Editing', style: 'cancel' },
          {
            text: 'Discard',
            style: 'destructive',
            onPress: () => {
              draftStore.clearDraft(noteId)
              router.back()
            },
          },
        ],
      )
    } else {
      router.back()
    }
  }, [hasChanges, noteId, router])

  const fg = themed(colors.foreground, scheme)
  const muted = themed(colors.muted, scheme)
  const primary = themed(colors.primary, scheme)
  const cardBg = themed(colors.card, scheme)
  const borderColor = themed(colors.border, scheme)
  const bgColor = themed(colors.background, scheme)

  if (!isNew && (noteQuery.isLoading || !initialized)) {
    return (
      <View style={[styles.container, styles.center, { backgroundColor: bgColor }]}>
        <ActivityIndicator size="large" color={primary} />
      </View>
    )
  }

  if (!isNew && noteQuery.isError) {
    return (
      <View style={[styles.container, styles.center, { backgroundColor: bgColor }]}>
        <Text style={[styles.errorText, { color: muted }]}>
          Could not load this note.
        </Text>
      </View>
    )
  }

  return (
    <>
      <Stack.Screen
        options={{
          title: isNew ? 'New Note' : 'Edit',
          headerLeft: () => (
            <Pressable onPress={handleCancel} hitSlop={8}>
              <Text style={[styles.headerButton, { color: muted }]}>Cancel</Text>
            </Pressable>
          ),
          headerRight: () => (
            <Pressable
              onPress={handleSave}
              disabled={isSaving}
              hitSlop={8}
            >
              {isSaving ? (
                <ActivityIndicator size="small" color={primary} />
              ) : (
                <Text style={[styles.headerButton, styles.saveBtn, { color: primary }]}>
                  Save
                </Text>
              )}
            </Pressable>
          ),
        }}
      />

      <KeyboardAvoidingView
        style={{ flex: 1 }}
        behavior={Platform.OS === 'ios' ? 'padding' : undefined}
        keyboardVerticalOffset={100}
      >
        <ScrollView
          style={[styles.container, { backgroundColor: bgColor }]}
          contentContainerStyle={styles.content}
          keyboardShouldPersistTaps="handled"
        >
          {/* Title input */}
          <TextInput
            style={[styles.titleInput, { color: fg, borderBottomColor: borderColor }]}
            value={title}
            onChangeText={handleTitleChange}
            placeholder="Note title"
            placeholderTextColor={muted}
            returnKeyType="next"
          />

          {/* Preview toggle */}
          <Pressable
            style={[styles.previewToggle, { borderColor }]}
            onPress={() => setPreview((v) => !v)}
          >
            {preview ? (
              <>
                <EyeOff size={16} color={muted} />
                <Text style={[styles.previewText, { color: muted }]}>Edit</Text>
              </>
            ) : (
              <>
                <Eye size={16} color={primary} />
                <Text style={[styles.previewText, { color: primary }]}>Preview</Text>
              </>
            )}
          </Pressable>

          {/* Content: editor or preview */}
          {preview ? (
            <View style={[styles.previewContainer, { borderColor, backgroundColor: cardBg }]}>
              {content.trim() ? (
                <NoteContent html={markdownToHtml(content)} scheme={scheme} />
              ) : (
                <Text style={[styles.previewPlaceholder, { color: muted }]}>
                  Nothing to preview.
                </Text>
              )}
            </View>
          ) : (
            <TextInput
              style={[
                styles.contentInput,
                {
                  color: fg,
                  backgroundColor: cardBg,
                  borderColor,
                },
              ]}
              value={content}
              onChangeText={handleContentChange}
              placeholder="Write your note in markdown..."
              placeholderTextColor={muted}
              multiline
              textAlignVertical="top"
            />
          )}

          {/* Tags */}
          <View style={styles.tagsSection}>
            <Text style={[styles.sectionLabel, { color: fg }]}>Tags</Text>
            <TagInput tags={tags} onChange={handleTagsChange} scheme={scheme} />
          </View>

          {/* Save error */}
          {(updateNote.isError || createNote.isError) && (
            <Text style={styles.mutationError}>
              Failed to save. Please try again.
            </Text>
          )}
        </ScrollView>
      </KeyboardAvoidingView>
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
  headerButton: {
    fontSize: typography.sizes.base,
  },
  saveBtn: {
    fontWeight: '600',
  },
  titleInput: {
    fontSize: typography.sizes.xl,
    fontFamily: typography.titleFamily,
    fontWeight: '700',
    borderBottomWidth: 1,
    paddingBottom: 12,
    marginBottom: 16,
  },
  previewToggle: {
    flexDirection: 'row',
    alignItems: 'center',
    alignSelf: 'flex-end',
    paddingHorizontal: 12,
    paddingVertical: 6,
    borderWidth: 1,
    borderRadius: 16,
    gap: 6,
    marginBottom: 12,
  },
  previewText: {
    fontSize: typography.sizes.sm,
    fontWeight: '500',
  },
  contentInput: {
    minHeight: 300,
    borderWidth: 1,
    borderRadius: 10,
    padding: 14,
    fontSize: typography.sizes.base,
    fontFamily: typography.monoFamily,
    lineHeight: typography.sizes.base * typography.lineHeights.relaxed,
  },
  previewContainer: {
    minHeight: 300,
    borderWidth: 1,
    borderRadius: 10,
    padding: 14,
  },
  previewPlaceholder: {
    fontSize: typography.sizes.base,
    fontStyle: 'italic',
    textAlign: 'center',
    marginTop: 40,
  },
  tagsSection: {
    marginTop: 20,
  },
  sectionLabel: {
    fontSize: typography.sizes.base,
    fontWeight: '600',
    marginBottom: 8,
  },
  errorText: {
    fontSize: typography.sizes.base,
    textAlign: 'center',
  },
  mutationError: {
    color: '#dc2626',
    fontSize: typography.sizes.sm,
    marginTop: 12,
    textAlign: 'center',
  },
})
