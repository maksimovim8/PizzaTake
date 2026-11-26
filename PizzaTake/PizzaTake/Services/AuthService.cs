using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    private readonly ILogger<AuthService> logger;
    private readonly ICrashReportingService? crashReporting;
    private bool? isAuthenticatedCache;

    public AuthService(
        HttpApiClient httpApiClient,
        IUserProfileRepository userProfileRepository,
        ILogger<AuthService>? logger = null,
        ICrashReportingService? crashReporting = null)
    {
        this.httpApiClient = httpApiClient;
        this.userProfileRepository = userProfileRepository;
        this.logger = logger ?? NullLogger<AuthService>.Instance;
        this.crashReporting = crashReporting;
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

    public async Task<bool> SendOtpAsync(string phone)
    {
        var sent = await httpApiClient.SendOtpAsync(phone);
        if (sent)
            logger.LogInformation("OTP verification code requested successfully");
        else
            logger.LogWarning("OTP verification code request was rejected");

        return sent;
    }

    public async Task<bool> VerifyOtpAsync(string phone, string code)
    {
        var verified = await httpApiClient.VerifyOtpAsync(phone, code);
        if (!verified)
        {
            logger.LogWarning("OTP verification failed for submitted code");
            return false;
        }

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
            logger.LogError(ex, "SecureStorage write failed while persisting auth session token");
        }

        Preferences.Default.Set(AuthFlagPreferenceKey, true);
        isAuthenticatedCache = true;
        logger.LogInformation("User authenticated successfully");
        crashReporting?.TrackEvent(CrashEventNames.LoginSuccess, new Dictionary<string, string>
        {
            ["method"] = "otp"
        });
        return true;
    }

    public Task LogoutAsync()
    {
        try
        {
            SecureStorage.Default.Remove(AuthTokenStorageKey);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SecureStorage remove failed while clearing auth session token");
        }

        Preferences.Default.Remove(AuthFlagPreferenceKey);
        isAuthenticatedCache = false;
        logger.LogInformation("User logged out and session cleared");
        return Task.CompletedTask;
    }

    private bool CheckPersistedAuthState()
    {
        try
        {
            var token = Task.Run(() => SecureStorage.Default.GetAsync(AuthTokenStorageKey)).GetAwaiter().GetResult();
            if (!string.IsNullOrEmpty(token))
                return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SecureStorage read failed while checking persisted auth state");
        }

        return Preferences.Default.Get(AuthFlagPreferenceKey, false);
    }
}
