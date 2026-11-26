using Microsoft.Extensions.Logging;
using PizzaTake.Core.Models;

namespace PizzaTake.Core.Services;

/// <summary>
/// Centralized storage key constants for authentication tokens and session metadata stored in SecureStorage.
/// </summary>
internal static class AuthStorageKeys
{
    public const string AuthToken = "pizzatake_auth_token";
    public const string UserRole = "pizzatake_user_role";
    public const string UserPhone = "pizzatake_user_phone";
}

/// <summary>
/// Singleton session service that restores and maintains the active user's role and identity from SecureStorage.
/// </summary>
public sealed class SessionService : ISessionService
{
    private readonly ISecureStoreService secureStore;
    private readonly ILogger<SessionService>? logger;
    private readonly object stateLock = new();
    private bool isHydrated;

    private UserRole? currentRole;
    private string? currentUserPhone;
    private bool isAuthenticated;

    public SessionService(ISecureStoreService secureStore, ILogger<SessionService>? logger = null)
    {
        this.secureStore = secureStore;
        this.logger = logger;
    }

    public event EventHandler? SessionChanged;

    public UserRole? CurrentRole
    {
        get
        {
            EnsureHydrated();
            lock (stateLock)
                return currentRole;
        }
    }

    public string? CurrentUserPhone
    {
        get
        {
            EnsureHydrated();
            lock (stateLock)
                return currentUserPhone;
        }
    }

    public bool IsAuthenticated
    {
        get
        {
            EnsureHydrated();
            lock (stateLock)
                return isAuthenticated;
        }
    }

    public bool IsInRole(UserRole role)
    {
        EnsureHydrated();
        lock (stateLock)
            return isAuthenticated && currentRole == role;
    }

    public async Task InitializeAsync()
    {
        var token = await secureStore.GetAsync(AuthStorageKeys.AuthToken);
        var roleRaw = await secureStore.GetAsync(AuthStorageKeys.UserRole);
        var phone = await secureStore.GetAsync(AuthStorageKeys.UserPhone);

        lock (stateLock)
        {
            isHydrated = true;
            if (!string.IsNullOrWhiteSpace(token))
            {
                currentRole = Enum.TryParse<UserRole>(roleRaw, ignoreCase: true, out var parsedRole)
                    ? parsedRole
                    : UserRole.Customer;
                currentUserPhone = phone;
                isAuthenticated = true;
            }
            else
            {
                currentRole = null;
                currentUserPhone = null;
                isAuthenticated = false;
            }
        }

        logger?.LogInformation("Session initialized: IsAuthenticated={IsAuthenticated}, Role={Role}",
            isAuthenticated, currentRole);
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetSession(string phone, UserRole role)
    {
        lock (stateLock)
        {
            isHydrated = true;
            currentUserPhone = phone.Trim();
            currentRole = role;
            isAuthenticated = true;
        }

        logger?.LogInformation("Session started for role {Role}.", role);
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void LockSession()
    {
        lock (stateLock)
        {
            isHydrated = true;
            currentUserPhone = null;
            currentRole = null;
            isAuthenticated = false;
        }

        logger?.LogInformation("Session locked; secure credentials retained for biometric unlock.");
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearSession()
    {
        lock (stateLock)
        {
            isHydrated = true;
            currentUserPhone = null;
            currentRole = null;
            isAuthenticated = false;
        }

        logger?.LogInformation("Session cleared.");
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void EnsureHydrated()
    {
        lock (stateLock)
        {
            if (isHydrated)
                return;
        }

        try
        {
            Task.Run(InitializeAsync).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to hydrate session from SecureStorage.");
            lock (stateLock)
                isHydrated = true;
        }
    }
}
