using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Core.Models;
using PizzaTake.Core.Repositories;
using PizzaTake.Core.Services;

namespace PizzaTake.Core.ViewModels;

/// <summary>
/// ViewModel for user account settings, role display, theme selection, decrypted profile display,
/// sync status, address management, biometric session lock, and administrator logs access.
/// </summary>
public sealed class SettingsViewModel : BaseViewModel, IQueryParameterReceiver
{
    private readonly IUserProfileRepository userProfileRepository;
    private readonly IAddressRepository addressRepository;
    private readonly IAuthService authService;
    private readonly ISessionService sessionService;
    private readonly IUserDataResetService userDataResetService;
    private readonly IAppStateService appState;
    private readonly ISyncService syncService;
    private readonly INavigationService navigation;
    private readonly IDialogService dialogService;
    private readonly IMainThreadDispatcher dispatcher;
    private readonly IBugReportService? bugReportService;
    private readonly ILogger<SettingsViewModel> logger;

    private bool isDarkTheme;
    private bool isLightTheme = true;
    private bool isSystemTheme;
    private string currentCityName = "Не обрано";
    private string maskedPhone = "Не вказано";
    private bool isPhoneVerified;
    private bool isSessionLocked;
    private int pendingSyncCount;
    private bool isReportingBug;

