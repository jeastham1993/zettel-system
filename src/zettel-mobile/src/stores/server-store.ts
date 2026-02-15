import { createMMKV } from 'react-native-mmkv'

export const storage = createMMKV({ id: 'server-store' })

const KEYS = {
  SERVER_URL: 'server-url',
  CONNECTION_STATE: 'connection-state',
} as const

export type ConnectionState = 'connected' | 'disconnected' | 'checking'

export const serverStore = {
  getServerUrl(): string | undefined {
    return storage.getString(KEYS.SERVER_URL)
  },

  setServerUrl(url: string): void {
    storage.set(KEYS.SERVER_URL, url)
  },

  clearServerUrl(): void {
    storage.remove(KEYS.SERVER_URL)
  },

  getConnectionState(): ConnectionState {
    return (storage.getString(KEYS.CONNECTION_STATE) as ConnectionState) ?? 'disconnected'
  },

  setConnectionState(state: ConnectionState): void {
    storage.set(KEYS.CONNECTION_STATE, state)
  },
}
