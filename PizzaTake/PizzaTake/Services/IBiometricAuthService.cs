namespace PizzaTake.Services;

/// <summary>
/// Result of a biometric authentication attempt.
/// </summary>
public sealed record BiometricAuthResult(bool IsSuccess, string? ErrorMessage = null);

/// <summary>
/// Platform abstraction for biometric authentication (Android BiometricPrompt, Windows Hello, iOS Face/Touch ID).
/// </summary>
public interface IBiometricAuthService
{
    Task<bool> IsAvailableAsync();
    Task<BiometricAuthResult> AuthenticateAsync(string reason);
}
