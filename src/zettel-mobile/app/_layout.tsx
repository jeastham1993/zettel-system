import { useCallback, useEffect, useRef, useState } from 'react'
import { useColorScheme } from 'react-native'
import { DarkTheme, DefaultTheme, ThemeProvider } from '@react-navigation/native'
import { useFonts } from 'expo-font'
import { Stack, useRouter, useSegments } from 'expo-router'
import * as SplashScreen from 'expo-splash-screen'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { GestureHandlerRootView } from 'react-native-gesture-handler'
import { serverStore } from '@/src/stores/server-store'
import { preferences } from '@/src/stores/preferences'
import { colors } from '@/src/theme/colors'
import { useForegroundSync } from '@/src/hooks/use-foreground-sync'

export { ErrorBoundary } from 'expo-router'

export const unstable_settings = {
  initialRouteName: '(tabs)',
}

SplashScreen.preventAutoHideAsync()

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 1000 * 60, // 1 minute
      retry: 2,
    },
  },
})

function useThemeFromPreferences() {
  const systemScheme = useColorScheme()
  const themePref = preferences.getTheme()

  if (themePref === 'system') return systemScheme ?? 'light'
  return themePref
}

function NavigationGuard({ children }: { children: React.ReactNode }) {
  const segments = useSegments()
  const router = useRouter()
  const [isReady, setIsReady] = useState(false)
  const hasInitialized = useRef(false)

  // Mark ready once on first mount
  useEffect(() => {
    if (!hasInitialized.current) {
      hasInitialized.current = true
      setIsReady(true)
    }
  }, [])

  // Handle redirects based on server connection state
  useEffect(() => {
    if (!isReady) return

    const serverUrl = serverStore.getServerUrl()
    const isOnConnect = segments[0] === 'connect'

    if (!serverUrl && !isOnConnect) {
      router.replace('/connect')
    } else if (serverUrl && isOnConnect) {
      router.replace('/')
    }
  }, [segments, isReady, router])

  if (!isReady) return null
  return <>{children}</>
}

export default function RootLayout() {
  const [loaded, error] = useFonts({
    SpaceMono: require('../assets/fonts/SpaceMono-Regular.ttf'),
  })

  useEffect(() => {
    if (error) throw error
  }, [error])

  useEffect(() => {
    if (loaded) {
      SplashScreen.hideAsync()
    }
  }, [loaded])

  if (!loaded) return null

  return <RootLayoutNav />
}

function ForegroundSyncProvider({ children }: { children: React.ReactNode }) {
  const handleSyncResult = useCallback(
    (result: { synced: number; failed: number }) => {
      if (result.synced > 0 && result.failed === 0) {
        console.log(`Sync complete: ${result.synced} offline capture(s) synced.`)
      } else if (result.synced > 0 && result.failed > 0) {
        console.log(
          `Partial sync: ${result.synced} synced, ${result.failed} failed. Will retry next time.`,
        )
      }
    },
    [],
  )

  useForegroundSync(handleSyncResult)

  return <>{children}</>
}

function RootLayoutNav() {
  const colorScheme = useThemeFromPreferences()

  const navTheme = colorScheme === 'dark'
    ? {
        ...DarkTheme,
        colors: {
          ...DarkTheme.colors,
          primary: colors.primary.dark,
          background: colors.background.dark,
          card: colors.card.dark,
          border: colors.border.dark,
          text: colors.foreground.dark,
        },
      }
    : {
        ...DefaultTheme,
        colors: {
          ...DefaultTheme.colors,
          primary: colors.primary.light,
          background: colors.background.light,
          card: colors.card.light,
          border: colors.border.light,
          text: colors.foreground.light,
        },
      }

  return (
    <GestureHandlerRootView style={{ flex: 1 }}>
      <QueryClientProvider client={queryClient}>
        <ThemeProvider value={navTheme}>
          <ForegroundSyncProvider>
            <NavigationGuard>
              <Stack>
                <Stack.Screen name="(tabs)" options={{ headerShown: false }} />
                <Stack.Screen
                  name="connect"
                  options={{ headerShown: false, gestureEnabled: false }}
                />
                <Stack.Screen
                  name="capture"
                  options={{ presentation: 'modal', headerShown: false }}
                />
                <Stack.Screen
                  name="note/[id]"
                  options={{ title: 'Note' }}
                />
                <Stack.Screen
                  name="note/[id]/edit"
                  options={{ title: 'Edit Note', presentation: 'modal' }}
                />
              </Stack>
            </NavigationGuard>
          </ForegroundSyncProvider>
        </ThemeProvider>
      </QueryClientProvider>
    </GestureHandlerRootView>
  )
}
