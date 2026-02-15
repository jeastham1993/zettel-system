import { useState, useCallback } from 'react'
import {
  StyleSheet,
  Text,
  View,
  TextInput,
  Pressable,
  ScrollView,
  Alert,
  ActivityIndicator,
} from 'react-native'
import { Wifi, WifiOff, RefreshCw, CheckCircle, XCircle } from 'lucide-react-native'
import { useQueryClient } from '@tanstack/react-query'
import { colors, themed, type ColorScheme } from '@/src/theme/colors'
import { typography } from '@/src/theme/typography'
import { preferences, type ThemePreference } from '@/src/stores/preferences'
import { useTheme } from '@/src/hooks/use-theme'
import { useServer } from '@/src/hooks/use-server'
import { useOfflineQueue } from '@/src/hooks/use-offline-queue'
import { useHealth } from '@/src/hooks/use-health'
import type { SearchType } from '@/src/api/types'

function Section({ title, scheme, children }: {
  title: string
  scheme: ColorScheme
  children: React.ReactNode
}) {
  return (
    <View style={styles.section}>
      <Text style={[styles.sectionTitle, { color: themed(colors.primary, scheme) }]}>
        {title}
      </Text>
      <View style={[styles.sectionContent, {
        backgroundColor: themed(colors.card, scheme),
        borderColor: themed(colors.border, scheme),
      }]}>
        {children}
      </View>
    </View>
  )
}

function OptionRow({ label, options, value, onChange, scheme }: {
  label: string
  options: { label: string; value: string }[]
  value: string
  onChange: (v: string) => void
  scheme: ColorScheme
}) {
  return (
    <View style={styles.optionRow}>
      <Text style={[styles.label, { color: themed(colors.foreground, scheme) }]}>{label}</Text>
      <View style={styles.optionGroup}>
        {options.map((opt) => (
          <Pressable
            key={opt.value}
            onPress={() => onChange(opt.value)}
            style={[
              styles.optionButton,
              {
                backgroundColor: value === opt.value
                  ? themed(colors.primary, scheme)
                  : themed(colors.background, scheme),
                borderColor: themed(colors.border, scheme),
              },
            ]}
          >
            <Text
              style={[
                styles.optionText,
                {
                  color: value === opt.value
                    ? '#fff'
                    : themed(colors.foreground, scheme),
                },
              ]}
            >
              {opt.label}
            </Text>
          </Pressable>
        ))}
      </View>
    </View>
  )
}

