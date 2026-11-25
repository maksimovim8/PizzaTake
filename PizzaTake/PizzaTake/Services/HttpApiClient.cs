using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Models;

namespace PizzaTake.Services;

/// <summary>
/// HTTP client wrapper for synchronization and OTP authentication requests.
/// Falls back to local simulation when no remote API base address is configured.
/// </summary>
public sealed class HttpApiClient
{
    private readonly HttpClient httpClient;
    private readonly ILogger<HttpApiClient> logger;

    public HttpApiClient(ILogger<HttpApiClient>? logger = null)
    {
        this.logger = logger ?? NullLogger<HttpApiClient>.Instance;
        httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    public async Task<bool> PushSyncItemAsync(SyncQueueItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (httpClient.BaseAddress is null)
        {
            await Task.Delay(80);
            logger.LogInformation("Synced {EntityType} ({Operation}) via local simulation", item.EntityType, item.Operation);
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

    private static bool IsValidPhone(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return false;

        var digits = Regex.Replace(phone, @"\D", string.Empty);
        return digits.Length >= 9;
    }
}
