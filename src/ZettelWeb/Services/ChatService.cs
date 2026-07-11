using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Pgvector;
using ZettelWeb.Data;
using ZettelWeb.Models;

namespace ZettelWeb.Services;

/// <summary>Service for managing chat sessions and conversations with the knowledge base.</summary>
public class ChatService : IChatService
{
    private readonly ZettelDbContext _db;
    private readonly ISearchService _searchService;
    private readonly IChatClient _chatClient;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly ILogger<ChatService> _logger;

    public ChatService(
        ZettelDbContext db,
        ISearchService searchService,
        IChatClient chatClient,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        ILogger<ChatService> logger)
    {
        _db = db;
        _searchService = searchService;
        _chatClient = chatClient;
        _embeddingGenerator = embeddingGenerator;
        _logger = logger;
    }

    public async Task<ChatSession> CreateSessionAsync(CreateChatSessionRequest request)
    {
        var session = new ChatSession
        {
            Id = GenerateId(),
            Title = request.Title ?? "New Chat",
            Status = ChatSessionStatus.Active,
            ContextNoteIds = request.ContextNoteIds ?? new List<string>(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.ChatSessions.Add(session);
        await _db.SaveChangesAsync();



        return session;
    }

    public async Task<ChatSession?> GetSessionAsync(string sessionId)
    {
        return await _db.ChatSessions
            .Include(s => s.Messages)
            .FirstOrDefaultAsync(s => s.Id == sessionId);
    }

    public async Task<IReadOnlyList<ChatSession>> ListSessionsAsync(
        ChatSessionStatus? status = null, int skip = 0, int take = 50)
    {
        var query = _db.ChatSessions.AsQueryable();
        
        if (status.HasValue)
        {
            query = query.Where(s => s.Status == status.Value);
        }

        return await query
            .OrderByDescending(s => s.UpdatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
    }

    public async Task<ChatSession?> UpdateSessionAsync(
        string sessionId, string? title = null, ChatSessionStatus? status = null)
    {
        var session = await _db.ChatSessions.FindAsync(sessionId);
        if (session == null) return null;

        if (title != null)
        {
            session.Title = title;
        }

        if (status.HasValue)
        {
            session.Status = status.Value;
        }

        session.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return session;
    }

    public async Task<bool> DeleteSessionAsync(string sessionId)
    {
        var session = await _db.ChatSessions
            .Include(s => s.Messages)
            .FirstOrDefaultAsync(s => s.Id == sessionId);

        if (session == null) return false;

        _db.ChatSessions.Remove(session);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<ChatMessageResponse> SendMessageAsync(string sessionId, SendChatMessageRequest request)
    {
        var session = await _db.ChatSessions
            .Include(s => s.Messages)
            .FirstOrDefaultAsync(s => s.Id == sessionId);

        if (session == null)
        {
            throw new ArgumentException("Session not found", nameof(sessionId));
        }

        // Add user message
        var userMessage = new Models.ChatMessage
        {
            Id = GenerateId(),
            SessionId = sessionId,
            Role = ChatMessageRole.User,
            Content = request.Content,
            CreatedAt = DateTime.UtcNow,
            ReferenceNoteIds = request.ContextNoteIds ?? new List<string>()
        };

        _db.ChatMessages.Add(userMessage);
        await _db.SaveChangesAsync();

        // Generate assistant response
        var assistantResponse = await GenerateAssistantResponseAsync(session, userMessage);

        // Update session context and embedding
        await UpdateSessionContextAsync(session, userMessage, assistantResponse);

        return assistantResponse;
    }

    public async Task<IReadOnlyList<ChatMessageResponse>> GetMessagesAsync(string sessionId, int skip = 0, int take = 50)
    {
        var messages = await _db.ChatMessages
            .Where(m => m.SessionId == sessionId)
            .OrderBy(m => m.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        return await Task.FromResult(messages.Select(m => new ChatMessageResponse
        {
            Id = m.Id,
            SessionId = m.SessionId,
            Role = m.Role,
            Content = m.Content,
            CreatedAt = m.CreatedAt,
            ReferenceNotes = m.ReferenceNoteIds.Select(noteId => new ChatReferenceNote
            {
                Id = noteId,
                Title = "Note " + noteId, // Will be enriched by frontend
                Relevance = 0.8 // Default relevance
            }).ToList()
        }).ToList());
    }

    public async Task<ChatMessageResponse> RegenerateResponseAsync(string sessionId)
    {
        var session = await _db.ChatSessions
            .Include(s => s.Messages)
            .FirstOrDefaultAsync(s => s.Id == sessionId);

        if (session == null)
        {
            throw new ArgumentException("Session not found", nameof(sessionId));
        }

        // Find the last user message
        var lastUserMessage = session.Messages.OfType<Models.ChatMessage>()
            .Where(m => m.Role == ChatMessageRole.User)
            .OrderByDescending(m => m.CreatedAt)
            .FirstOrDefault();

        if (lastUserMessage == null)
        {
            throw new InvalidOperationException("No user message found to regenerate response for");
        }

        // Remove the last assistant message if it exists
        var lastAssistantMessage = session.Messages.OfType<Models.ChatMessage>()
            .Where(m => m.Role == ChatMessageRole.Assistant && m.CreatedAt > lastUserMessage.CreatedAt)
            .OrderByDescending(m => m.CreatedAt)
            .FirstOrDefault();

        if (lastAssistantMessage != null)
        {
            _db.ChatMessages.Remove(lastAssistantMessage);
        }

        // Generate new response
        var newResponse = await GenerateAssistantResponseAsync(session, lastUserMessage);
        await _db.SaveChangesAsync();

        return newResponse;
    }

    private async Task<ChatMessageResponse> GenerateAssistantResponseAsync(ChatSession session, Models.ChatMessage userMessage)
    {
        using var activity = ZettelTelemetry.ActivitySource.StartActivity("chat.generate_response");
        var sw = Stopwatch.StartNew();

        try
        {
            // Build context from session and user message
            var contextNotes = await BuildContextNotesAsync(session, userMessage);
            var contextText = BuildContextText(contextNotes);

            // Create prompt for LLM
            var prompt = BuildPrompt(session, userMessage, contextText);

            _logger.LogInformation("Sending prompt to LLM with {NoteCount} context notes", contextNotes.Count);

            // Call LLM
            var llmResponse = await _chatClient.GetResponseAsync(
                [new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, prompt)],
                new ChatOptions()
            );
            var responseContent = llmResponse.ToString();

            // Create assistant message
            var assistantMessage = new Models.ChatMessage
            {
                Id = GenerateId(),
                SessionId = session.Id,
                Role = ChatMessageRole.Assistant,
                Content = responseContent,
                CreatedAt = DateTime.UtcNow,
                ReferenceNoteIds = contextNotes.Select(n => n.Id).ToList()
            };

            _db.ChatMessages.Add(assistantMessage);
            await _db.SaveChangesAsync();

            // Generate embedding for the message
            try
            {
                var embedding = await _embeddingGenerator.GenerateVectorAsync(assistantMessage.Content);
                assistantMessage.Embedding = embedding.ToArray();
                assistantMessage.EmbeddingUpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to generate embedding for message {MessageId}", assistantMessage.Id);
            }

            // Create response with reference notes
            var response = new ChatMessageResponse
            {
                Id = assistantMessage.Id,
                SessionId = assistantMessage.SessionId,
                Role = assistantMessage.Role,
                Content = assistantMessage.Content,
                CreatedAt = assistantMessage.CreatedAt,
                ReferenceNotes = contextNotes.Select(n => new ChatReferenceNote
                {
                    Id = n.Id,
                    Title = n.Title,
                    Relevance = CalculateNoteRelevance(n, userMessage.Content)
                }).ToList()
            };

            activity?.SetTag("chat.context_notes", contextNotes.Count);
            ZettelTelemetry.ChatMessagesGenerated.Add(1);
            ZettelTelemetry.ChatResponseDuration.Record(sw.Elapsed.TotalMilliseconds);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate assistant response");
            throw;
        }
    }

    private async Task<List<Note>> BuildContextNotesAsync(ChatSession session, Models.ChatMessage userMessage)
    {
        var contextNoteIds = new HashSet<string>();

        // Add session context notes
        if (session.ContextNoteIds != null)
        {
            foreach (var noteId in session.ContextNoteIds)
            {
                contextNoteIds.Add(noteId);
            }
        }

        // Add user message context notes
        if (userMessage.ReferenceNoteIds != null)
        {
            foreach (var noteId in userMessage.ReferenceNoteIds)
            {
                contextNoteIds.Add(noteId);
            }
        }

        // If no specific context, find relevant notes using semantic search
        if (contextNoteIds.Count == 0)
        {
            var searchResults = await _searchService.SemanticSearchAsync(userMessage.Content);
            foreach (var result in searchResults.Take(5)) // Top 5 relevant notes
            {
                contextNoteIds.Add(result.NoteId);
            }
        }

        // Fetch the notes
        var notes = new List<Note>();
        foreach (var noteId in contextNoteIds)
        {
            var note = await _db.Notes.FindAsync(noteId);
            if (note != null)
            {
                notes.Add(note);
            }
        }

        return notes;
    }

    private string BuildContextText(List<Note> contextNotes)
    {
        if (contextNotes.Count == 0)
        {
            return "No specific context provided. Using general knowledge.";
        }

        var contextText = "Relevant knowledge base context:\n\n";
        for (int i = 0; i < contextNotes.Count; i++)
        {
            var note = contextNotes[i];
            contextText += $"Note {i + 1}: {note.Title}\n";
            contextText += $"Content: {note.Content}\n";
            if (i < contextNotes.Count - 1)
            {
                contextText += "\n---\n\n";
            }
        }

        return contextText;
    }

    private string BuildPrompt(ChatSession session, Models.ChatMessage userMessage, string contextText)
    {
        return $$$"""
        You are an intelligent assistant helping a user interact with their personal knowledge base. 
        Use the provided context from their notes to answer questions, provide insights, and engage in meaningful conversation.

        {{contextText}}

        Conversation history (most recent last):
        {{GetConversationHistory(session, userMessage)}}

        User's current message: "{{userMessage.Content}}"

        Please respond helpfully based on the context and conversation history. 
        If the user's question cannot be answered from the context, say so clearly.
        Cite relevant notes when appropriate by referencing their titles.
        Keep responses concise and focused.
        """
        .Trim();
    }

    private string GetConversationHistory(ChatSession session, Models.ChatMessage currentUserMessage)
    {
        var history = new System.Text.StringBuilder();
        
        // Get recent messages (excluding the current user message)
        var recentMessages = session.Messages
            .Where(m => m.Id != currentUserMessage.Id)
            .OrderBy(m => m.CreatedAt)
            .TakeLast(10) // Last 10 messages for context
            .ToList();

        foreach (var message in recentMessages)
        {
            var role = message.Role == ChatMessageRole.User ? "User" : "Assistant";
            history.AppendLine($"{role}: {message.Content}");
        }

        return history.ToString();
    }

    private double CalculateNoteRelevance(Note note, string query)
    {
        // Simple relevance calculation based on title and content matches
        // In a production system, this could use more sophisticated methods
        double relevance = 0.0;

        if (note.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            relevance += 0.3;
        }

        if (note.Content.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            relevance += 0.5;
        }

        // Cap at 1.0
        return Math.Min(relevance, 1.0);
    }

    private async Task UpdateSessionContextAsync(
        ChatSession session, Models.ChatMessage userMessage, ChatMessageResponse assistantResponse)
    {
        // Update session title if it's still the default
        if (session.Title == "New Chat" && session.Messages.Count >= 4)
        {
            // Try to generate a better title based on conversation
            var titlePrompt = $"Based on this conversation, suggest a concise title (3-6 words) for this chat session:\n\n{GetConversationSummary(session, userMessage, assistantResponse)}\n\nTitle:";

            try
            {
                var titleResponse = await _chatClient.GetResponseAsync(
                    [new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, titlePrompt)],
                    new ChatOptions()
                );
                var suggestedTitle = titleResponse.ToString();
                session.Title = suggestedTitle.Trim().Trim('"', '\'', '`');
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to generate chat session title");
            }
        }

        // Update session context with any new reference notes
        var newContextNotes = assistantResponse.ReferenceNotes
            .Select(r => r.Id)
            .Except(session.ContextNoteIds)
            .ToList();

        if (newContextNotes.Count > 0)
        {
            session.ContextNoteIds.AddRange(newContextNotes);
        }



        session.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    private string GetConversationSummary(ChatSession session, Models.ChatMessage userMessage, ChatMessageResponse assistantResponse)
    {
        var summary = new System.Text.StringBuilder();
        
        // Add recent messages
        var recentMessages = session.Messages.OfType<Models.ChatMessage>()
            .OrderBy(m => m.CreatedAt)
            .TakeLast(5)
            .ToList();

        foreach (var message in recentMessages)
        {
            summary.AppendLine($"{(message.Role == ChatMessageRole.User ? "User" : "Assistant")}: {message.Content}");
        }

        // Add current exchange
        summary.AppendLine($"User: {userMessage.Content}");
        summary.AppendLine($"Assistant: {assistantResponse.Content}");
        
        return summary.ToString();
    }

    private static string GenerateId()
    {
        var now = DateTime.UtcNow;
        return $"{now:yyyyMMddHHmmssfff}{Random.Shared.Next(1000, 9999)}";
    }
}