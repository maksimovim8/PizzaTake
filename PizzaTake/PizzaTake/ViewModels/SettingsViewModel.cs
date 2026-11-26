using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Models;
using PizzaTake.Repositories;
using PizzaTake.Services;

namespace PizzaTake.ViewModels;

/// <summary>
/// ViewModel for user account settings, theme selection, decrypted profile display, sync status, and address management.
/// </summary>
public sealed class SettingsViewModel : BaseViewModel, IQueryAttributable
{
    private readonly IUserProfileRepository userProfileRepository;
    private readonly IAddressRepository addressRepository;
    private readonly IAuthService authService;
    private readonly IUserDataResetService userDataResetService;
    private readonly IAppStateService appState;
    private readonly ISyncService syncService;
    private readonly ILogger<SettingsViewModel> logger;

    private bool isDarkTheme;
    private bool isLightTheme = true;
    private bool isSystemTheme;
    private string currentCityName = "Не обрано";
    private string maskedPhone = "Не вказано";
    private bool isPhoneVerified;
    private int pendingSyncCount;

    public SettingsViewModel(
        IUserProfileRepository userProfileRepository,
        IAddressRepository addressRepository,
        IAuthService authService,
        IAppStateService appState,
        IUserDataResetService userDataResetService,
        ISyncService syncService,
        ILogger<SettingsViewModel>? logger = null)
    {
        this.userProfileRepository = userProfileRepository;
        this.addressRepository = addressRepository;
        this.authService = authService;
        this.appState = appState;
        this.userDataResetService = userDataResetService;
        this.syncService = syncService;
        this.logger = logger ?? NullLogger<SettingsViewModel>.Instance;
        Title = "Налаштування";

        LoadSettingsCommand = new Command(async () => await LoadSettingsAsync());
        ChangeCityCommand = new Command(async () => await Shell.Current.GoToAsync("cities?returnTo=settings"));
        ToggleThemeCommand = new Command<string?>(ApplyThemeMode);
        ChangePhoneCommand = new Command(async () => await Shell.Current.GoToAsync("auth"));
        EditAddressCommand = new Command<UserAddress?>(async address => await EditAddressAsync(address));
        AddAddressCommand = new Command(async () => await EditAddressAsync(null));
        SyncNowCommand = new Command(async () => await SyncNowAsync());
        OpenLogsCommand = new Command(async () => await OpenLogsAsync());
        LogoutCommand = new Command(async () => await LogoutAsync(), () => IsPhoneVerified);

        this.syncService.SyncCompleted += async (_, _) => await RefreshSyncStatusAsync();

        Addresses.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasAddresses));
            OnPropertyChanged(nameof(HasNoAddresses));
        };
    }

    public ObservableCollection<UserAddress> Addresses { get; } = new();
    public bool HasAddresses => Addresses.Count > 0;
    public bool HasNoAddresses => Addresses.Count == 0;

    public bool IsDarkTheme
    {
        get => isDarkTheme;
        private set => SetProperty(ref isDarkTheme, value);
    }

    public bool IsLightTheme
    {
        get => isLightTheme;
        private set => SetProperty(ref isLightTheme, value);
    }

    public bool IsSystemTheme
    {
        get => isSystemTheme;
        private set => SetProperty(ref isSystemTheme, value);
    }

    public string CurrentCityName
    {
        get => currentCityName;
        private set => SetProperty(ref currentCityName, value);
    }

    public string MaskedPhone
    {
        get => maskedPhone;
        private set => SetProperty(ref maskedPhone, value);
    }

    public bool IsPhoneVerified
    {
        get => isPhoneVerified;
        private set
        {
            if (SetProperty(ref isPhoneVerified, value))
                ((Command)LogoutCommand).ChangeCanExecute();
        }
    }

    public int PendingSyncCount
    {
        get => pendingSyncCount;
        private set
        {
            if (SetProperty(ref pendingSyncCount, value))
                OnPropertyChanged(nameof(SyncStatusText));
        }
    }

    public string SyncStatusText => PendingSyncCount == 0
        ? "Усі дані синхронізовано"
        : $"У черзі офлайн-змін: {PendingSyncCount}";

    public ICommand LoadSettingsCommand { get; }
    public ICommand ChangeCityCommand { get; }
    public ICommand ToggleThemeCommand { get; }
    public ICommand ChangePhoneCommand { get; }
    public ICommand EditAddressCommand { get; }
    public ICommand AddAddressCommand { get; }
    public ICommand SyncNowCommand { get; }
    public ICommand OpenLogsCommand { get; }
    public ICommand LogoutCommand { get; }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("UpdatedAddress", out var updatedObj) && updatedObj is UserAddress updated)
        {
            var existing = Addresses.FirstOrDefault(a => a.Id == updated.Id);
            if (existing is not null)
            {
                var index = Addresses.IndexOf(existing);
                Addresses[index] = updated;
            }
            else
            {
                Addresses.Insert(0, updated);
            }
            OnPropertyChanged(nameof(HasNoAddresses));
        }
        else if (query.TryGetValue("DeletedAddressId", out var deletedIdObj) &&
                 int.TryParse(deletedIdObj?.ToString(), out var deletedId))
        {
            var existing = Addresses.FirstOrDefault(a => a.Id == deletedId);
            if (existing is not null)
                Addresses.Remove(existing);
            OnPropertyChanged(nameof(HasNoAddresses));
        }
    }

    public async Task LoadSettingsAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        try
        {
            SyncThemeFlags();

            var profile = await userProfileRepository.GetCurrentAsync();
            if (profile is not null)
            {
                CurrentCityName = string.IsNullOrWhiteSpace(profile.CityName) ? "Не обрано" : profile.CityName;
                MaskedPhone = MaskPhone(profile.Phone);
                IsPhoneVerified = profile.IsPhoneVerified && authService.IsAuthenticated;
            }
            else
            {
                CurrentCityName = "Не обрано";
                MaskedPhone = "Не вказано";
                IsPhoneVerified = false;
            }

            var savedAddresses = await addressRepository.GetAllAsync();
            Addresses.Clear();
            foreach (var address in savedAddresses)
                Addresses.Add(address);
            OnPropertyChanged(nameof(HasNoAddresses));

            PendingSyncCount = await syncService.GetPendingCountAsync();
            logger.LogInformation("Settings loaded with {AddressCount} saved address(es)", Addresses.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load user settings");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SyncNowAsync()
    {
        await syncService.SyncPendingAsync();
        await RefreshSyncStatusAsync();
    }

    private async Task RefreshSyncStatusAsync()
    {
        var count = await syncService.GetPendingCountAsync();
        await MainThread.InvokeOnMainThreadAsync(() => PendingSyncCount = count);
    }

    private void ApplyThemeMode(string? mode)
    {
        var selectedMode = string.IsNullOrWhiteSpace(mode)
            ? IsDarkTheme ? "Light" : "Dark"
            : mode;

        switch (selectedMode)
        {
            case "Dark":
                appState.ApplyTheme(true);
                break;
            case "Light":
                appState.ApplyTheme(false);
                break;
            default:
                appState.ApplySystemTheme();
                break;
        }

        SyncThemeFlags();
        logger.LogInformation("Application theme changed to {ThemeMode}", selectedMode);
    }

    private void SyncThemeFlags()
    {
        IsLightTheme = !appState.IsDarkTheme && !appState.IsSystemTheme;
        IsDarkTheme = appState.IsDarkTheme;
        IsSystemTheme = appState.IsSystemTheme;
    }

    private static async Task EditAddressAsync(UserAddress? address)
    {
        var parameters = new Dictionary<string, object>();
        if (address is not null)
            parameters["Address"] = address;

        await Shell.Current.GoToAsync("addressedit", parameters);
    }

    private async Task OpenLogsAsync()
    {
        logger.LogInformation("Opening local logs diagnostics screen");
        await Shell.Current.GoToAsync("logs");
    }

    private async Task LogoutAsync()
    {
        try
        {
            await userDataResetService.ResetForLogoutAsync();
            Addresses.Clear();
            OnPropertyChanged(nameof(HasNoAddresses));
            CurrentCityName = "Не обрано";
            MaskedPhone = "Не вказано";
            IsPhoneVerified = false;
            SyncThemeFlags();

            logger.LogInformation("User logged out from SettingsPage");
            await Shell.Current.GoToAsync("///welcome-start");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Logout cleanup failed");
            await Shell.Current.DisplayAlertAsync("Не вдалося вийти", "Не вдалося очистити дані облікового запису. Спробуйте ще раз.", "OK");
        }
    }

    private static string MaskPhone(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return "Не вказано";

        var trimmed = phone.Trim();
        if (trimmed.Length <= 6)
            return trimmed;

        return $"{trimmed[..4]} *** **{trimmed[^2..]}";
    }
}
