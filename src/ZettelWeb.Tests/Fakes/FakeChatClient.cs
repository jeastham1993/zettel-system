using Microsoft.Extensions.AI;

namespace ZettelWeb.Tests.Fakes;

/// <summary>
/// Fake IChatClient that returns preset responses for testing content generation
/// without making real LLM API calls.
/// </summary>
public class FakeChatClient : IChatClient
{
    private readonly string _blogResponse;
    private readonly string _socialResponse;
    private int _callCount;

    public int CallCount => _callCount;

    public FakeChatClient(
        string? blogResponse = null,
        string? socialResponse = null)
    {
        _blogResponse = blogResponse ?? """
            # Test Blog Post Title

            This is the body of the test blog post. It has some content drawn from the notes.

            ## Key Ideas

            Here are the main ideas explored in this post.
            """;

        _socialResponse = socialResponse ?? """
            This is the first test social media post about an interesting idea.
            ---
            Here is a second take — a question to make people think.
            ---
            Hot take: this is the third social post with a strong opinion.
            """;
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var callIndex = Interlocked.Increment(ref _callCount);

        // First call generates the blog post, second generates social posts
        var responseText = callIndex == 1 ? _blogResponse : _socialResponse;

        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText));
        return Task.FromResult(response);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Streaming not used in content generation.");

    public object? GetService(Type serviceType, object? serviceKey = null)
        => serviceType?.IsInstanceOfType(this) is true ? this : null;

    public void Dispose() { }
}
