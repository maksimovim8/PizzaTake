using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using PizzaTake.Models;
using PizzaTake.Repositories;
using PizzaTake.Services;

namespace PizzaTake.ViewModels;

/// <summary>
/// ViewModel for user account settings, theme selection, decrypted profile display, and address management.
/// </summary>
public sealed class SettingsViewModel : BaseViewModel, IQueryAttributable
{
    private readonly IUserProfileRepository userProfileRepository;
    private readonly IAddressRepository addressRepository;
    private readonly IAuthService authService;
    private readonly IUserDataResetService userDataResetService;
    private readonly IAppStateService appState;

    private bool isDarkTheme;
    private bool isLightTheme = true;
    private bool isSystemTheme;
    private string currentCityName = "Не обрано";
    private string maskedPhone = "Не вказано";
    private bool isPhoneVerified;

    public SettingsViewModel(
        IUserProfileRepository userProfileRepository,
        IAddressRepository addressRepository,
        IAuthService authService,
        IAppStateService appState,
        IUserDataResetService userDataResetService)
    {
        this.userProfileRepository = userProfileRepository;
        this.addressRepository = addressRepository;
        this.authService = authService;
        this.appState = appState;
        this.userDataResetService = userDataResetService;
        Title = "Налаштування";

        LoadSettingsCommand = new Command(async () => await LoadSettingsAsync());
        ChangeCityCommand = new Command(async () => await Shell.Current.GoToAsync("cities?returnTo=settings"));
        ToggleThemeCommand = new Command<string?>(ApplyThemeMode);
        ChangePhoneCommand = new Command(async () => await Shell.Current.GoToAsync("auth"));
        EditAddressCommand = new Command<UserAddress?>(async address => await EditAddressAsync(address));
        AddAddressCommand = new Command(async () => await EditAddressAsync(null));
        LogoutCommand = new Command(async () => await LogoutAsync(), () => IsPhoneVerified);

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

    public ICommand LoadSettingsCommand { get; }
    public ICommand ChangeCityCommand { get; }
    public ICommand ToggleThemeCommand { get; }
    public ICommand ChangePhoneCommand { get; }
    public ICommand EditAddressCommand { get; }
    public ICommand AddAddressCommand { get; }
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
        }
        else if (query.TryGetValue("DeletedAddressId", out var deletedIdObj) &&
                 int.TryParse(deletedIdObj?.ToString(), out var deletedId))
        {
            var existing = Addresses.FirstOrDefault(a => a.Id == deletedId);
            if (existing is not null)
                Addresses.Remove(existing);
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
        }
        finally
        {
            IsBusy = false;
        }
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
            CurrentCityName = "Не обрано";
            MaskedPhone = "Не вказано";
            IsPhoneVerified = false;
            SyncThemeFlags();

            await Shell.Current.GoToAsync("///menu");
            await Shell.Current.GoToAsync("welcome");
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[Settings] Logout cleanup failed: {exception}");
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
