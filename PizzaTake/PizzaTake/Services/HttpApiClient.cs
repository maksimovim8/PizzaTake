using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PizzaTake.Models;

namespace PizzaTake.Services;

/// <summary>
/// HTTP client wrapper for synchronization and OTP authentication requests with SSL Certificate Pinning.
/// Falls back to local simulation with server timestamp tracking when no remote API base address is configured.
/// </summary>
public sealed class HttpApiClient : IDisposable
{
    /// <summary>
    /// Default SHA-256 certificate thumbprints pinned for the PizzaTake backend API (primary + backup pin).
    /// </summary>
    public static readonly string[] DefaultPinnedSha256Thumbprints =
    [
        "A1B2C3D4E5F60718293A4B5C6D7E8F90112233445566778899AABBCCDDEEFF01",
        "10FFEEDDCCBBAA99887766554433221109F8E7D6C5B4A39281706F5E4D3C2B1A"
    ];

    private readonly HttpClient httpClient;
    private readonly ILogger<HttpApiClient>? logger;
    private readonly HashSet<string> allowedSha256Pins;
    private readonly bool allowPinnedSelfSigned;
    private readonly ConcurrentDictionary<string, DateTime> simulatedRemoteTimestamps = new(StringComparer.Ordinal);

    public HttpApiClient(
        ILogger<HttpApiClient>? logger = null,
        IEnumerable<string>? pinnedSha256Thumbprints = null,
        Uri? baseAddress = null,
        bool allowPinnedSelfSigned = false)
    {
        this.logger = logger;
        this.allowPinnedSelfSigned = allowPinnedSelfSigned;

        var pinsSource = pinnedSha256Thumbprints ?? DefaultPinnedSha256Thumbprints;
        allowedSha256Pins = new HashSet<string>(
            pinsSource.Where(p => !string.IsNullOrWhiteSpace(p)).Select(NormalizeThumbprint),
            StringComparer.OrdinalIgnoreCase);

        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, certificate, _, sslPolicyErrors) =>
                ValidateServerCertificate(certificate, sslPolicyErrors)
        };

        httpClient = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(10),
            BaseAddress = baseAddress
        };
    }

    public IReadOnlyCollection<string> AllowedSha256Pins => allowedSha256Pins;

    /// <summary>
    /// Validates the presented server X.509 certificate against the pinned SHA-256 thumbprint allowlist.
    /// Rejects missing, untrusted, or mismatched certificates and logs an Error on failure.
    /// </summary>
    public bool ValidateServerCertificate(X509Certificate2? certificate, SslPolicyErrors sslPolicyErrors)
    {
        if (certificate is null)
        {
            logger?.LogError("SSL certificate pinning validation failed: server certificate is null.");
            return false;
        }

        var thumbprint = NormalizeThumbprint(certificate.GetCertHashString(HashAlgorithmName.SHA256));
        var isPinMatched = allowedSha256Pins.Contains(thumbprint);

        if (!isPinMatched)
        {
            logger?.LogError(
                "SSL certificate pinning validation failed. Presented SHA-256 thumbprint {Thumbprint} is not in allowed pins (PolicyErrors={PolicyErrors}).",
                thumbprint, sslPolicyErrors);
            return false;
        }

        if (sslPolicyErrors != SslPolicyErrors.None && !allowPinnedSelfSigned)
        {
            logger?.LogError(
                "SSL certificate pinning validation failed due to TLS policy errors {PolicyErrors} for thumbprint {Thumbprint}.",
                sslPolicyErrors, thumbprint);
            return false;
        }

        logger?.LogInformation("SSL certificate pinning validation succeeded for SHA-256 {Thumbprint}.", thumbprint);
        return true;
    }

    /// <summary>
    /// Demonstrates and verifies that a spoofed/unpinned certificate is rejected by SSL pinning validation.
    /// Returns true if the spoofed certificate was properly rejected.
    /// </summary>
    public bool VerifySpoofedCertificateRejected()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=spoofed.pizzatake.invalid", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var spoofedCert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

        var accepted = ValidateServerCertificate(spoofedCert, SslPolicyErrors.RemoteCertificateChainErrors);
        return !accepted;
    }

    public async Task<DateTime?> GetRemoteUpdatedAtAsync(string entityType, string entityKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityKey);

        var compositeKey = BuildCompositeKey(entityType, entityKey);
        if (httpClient.BaseAddress is null)
        {
            await Task.Delay(25);
            return simulatedRemoteTimestamps.TryGetValue(compositeKey, out var timestamp) ? timestamp : null;
        }

        try
        {
            var metadata = await httpClient.GetFromJsonAsync<RemoteSyncMetadata>(
                $"api/sync/metadata?entityType={Uri.EscapeDataString(entityType)}&entityKey={Uri.EscapeDataString(entityKey)}");
            return metadata?.UpdatedAtUtc;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "HttpApiClient.GetRemoteUpdatedAtAsync failed for {EntityType}:{EntityKey}.", entityType, entityKey);
            return null;
        }
    }

    public Task<bool> PushSyncItemAsync(SyncQueueItem item) =>
        PushSyncItemAsync(item, item.Id.ToString(), item.CreatedAt);

    public async Task<bool> PushSyncItemAsync(SyncQueueItem item, string entityKey, DateTime localUpdatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(item);

        var compositeKey = BuildCompositeKey(item.EntityType, entityKey);
        if (httpClient.BaseAddress is null)
        {
            await Task.Delay(80);
            simulatedRemoteTimestamps[compositeKey] = localUpdatedAtUtc;
            logger?.LogInformation("Synced {EntityType}:{EntityKey} ({Operation}) locally at {UpdatedAt:O}.",
                item.EntityType, entityKey, item.Operation, localUpdatedAtUtc);
            return true;
        }

        try
        {
            using var response = await httpClient.PostAsJsonAsync("api/sync", item);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "HttpApiClient.PushSyncItemAsync failed for {EntityType}:{EntityKey}.", item.EntityType, entityKey);
            return false;
        }
    }

    public async Task<bool> SendOtpAsync(string phone)
    {
        if (!IsValidPhone(phone))
            return false;

        if (httpClient.BaseAddress is null)
        {
            await Task.Delay(120);
            logger?.LogInformation("OTP code dispatched (local simulation).");
            return true;
        }

        try
        {
            using var response = await httpClient.PostAsJsonAsync("api/auth/send-otp", new { phone });
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "HttpApiClient.SendOtpAsync failed.");
            return false;
        }
    }

    public async Task<bool> VerifyOtpAsync(string phone, string code)
    {
        if (!IsValidPhone(phone) || string.IsNullOrWhiteSpace(code))
            return false;

        var trimmedCode = code.Trim();
        if (httpClient.BaseAddress is null)
        {
            await Task.Delay(120);
            return trimmedCode.Length == 4 && trimmedCode.All(char.IsDigit);
        }

        try
        {
            using var response = await httpClient.PostAsJsonAsync("api/auth/verify-otp", new { phone, code = trimmedCode });
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "HttpApiClient.VerifyOtpAsync failed.");
            return false;
        }
    }

    public void Dispose() => httpClient.Dispose();

    private static string NormalizeThumbprint(string thumbprint) =>
        thumbprint.Replace(":", string.Empty, StringComparison.Ordinal)
                  .Replace(" ", string.Empty, StringComparison.Ordinal)
                  .Trim()
                  .ToUpperInvariant();

    private static string BuildCompositeKey(string entityType, string entityKey) =>
        $"{entityType.Trim()}:{entityKey.Trim()}";

    private static bool IsValidPhone(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return false;

        var digits = Regex.Replace(phone, @"\D", string.Empty);
        return digits.Length >= 9;
    }

    private sealed record RemoteSyncMetadata(DateTime UpdatedAtUtc);
}
