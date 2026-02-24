using Microsoft.Extensions.AI;

namespace ZettelWeb.Tests.Fakes;

/// <summary>
/// Fake IChatClient that returns preset responses for testing content generation
/// without making real LLM API calls.
///
/// Call order: 1 = blog post, 2 = editor feedback, 3+ = social posts.
/// </summary>
public class FakeChatClient : IChatClient
{
    private readonly string _blogResponse;
    private readonly string _editorResponse;
    private readonly string _socialResponse;
    private int _callCount;

    public int CallCount => _callCount;

    public FakeChatClient(
        string? blogResponse = null,
        string? editorResponse = null,
        string? socialResponse = null)
    {
        _blogResponse = blogResponse ?? """
            TITLE: Test Blog Post Title
            DESCRIPTION: A concise description of this test blog post for SEO purposes.
            TAGS: testing, dotnet, content

            This is the body of the test blog post. It has some content drawn from the notes.

            ## Key Ideas

            Here are the main ideas explored in this post.
            """;

        _editorResponse = editorResponse ?? """
            1. **Spelling & Grammar** — ✓ No issues found.
            2. **Sloppy thinking** — Consider expanding the section on key ideas with specific examples.
            3. **AI tells** — "Explore" in the opening paragraph reads as slightly generic; replace with a concrete claim.
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

        // Call 1 = blog post, call 2 = editor feedback, call 3+ = social posts
        var responseText = callIndex switch
        {
            1 => _blogResponse,
            2 => _editorResponse,
            _ => _socialResponse,
        };

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
