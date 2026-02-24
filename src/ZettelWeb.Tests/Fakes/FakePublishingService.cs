using ZettelWeb.Models;
using ZettelWeb.Services.Publishing;

namespace ZettelWeb.Tests.Fakes;

/// <summary>
/// Fake IPublishingService that returns a predictable draft reference for testing.
/// Always reports as configured and returns a deterministic URL based on the piece ID.
/// </summary>
public class FakePublishingService : IPublishingService
{
    public bool IsConfigured => true;

    public Task<string> SendToDraftAsync(ContentPiece piece, CancellationToken ct = default)
        => Task.FromResult($"https://fake.draft.example.com/{piece.Id}");
}
