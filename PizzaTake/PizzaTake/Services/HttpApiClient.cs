using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Models;

namespace PizzaTake.Services;

/// <summary>
/// HTTP client wrapper for synchronization and OTP authentication requests.
/// Falls back to local simulation with server timestamp tracking when no remote API base address is configured.
/// </summary>
public sealed class HttpApiClient
{
    private readonly HttpClient httpClient;
    private readonly ILogger<HttpApiClient> logger;
    private readonly ConcurrentDictionary<string, DateTime> simulatedRemoteTimestamps = new(StringComparer.Ordinal);

    public HttpApiClient(ILogger<HttpApiClient>? logger = null)
    {
        this.logger = logger ?? NullLogger<HttpApiClient>.Instance;
        httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
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
            logger.LogError(ex, "GetRemoteUpdatedAtAsync failed for {EntityType}:{EntityKey}", entityType, entityKey);
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
            logger.LogInformation("Synced {EntityType}:{EntityKey} ({Operation}) via local simulation at {UpdatedAt:O}",
                item.EntityType, entityKey, item.Operation, localUpdatedAtUtc);
            return true;
        }

        try
        {
            using var response = await httpClient.PostAsJsonAsync("api/sync", item);
            if (!response.IsSuccessStatusCode)
                logger.LogWarning("PushSyncItemAsync returned HTTP {StatusCode} for {EntityType}", (int)response.StatusCode, item.EntityType);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "PushSyncItemAsync failed for {EntityType} ({Operation})", item.EntityType, item.Operation);
            return false;
        }
    }

    public async Task<bool> SendOtpAsync(string phone)
    {
        if (!IsValidPhone(phone))
        {
            logger.LogWarning("SendOtpAsync rejected invalid phone format");
            return false;
        }

        if (httpClient.BaseAddress is null)
        {
            await Task.Delay(120);
            logger.LogInformation("Simulated OTP dispatch completed");
            return true;
        }

        try
        {
            using var response = await httpClient.PostAsJsonAsync("api/auth/send-otp", new { phone });
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SendOtpAsync request failed");
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
            logger.LogError(ex, "VerifyOtpAsync request failed");
            return false;
        }
    }

    private static string BuildCompositeKey(string entityType, string entityKey) =>
        $"{entityType.Trim()}:{entityKey.Trim()}";

    public static bool IsValidPhone(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return false;

        var trimmed = phone.Trim();
        var digits = Regex.Replace(trimmed, @"\D", string.Empty);
        if (trimmed.StartsWith('+'))
            return digits.Length == 12 && digits.StartsWith("380", StringComparison.Ordinal);

        return (digits.Length == 12 && digits.StartsWith("380", StringComparison.Ordinal))
            || (digits.Length == 10 && digits.StartsWith("0", StringComparison.Ordinal));
    }

    private sealed record RemoteSyncMetadata(DateTime UpdatedAtUtc);
}
