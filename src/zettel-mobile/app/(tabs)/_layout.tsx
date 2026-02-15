import React from 'react'
import { StyleSheet, Text, TouchableOpacity, View } from 'react-native'
import { Tabs, useRouter } from 'expo-router'
import { Home, Search, Inbox, Settings, Plus } from 'lucide-react-native'
import { colors, themed } from '@/src/theme/colors'
import { typography } from '@/src/theme/typography'
import { useTheme } from '@/src/hooks/use-theme'
import { useInboxCount } from '@/src/hooks/use-inbox'

function InboxTabIcon({ color, size }: { color: string; size: number }) {
  const { data } = useInboxCount()
  const count = data?.count ?? 0

  return (
    <View>
      <Inbox size={size} color={color} />
      {count > 0 && (
        <View style={styles.badge}>
          <Text style={styles.badgeText}>{count > 99 ? '99+' : count}</Text>
        </View>
      )}
    </View>
  )
}

export default function TabLayout() {
  const scheme = useTheme()
  const router = useRouter()

  return (
    <View style={styles.wrapper}>
      <Tabs
        screenOptions={{
          tabBarActiveTintColor: themed(colors.primary, scheme),
          tabBarInactiveTintColor: themed(colors.muted, scheme),
          tabBarStyle: {
            backgroundColor: themed(colors.card, scheme),
            borderTopColor: themed(colors.border, scheme),
          },
          headerStyle: {
            backgroundColor: themed(colors.card, scheme),
          },
          headerTintColor: themed(colors.foreground, scheme),
          headerShadowVisible: false,
        }}
      >
        <Tabs.Screen
          name="index"
          options={{
            title: 'Home',
            tabBarIcon: ({ color, size }) => <Home size={size} color={color} />,
          }}
        />
        <Tabs.Screen
          name="search"
          options={{
            title: 'Search',
            tabBarIcon: ({ color, size }) => <Search size={size} color={color} />,
          }}
        />
        <Tabs.Screen
          name="inbox"
          options={{
            title: 'Inbox',
            tabBarIcon: ({ color, size }) => <InboxTabIcon color={color} size={size} />,
          }}
        />
        <Tabs.Screen
          name="settings"
          options={{
            title: 'Settings',
            tabBarIcon: ({ color, size }) => <Settings size={size} color={color} />,
          }}
        />
      </Tabs>

      {/* Floating Action Button -- visible on all tabs */}
      <TouchableOpacity
        style={[styles.fab, { backgroundColor: themed(colors.primary, scheme) }]}
        onPress={() => router.push('/capture')}
        activeOpacity={0.8}
        accessibilityLabel="Capture a new thought"
        accessibilityRole="button"
      >
        <Plus size={28} color="#fff" strokeWidth={2.5} />
      </TouchableOpacity>
    </View>
  )
}

const styles = StyleSheet.create({
  wrapper: {
    flex: 1,
  },
  badge: {
    position: 'absolute',
    top: -4,
    right: -10,
    minWidth: 18,
    height: 18,
    borderRadius: 9,
    backgroundColor: colors.ageStale,
    alignItems: 'center',
    justifyContent: 'center',
    paddingHorizontal: 4,
  },
  badgeText: {
    color: '#fff',
    fontSize: typography.sizes.xs - 2,
    fontWeight: '700',
    lineHeight: 14,
  },
  fab: {
    position: 'absolute',
    bottom: 80,
    alignSelf: 'center',
    width: 56,
    height: 56,
    borderRadius: 28,
    justifyContent: 'center',
    alignItems: 'center',
    // Shadow for iOS
    shadowColor: '#000',
    shadowOffset: { width: 0, height: 4 },
    shadowOpacity: 0.3,
    shadowRadius: 6,
    // Shadow for Android
    elevation: 8,
  },
})
