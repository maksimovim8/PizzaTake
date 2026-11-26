using PizzaTake.Core.Models;

namespace PizzaTake.Core.Services;

/// <summary>
/// Singleton session service tracking the authenticated user's phone, role, and authorization state.
/// </summary>
public interface ISessionService
{
    UserRole? CurrentRole { get; }
    string? CurrentUserPhone { get; }
    bool IsAuthenticated { get; }
    bool IsInRole(UserRole role);
    event EventHandler? SessionChanged;

    Task InitializeAsync();
    void SetSession(string phone, UserRole role);
    void LockSession();
    void ClearSession();
}
