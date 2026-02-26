namespace ZettelWeb.Services;

/// <summary>
/// Validates URLs before fetching — guards against SSRF by resolving the
/// target hostname and rejecting requests to private/loopback addresses.
///
/// LIMITATION: This guard checks the hostname at call time (DNS resolution).
/// It does not protect against HTTP redirect chains that resolve to private
/// addresses after the check passes (DNS rebinding / TOCTOU). Any future
/// server-side HTTP fetch must re-validate the final destination IP after
/// following redirects.
/// </summary>
public interface IUrlSafetyChecker
{
    /// <summary>
    /// Returns true only if the URL is http/https and resolves to a public IP address.
    /// </summary>
    Task<bool> IsUrlSafeAsync(string url, CancellationToken cancellationToken);
}
