using PizzaTake.Models;

namespace PizzaTake.Services;

/// <summary>
/// Manages user phone authentication, role assignment, and secure session persistence.
/// </summary>
public interface IAuthService
{
    bool IsAuthenticated { get; }
    UserRole? CurrentRole { get; }
    Task<bool> SendOtpAsync(string phone);
    Task<bool> VerifyOtpAsync(string phone, string code);
    Task<bool> HasSavedCredentialsAsync();
    Task<bool> RestoreSavedSessionAsync();
    Task LockSessionAsync();
    Task LogoutAsync();
}
