using PizzaTake.Models;

namespace PizzaTake.Repositories;

// Shared contract consumed by onboarding and implemented by the profile/security workstream.
public interface IUserProfileRepository
{
    Task<UserProfile?> GetCurrentAsync();
    Task SaveAsync(UserProfile profile);
}
