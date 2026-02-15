import { useState, useCallback } from 'react'
import {
  StyleSheet,
  Text,
  TextInput,
  View,
  Pressable,
  FlatList,
} from 'react-native'
import { X, Plus } from 'lucide-react-native'
import { useQuery } from '@tanstack/react-query'
import { searchTags } from '../api/tags'
import { useDebounce } from '../hooks/use-debounce'
import { colors, themed, type ColorScheme } from '../theme/colors'
import { typography } from '../theme/typography'

interface TagInputProps {
  tags: string[]
  onChange: (tags: string[]) => void
  scheme: ColorScheme
}

export function TagInput({ tags, onChange, scheme }: TagInputProps) {
  const [inputValue, setInputValue] = useState('')
  const [showInput, setShowInput] = useState(false)

  const debouncedQuery = useDebounce(inputValue, 300)

  const suggestionsQuery = useQuery({
    queryKey: ['tags', debouncedQuery],
    queryFn: () => searchTags(debouncedQuery),
    enabled: debouncedQuery.length >= 1,
  })

  const suggestions = (suggestionsQuery.data ?? []).filter(
    (s) => !tags.includes(s),
  )

  const addTag = useCallback(
    (tag: string) => {
      const normalized = tag.trim().toLowerCase()
      if (normalized && !tags.includes(normalized)) {
        onChange([...tags, normalized])
      }
      setInputValue('')
    },
    [tags, onChange],
  )

  const removeTag = useCallback(
    (tag: string) => {
      onChange(tags.filter((t) => t !== tag))
    },
    [tags, onChange],
  )

  const handleSubmit = useCallback(() => {
    if (inputValue.trim()) {
      addTag(inputValue)
    }
  }, [inputValue, addTag])

  const fg = themed(colors.foreground, scheme)
  const muted = themed(colors.muted, scheme)
  const primary = themed(colors.primary, scheme)
  const primaryMutedBg = themed(colors.primaryMuted, scheme)
  const cardBg = themed(colors.card, scheme)
  const borderColor = themed(colors.border, scheme)

  return (
    <View>
      {/* Existing tags */}
      <View style={styles.tagsRow}>
        {tags.map((tag) => (
          <View
            key={tag}
            style={[styles.tagChip, { backgroundColor: primaryMutedBg }]}
          >
            <Text style={[styles.tagText, { color: primary }]}>{tag}</Text>
            <Pressable onPress={() => removeTag(tag)} hitSlop={6}>
              <X size={14} color={primary} />
            </Pressable>
          </View>
        ))}
        {!showInput && (
          <Pressable
            style={[styles.addButton, { borderColor }]}
            onPress={() => setShowInput(true)}
          >
            <Plus size={14} color={muted} />
            <Text style={[styles.addText, { color: muted }]}>Add tag</Text>
          </Pressable>
        )}
      </View>

      {/* Tag input with autocomplete */}
      {showInput && (
        <View>
          <TextInput
            style={[
              styles.input,
              {
                color: fg,
                backgroundColor: cardBg,
                borderColor,
              },
            ]}
            value={inputValue}
            onChangeText={setInputValue}
            placeholder="Type a tag..."
            placeholderTextColor={muted}
            autoFocus
            autoCapitalize="none"
            autoCorrect={false}
            returnKeyType="done"
            onSubmitEditing={handleSubmit}
            onBlur={() => {
              if (!inputValue.trim()) setShowInput(false)
            }}
          />

          {suggestions.length > 0 && (
            <View style={[styles.suggestions, { backgroundColor: cardBg, borderColor }]}>
              <FlatList
                data={suggestions.slice(0, 6)}
                keyExtractor={(item) => item}
                keyboardShouldPersistTaps="handled"
                renderItem={({ item }) => (
                  <Pressable
                    style={({ pressed }) => [
                      styles.suggestionItem,
                      pressed && { backgroundColor: borderColor },
                    ]}
                    onPress={() => {
                      addTag(item)
                      setShowInput(false)
                    }}
                  >
                    <Text style={[styles.suggestionText, { color: fg }]}>
                      {item}
                    </Text>
                  </Pressable>
                )}
              />
            </View>
          )}
        </View>
      )}
    </View>
  )
}

const styles = StyleSheet.create({
  tagsRow: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    alignItems: 'center',
    gap: 6,
  },
  tagChip: {
    flexDirection: 'row',
    alignItems: 'center',
    paddingHorizontal: 10,
    paddingVertical: 5,
    borderRadius: 14,
    gap: 4,
  },
  tagText: {
    fontSize: typography.sizes.sm,
    fontWeight: '600',
  },
  addButton: {
    flexDirection: 'row',
    alignItems: 'center',
    paddingHorizontal: 10,
    paddingVertical: 5,
    borderRadius: 14,
    borderWidth: 1,
    borderStyle: 'dashed',
    gap: 4,
  },
  addText: {
    fontSize: typography.sizes.sm,
  },
  input: {
    marginTop: 8,
    borderWidth: 1,
    borderRadius: 8,
    paddingHorizontal: 12,
    paddingVertical: 10,
    fontSize: typography.sizes.base,
  },
  suggestions: {
    marginTop: 4,
    borderWidth: 1,
    borderRadius: 8,
    maxHeight: 200,
    overflow: 'hidden',
  },
  suggestionItem: {
    paddingHorizontal: 12,
    paddingVertical: 10,
  },
  suggestionText: {
    fontSize: typography.sizes.base,
  },
})
