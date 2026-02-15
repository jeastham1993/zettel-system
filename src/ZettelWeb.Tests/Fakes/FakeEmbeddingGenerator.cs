using Microsoft.Extensions.AI;

namespace ZettelWeb.Tests.Fakes;

/// <summary>
/// Shared fake IEmbeddingGenerator that supports configurable results,
/// exceptions, model IDs, and callback hooks for mid-call assertions.
/// Replaces private FakeEmbeddingGenerator in EmbeddingBackgroundServiceTests,
/// EmbeddingHealthCheckTests, and SearchServiceIntegrationTests.
/// </summary>
public class FakeEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private readonly float[]? _result;
    private readonly Exception? _exception;
    private readonly Func<IServiceProvider, Task>? _onCall;
    private readonly EmbeddingGeneratorMetadata _metadata;

    public string? LastInput { get; private set; }
    public int CallCount { get; private set; }
    public IServiceProvider? ServiceProvider { get; set; }

    public FakeEmbeddingGenerator(
        float[]? result = null,
        Exception? exception = null,
        Func<IServiceProvider, Task>? onCall = null,
        string modelId = "fake-model")
    {
        _result = result;
        _exception = exception;
        _onCall = onCall;
        _metadata = new EmbeddingGeneratorMetadata(
            "FakeEmbeddingGenerator", defaultModelId: modelId);
    }

    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var input = values.First();
        CallCount++;
        LastInput = input;

        if (_onCall is not null && ServiceProvider is not null)
            await _onCall(ServiceProvider);

        if (_exception is not null)
            throw _exception;

        return new GeneratedEmbeddings<Embedding<float>>(
            [new Embedding<float>(_result!)]);
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceType == typeof(EmbeddingGeneratorMetadata))
            return _metadata;
        return serviceType?.IsInstanceOfType(this) is true ? this : null;
    }

    public void Dispose() { }
}

/// <summary>
/// Embedding generator that always throws, for testing fallback behavior.
/// </summary>
public class ThrowingEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw new HttpRequestException("Embedding API unavailable");

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType?.IsInstanceOfType(this) is true ? this : null;

    public void Dispose() { }
}
