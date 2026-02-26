using System.Net;
using System.Net.Sockets;

namespace ZettelWeb.Services;

/// <summary>
/// Default implementation of <see cref="IUrlSafetyChecker"/>.
/// Resolves the URL's hostname via DNS and rejects any address that falls in
/// private/loopback/link-local ranges (SSRF protection).
/// </summary>
public class UrlSafetyChecker : IUrlSafetyChecker
{
    public async Task<bool> IsUrlSafeAsync(string url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme != "http" && uri.Scheme != "https")
            return false;

        try
        {
            var addresses = await ResolveHostAsync(uri.Host, cancellationToken);
            foreach (var addr in addresses)
            {
                if (IsPrivateAddress(addr))
                    return false;
            }
        }
        catch
        {
            return false;
        }

        return true;
    }

    public bool IsPrivateAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
            return true;

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            if ((bytes[0] & 0xFE) == 0xFC) return true; // fc00::/7 unique local
            if (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80) return true; // fe80::/10 link-local
            return false;
        }

        var ipBytes = address.GetAddressBytes();
        if (ipBytes[0] == 10) return true;                                          // 10.0.0.0/8
        if (ipBytes[0] == 172 && ipBytes[1] >= 16 && ipBytes[1] <= 31) return true; // 172.16.0.0/12
        if (ipBytes[0] == 192 && ipBytes[1] == 168) return true;                    // 192.168.0.0/16
        if (ipBytes[0] == 127) return true;                                          // 127.0.0.0/8
        if (ipBytes[0] == 169 && ipBytes[1] == 254) return true;                    // 169.254.0.0/16

        return false;
    }

    /// <summary>
    /// Resolves a hostname to IP addresses. Virtual to allow test overrides
    /// that skip real DNS lookups.
    /// </summary>
    protected virtual Task<IPAddress[]> ResolveHostAsync(string host, CancellationToken cancellationToken)
        => Dns.GetHostAddressesAsync(host, cancellationToken);
}
