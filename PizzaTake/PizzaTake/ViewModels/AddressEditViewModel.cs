using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Models;
using PizzaTake.Repositories;
using PizzaTake.Services;

namespace PizzaTake.ViewModels;

/// <summary>
/// ViewModel for creating, editing, and deleting saved delivery addresses with query-parameter return navigation
/// and draft persistence across app lifecycle transitions.
/// </summary>
public sealed class AddressEditViewModel : BaseViewModel, IQueryAttributable
{
    private const string DraftPrefix = "addressEdit.";
    private const string DraftLabelKey = "addressEdit.label";
    private const string DraftTextKey = "addressEdit.text";
    private const string DraftNotesKey = "addressEdit.notes";
    private const string DraftIdKey = "addressEdit.id";

    private readonly IAddressRepository addressRepository;
    private readonly IUserProfileRepository userProfileRepository;
    private readonly IAppStateService appState;
    private readonly ISyncService syncService;
    private readonly ILocationService? locationService;
    private readonly IPermissionService? permissionService;
    private readonly IBugReportService? bugReportService;
    private readonly ILogger<AddressEditViewModel> logger;

    private UserAddress? editingAddress;
    private string label = string.Empty;
    private string addressText = string.Empty;
    private string notes = string.Empty;
    private double latitude;
    private double longitude;
    private string statusMessage = string.Empty;
    private string errorMessage = string.Empty;
    private string? reportableErrorContext;
    private bool isEditMode;
    private bool suppressDraftSync;
    private bool isDraftCleared;

    public AddressEditViewModel(
        IAddressRepository addressRepository,
        IUserProfileRepository userProfileRepository,
        IAppStateService appState,
        ISyncService syncService,
        ILocationService? locationService = null,
        IPermissionService? permissionService = null,
        IBugReportService? bugReportService = null,
        ILogger<AddressEditViewModel>? logger = null)
    {
        this.addressRepository = addressRepository;
        this.userProfileRepository = userProfileRepository;
        this.appState = appState;
        this.syncService = syncService;
        this.locationService = locationService;
        this.permissionService = permissionService;
        this.bugReportService = bugReportService;
        this.logger = logger ?? NullLogger<AddressEditViewModel>.Instance;
        Title = "Нова адреса";

        SaveCommand = new Command(async () => await SaveAsync(), CanSave);
        DeleteCommand = new Command(async () => await DeleteAsync(), CanDelete);
        GetMyCoordinatesCommand = new Command(async () => await GetMyCoordinatesAsync(), () => !IsBusy);
        ReportErrorCommand = new Command(async () => await ReportErrorAsync(), () => CanReportError);
        GoBackCommand = new Command(async () => await GoBackAsync());
    }

    public string Label
    {
        get => label;
        set
        {
            if (SetProperty(ref label, value))
            {
                if (!suppressDraftSync)
                    appState.SetDraftValue(DraftLabelKey, value);
                ErrorMessage = string.Empty;
                ((Command)SaveCommand).ChangeCanExecute();
            }
        }
    }

    public string AddressText
    {
        get => addressText;
        set
        {
            if (SetProperty(ref addressText, value))
            {
                if (!suppressDraftSync)
                    appState.SetDraftValue(DraftTextKey, value);
                ErrorMessage = string.Empty;
                ((Command)SaveCommand).ChangeCanExecute();
            }
        }
    }

    public string Notes
    {
        get => notes;
        set
        {
            if (SetProperty(ref notes, value) && !suppressDraftSync)
                appState.SetDraftValue(DraftNotesKey, value);
        }
    }

    public bool IsEditMode
    {
        get => isEditMode;
        private set
        {
            if (SetProperty(ref isEditMode, value))
            {
                Title = value ? "Редагування адреси" : "Нова адреса";
                ((Command)DeleteCommand).ChangeCanExecute();
            }
        }
    }

    public double Latitude
    {
        get => latitude;
        private set
        {
            if (SetProperty(ref latitude, value))
            {
                OnPropertyChanged(nameof(HasCoordinates));
                OnPropertyChanged(nameof(CoordinatesText));
            }
        }
    }

    public double Longitude
    {
        get => longitude;
        private set
        {
            if (SetProperty(ref longitude, value))
            {
                OnPropertyChanged(nameof(HasCoordinates));
                OnPropertyChanged(nameof(CoordinatesText));
            }
        }
    }

    public bool HasCoordinates => Math.Abs(Latitude) > 0.00001 || Math.Abs(Longitude) > 0.00001;

    public string CoordinatesText => HasCoordinates
        ? string.Format(System.Globalization.CultureInfo.InvariantCulture, "GPS: {0:F5}, {1:F5}", Latitude, Longitude)
        : "Координати не визначено";