export default function SettingsScreen() {
  const scheme = useTheme()
  const queryClient = useQueryClient()
  const server = useServer()
  const offlineQueue = useOfflineQueue()
  const health = useHealth()

  const [urlInput, setUrlInput] = useState(server.serverUrl ?? '')
  const [themePref, setThemePref] = useState<ThemePreference>(preferences.getTheme())
  const [searchType, setSearchType] = useState<SearchType>(preferences.getDefaultSearchType())

  const handleSaveUrl = useCallback(async () => {
    const trimmed = urlInput.trim().replace(/\/+$/, '')
    if (!trimmed) {
      Alert.alert('Error', 'Please enter a server URL.')
      return
    }

    const ok = await server.checkConnection(trimmed)
    if (ok) {
      server.setServerUrl(trimmed)
      queryClient.clear()
      Alert.alert('Connected', 'Server connection verified.')
    } else {
      Alert.alert('Connection Failed', 'Could not reach the server. Check the URL and try again.')
    }
  }, [urlInput, server, queryClient])

  const handleThemeChange = useCallback((val: string) => {
    const t = val as ThemePreference
    preferences.setTheme(t)
    setThemePref(t)
  }, [])

  const handleSearchTypeChange = useCallback((val: string) => {
    const t = val as SearchType
    preferences.setDefaultSearchType(t)
    setSearchType(t)
  }, [])

  const handleSync = useCallback(async () => {
    const result = await offlineQueue.syncAll()
    if (result.synced > 0) {
      Alert.alert('Synced', `${result.synced} note(s) synced.${result.failed > 0 ? ` ${result.failed} failed.` : ''}`)
    } else if (result.failed > 0) {
      Alert.alert('Sync Failed', `${result.failed} note(s) failed to sync. Check your connection.`)
    } else {
      Alert.alert('Nothing to sync', 'No pending captures in the queue.')
    }
  }, [offlineQueue])

  const connectionIcon = server.connectionState === 'connected'
    ? <CheckCircle size={18} color={colors.success} />
    : server.connectionState === 'checking'
    ? <ActivityIndicator size="small" color={themed(colors.primary, scheme)} />
    : <XCircle size={18} color={colors.destructive} />

  const connectionText = server.connectionState === 'connected'
    ? 'Connected'
    : server.connectionState === 'checking'
    ? 'Checking...'
    : 'Disconnected'

  // Extract health data
  const noteData = health.data?.entries?.['note-store']?.data as
    | Record<string, unknown>
    | undefined
  const totalNotes = noteData?.['total-notes'] as number | undefined
  const embeddedNotes = noteData?.['embedded-notes'] as number | undefined
  const pendingEmbeds = noteData?.['pending-embeds'] as number | undefined

  return (
    <ScrollView
      style={[styles.container, { backgroundColor: themed(colors.background, scheme) }]}
      contentContainerStyle={styles.content}
    >
      <Section title="Server" scheme={scheme}>
        <Text style={[styles.label, { color: themed(colors.foreground, scheme) }]}>URL</Text>
        <TextInput
          style={[styles.input, {
            color: themed(colors.foreground, scheme),
            backgroundColor: themed(colors.background, scheme),
            borderColor: themed(colors.border, scheme),
          }]}
          value={urlInput}
          onChangeText={setUrlInput}
          placeholder="http://192.168.1.100:80"
          placeholderTextColor={themed(colors.muted, scheme)}
          autoCapitalize="none"
          autoCorrect={false}
          keyboardType="url"
        />
        <Pressable
          style={[styles.button, { backgroundColor: themed(colors.primary, scheme) }]}
          onPress={handleSaveUrl}
          disabled={server.isChecking}
        >
          {server.isChecking ? (
            <ActivityIndicator color="#fff" size="small" />
          ) : (
            <Text style={styles.buttonText}>Test Connection</Text>
          )}
        </Pressable>
        <View style={styles.statusRow}>
          {connectionIcon}
          <Text style={[styles.statusText, { color: themed(colors.foreground, scheme) }]}>
            {' '}{connectionText}
          </Text>
        </View>
        {server.connectionState === 'connected' && totalNotes !== undefined && (
          <Text style={[styles.healthInfo, { color: themed(colors.muted, scheme) }]}>
            Notes: {totalNotes} | Embedded: {embeddedNotes ?? '?'} | Pending: {pendingEmbeds ?? 0}
          </Text>
        )}
      </Section>

      <Section title="Appearance" scheme={scheme}>
        <OptionRow
          label="Theme"
          options={[
            { label: 'System', value: 'system' },
            { label: 'Light', value: 'light' },
            { label: 'Dark', value: 'dark' },
          ]}
          value={themePref}
          onChange={handleThemeChange}
          scheme={scheme}
        />
      </Section>

      <Section title="Search" scheme={scheme}>
        <OptionRow
          label="Default type"
          options={[
            { label: 'Hybrid', value: 'hybrid' },
            { label: 'Full-text', value: 'fulltext' },
            { label: 'Semantic', value: 'semantic' },
          ]}
          value={searchType}
          onChange={handleSearchTypeChange}
          scheme={scheme}
        />
      </Section>

      <Section title="Offline" scheme={scheme}>
        <View style={styles.offlineRow}>
          <Text style={[styles.label, { color: themed(colors.foreground, scheme) }]}>
            Pending captures: {offlineQueue.count}
          </Text>
          <Pressable
            style={[styles.syncButton, { backgroundColor: themed(colors.primary, scheme) }]}
            onPress={handleSync}
            disabled={offlineQueue.isSyncing || offlineQueue.count === 0}
          >
            {offlineQueue.isSyncing ? (
              <ActivityIndicator color="#fff" size="small" />
            ) : (
              <>
                <RefreshCw size={16} color="#fff" />
                <Text style={styles.syncButtonText}> Sync Now</Text>
              </>
            )}
          </Pressable>
        </View>
      </Section>

      <Section title="About" scheme={scheme}>
        <Text style={[styles.label, { color: themed(colors.foreground, scheme) }]}>
          Zettel Mobile v1.0.0
        </Text>
      </Section>
    </ScrollView>
  )
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
  },
  content: {
    padding: 16,
    paddingBottom: 40,
  },
  section: {
    marginBottom: 24,
  },
  sectionTitle: {
    fontSize: typography.sizes.sm,
    fontWeight: '600',
    textTransform: 'uppercase',
    letterSpacing: 0.8,
    marginBottom: 8,
  },
  sectionContent: {
    borderRadius: 12,
    borderWidth: 1,
    padding: 16,
  },
  label: {
    fontSize: typography.sizes.base,
    marginBottom: 6,
  },
  input: {
    borderWidth: 1,
    borderRadius: 8,
    padding: 12,
    fontSize: typography.sizes.base,
    marginBottom: 12,
  },
  button: {
    borderRadius: 8,
    padding: 14,
    alignItems: 'center',
    marginBottom: 12,
  },
  buttonText: {
    color: '#fff',
    fontWeight: '600',
    fontSize: typography.sizes.base,
  },
  statusRow: {
    flexDirection: 'row',
    alignItems: 'center',
  },
  statusText: {
    fontSize: typography.sizes.sm,
  },
  healthInfo: {
    fontSize: typography.sizes.sm,
    marginTop: 8,
  },
  optionRow: {
    marginBottom: 4,
  },
  optionGroup: {
    flexDirection: 'row',
    gap: 8,
    marginTop: 4,
  },
  optionButton: {
    borderRadius: 8,
    borderWidth: 1,
    paddingVertical: 8,
    paddingHorizontal: 16,
  },
  optionText: {
    fontSize: typography.sizes.sm,
    fontWeight: '500',
  },
  offlineRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
  },
  syncButton: {
    flexDirection: 'row',
    alignItems: 'center',
    borderRadius: 8,
    paddingVertical: 8,
    paddingHorizontal: 14,
  },
  syncButtonText: {
    color: '#fff',
    fontWeight: '600',
    fontSize: typography.sizes.sm,
  },
})
