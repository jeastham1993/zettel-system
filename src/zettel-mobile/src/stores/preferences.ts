import { createMMKV } from 'react-native-mmkv'
import type { SearchType } from '../api/types'

const storage = createMMKV({ id: 'preferences' })

const KEYS = {
  THEME: 'theme',
  DEFAULT_SEARCH_TYPE: 'default-search-type',
} as const

export type ThemePreference = 'system' | 'light' | 'dark'

export const preferences = {
  getTheme(): ThemePreference {
    return (storage.getString(KEYS.THEME) as ThemePreference) ?? 'system'
  },

  setTheme(theme: ThemePreference): void {
    storage.set(KEYS.THEME, theme)
  },

  getDefaultSearchType(): SearchType {
    return (storage.getString(KEYS.DEFAULT_SEARCH_TYPE) as SearchType) ?? 'hybrid'
  },

  setDefaultSearchType(type: SearchType): void {
    storage.set(KEYS.DEFAULT_SEARCH_TYPE, type)
  },
}
