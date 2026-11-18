using System.Diagnostics;
using PizzaTake.Models;
using PizzaTake.Repositories;

namespace PizzaTake.Services;

/// <summary>
/// OTP authentication service persisting session tokens in SecureStorage and verified profiles in SQLite.
/// </summary>
public sealed class AuthService : IAuthService
{
    private const string AuthTokenStorageKey = "pizzatake_auth_token";
    private const string AuthFlagPreferenceKey = "pizzatake_is_authenticated";

    private readonly HttpApiClient httpApiClient;
    private readonly IUserProfileRepository userProfileRepository;
    private bool? isAuthenticatedCache;

    public AuthService(HttpApiClient httpApiClient, IUserProfileRepository userProfileRepository)
    {
        this.httpApiClient = httpApiClient;
        this.userProfileRepository = userProfileRepository;
    }

    public bool IsAuthenticated
    {
        get
        {
            if (isAuthenticatedCache.HasValue)
                return isAuthenticatedCache.Value;

            isAuthenticatedCache = CheckPersistedAuthState();
            return isAuthenticatedCache.Value;
        }
    }

    public Task<bool> SendOtpAsync(string phone) => httpApiClient.SendOtpAsync(phone);

    public async Task<bool> VerifyOtpAsync(string phone, string code)
    {
        var verified = await httpApiClient.VerifyOtpAsync(phone, code);
        if (!verified)
            return false;

        var profile = await userProfileRepository.GetCurrentAsync() ?? new UserProfile();
        profile.Phone = phone.Trim();
        profile.IsPhoneVerified = true;
        profile.UpdatedAt = DateTime.UtcNow;
        await userProfileRepository.SaveAsync(profile);

        var sessionToken = Guid.NewGuid().ToString("N");
        try
        {
            await SecureStorage.Default.SetAsync(AuthTokenStorageKey, sessionToken);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AuthService] SecureStorage write failed: {ex.Message}");
        }

        Preferences.Default.Set(AuthFlagPreferenceKey, true);
        isAuthenticatedCache = true;
        return true;
    }

    public async Task LogoutAsync()
    {
        try
        {
            SecureStorage.Default.Remove(AuthTokenStorageKey);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AuthService] SecureStorage remove failed: {ex.Message}");
        }

        Preferences.Default.Remove(AuthFlagPreferenceKey);
        isAuthenticatedCache = false;

        var profile = await userProfileRepository.GetCurrentAsync();
        if (profile is not null)
        {
            profile.IsPhoneVerified = false;
            profile.UpdatedAt = DateTime.UtcNow;
            await userProfileRepository.SaveAsync(profile);
        }
    }

    private static bool CheckPersistedAuthState()
    {
        try
        {
            var token = Task.Run(() => SecureStorage.Default.GetAsync(AuthTokenStorageKey)).GetAwaiter().GetResult();
            if (!string.IsNullOrEmpty(token))
                return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AuthService] SecureStorage read failed: {ex.Message}");
        }

        return Preferences.Default.Get(AuthFlagPreferenceKey, false);
    }
}
