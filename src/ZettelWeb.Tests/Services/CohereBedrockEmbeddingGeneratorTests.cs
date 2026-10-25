using System.Text.Json;
using Amazon.BedrockRuntime.Model;
using Microsoft.Extensions.AI;
using ZettelWeb.Services;

namespace ZettelWeb.Tests.Services;

public class CohereBedrockEmbeddingGeneratorTests
{
    private const string ModelId = "arn:aws:bedrock:eu-west-1:469909854323:inference-profile/global.cohere.embed-v4:0";

    private sealed class FakeBedrock
    {
        public InvokeModelRequest? Captured;
        public string ResponseBody { get; set; } = """{"id":"x","embeddings":[[0.1,0.2]],"response_type":"embeddings_floats"}""";

        public CohereBedrockEmbeddingGenerator Build(int? dimensions = 1024) =>
            new((req, _) =>
            {
                Captured = req;
                return Task.FromResult(new InvokeModelResponse
                {
                    Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(ResponseBody)),
                });
            }, ModelId, dimensions);

        public async Task<JsonDocument> RequestBodyAsync()
        {
            Assert.NotNull(Captured);
            Captured!.Body.Position = 0;
            return await JsonDocument.ParseAsync(Captured.Body);
        }
    }

    [Fact]
    public async Task GenerateAsync_SendsCohereRequestFormat()
    {
        var bedrock = new FakeBedrock();
        var generator = bedrock.Build();

        await generator.GenerateAsync(["some note text"]);

        Assert.Equal(ModelId, bedrock.Captured!.ModelId);
        Assert.Equal("application/json", bedrock.Captured.ContentType);

        var body = await bedrock.RequestBodyAsync();
        var root = body.RootElement;

        Assert.Equal(JsonValueKind.Array, root.GetProperty("texts").ValueKind);
        Assert.Equal("some note text", root.GetProperty("texts")[0].GetString());
        Assert.Equal("search_document", root.GetProperty("input_type").GetString());
        Assert.Equal("float", root.GetProperty("embedding_types")[0].GetString());
        // embed-v4 field is output_dimension, NOT "dimensions" — an unknown
        // field makes Bedrock reject the whole request as malformed
        Assert.Equal(1024, root.GetProperty("output_dimension").GetInt32());
        Assert.False(root.TryGetProperty("dimensions", out _));
    }

    [Fact]
    public async Task GenerateAsync_ParsesFloatVectors()
    {
        var bedrock = new FakeBedrock
        {
            // embed-v4 single-type response: flat array under "embeddings",
            // not the v3 nested {"float": [...]} object
            ResponseBody = """{"id":"x","embeddings":[[0.25,-0.75],[1.0,0.5]],"response_type":"embeddings_floats"}""",
        };
        var generator = bedrock.Build();

        var result = await generator.GenerateAsync(["first", "second"]);

        Assert.Equal(2, result.Count);
        Assert.Equal([0.25f, -0.75f], result[0].Vector.ToArray());
        Assert.Equal([1.0f, 0.5f], result[1].Vector.ToArray());
    }

    [Fact]
    public async Task GenerateAsync_ParsesNestedFloatVectors()
    {
        // The shape production Bedrock actually returns for embed-v4 with a
        // single embedding type — embeddings nested by type (see BUG-011).
        var bedrock = new FakeBedrock
        {
            ResponseBody = """{"id":"x","embeddings":{"float":[[0.25,-0.75],[1.0,0.5]]}}""",
        };
        var generator = bedrock.Build();

        var result = await generator.GenerateAsync(["first", "second"]);

        Assert.Equal(2, result.Count);
        Assert.Equal([0.25f, -0.75f], result[0].Vector.ToArray());
        Assert.Equal([1.0f, 0.5f], result[1].Vector.ToArray());
    }

    [Fact]
    public async Task GenerateAsync_UsesQueryInputTypeFromOptions()
    {
        var bedrock = new FakeBedrock();
        var generator = bedrock.Build();

        await generator.GenerateAsync(
            ["find me something"],
            new EmbeddingGenerationOptions
            {
                AdditionalProperties = new AdditionalPropertiesDictionary { ["input_type"] = "search_query" },
            });

        var body = await bedrock.RequestBodyAsync();
        Assert.Equal("search_query", body.RootElement.GetProperty("input_type").GetString());
    }

    [Fact]
    public async Task GenerateAsync_OmitsDimensionsWhenNull()
    {
        var bedrock = new FakeBedrock();
        var generator = bedrock.Build(dimensions: null);

        await generator.GenerateAsync(["some note text"]);

        var body = await bedrock.RequestBodyAsync();
        Assert.False(body.RootElement.TryGetProperty("output_dimension", out _));
        Assert.False(body.RootElement.TryGetProperty("dimensions", out _));
    }
}
