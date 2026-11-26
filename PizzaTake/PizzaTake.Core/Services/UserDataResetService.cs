using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Core.Models;

namespace PizzaTake.Core.Services;

public interface IUserDataResetService
{
    Task ResetForLogoutAsync();
}

/// <summary>
/// Clears account-owned data while preserving the shared city, pizzeria, and product catalog.
/// </summary>
public sealed class UserDataResetService : IUserDataResetService
{
    private readonly SqliteDatabase database;
    private readonly IAuthService authService;
    private readonly IAppStateService appState;
    private readonly ISettingsService settings;
    private readonly ILogger<UserDataResetService> logger;

    public UserDataResetService(
        SqliteDatabase database,
        IAuthService authService,
        IAppStateService appState,
        ISettingsService settings,
        ILogger<UserDataResetService>? logger = null)
    {
        this.database = database;
        this.authService = authService;
        this.appState = appState;
        this.settings = settings;
        this.logger = logger ?? NullLogger<UserDataResetService>.Instance;
    }

    public async Task ResetForLogoutAsync()
    {
        logger.LogInformation("Resetting account-owned local data for logout");
        try
        {
            await authService.LogoutAsync();
            await database.InitializeAsync();

            await database.Connection.RunInTransactionAsync(transaction =>
            {
                transaction.DeleteAll<UserProfile>();
                transaction.DeleteAll<UserAddress>();
                transaction.DeleteAll<CartItem>();
                transaction.DeleteAll<OrderItem>();
                transaction.DeleteAll<Order>();
                transaction.DeleteAll<FavoriteProduct>();
                transaction.DeleteAll<QualityReport>();
                transaction.DeleteAll<SyncQueueItem>();
            });

            settings.Clear();
            appState.SelectedCity = null;
            appState.SelectedPizzeria = null;
            appState.SelectedDeliveryType = DeliveryType.Delivery;
            appState.DraftFormState = new(StringComparer.Ordinal);
            appState.ApplySystemTheme();
            appState.SaveState();
            logger.LogInformation("Logout data reset completed successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Logout data reset failed");
            throw;
        }
    }
}
