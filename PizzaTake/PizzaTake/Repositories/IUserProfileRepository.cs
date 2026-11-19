using PizzaTake.Models;

namespace PizzaTake.Repositories;

/// <summary>
/// Data access contract for the current user profile with encrypted PII fields.
/// </summary>
public interface IUserProfileRepository
{
    Task<UserProfile?> GetCurrentAsync();
    Task<int> SaveAsync(UserProfile profile);
}
