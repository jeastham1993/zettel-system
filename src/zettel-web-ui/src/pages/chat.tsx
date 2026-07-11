import { useState, useEffect, useRef } from 'react'
import { useParams, useNavigate } from 'react-router'
import { Send, Plus, Archive, Trash2, RefreshCw, Loader2, MessageSquare, X } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Card, CardHeader, CardTitle, CardContent, CardFooter } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'
import { Separator } from '@/components/ui/separator'
import { toast } from 'sonner'
import {
  createChatSession,
  getChatSession,
  listChatSessions,
  updateChatSession,
  deleteChatSession,
  sendChatMessage,
  getChatMessages,
  regenerateChatResponse
} from '@/api/chat'
import { useNotes } from '@/hooks/use-notes'
import type { ChatSession, ChatMessageResponse, ChatSessionStatus } from '@/api/types'

export function ChatPage() {
  const { sessionId } = useParams<{ sessionId?: string }>()
  const navigate = useNavigate()
  const [sessions, setSessions] = useState<ChatSession[]>([])
  const [currentSession, setCurrentSession] = useState<ChatSession | null>(null)
  const [messages, setMessages] = useState<ChatMessageResponse[]>([])
  const [newMessage, setNewMessage] = useState('')
  const [isLoading, setIsLoading] = useState(false)
  const [isSending, setIsSending] = useState(false)
  const messagesEndRef = useRef<HTMLDivElement>(null)

  const { data: notesData } = useNotes()

  useEffect(() => {
    loadSessions()
  }, [])

  useEffect(() => {
    if (sessionId) {
      loadSession(sessionId)
      loadMessages(sessionId)
    }
  }, [sessionId])

  useEffect(() => {
    scrollToBottom()
  }, [messages])

  const loadSessions = async () => {
    try {
      setIsLoading(true)
      const sessions = await listChatSessions('Active')
      setSessions(sessions)
      
      // If no session selected and sessions exist, select the first one
      if (!sessionId && sessions.length > 0) {
        navigate(`/chat/${sessions[0].id}`)
      }
    } catch (error) {
      toast.error('Failed to load chat sessions')
    } finally {
      setIsLoading(false)
    }
  }

  const loadSession = async (id: string) => {
    try {
      const session = await getChatSession(id)
      setCurrentSession(session)
    } catch (error) {
      toast.error('Failed to load chat session')
    }
  }

  const loadMessages = async (id: string) => {
    try {
      const messages = await getChatMessages(id)
      setMessages(messages)
    } catch (error) {
      toast.error('Failed to load chat messages')
    }
  }

  const handleCreateSession = async () => {
    try {
      const newSession = await createChatSession({})
      setSessions([newSession, ...sessions])
      navigate(`/chat/${newSession.id}`)
      toast.success('New chat session created')
    } catch (error) {
      toast.error('Failed to create chat session')
    }
  }

  const handleSendMessage = async () => {
    if (!newMessage.trim() || !currentSession) return

    try {
      setIsSending(true)
      const response = await sendChatMessage(currentSession.id, {
        content: newMessage
      })

      setMessages([...messages, 
        {
          id: 'temp-user',
          sessionId: currentSession.id,
          role: 'User',
          content: newMessage,
          createdAt: new Date().toISOString(),
          referenceNotes: []
        } as ChatMessageResponse,
        response
      ])
      
      setNewMessage('')
      await loadSession(currentSession.id) // Refresh session info
    } catch (error) {
      toast.error('Failed to send message')
    } finally {
      setIsSending(false)
    }
  }

  const handleRegenerateResponse = async () => {
    if (!currentSession) return

    try {
      setIsSending(true)
      const response = await regenerateChatResponse(currentSession.id)
      
      // Replace the last assistant message
      const updatedMessages = [...messages]
      const lastAssistantIndex = updatedMessages.findLastIndex(m => m.role === 'Assistant')
      if (lastAssistantIndex >= 0) {
        updatedMessages[lastAssistantIndex] = response
      } else {
        updatedMessages.push(response)
      }
      
      setMessages(updatedMessages)
      toast.success('Response regenerated')
    } catch (error) {
      toast.error('Failed to regenerate response')
    } finally {
      setIsSending(false)
    }
  }

  const handleArchiveSession = async () => {
    if (!currentSession) return

    try {
      await updateChatSession(currentSession.id, {
        status: 'Archived'
      })
      setSessions(sessions.filter(s => s.id !== currentSession.id))
      navigate('/chat')
      toast.success('Session archived')
    } catch (error) {
      toast.error('Failed to archive session')
    }
  }

  const handleDeleteSession = async () => {
    if (!currentSession) return

    if (!confirm('Are you sure you want to delete this chat session? This cannot be undone.')) {
      return
    }

    try {
      await deleteChatSession(currentSession.id)
      setSessions(sessions.filter(s => s.id !== currentSession.id))
      navigate('/chat')
      toast.success('Session deleted')
    } catch (error) {
      toast.error('Failed to delete session')
    }
  }

  const scrollToBottom = () => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' })
  }

  const getNoteTitle = (noteId: string) => {
    return notesData?.items.find(n => n.id === noteId)?.title || noteId
  }

  return (
    <div className="flex h-[calc(100vh-4rem)] overflow-hidden">
      {/* Sidebar - Chat Sessions List */}
      <div className="w-64 border-r bg-background p-4 overflow-y-auto">
        <div className="flex items-center justify-between mb-4">
          <h2 className="text-lg font-semibold">Chat Sessions</h2>
          <Button size="sm" onClick={handleCreateSession}>
            <Plus className="h-4 w-4 mr-2" />
            New Chat
          </Button>
        </div>

        {isLoading ? (
          <div className="flex justify-center py-8">
            <Loader2 className="h-6 w-6 animate-spin" />
          </div>
        ) : sessions.length === 0 ? (
          <div className="text-center py-8 text-muted-foreground">
            <MessageSquare className="mx-auto h-8 w-8 mb-2" />
            <p>No chat sessions yet</p>
            <Button size="sm" onClick={handleCreateSession} className="mt-4">
              Start a new chat
            </Button>
          </div>
        ) : (
          <div className="space-y-2">
            {sessions.map((session) => (
              <Card
                key={session.id}
                className={`cursor-pointer ${currentSession?.id === session.id ? 'border-primary' : ''}`}
                onClick={() => navigate(`/chat/${session.id}`)}
              >
                <CardContent className="p-3">
                  <div className="flex justify-between items-start">
                    <div>
                      <p className="font-medium truncate">{session.title}</p>
                      <p className="text-xs text-muted-foreground">
                        {new Date(session.updatedAt).toLocaleString()}
                      </p>
                    </div>
                    {session.contextNoteIds.length > 0 && (
                      <Badge variant="secondary" className="ml-2">
                        {session.contextNoteIds.length} notes
                      </Badge>
                    )}
                  </div>
                </CardContent>
              </Card>
            ))}
          </div>
        )}
      </div>

      {/* Main Chat Area */}
      <div className="flex-1 flex flex-col">
        {!currentSession ? (
          <div className="flex-1 flex items-center justify-center">
            <div className="text-center text-muted-foreground">
              <MessageSquare className="mx-auto h-12 w-12 mb-4" />
              <p className="mb-2">Select a chat session or create a new one</p>
              <Button onClick={handleCreateSession}>
                <Plus className="h-4 w-4 mr-2" />
                New Chat
              </Button>
            </div>
          </div>
        ) : (
          <>{
            {/* Chat Header */}
            <div className="border-b p-4 flex justify-between items-center">
              <div>
                <h2 className="text-lg font-semibold">{currentSession.title}</h2>
                <p className="text-sm text-muted-foreground">
                  {currentSession.contextNoteIds.length} context notes • 
                  Updated {new Date(currentSession.updatedAt).toLocaleString()}
                </p>
              </div>
              <div className="flex gap-2">
                <Button variant="ghost" size="sm" onClick={handleRegenerateResponse} disabled={isSending}>
                  <RefreshCw className="h-4 w-4" />
                </Button>
                <Button variant="ghost" size="sm" onClick={handleArchiveSession}>
                  <Archive className="h-4 w-4" />
                </Button>
                <Button variant="ghost" size="sm" onClick={handleDeleteSession}>
                  <Trash2 className="h-4 w-4" />
                </Button>
              </div>
            </div>

            {/* Messages */}
            <div className="flex-1 overflow-y-auto p-4 space-y-4">
              {messages.length === 0 ? (
                <div className="text-center py-8 text-muted-foreground">
                  <p>Start the conversation by sending a message</p>
                </div>
              ) : (
                messages.map((message) => (
                  <div
                    key={message.id}
                    className={`flex ${message.role === 'User' ? 'justify-end' : 'justify-start'}`}
                  >
                    <div
                      className={`max-w-[70%] p-3 rounded-lg ${message.role === 'User' ? 'bg-primary text-primary-foreground' : 'bg-muted'}`}
                    >
                      <p className="whitespace-pre-wrap">{message.content}</p>
                      {message.referenceNotes.length > 0 && (
                        <div className="mt-2 pt-2 border-t border-muted-foreground/20">
                          <p className="text-xs text-muted-foreground mb-1">References:</p>
                          <div className="flex flex-wrap gap-1">
                            {message.referenceNotes.map((ref) => (
                              <Badge
                                key={ref.id}
                                variant="secondary"
                                className="cursor-pointer hover:bg-accent"
                                onClick={() => navigate(`/notes/${ref.id}`)}
                              >
                                {getNoteTitle(ref.id)}
                              </Badge>
                            ))}
                          </div>
                        </div>
                      )}
                    </div>
                  </div>
                ))
              )}
              <div ref={messagesEndRef} />
            </div>

            {/* Message Input */}
            <div className="border-t p-4">
              <div className="flex gap-2">
                <Input
                  value={newMessage}
                  onChange={(e) => setNewMessage(e.target.value)}
                  onKeyPress={(e) => e.key === 'Enter' && handleSendMessage()}
                  placeholder="Type your message..."
                  disabled={isSending}
                />
                <Button onClick={handleSendMessage} disabled={isSending || !newMessage.trim()}>
                  {isSending ? (
                    <Loader2 className="h-4 w-4 animate-spin" />
                  ) : (
                    <Send className="h-4 w-4" />
                  )}
                </Button>
              </div>
            </div>
          </>
        )}
      </div>
    </div>
  )
}