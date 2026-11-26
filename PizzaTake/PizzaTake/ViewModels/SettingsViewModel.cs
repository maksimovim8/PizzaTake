using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Models;
using PizzaTake.Repositories;
using PizzaTake.Services;

namespace PizzaTake.ViewModels;

/// <summary>
/// ViewModel for user account settings, role display, theme selection, decrypted profile display,
/// sync status, address management, biometric session lock, and administrator logs access.
/// </summary>
public sealed class SettingsViewModel : BaseViewModel, IQueryAttributable
{
    private readonly IUserProfileRepository userProfileRepository;
    private readonly IAddressRepository addressRepository;
    private readonly IAuthService authService;
    private readonly ISessionService sessionService;
    private readonly IUserDataResetService userDataResetService;
    private readonly IAppStateService appState;
    private readonly ISyncService syncService;
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
        this.bugReportService = bugReportService;
        this.logger = logger ?? NullLogger<SettingsViewModel>.Instance;
        Title = "Налаштування";

        this.sessionService.SessionChanged += (_, _) => MainThread.BeginInvokeOnMainThread(() =>
        {
            OnPropertyChanged(nameof(IsAdmin));
            OnPropertyChanged(nameof(CurrentRoleText));
        });

        LoadSettingsCommand = new Command(async () => await LoadSettingsAsync());
        ChangeCityCommand = new Command(async () => await Shell.Current.GoToAsync("cities?returnTo=settings"));
        ToggleThemeCommand = new Command<string?>(ApplyThemeMode);
        ChangePhoneCommand = new Command(async () => await Shell.Current.GoToAsync(IsSessionLocked ? "auth?locked=true" : "auth"));
        EditAddressCommand = new Command<UserAddress?>(async address => await EditAddressAsync(address));
        AddAddressCommand = new Command(async () => await EditAddressAsync(null));
        SyncNowCommand = new Command(async () => await SyncNowAsync());
        ReportBugCommand = new Command(async () => await ReportBugAsync(), () => !isReportingBug);
        OpenLogsCommand = new Command(async () => await OpenLogsAsync());
        LockSessionCommand = new Command(async () => await LockSessionAsync(), () => IsPhoneVerified);
        UnlockSessionCommand = new Command(async () => await Shell.Current.GoToAsync("auth?locked=true"), () => IsSessionLocked);
        LogoutCommand = new Command(async () => await LogoutAsync(), () => HasAccountSession);

        this.syncService.SyncCompleted += async (_, _) => await RefreshSyncStatusAsync();

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
                ((Command)LockSessionCommand).ChangeCanExecute();
                ((Command)LogoutCommand).ChangeCanExecute();
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
                ((Command)UnlockSessionCommand).ChangeCanExecute();
                ((Command)LogoutCommand).ChangeCanExecute();
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

    public ICommand LoadSettingsCommand { get; }
    public ICommand ChangeCityCommand { get; }
    public ICommand ToggleThemeCommand { get; }
    public ICommand ChangePhoneCommand { get; }
    public ICommand EditAddressCommand { get; }
    public ICommand AddAddressCommand { get; }
    public ICommand SyncNowCommand { get; }
    public ICommand ReportBugCommand { get; }
    public ICommand OpenLogsCommand { get; }
    public ICommand LockSessionCommand { get; }
    public ICommand UnlockSessionCommand { get; }
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
            else if (profile is not null)
            {
                CurrentCityName = string.IsNullOrWhiteSpace(profile.CityName) ? "Не обрано" : profile.CityName;
                MaskedPhone = MaskPhone(profile.Phone);
                IsPhoneVerified = profile.IsPhoneVerified && authService.IsAuthenticated;

                var savedAddresses = await addressRepository.GetAllAsync();
                Addresses.Clear();
                foreach (var address in savedAddresses)
                    Addresses.Add(address);
            }
            else
            {
                CurrentCityName = "Не обрано";
                MaskedPhone = "Не вказано";
                IsPhoneVerified = false;
                Addresses.Clear();
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
        if (!sessionService.IsInRole(UserRole.Administrator))
        {
            logger.LogWarning("Access denied for role {Role} on {Command}", sessionService.CurrentRole, nameof(OpenLogsCommand));
            await Shell.Current.DisplayAlertAsync("Доступ заборонено", "Дія доступна лише адміністратору.", "OK");
            return;
        }

        logger.LogInformation("Opening local logs diagnostics screen");
        await Shell.Current.GoToAsync("logs");
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
        await Shell.Current.GoToAsync("auth?locked=true");
    }

    private async Task ReportBugAsync()
    {
        if (isReportingBug || bugReportService is null)
            return;

        isReportingBug = true;
        ((Command)ReportBugCommand).ChangeCanExecute();
        try
        {
            await bugReportService.PromptAndSendAsync("Налаштування");
        }
        finally
        {
            isReportingBug = false;
            ((Command)ReportBugCommand).ChangeCanExecute();
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
                await Shell.Current.DisplayAlertAsync("Помилка синхронізації", "Не вдалося синхронізувати дані. Спробуйте ще раз.", "OK");
        }
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
            await Shell.Current.GoToAsync("///welcome-start");
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
