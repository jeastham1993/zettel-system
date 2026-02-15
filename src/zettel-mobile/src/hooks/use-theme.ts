import { useColorScheme } from 'react-native'
import { preferences } from '../stores/preferences'
import type { ColorScheme } from '../theme/colors'

export function useTheme(): ColorScheme {
  const systemScheme = useColorScheme()
  const pref = preferences.getTheme()
  if (pref === 'system') return systemScheme ?? 'light'
  return pref
}
