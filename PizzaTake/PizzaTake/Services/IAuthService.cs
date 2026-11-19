namespace PizzaTake.Services;

/// <summary>
/// Manages user phone authentication and session persistence.
/// </summary>
public interface IAuthService
{
    bool IsAuthenticated { get; }
    Task<bool> SendOtpAsync(string phone);
    Task<bool> VerifyOtpAsync(string phone, string code);
    Task LogoutAsync();
}
