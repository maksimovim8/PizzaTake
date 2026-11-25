using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PizzaTake.Models;
using PizzaTake.Repositories;

namespace PizzaTake.Services;

/// <summary>
/// OTP authentication service assigning user roles, persisting tokens/roles exclusively in SecureStorage,
/// and synchronizing active session state via ISessionService.
/// </summary>
public sealed class AuthService : IAuthService
{
    public const string AdminPhone = "+380990000001";
    private const string AdminPhoneDigits = "380990000001";

    private readonly HttpApiClient httpApiClient;
    private readonly IUserProfileRepository userProfileRepository;
    private readonly ISecureStoreService secureStore;
    private readonly ISessionService sessionService;
    private readonly ILogger<AuthService>? logger;

    public AuthService(
        HttpApiClient httpApiClient,
        IUserProfileRepository userProfileRepository,
        ISecureStoreService secureStore,
        ISessionService sessionService,
        ILogger<AuthService>? logger = null)
    {
        this.httpApiClient = httpApiClient;
        this.userProfileRepository = userProfileRepository;
        this.secureStore = secureStore;
        this.sessionService = sessionService;
        this.logger = logger;
    }

    public bool IsAuthenticated => sessionService.IsAuthenticated;

    public UserRole? CurrentRole => sessionService.CurrentRole;

    public Task<bool> SendOtpAsync(string phone) => httpApiClient.SendOtpAsync(phone);

    public async Task<bool> VerifyOtpAsync(string phone, string code)
    {
        var verified = await httpApiClient.VerifyOtpAsync(phone, code);
        if (!verified)
        {
            logger?.LogWarning("OTP verification failed for submitted phone number.");
            return false;
        }

        var normalizedPhone = phone.Trim();
        var role = ResolveRoleForPhone(normalizedPhone);

        var profile = await userProfileRepository.GetCurrentAsync() ?? new UserProfile();
        profile.Phone = normalizedPhone;
        profile.IsPhoneVerified = true;
        profile.Role = role;
        profile.UpdatedAt = DateTime.UtcNow;
        await userProfileRepository.SaveAsync(profile);

        var sessionToken = Guid.NewGuid().ToString("N");
        await secureStore.SetAsync(AuthStorageKeys.AuthToken, sessionToken);
        await secureStore.SetAsync(AuthStorageKeys.UserRole, role.ToString());
        await secureStore.SetAsync(AuthStorageKeys.UserPhone, normalizedPhone);

        sessionService.SetSession(normalizedPhone, role);
        logger?.LogInformation("User authenticated via OTP with role {Role}.", role);
        return true;
    }

    public async Task<bool> HasSavedCredentialsAsync()
    {
        var token = await secureStore.GetAsync(AuthStorageKeys.AuthToken);
        return !string.IsNullOrWhiteSpace(token);
    }

    public async Task<bool> RestoreSavedSessionAsync()
    {
        var token = await secureStore.GetAsync(AuthStorageKeys.AuthToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            logger?.LogWarning("Cannot restore session: no saved token in SecureStorage.");
            return false;
        }

        var roleRaw = await secureStore.GetAsync(AuthStorageKeys.UserRole);
        var phone = await secureStore.GetAsync(AuthStorageKeys.UserPhone);

        if (string.IsNullOrWhiteSpace(phone))
        {
            var profile = await userProfileRepository.GetCurrentAsync();
            phone = profile?.Phone ?? string.Empty;
        }

        var role = Enum.TryParse<UserRole>(roleRaw, ignoreCase: true, out var parsedRole)
            ? parsedRole
            : ResolveRoleForPhone(phone);

        sessionService.SetSession(phone, role);
        logger?.LogInformation("Session restored from SecureStorage with role {Role}.", role);
        return true;
    }

    public Task LockSessionAsync()
    {
        sessionService.LockSession();
        return Task.CompletedTask;
    }

    public Task LogoutAsync()
    {
        secureStore.Remove(AuthStorageKeys.AuthToken);
        secureStore.Remove(AuthStorageKeys.UserRole);
        secureStore.Remove(AuthStorageKeys.UserPhone);
        sessionService.ClearSession();
        logger?.LogInformation("User logged out and SecureStorage credentials removed.");
        return Task.CompletedTask;
    }

    public static UserRole ResolveRoleForPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return UserRole.Customer;

        var digits = Regex.Replace(phone, @"\D", string.Empty);
        if (digits == AdminPhoneDigits || digits == "0990000001")
            return UserRole.Administrator;

        return UserRole.Customer;
    }
}
