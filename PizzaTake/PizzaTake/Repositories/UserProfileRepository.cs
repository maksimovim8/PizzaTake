using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Models;
using PizzaTake.Services;

namespace PizzaTake.Repositories;

/// <summary>
/// SQLite repository for user profiles that transparently encrypts Phone and CityName at rest.
/// </summary>
public sealed class UserProfileRepository : SqliteRepositoryBase, IUserProfileRepository
{
    private readonly IEncryptionService encryptionService;
    private readonly ILogger<UserProfileRepository> logger;

    public UserProfileRepository(
        SqliteDatabase database,
        IEncryptionService encryptionService,
        ILogger<UserProfileRepository>? logger = null)
        : base(database)
    {
        this.encryptionService = encryptionService;
        this.logger = logger ?? NullLogger<UserProfileRepository>.Instance;
    }

    public async Task<UserProfile?> GetCurrentAsync()
    {
        var connection = await GetConnectionAsync();
        var stored = await connection.Table<UserProfile>()
            .OrderByDescending(p => p.UpdatedAt)
            .FirstOrDefaultAsync();

        if (stored is null)
            return null;

        return new UserProfile
        {
            Id = stored.Id,
            Phone = DecryptIfNotEmpty(stored.Phone),
            CityName = DecryptIfNotEmpty(stored.CityName),
            IsPhoneVerified = stored.IsPhoneVerified,
            UpdatedAt = stored.UpdatedAt
        };
    }

    public async Task<int> SaveAsync(UserProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var connection = await GetConnectionAsync();
        var targetId = profile.Id;
        if (targetId == 0)
        {
            var existing = await connection.Table<UserProfile>().FirstOrDefaultAsync();
            if (existing is not null)
                targetId = existing.Id;
        }

        var updatedAt = profile.UpdatedAt == default ? DateTime.UtcNow : profile.UpdatedAt;

        // Persist encrypted fields without mutating the caller's plaintext model instance.
        var entity = new UserProfile
        {
            Id = targetId,
            Phone = EncryptIfNotEmpty(profile.Phone),
            CityName = EncryptIfNotEmpty(profile.CityName),
            IsPhoneVerified = profile.IsPhoneVerified,
            UpdatedAt = updatedAt
        };

        var rows = await InsertOrUpdateAsync(connection, entity, entity.Id);
        profile.Id = entity.Id;
        profile.UpdatedAt = updatedAt;
        logger.LogInformation("Saved encrypted user profile (Id={ProfileId}, Verified={IsVerified})", profile.Id, profile.IsPhoneVerified);
        return rows;
    }

    private string EncryptIfNotEmpty(string value) =>
        string.IsNullOrEmpty(value) ? string.Empty : encryptionService.Encrypt(value);

    private string DecryptIfNotEmpty(string value) =>
        string.IsNullOrEmpty(value) ? string.Empty : encryptionService.Decrypt(value);
}