    public SettingsViewModel(
        IUserProfileRepository userProfileRepository,
        IAddressRepository addressRepository,
        IAuthService authService,
        ISessionService sessionService,
        IAppStateService appState,
        IUserDataResetService userDataResetService,
        ISyncService syncService,
        INavigationService navigation,
        IDialogService dialogService,
        IMainThreadDispatcher dispatcher,
        IBugReportService? bugReportService = null,
        ILogger<SettingsViewModel>? logger = null)
    {
        this.userProfileRepository = userProfileRepository;
        this.addressRepository = addressRepository;
        this.authService = authService;
        this.sessionService = sessionService;
        this.appState = appState;
        this.userDataResetService = userDataResetService;
        this.syncService = syncService;
        this.navigation = navigation;
        this.dialogService = dialogService;
        this.dispatcher = dispatcher;
        this.bugReportService = bugReportService;
        this.logger = logger ?? NullLogger<SettingsViewModel>.Instance;
        Title = "Налаштування";

        this.sessionService.SessionChanged += (_, _) => this.dispatcher.BeginInvokeOnMainThread(() =>
        {
            OnPropertyChanged(nameof(IsAdmin));
            OnPropertyChanged(nameof(CurrentRoleText));
        });

        LoadSettingsCommand = new AsyncRelayCommand(LoadSettingsAsync);
        ChangeCityCommand = new AsyncRelayCommand(() => navigation.GoToAsync("cities?returnTo=settings"));
        ToggleThemeCommand = new RelayCommand<string?>(ApplyThemeMode);
        ChangePhoneCommand = new AsyncRelayCommand(() => navigation.GoToAsync(IsSessionLocked ? "auth?locked=true" : "auth"));
        EditAddressCommand = new AsyncRelayCommand<UserAddress?>(EditAddressAsync);
        AddAddressCommand = new AsyncRelayCommand(() => EditAddressAsync(null));
        SyncNowCommand = new AsyncRelayCommand(SyncNowAsync);
        ReportBugCommand = new AsyncRelayCommand(ReportBugAsync, () => !isReportingBug);
        OpenMapCommand = new AsyncRelayCommand(() => navigation.GoToAsync("pizzeriasmap"));
        OpenQualityReportCommand = new AsyncRelayCommand(() => navigation.GoToAsync("qualityreport"));
        OpenLogsCommand = new AsyncRelayCommand(OpenLogsAsync);
        LockSessionCommand = new AsyncRelayCommand(LockSessionAsync, () => IsPhoneVerified);
        UnlockSessionCommand = new AsyncRelayCommand(() => navigation.GoToAsync("auth?locked=true"), () => IsSessionLocked);
        LogoutCommand = new AsyncRelayCommand(LogoutAsync, () => HasAccountSession);

        this.syncService.SyncCompleted += (_, _) => _ = RefreshSyncStatusSafeAsync();

        Addresses.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasAddresses));
            OnPropertyChanged(nameof(HasNoAddresses));
        };
    }

    public ObservableCollection<UserAddress> Addresses { get; } = new();
    public bool HasAddresses => !IsSessionLocked && Addresses.Count > 0;
    public bool HasNoAddresses => !IsSessionLocked && Addresses.Count == 0;

    public bool IsAdmin => sessionService.IsInRole(UserRole.Administrator);

    public string CurrentRoleText => IsSessionLocked
        ? "Сесію заблоковано (потрібне розблокування)"
        : sessionService.CurrentRole switch
        {
            UserRole.Administrator => "Роль: Адміністратор",
            UserRole.Customer => "Роль: Клієнт",
            _ => "Роль: Гість"
        };

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
            {
                OnPropertyChanged(nameof(HasAccountSession));
                LockSessionCommand.NotifyCanExecuteChanged();
                LogoutCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsSessionLocked
    {
        get => isSessionLocked;
        private set
        {
            if (SetProperty(ref isSessionLocked, value))
            {
                OnPropertyChanged(nameof(HasAccountSession));
                OnPropertyChanged(nameof(CurrentRoleText));
                OnPropertyChanged(nameof(HasAddresses));
                OnPropertyChanged(nameof(HasNoAddresses));
                OnPropertyChanged(nameof(CanManageAddresses));
                UnlockSessionCommand.NotifyCanExecuteChanged();
                LogoutCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool HasAccountSession => IsPhoneVerified || IsSessionLocked;
    public bool CanManageAddresses => !IsSessionLocked;

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

    public IAsyncRelayCommand LoadSettingsCommand { get; }
    public IAsyncRelayCommand ChangeCityCommand { get; }
    public IRelayCommand<string?> ToggleThemeCommand { get; }
    public IAsyncRelayCommand ChangePhoneCommand { get; }
    public IAsyncRelayCommand<UserAddress?> EditAddressCommand { get; }
    public IAsyncRelayCommand AddAddressCommand { get; }
    public IAsyncRelayCommand SyncNowCommand { get; }
    public IAsyncRelayCommand ReportBugCommand { get; }
    public IAsyncRelayCommand OpenMapCommand { get; }
    public IAsyncRelayCommand OpenQualityReportCommand { get; }
    public IAsyncRelayCommand OpenLogsCommand { get; }
    public IAsyncRelayCommand LockSessionCommand { get; }
    public IAsyncRelayCommand UnlockSessionCommand { get; }
    public IAsyncRelayCommand LogoutCommand { get; }

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
            var hasSavedCredentials = await authService.HasSavedCredentialsAsync();
            var locked = hasSavedCredentials && !authService.IsAuthenticated;
            IsSessionLocked = locked;

            OnPropertyChanged(nameof(IsAdmin));
            OnPropertyChanged(nameof(CurrentRoleText));

            var profile = await userProfileRepository.GetCurrentAsync();
            if (locked)
            {
                CurrentCityName = string.IsNullOrWhiteSpace(profile?.CityName) ? "Не обрано" : profile.CityName;
                MaskedPhone = "Сесію заблоковано";
                IsPhoneVerified = false;
                Addresses.Clear();
            }
            else
            {
                if (profile is not null)
                {
                    CurrentCityName = string.IsNullOrWhiteSpace(profile.CityName) ? "Не обрано" : profile.CityName;
                    MaskedPhone = MaskPhone(profile.Phone);
                    IsPhoneVerified = profile.IsPhoneVerified && authService.IsAuthenticated;
                }
                else
                {
                    CurrentCityName = appState.SelectedCity?.Name ?? "Не обрано";
                    MaskedPhone = "Не вказано";
                    IsPhoneVerified = false;
                }

                var savedAddresses = await addressRepository.GetAllAsync();
                Addresses.Clear();
                foreach (var address in savedAddresses)
                    Addresses.Add(address);
            }

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

    private async Task OpenLogsAsync()
    {
        if (!await RequireAdminAsync(sessionService, dialogService, logger, nameof(OpenLogsCommand)))
            return;

        logger.LogInformation("Opening local logs diagnostics screen");
        await navigation.GoToAsync("logs");
    }

    private async Task LockSessionAsync()
    {
        await authService.LockSessionAsync();
        IsPhoneVerified = false;
        IsSessionLocked = true;
        MaskedPhone = "Сесію заблоковано";
        Addresses.Clear();
        OnPropertyChanged(nameof(IsAdmin));
        OnPropertyChanged(nameof(CurrentRoleText));
        await navigation.GoToAsync("auth?locked=true");
    }

    private async Task ReportBugAsync()
    {
        if (isReportingBug || bugReportService is null)
            return;

        isReportingBug = true;
        ReportBugCommand.NotifyCanExecuteChanged();
        try
        {
            await bugReportService.PromptAndSendAsync("Налаштування");
        }
        finally
        {
            isReportingBug = false;
            ReportBugCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task SyncNowAsync()
    {
        try
        {
            await syncService.SyncPendingAsync();
            await RefreshSyncStatusAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Manual synchronization failed from SettingsPage");
            if (bugReportService is not null)
                await bugReportService.ShowErrorWithReportOptionAsync(
                    "Помилка синхронізації",
                    "Не вдалося синхронізувати дані. Спробуйте ще раз.",
                    "Settings: SyncNowAsync");
            else
                await dialogService.AlertAsync("Помилка синхронізації", "Не вдалося синхронізувати дані. Спробуйте ще раз.");
        }
    }

    private async Task RefreshSyncStatusSafeAsync()
    {
        try
        {
            await RefreshSyncStatusAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to refresh pending sync count after SyncCompleted");
        }
    }

    private async Task RefreshSyncStatusAsync()
    {
        var count = await syncService.GetPendingCountAsync();
        await dispatcher.InvokeOnMainThreadAsync(() => PendingSyncCount = count);
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

    private Task EditAddressAsync(UserAddress? address)
    {
        if (IsSessionLocked)
            return Task.CompletedTask;

        var parameters = new Dictionary<string, object>();
        if (address is not null)
            parameters["Address"] = address;

        return navigation.GoToAsync("addressedit", parameters);
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
            IsSessionLocked = false;
            OnPropertyChanged(nameof(IsAdmin));
            OnPropertyChanged(nameof(CurrentRoleText));
            SyncThemeFlags();

            logger.LogInformation("User logged out from SettingsPage");
            await navigation.GoToAsync("///welcome-start");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Logout cleanup failed");
            if (bugReportService is not null)
                await bugReportService.ShowErrorWithReportOptionAsync(
                    "Не вдалося вийти",
                    "Не вдалося очистити дані облікового запису. Спробуйте ще раз.",
                    "Settings: LogoutAsync");
            else
                await dialogService.AlertAsync("Не вдалося вийти", "Не вдалося очистити дані облікового запису. Спробуйте ще раз.");
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
