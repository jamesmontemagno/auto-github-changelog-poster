using System.Net;
using System.Net.Sockets;

namespace AutoGithubChangelogPoster.Services;

/// <summary>
/// Guards against SSRF attacks when fetching external media.
/// Enforces HTTPS-only schemes, blocks private/reserved IP ranges,
/// enforces a download size cap, and maintains an allowlist of accepted media types.
/// </summary>
public static class MediaSsrfGuard
{
    /// <summary>Maximum bytes allowed when downloading a single media file (15 MiB).</summary>
    public const long MaxDownloadBytes = 15 * 1024 * 1024;

    private static readonly HashSet<string> AllowedSchemes =
        new(StringComparer.OrdinalIgnoreCase) { "https" };

    private static readonly HashSet<string> AllowedMediaTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "image/jpeg",
            "image/png",
            "image/gif",
            "image/webp",
            "image/bmp",
            "video/mp4",
            "video/quicktime",
        };

    /// <summary>Returns true when the URI scheme is permitted (https only).</summary>
    public static bool IsAllowedScheme(Uri uri) =>
        AllowedSchemes.Contains(uri.Scheme);

    /// <summary>
    /// Returns true when the content-type is on the accepted media allowlist.
    /// Parameters such as "; charset=utf-8" are stripped before comparison.
    /// </summary>
    public static bool IsAllowedMediaType(string contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }

        var baseType = contentType.Split(';')[0].Trim();
        return AllowedMediaTypes.Contains(baseType);
    }

    /// <summary>
    /// Returns true when the IP address falls inside a private, loopback,
    /// link-local, or otherwise reserved range that must never be reached by
    /// an outbound media fetch.
    /// </summary>
    public static bool IsPrivateOrReservedAddress(IPAddress address)
    {
        // Unwrap IPv4-mapped IPv6 addresses (e.g. ::ffff:192.168.1.1)
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => IsPrivateIPv4(address.GetAddressBytes()),
            AddressFamily.InterNetworkV6 => IsPrivateIPv6(address),
            _ => true, // Fail closed for unknown address families
        };
    }

    private static bool IsPrivateIPv4(byte[] b)
    {
        // Loopback 127.0.0.0/8
        if (b[0] == 127) return true;
        // Unspecified 0.0.0.0/8
        if (b[0] == 0) return true;
        // Private 10.0.0.0/8
        if (b[0] == 10) return true;
        // Private 172.16.0.0/12
        if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;
        // Private 192.168.0.0/16
        if (b[0] == 192 && b[1] == 168) return true;
        // Link-local 169.254.0.0/16 (covers Azure/AWS/GCP metadata IPs)
        if (b[0] == 169 && b[1] == 254) return true;
        // Carrier-grade NAT 100.64.0.0/10
        if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) return true;
        // Broadcast 255.255.255.255/32
        if (b[0] == 255) return true;
        // Documentation / TEST-NET ranges
        if (b[0] == 192 && b[1] == 0 && b[2] == 2) return true;
        if (b[0] == 198 && b[1] == 51 && b[2] == 100) return true;
        if (b[0] == 203 && b[1] == 0 && b[2] == 113) return true;

        return false;
    }

    private static bool IsPrivateIPv6(IPAddress address)
    {
        // Loopback ::1
        if (IPAddress.IsLoopback(address)) return true;
        // Unspecified ::
        if (address.Equals(IPAddress.IPv6Any)) return true;

        var b = address.GetAddressBytes();
        // Link-local fe80::/10
        if (b[0] == 0xfe && (b[1] & 0xc0) == 0x80) return true;
        // Unique-local fc00::/7 (fc::/8 and fd::/8)
        if ((b[0] & 0xfe) == 0xfc) return true;
        // Multicast ff00::/8
        if (b[0] == 0xff) return true;

        return false;
    }
}
