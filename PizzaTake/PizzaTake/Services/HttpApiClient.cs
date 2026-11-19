using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using PizzaTake.Models;

namespace PizzaTake.Services;

/// <summary>
/// HTTP client wrapper for synchronization and OTP authentication requests.
/// Falls back to local simulation when no remote API base address is configured.
/// </summary>
public sealed class HttpApiClient
{
    private readonly HttpClient httpClient;

    public HttpApiClient()
    {
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
            Debug.WriteLine($"[HttpApiClient] Synced {item.EntityType} ({item.Operation}) locally.");
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

    private static bool IsValidPhone(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return false;

        var digits = Regex.Replace(phone, @"\D", string.Empty);
        return digits.Length >= 9;
    }
}
