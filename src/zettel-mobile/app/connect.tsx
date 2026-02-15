import { useState, useCallback } from 'react'
import {
  StyleSheet,
  Text,
  View,
  TextInput,
  Pressable,
  ActivityIndicator,
  KeyboardAvoidingView,
  Platform,
} from 'react-native'
import { useRouter } from 'expo-router'
import { Wifi } from 'lucide-react-native'
import { colors } from '@/src/theme/colors'
import { typography } from '@/src/theme/typography'
import { useServer } from '@/src/hooks/use-server'

export default function ConnectScreen() {
  const router = useRouter()
  const { setServerUrl, checkConnection } = useServer()
  const [url, setUrl] = useState('')
  const [isChecking, setIsChecking] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const handleConnect = useCallback(async () => {
    const trimmed = url.trim().replace(/\/+$/, '')
    if (!trimmed) {
      setError('Please enter a server URL.')
      return
    }

    setIsChecking(true)
    setError(null)

    try {
      const ok = await checkConnection(trimmed)
      if (ok) {
        setServerUrl(trimmed)
        router.replace('/')
      } else {
        setError('Could not connect to server. Check the URL and ensure the server is running.')
      }
    } catch {
      setError('Could not connect to server. Check the URL and ensure the server is running.')
    } finally {
      setIsChecking(false)
    }
  }, [url, router, setServerUrl, checkConnection])

  // Use light theme for the connect screen (first-launch)
  const scheme = 'light'

  return (
    <KeyboardAvoidingView
      style={[styles.container, { backgroundColor: colors.background[scheme] }]}
      behavior={Platform.OS === 'ios' ? 'padding' : undefined}
    >
      <View style={styles.content}>
        <Wifi size={64} color={colors.primary[scheme]} style={styles.icon} />

        <Text style={[styles.title, { color: colors.foreground[scheme] }]}>
          Connect to Server
        </Text>
        <Text style={[styles.subtitle, { color: colors.muted[scheme] }]}>
          Enter your Zettel server URL to get started. This is the URL where your self-hosted
          Zettel instance is running.
        </Text>

        <TextInput
          style={[styles.input, {
            color: colors.foreground[scheme],
            backgroundColor: colors.card[scheme],
            borderColor: error ? colors.destructive : colors.border[scheme],
          }]}
          value={url}
          onChangeText={(v) => { setUrl(v); setError(null) }}
          placeholder="http://192.168.1.100:80"
          placeholderTextColor={colors.muted[scheme]}
          autoCapitalize="none"
          autoCorrect={false}
          keyboardType="url"
          autoFocus
          onSubmitEditing={handleConnect}
          returnKeyType="go"
        />

        {error && (
          <Text style={styles.errorText}>{error}</Text>
        )}

        <Pressable
          style={[styles.button, {
            backgroundColor: colors.primary[scheme],
            opacity: isChecking ? 0.7 : 1,
          }]}
          onPress={handleConnect}
          disabled={isChecking}
        >
          {isChecking ? (
            <ActivityIndicator color="#fff" size="small" />
          ) : (
            <Text style={styles.buttonText}>Connect</Text>
          )}
        </Pressable>

        <Text style={[styles.hint, { color: colors.muted[scheme] }]}>
          The app will validate the connection by calling the /health endpoint.
        </Text>
      </View>
    </KeyboardAvoidingView>
  )
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
  },
  content: {
    flex: 1,
    justifyContent: 'center',
    padding: 32,
  },
  icon: {
    alignSelf: 'center',
    marginBottom: 24,
  },
  title: {
    fontSize: typography.sizes['2xl'],
    fontFamily: typography.titleFamily,
    fontWeight: '700',
    textAlign: 'center',
    marginBottom: 12,
  },
  subtitle: {
    fontSize: typography.sizes.base,
    textAlign: 'center',
    marginBottom: 32,
    lineHeight: 22,
  },
  input: {
    borderWidth: 1,
    borderRadius: 12,
    padding: 16,
    fontSize: typography.sizes.lg,
    marginBottom: 8,
  },
  errorText: {
    color: colors.destructive,
    fontSize: typography.sizes.sm,
    marginBottom: 8,
  },
  button: {
    borderRadius: 12,
    padding: 16,
    alignItems: 'center',
    marginTop: 8,
  },
  buttonText: {
    color: '#fff',
    fontWeight: '700',
    fontSize: typography.sizes.lg,
  },
  hint: {
    fontSize: typography.sizes.sm,
    textAlign: 'center',
    marginTop: 16,
  },
})
