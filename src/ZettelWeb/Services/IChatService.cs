using ZettelWeb.Models;

namespace ZettelWeb.Services;

/// <summary>Service for managing chat sessions and conversations with the knowledge base.</summary>
public interface IChatService
{
    /// <summary>Create a new chat session.</summary>
    /// <param name="request">Creation request with optional title and context notes.</param>
    /// <returns>The created chat session.</returns>
    Task<ChatSession> CreateSessionAsync(CreateChatSessionRequest request);

    /// <summary>Get a chat session by ID.</summary>
    /// <param name="sessionId">The session ID.</param>
    /// <returns>The chat session or null if not found.</returns>
    Task<ChatSession?> GetSessionAsync(string sessionId);

    /// <summary>List all chat sessions, ordered by most recent.</summary>
    /// <param name="status">Optional filter by status.</param>
    /// <param name="skip">Number of sessions to skip.</param>
    /// <param name="take">Number of sessions to return.</param>
    /// <returns>List of chat sessions.</returns>
    Task<IReadOnlyList<ChatSession>> ListSessionsAsync(ChatSessionStatus? status = null, int skip = 0, int take = 50);

    /// <summary>Update a chat session title or status.</summary>
    /// <param name="sessionId">The session ID.</param>
    /// <param name="title">Optional new title.</param>
    /// <param name="status">Optional new status.</param>
    /// <returns>The updated session or null if not found.</returns>
    Task<ChatSession?> UpdateSessionAsync(string sessionId, string? title = null, ChatSessionStatus? status = null);

    /// <summary>Delete a chat session and all its messages.</summary>
    /// <param name="sessionId">The session ID.</param>
    /// <returns>True if deleted, false if not found.</returns>
    Task<bool> DeleteSessionAsync(string sessionId);

    /// <summary>Send a message to a chat session and get the assistant's response.</summary>
    /// <param name="sessionId">The session ID.</param>
    /// <param name="request">The message request.</param>
    /// <returns>The assistant's response message.</returns>
    Task<ChatMessageResponse> SendMessageAsync(string sessionId, SendChatMessageRequest request);

    /// <summary>Get messages for a chat session.</summary>
    /// <param name="sessionId">The session ID.</param>
    /// <param name="skip">Number of messages to skip.</param>
    /// <param name="take">Number of messages to return.</param>
    /// <returns>List of messages in the session.</returns>
    Task<IReadOnlyList<ChatMessageResponse>> GetMessagesAsync(string sessionId, int skip = 0, int take = 50);

    /// <summary>Regenerate the last assistant response in a session.</summary>
    /// <param name="sessionId">The session ID.</param>
    /// <returns>The new assistant response message.</returns>
    Task<ChatMessageResponse> RegenerateResponseAsync(string sessionId);
}