import { StyleSheet, TextInput, View } from 'react-native'
import { Search } from 'lucide-react-native'
import { colors, themed, type ColorScheme } from '../theme/colors'
import { typography } from '../theme/typography'

interface SearchBarProps {
  value: string
  onChange: (text: string) => void
  scheme: ColorScheme
}

export function SearchBar({ value, onChange, scheme }: SearchBarProps) {
  return (
    <View
      style={[
        styles.container,
        {
          backgroundColor: themed(colors.card, scheme),
          borderColor: themed(colors.border, scheme),
        },
      ]}
    >
      <Search size={20} color={themed(colors.muted, scheme)} />
      <TextInput
        style={[
          styles.input,
          { color: themed(colors.foreground, scheme) },
        ]}
        placeholder="Search your notes..."
        placeholderTextColor={themed(colors.muted, scheme)}
        value={value}
        onChangeText={onChange}
        autoCapitalize="none"
        autoCorrect={false}
        returnKeyType="search"
        accessibilityLabel="Search notes"
      />
    </View>
  )
}

const styles = StyleSheet.create({
  container: {
    flexDirection: 'row',
    alignItems: 'center',
    borderWidth: 1,
    borderRadius: 10,
    paddingHorizontal: 12,
    paddingVertical: 10,
    gap: 10,
  },
  input: {
    flex: 1,
    fontSize: typography.sizes.base,
    padding: 0,
  },
})