    public string StatusMessage
    {
        get => statusMessage;
        private set
        {
            if (SetProperty(ref statusMessage, value))
                OnPropertyChanged(nameof(HasStatusMessage));
        }
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public string ErrorMessage
    {
        get => errorMessage;
        private set
        {
            if (SetProperty(ref errorMessage, value))
            {
                if (string.IsNullOrWhiteSpace(value))
                    SetReportableErrorContext(null);
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool CanReportError => HasError && !string.IsNullOrEmpty(reportableErrorContext) && bugReportService is not null;

    public ICommand SaveCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand GetMyCoordinatesCommand { get; }
    public ICommand ReportErrorCommand { get; }
    public ICommand GoBackCommand { get; }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        isDraftCleared = false;
        suppressDraftSync = true;
        try
        {
            if (query.TryGetValue("Address", out var value) && value is UserAddress address)
            {
                editingAddress = address;
                IsEditMode = address.Id > 0;
                Latitude = address.Latitude;
                Longitude = address.Longitude;

                var draftId = appState.GetDraftValue(DraftIdKey);
                if (string.Equals(draftId, address.Id.ToString(), StringComparison.Ordinal))
                {
                    Label = appState.GetDraftValue(DraftLabelKey) ?? address.Label;
                    AddressText = appState.GetDraftValue(DraftTextKey) ?? address.Text;
                    Notes = appState.GetDraftValue(DraftNotesKey) ?? address.Notes;
                }
                else
                {
                    Label = address.Label;
                    AddressText = address.Text;
                    Notes = address.Notes;
                }
            }
            else
            {
                editingAddress = null;
                IsEditMode = false;
                Latitude = 0;
                Longitude = 0;
                var draftId = appState.GetDraftValue(DraftIdKey);
                if (string.IsNullOrEmpty(draftId) || draftId == "0")
                {
                    Label = appState.GetDraftValue(DraftLabelKey) ?? string.Empty;
                    AddressText = appState.GetDraftValue(DraftTextKey) ?? string.Empty;
                    Notes = appState.GetDraftValue(DraftNotesKey) ?? string.Empty;
                }
                else
                {
                    Label = string.Empty;
                    AddressText = string.Empty;
                    Notes = string.Empty;
                }
            }
        }
        finally
        {
            suppressDraftSync = false;
        }

        appState.SetDraftValue(DraftIdKey, (editingAddress?.Id ?? 0).ToString());
        appState.SetDraftValue(DraftLabelKey, Label);
        appState.SetDraftValue(DraftTextKey, AddressText);
        appState.SetDraftValue(DraftNotesKey, Notes);

        StatusMessage = string.Empty;
        ErrorMessage = string.Empty;
        RefreshCommandStates();
    }

    public void SaveDraftState()
    {
        if (isDraftCleared)
            return;

        appState.SetDraftValue(DraftIdKey, (editingAddress?.Id ?? 0).ToString());
        appState.SetDraftValue(DraftLabelKey, Label);
        appState.SetDraftValue(DraftTextKey, AddressText);
        appState.SetDraftValue(DraftNotesKey, Notes);
        appState.SaveState();
    }

    private async Task GetMyCoordinatesAsync()
    {
        if (IsBusy)
            return;

        if (locationService is null)
        {
            ErrorMessage = "Сервіс геолокації недоступний.";
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;
        StatusMessage = "Визначаємо координати...";
        RefreshCommandStates();
        try
        {
            if (permissionService is not null)
            {
                var granted = await permissionService.EnsureGrantedAsync(
                    AppPermission.LocationWhenInUse,
                    "Дозвіл на геолокацію",
                    "Геолокація потрібна, щоб зберегти точні координати вашої адреси доставки.");
                if (!granted)
                {
                    StatusMessage = string.Empty;
                    ErrorMessage = "Дозвіл на геолокацію не надано.";
                    return;
                }
            }

            var result = await locationService.GetCurrentLocationAsync(TimeSpan.FromSeconds(10));
            if (result.IsSuccess && result.Location is not null)
            {
                Latitude = result.Location.Latitude;
                Longitude = result.Location.Longitude;
                StatusMessage = "Координати оновлено.";
                logger.LogInformation("Address GPS coordinates updated ({Latitude:F5}, {Longitude:F5})", Latitude, Longitude);
            }
            else
            {
                StatusMessage = string.Empty;
                logger.LogWarning("Failed to detect address coordinates: {Reason} {Message}", result.FailureReason, result.ErrorMessage);
                ErrorMessage = result.ErrorMessage ?? "Не вдалося визначити координати.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = string.Empty;
            logger.LogError(ex, "Unexpected error while detecting coordinates for address");
            ErrorMessage = "Не вдалося визначити координати.";
        }
        finally
        {
            IsBusy = false;
            RefreshCommandStates();
        }
    }

    private async Task GoBackAsync()
    {
        ClearDraft();
        await Shell.Current.GoToAsync("..");
    }

    private void ClearDraft()
    {
        isDraftCleared = true;
        appState.ClearDraftPrefix(DraftPrefix);
        appState.SaveState();
    }

    private bool CanSave() => !IsBusy && !string.IsNullOrWhiteSpace(AddressText);

    private bool CanDelete() => !IsBusy && IsEditMode && editingAddress is not null;

    private async Task SaveAsync()
    {
        if (!CanSave())
            return;

        var trimmedText = AddressText.Trim();
        if (trimmedText.Length < 4)
        {
            ErrorMessage = "Введіть повну адресу доставки (щонайменше 4 символи).";
            return;
        }

        IsBusy = true;
        RefreshCommandStates();
        try
        {
            var profile = await userProfileRepository.GetCurrentAsync();
            var address = editingAddress is not null
                ? new UserAddress
                {
                    Id = editingAddress.Id,
                    UserId = editingAddress.UserId != 0 ? editingAddress.UserId : profile?.Id ?? 0,
                    Label = string.IsNullOrWhiteSpace(Label) ? "Доставка" : Label.Trim(),
                    Text = trimmedText,
                    Notes = Notes.Trim(),
                    Latitude = Latitude,
                    Longitude = Longitude,
                    LastUsedAt = DateTime.UtcNow
                }
                : new UserAddress
                {
                    UserId = profile?.Id ?? 0,
                    Label = string.IsNullOrWhiteSpace(Label) ? "Доставка" : Label.Trim(),
                    Text = trimmedText,
                    Notes = Notes.Trim(),
                    Latitude = Latitude,
                    Longitude = Longitude,
                    LastUsedAt = DateTime.UtcNow
                };

            var operation = address.Id == 0 ? "Insert" : "Update";
            await addressRepository.SaveAsync(address);
            await syncService.EnqueueAsync("UserAddress", operation, new
            {
                address.Id,
                address.Label,
                address.Text,
                address.Notes,
                address.Latitude,
                address.Longitude,
                UpdatedAt = address.LastUsedAt
            });
            ClearDraft();

            logger.LogInformation("Address {Operation} succeeded (Id={AddressId})", operation, address.Id);
            await Shell.Current.GoToAsync("..", new Dictionary<string, object>
            {
                ["UpdatedAddress"] = address
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save user address");
            ErrorMessage = "Не вдалося зберегти адресу. Спробуйте ще раз.";
            SetReportableErrorContext("AddressEdit: SaveAsync");
        }
        finally
        {
            IsBusy = false;
            RefreshCommandStates();
        }
    }

    private async Task DeleteAsync()
    {
        if (!CanDelete() || editingAddress is null)
            return;

        IsBusy = true;
        RefreshCommandStates();
        try
        {
            var deletedId = editingAddress.Id;
            await addressRepository.DeleteAsync(editingAddress);
            await syncService.EnqueueAsync("UserAddress", "Delete", new
            {
                Id = deletedId,
                UpdatedAt = DateTime.UtcNow
            });
            ClearDraft();

            logger.LogInformation("Address deleted (Id={AddressId})", deletedId);
            await Shell.Current.GoToAsync("..", new Dictionary<string, object>
            {
                ["DeletedAddressId"] = deletedId
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete user address {AddressId}", editingAddress.Id);
            ErrorMessage = "Не вдалося видалити адресу. Спробуйте ще раз.";
            SetReportableErrorContext("AddressEdit: DeleteAsync");
        }
        finally
        {
            IsBusy = false;
            RefreshCommandStates();
        }
    }

    private void SetReportableErrorContext(string? context)
    {
        reportableErrorContext = context;
        OnPropertyChanged(nameof(CanReportError));
        ((Command)ReportErrorCommand).ChangeCanExecute();
    }

    private async Task ReportErrorAsync()
    {
        if (bugReportService is null)
            return;

        await bugReportService.PromptAndSendAsync(reportableErrorContext ?? "Редагування адреси");
    }

    private void RefreshCommandStates()
    {
        ((Command)SaveCommand).ChangeCanExecute();
        ((Command)DeleteCommand).ChangeCanExecute();
        ((Command)GetMyCoordinatesCommand).ChangeCanExecute();
    }
}
