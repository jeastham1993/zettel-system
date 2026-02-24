using ZettelWeb.Models;

namespace ZettelWeb.Services.Publishing;

/// <summary>Sends an approved content piece to an external publishing destination as a draft.</summary>
public interface IPublishingService
{
    /// <summary>True when the service has the credentials needed to publish.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Sends <paramref name="piece"/> to the destination as a draft.
    /// Returns a reference string (URL or job ID) for the created draft.
    /// </summary>
    Task<string> SendToDraftAsync(ContentPiece piece, CancellationToken ct = default);
}
