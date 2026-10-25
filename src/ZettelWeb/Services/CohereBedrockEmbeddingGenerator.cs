using System.Text.Json;
using System.Text.Json.Serialization;
using Amazon.BedrockRuntime.Model;
using Microsoft.Extensions.AI;

namespace ZettelWeb.Services;

/// <summary>
/// Bedrock embedding generator for Cohere embed models (cohere.embed-english-v3,
/// cohere.embed-v4, or their cross-region inference-profile ARNs).
///
/// The bundled AWSSDK.Extensions.Bedrock.MEAI adapter only speaks the Titan
/// request format ("inputText"), so Cohere models need their own request
/// shape: { "texts": [...], "input_type": "search_document"|"search_query",
/// "embedding_types": ["float"], "dimensions": N (optional) }.
///
/// Input type matters for retrieval quality — the document/query call sites
/// pass it via EmbeddingGenerationOptions.AdditionalProperties["input_type"];
/// the default here is "search_document" (the common case: embedding stored
/// note content).
/// </summary>
public sealed class CohereBedrockEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private const int BatchSize = 96; // Cohere Bedrock max texts per request
    private const string DefaultInputType = "search_document";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly Func<InvokeModelRequest, CancellationToken, Task<InvokeModelResponse>> _invoke;
    private readonly string _modelId;
    private readonly int? _dimensions;

    public CohereBedrockEmbeddingGenerator(
        Func<InvokeModelRequest, CancellationToken, Task<InvokeModelResponse>> invoke,
        string modelId,
        int? dimensions = null)
    {
        _invoke = invoke;
        _modelId = modelId;
        _dimensions = dimensions;
    }

    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var texts = values.ToList();
        var inputType = DefaultInputType;
        if (options?.AdditionalProperties?.TryGetValue("input_type", out var type) == true
            && type?.ToString() is { Length: > 0 } typeString)
        {
            inputType = typeString;
        }

        var results = new List<Embedding<float>>(texts.Count);
        foreach (var batch in Chunk(texts, BatchSize))
        {
            var request = new InvokeModelRequest
            {
                ModelId = _modelId,
                ContentType = "application/json",
                Accept = "application/json",
                Body = new MemoryStream(
                    JsonSerializer.SerializeToUtf8Bytes(new CohereEmbeddingRequest(batch, inputType, _dimensions), SerializerOptions)),
            };

            var response = await _invoke(request, cancellationToken);
            using var doc = JsonDocument.Parse(response.Body);
            // embed-v4: with a single embedding_types entry ("float"), the
            // response is a flat array-of-arrays under "embeddings"
            // (response_type: embeddings_floats) — NOT the v3 nested
            // embeddings.float object.
            var floats = doc.RootElement.GetProperty("embeddings");

            foreach (var vector in floats.EnumerateArray())
            {
                var values2 = new float[vector.GetArrayLength()];
                var i = 0;
                foreach (var component in vector.EnumerateArray())
                {
                    values2[i++] = component.GetSingle();
                }
                results.Add(new Embedding<float>(values2));
            }
        }

        return new GeneratedEmbeddings<Embedding<float>>(results);
    }

    public object? GetService(Type serviceType, object? key = null) => null;

    public void Dispose()
    {
    }

    private static IEnumerable<List<T>> Chunk<T>(List<T> items, int size)
    {
        for (var i = 0; i < items.Count; i += size)
        {
            yield return items.GetRange(i, Math.Min(size, items.Count - i));
        }
    }

    private sealed class CohereEmbeddingRequest(IReadOnlyList<string> texts, string inputType, int? dimensions)
    {
        [JsonPropertyName("texts")]
        public IReadOnlyList<string> Texts { get; } = texts;

        [JsonPropertyName("input_type")]
        public string InputType { get; } = inputType;

        [JsonPropertyName("embedding_types")]
        public IReadOnlyList<string> EmbeddingTypes { get; } = ["float"];

        // embed-v4 field name is output_dimension (allowed 256/512/1024/1536,
        // default 1536) — NOT the v3-style "dimensions".
        [JsonPropertyName("output_dimension")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Dimensions { get; } = dimensions;
    }
}
