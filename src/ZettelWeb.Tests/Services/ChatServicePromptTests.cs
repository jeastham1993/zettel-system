using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using ZettelWeb.Models;
using ZettelWeb.Services;

namespace ZettelWeb.Tests.Services;

/// <summary>
/// Regression tests for ChatService.BuildPrompt. The prompt previously used
/// a $$$ raw string literal, which left {{contextText}} and other placeholders
/// unsubstituted — the raw template was sent verbatim to the LLM.
/// </summary>
public class ChatServicePromptTests
{
    private static string InvokeBuildPrompt(ChatSession session, ChatMessage userMessage, string contextText)
    {
        var sut = new ChatService(
            db: null!,
            searchService: null!,
            chatClient: null!,
            embeddingGenerator: null!,
            logger: NullLogger<ChatService>.Instance);

        var method = typeof(ChatService).GetMethod(
            "BuildPrompt",
            BindingFlags.NonPublic | BindingFlags.Instance,
            [typeof(ChatSession), typeof(ZettelWeb.Models.ChatMessage), typeof(string)]);

        Assert.NotNull(method);
        return (string)method!.Invoke(sut, [session, userMessage, contextText])!;
    }

    [Fact]
    public void BuildPrompt_InterpolatesContext_UserMessage_AndHistory()
    {
        var session = new ChatSession
        {
            Id = "s1",
            Title = "Test session",
            Messages =
            [
                new ChatMessage
                {
                    Id = "m0", SessionId = "s1", Role = ChatMessageRole.User,
                    Content = "Earlier question", CreatedAt = DateTime.UtcNow.AddMinutes(-2),
                },
            ],
        };
        var userMessage = new ChatMessage
        {
            Id = "m1", SessionId = "s1", Role = ChatMessageRole.User,
            Content = "What is a Zettelkasten?", CreatedAt = DateTime.UtcNow,
        };

        var prompt = InvokeBuildPrompt(session, userMessage, "Relevant knowledge base context:\n\nNote 1: Zettelkasten basics");

        Assert.Contains("What is a Zettelkasten?", prompt);
        Assert.Contains("Zettelkasten basics", prompt);
        Assert.Contains("Earlier question", prompt); // conversation history is rendered
        Assert.DoesNotContain("{{contextText}}", prompt);
        Assert.DoesNotContain("{{GetConversationHistory", prompt);
        Assert.DoesNotContain("{{userMessage.Content}}", prompt);
    }

    [Fact]
    public void BuildPrompt_IncludesSystemInstruction()
    {
        var session = new ChatSession { Id = "s1", Title = "T", Messages = [] };
        var userMessage = new ChatMessage
        {
            Id = "m1", SessionId = "s1", Role = ChatMessageRole.User,
            Content = "Hello", CreatedAt = DateTime.UtcNow,
        };

        var prompt = InvokeBuildPrompt(session, userMessage, "context");

        Assert.Contains("personal knowledge base", prompt);
        Assert.Contains("User's current message: \"Hello\"", prompt);
    }
}
