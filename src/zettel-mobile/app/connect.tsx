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
  Alert,
} from 'react-native'
import { useRouter } from 'expo-router'
import { Wifi } from 'lucide-react-native'
import { colors } from '@/src/theme/colors'
import { typography } from '@/src/theme/typography'
import { useServer } from '@/src/hooks/use-server'
import { testConnection } from '@/src/api/client'

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

    // Validate URL format
    if (!trimmed.startsWith('http://') && !trimmed.startsWith('https://')) {
      setError('URL must start with http:// or https://')
      return
    }

    setIsChecking(true)
    setError(null)

    try {
      const result = await testConnection()
      
      if (result.success) {
        setServerUrl(trimmed)
        router.replace('/')
      } else {
        // Provide more specific error messages
        if (result.error?.includes('timed out')) {
          setError('Connection timed out. The server might be unreachable or too slow to respond.')
        } else if (result.error?.includes('Network request failed')) {
          setError('Network request failed. Check your internet connection and server URL.')
        } else if (result.error?.includes('404')) {
          setError('Server reached but /api/health endpoint not found. The server is working (notes load) but health check failed.')
        } else if (result.error?.includes('Health check failed')) {
          setError('Health check failed but server is working. Notes will load normally.')
        } else if (result.error) {
          setError(`Connection failed: ${result.error}`)
        } else {
          setError('Could not connect to server. Check the URL and ensure the server is running.')
        }
        
        // Show alert with troubleshooting tips
        Alert.alert(
          'Connection Failed',
          `The app couldn't connect to ${trimmed}.\n\n` +
          'Troubleshooting tips:\n' +
          '1. Make sure the URL starts with http:// or https://\n' +
          '2. Verify the server is running on this device\n' +
          '3. Check that port 9010 is open in your firewall\n' +
          '4. Try accessing the URL in your browser first',
          [
            { text: 'Try Again', onPress: () => {} },
            { text: 'Edit URL', style: 'cancel' }
          ]
        )
      }
    } catch (error) {
      console.error('Connection test error:', error)
      setError(`Unexpected error: ${error.message}`)
      Alert.alert('Error', `An unexpected error occurred: ${error.message}`)
    } finally {
      setIsChecking(false)
    }
  }, [url, router, setServerUrl])

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
