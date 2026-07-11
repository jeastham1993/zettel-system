import { get, post, put, del } from './client'
import type {
  ChatSession,
  ChatMessageResponse,
  CreateChatSessionRequest,
  SendChatMessageRequest,
  UpdateChatSessionRequest,
  ChatSessionStatus
} from './types'

export function createChatSession(request: CreateChatSessionRequest): Promise<ChatSession> {
  return post<ChatSession>('/api/chat/sessions', request)
}

export function getChatSession(sessionId: string): Promise<ChatSession> {
  return get<ChatSession>(`/api/chat/sessions/${sessionId}`)
}

export function listChatSessions(
  status?: ChatSessionStatus,
  skip: number = 0,
  take: number = 50
): Promise<ChatSession[]> {
  const params = new URLSearchParams()
  if (status) params.append('status', status)
  if (skip > 0) params.append('skip', skip.toString())
  if (take !== 50) params.append('take', take.toString())
  return get<ChatSession[]>(`/api/chat/sessions?${params}`)
}

export function updateChatSession(
  sessionId: string,
  request: UpdateChatSessionRequest
): Promise<ChatSession> {
  return put<ChatSession>(`/api/chat/sessions/${sessionId}`, request)
}

export function deleteChatSession(sessionId: string): Promise<void> {
  return del(`/api/chat/sessions/${sessionId}`)
}

export function sendChatMessage(
  sessionId: string,
  request: SendChatMessageRequest
): Promise<ChatMessageResponse> {
  return post<ChatMessageResponse>(`/api/chat/sessions/${sessionId}/messages`, request)
}

export function getChatMessages(
  sessionId: string,
  skip: number = 0,
  take: number = 50
): Promise<ChatMessageResponse[]> {
  const params = new URLSearchParams()
  if (skip > 0) params.append('skip', skip.toString())
  if (take !== 50) params.append('take', take.toString())
  return get<ChatMessageResponse[]>(`/api/chat/sessions/${sessionId}/messages?${params}`)
}

export function regenerateChatResponse(sessionId: string): Promise<ChatMessageResponse> {
  return post<ChatMessageResponse>(`/api/chat/sessions/${sessionId}/regenerate`)
}