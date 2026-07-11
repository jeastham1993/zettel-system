using System.Text.Json.Serialization;

namespace ZettelWeb.Models;

/// <summary>Status of a chat session.</summary>
public enum ChatSessionStatus
{
    /// <summary>Session is active and ongoing.</summary>
    Active = 0,
    /// <summary>Session has been archived by the user.</summary>
    Archived = 1
}

/// <summary>Role of a chat message sender.</summary>
public enum ChatMessageRole
{
    /// <summary>Message sent by the user.</summary>
    User = 0,
    /// <summary>Message sent by the assistant/chatbot.</summary>
    Assistant = 1,
    /// <summary>System message (context, instructions, etc.).</summary>
    System = 2
}

/// <summary>A chat session representing a conversation with the knowledge base.</summary>
public class ChatSession
{
    /// <summary>Unique identifier for the chat session.</summary>
    public required string Id { get; set; }
    /// <summary>Title of the chat session.</summary>
    public required string Title { get; set; }
    /// <summary>When the session was created (UTC).</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>When the session was last updated (UTC).</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>Status of the chat session.</summary>
    public ChatSessionStatus Status { get; set; } = ChatSessionStatus.Active;
    /// <summary>IDs of notes that are relevant to this conversation context.</summary>
    public List<string> ContextNoteIds { get; set; } = new();
    /// <summary>Summary of the conversation topic.</summary>
    public string? TopicSummary { get; set; }
    /// <summary>Embedding of the conversation context for semantic search.</summary>
    [JsonIgnore]
    public float[]? ContextEmbedding { get; set; }
    /// <summary>When the context embedding was last updated.</summary>
    [JsonIgnore]
    public DateTime? ContextEmbeddingUpdatedAt { get; set; }
    /// <summary>Messages in this chat session.</summary>
    public List<ChatMessage> Messages { get; set; } = new();
}

/// <summary>An individual message in a chat conversation.</summary>
public class ChatMessage
{
    /// <summary>Unique identifier for the message.</summary>
    public required string Id { get; set; }
    /// <summary>ID of the chat session this message belongs to.</summary>
    public required string SessionId { get; set; }
    /// <summary>Role of the sender.</summary>
    public ChatMessageRole Role { get; set; }
    /// <summary>Content of the message.</summary>
    public required string Content { get; set; }
    /// <summary>When the message was created (UTC).</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>IDs of notes that were referenced or used to generate this message.</summary>
    public List<string> ReferenceNoteIds { get; set; } = new();
    /// <summary>Embedding of the message content for semantic search.</summary>
    [JsonIgnore]
    public float[]? Embedding { get; set; }
    /// <summary>When the message embedding was last updated.</summary>
    [JsonIgnore]
    public DateTime? EmbeddingUpdatedAt { get; set; }
}

/// <summary>Request to create a new chat session.</summary>
public class CreateChatSessionRequest
{
    /// <summary>Optional title for the chat session.</summary>
    public string? Title { get; set; }
    /// <summary>Optional note IDs to use as initial context.</summary>
    public List<string>? ContextNoteIds { get; set; }
}

/// <summary>Request to send a message in a chat session.</summary>
public class SendChatMessageRequest
{
    /// <summary>The user's message content.</summary>
    public required string Content { get; set; }
    /// <summary>Optional note IDs to use as additional context for this message.</summary>
    public List<string>? ContextNoteIds { get; set; }
}

/// <summary>Response containing a chat message with references.</summary>
public class ChatMessageResponse
{
    /// <summary>The message ID.</summary>
    public required string Id { get; set; }
    /// <summary>The session ID.</summary>
    public required string SessionId { get; set; }
    /// <summary>Role of the sender.</summary>
    public ChatMessageRole Role { get; set; }
    /// <summary>Content of the message.</summary>
    public required string Content { get; set; }
    /// <summary>When the message was created.</summary>
    public DateTime CreatedAt { get; set; }
    /// <summary>Notes referenced in this message.</summary>
    public List<ChatReferenceNote> ReferenceNotes { get; set; } = new();
}

/// <summary>Information about a note referenced in a chat message.</summary>
public class ChatReferenceNote
{
    /// <summary>The note ID.</summary>
    public required string Id { get; set; }
    /// <summary>The note title.</summary>
    public required string Title { get; set; }
    /// <summary>Relevance score (0.0–1.0).</summary>
    public double Relevance { get; set; }
}