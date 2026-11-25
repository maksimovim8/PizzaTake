using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using PizzaTake.Models;

namespace PizzaTake.Services;

/// <summary>
/// HTTP client wrapper for synchronization and OTP authentication requests.
/// Falls back to local simulation with server timestamp tracking when no remote API base address is configured.
/// </summary>
public sealed class HttpApiClient
{
    private readonly HttpClient httpClient;
    private readonly ConcurrentDictionary<string, DateTime> simulatedRemoteTimestamps = new(StringComparer.Ordinal);

    public HttpApiClient()
    {
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
            Debug.WriteLine($"[HttpApiClient] GetRemoteUpdatedAtAsync failed: {ex.Message}");
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
            Debug.WriteLine($"[HttpApiClient] Synced {item.EntityType}:{entityKey} ({item.Operation}) locally at {localUpdatedAtUtc:O}.");
            return true;
        }

        try
        {
            using var response = await httpClient.PostAsJsonAsync("api/sync", item);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HttpApiClient] PushSyncItemAsync failed: {ex.Message}");
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
            Debug.WriteLine($"[HttpApiClient] OTP sent to {phone}.");
            return true;
        }

        try
        {
            using var response = await httpClient.PostAsJsonAsync("api/auth/send-otp", new { phone });
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HttpApiClient] SendOtpAsync failed: {ex.Message}");
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
            Debug.WriteLine($"[HttpApiClient] VerifyOtpAsync failed: {ex.Message}");
            return false;
        }
    }

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
