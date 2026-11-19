using Microsoft.Maui.Storage;
using PizzaTake.Models;

namespace PizzaTake.Services;

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

    public UserDataResetService(SqliteDatabase database, IAuthService authService, IAppStateService appState)
    {
        this.database = database;
        this.authService = authService;
        this.appState = appState;
    }

    public async Task ResetForLogoutAsync()
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
            transaction.DeleteAll<SyncQueueItem>();
        });

        Preferences.Default.Clear();
        appState.SelectedCity = null;
        appState.SelectedPizzeria = null;
        appState.SelectedDeliveryType = DeliveryType.Delivery;
        appState.DraftFormState = new(StringComparer.Ordinal);
        appState.ApplySystemTheme();
        appState.SaveState();
    }
}
