using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Core.Models;
using PizzaTake.Core.Repositories;
using PizzaTake.Core.Services;

namespace PizzaTake.Core.ViewModels;

/// <summary>
/// ViewModel for creating and reviewing order quality reports with camera/gallery photos and GPS coordinates.
/// </summary>
public sealed class QualityReportViewModel : FormFeedbackViewModel, IQueryParameterReceiver
{
    private readonly IQualityReportRepository qualityReportRepository;
    private readonly IPermissionService permissionService;
    private readonly IMediaService mediaService;
    private readonly ILocationService locationService;
    private readonly ISyncService syncService;
    private readonly INavigationService navigation;
    private readonly IDialogService dialogService;
    private readonly IMainThreadDispatcher dispatcher;
    private readonly ICrashReportingService? crashReporting;
    private readonly ILogger<QualityReportViewModel> logger;

    private int? orderId;
    private string orderNumber = string.Empty;
    private string description = string.Empty;
    private string photoPath = string.Empty;
    private double? latitude;
    private double? longitude;

    public QualityReportViewModel(
        IQualityReportRepository qualityReportRepository,
        IPermissionService permissionService,
        IMediaService mediaService,
        ILocationService locationService,
        ISyncService syncService,
        INavigationService navigation,
        IDialogService dialogService,
        IMainThreadDispatcher dispatcher,
        ICrashReportingService? crashReporting = null,
        ILogger<QualityReportViewModel>? logger = null)
    {
        this.qualityReportRepository = qualityReportRepository;
        this.permissionService = permissionService;
        this.mediaService = mediaService;
        this.locationService = locationService;
        this.syncService = syncService;
        this.navigation = navigation;
        this.dialogService = dialogService;
        this.dispatcher = dispatcher;
        this.crashReporting = crashReporting;
        this.logger = logger ?? NullLogger<QualityReportViewModel>.Instance;

        Title = "Контроль якості";

        LoadReportsCommand = new AsyncRelayCommand(LoadReportsAsync);
        TakePhotoCommand = new AsyncRelayCommand(TakePhotoAsync, () => !IsBusy);
        PickPhotoCommand = new AsyncRelayCommand(PickPhotoAsync, () => !IsBusy);
        GetLocationCommand = new AsyncRelayCommand(GetLocationAsync, () => !IsBusy);
        SaveReportCommand = new AsyncRelayCommand(SaveReportAsync, CanSaveReport);
        DeleteReportCommand = new AsyncRelayCommand<QualityReport?>(DeleteReportAsync);
        OpenMapCommand = new AsyncRelayCommand(() => navigation.GoToAsync("pizzeriasmap"));
        GoBackCommand = new AsyncRelayCommand(() => navigation.GoBackAsync());

        SavedReports.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasSavedReports));
            OnPropertyChanged(nameof(HasNoSavedReports));
        };
    }

    public ObservableCollection<QualityReport> SavedReports { get; } = [];

    public int? OrderId { get => orderId; private set { if (SetProperty(ref orderId, value)) OnPropertyChanged(nameof(HasOrderContext)); } }

    public string OrderNumber
    {
        get => orderNumber;
        set { if (SetProperty(ref orderNumber, value)) { OnPropertyChanged(nameof(HasOrderContext)); OnPropertyChanged(nameof(OrderContextText)); } }
    }

    public bool HasOrderContext => !string.IsNullOrWhiteSpace(OrderNumber);
    public string OrderContextText => HasOrderContext ? $"Прив'язано до замовлення №{OrderNumber}" : "Загальне звернення щодо якості";
    public string Description { get => description; set => SetInput(ref description, value, SaveReportCommand); }
    public string PhotoPath { get => photoPath; private set { if (SetProperty(ref photoPath, value)) OnPropertyChanged(nameof(HasPhoto)); } }
    public bool HasPhoto => !string.IsNullOrWhiteSpace(PhotoPath);

    public double? Latitude
    {
        get => latitude;
        private set { if (SetProperty(ref latitude, value)) { OnPropertyChanged(nameof(HasCoordinates)); OnPropertyChanged(nameof(CoordinatesText)); } }
    }

    public double? Longitude
    {
        get => longitude;
        private set { if (SetProperty(ref longitude, value)) { OnPropertyChanged(nameof(HasCoordinates)); OnPropertyChanged(nameof(CoordinatesText)); } }
    }

    public bool HasCoordinates => GeoMath.HasValidCoordinates(Latitude, Longitude);

    public string CoordinatesText => HasCoordinates
        ? string.Format(CultureInfo.InvariantCulture, "GPS: {0:F5}, {1:F5}", Latitude!.Value, Longitude!.Value)
        : "Координати ще не визначено";

    public bool HasSavedReports => SavedReports.Count > 0;
    public bool HasNoSavedReports => SavedReports.Count == 0;

    public IAsyncRelayCommand LoadReportsCommand { get; }
    public IAsyncRelayCommand TakePhotoCommand { get; }
    public IAsyncRelayCommand PickPhotoCommand { get; }
    public IAsyncRelayCommand GetLocationCommand { get; }
    public IAsyncRelayCommand SaveReportCommand { get; }
    public IAsyncRelayCommand<QualityReport?> DeleteReportCommand { get; }
    public IAsyncRelayCommand OpenMapCommand { get; }
    public IAsyncRelayCommand GoBackCommand { get; }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("Order", out var orderObj) && orderObj is Order order)
        {
            OrderId = order.Id;
            OrderNumber = order.OrderNumber;
            return;
        }

        if (query.TryGetValue("OrderId", out var idObj) && int.TryParse(idObj?.ToString(), out var parsedId))
            OrderId = parsedId;
        if (query.TryGetValue("OrderNumber", out var numObj) && numObj is not null)
            OrderNumber = numObj.ToString() ?? string.Empty;
    }

    public async Task LoadReportsAsync()
    {
        try
        {
            var reports = await qualityReportRepository.GetAllAsync();
            await dispatcher.InvokeOnMainThreadAsync(() => ReplaceCollection(SavedReports, reports));
            logger.LogInformation("Loaded {ReportCount} saved quality report(s)", reports.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load saved quality reports");
            ErrorMessage = "Не вдалося завантажити список звернень.";
        }
    }

    public Task TakePhotoAsync() => AcquirePhotoAsync(
        requireCameraPermission: true,
        () => mediaService.CapturePhotoAsync("report"),
        "Фото успішно додано.",
        "Зйомку фото скасовано.",
        "Не вдалося зробити фото.",
        "Сталася помилка під час роботи з камерою.");

    public Task PickPhotoAsync() => AcquirePhotoAsync(
        requireCameraPermission: false,
        () => mediaService.PickPhotoAsync("report"),
        "Фото обрано з галереї.",
        "Вибір фото скасовано.",
        "Не вдалося обрати фото.",
        "Сталася помилка під час вибору фото.");

    private async Task AcquirePhotoAsync(
        bool requireCameraPermission,
        Func<Task<MediaCaptureResult>> action,
        string successMsg,
        string cancelMsg,
        string failMsg,
        string errorMsg)
    {
        if (IsBusy) return;
        ErrorMessage = StatusMessage = string.Empty;
        IsBusy = true;
        RefreshCommandStates();
        try
        {
            if (requireCameraPermission && !await permissionService.EnsureGrantedAsync(
                    AppPermission.Camera,
                    "Дозвіл на камеру",
                    "Камера потрібна, щоб сфотографувати піцу або стан доставки для акту якості."))
            {
                logger.LogWarning("Camera permission denied while creating quality report");
                ErrorMessage = "Дозвіл на камеру не надано. Ви можете обрати фото з галереї.";
                return;
            }

            var result = await action();
            if (result.IsSuccess)
            {
                PhotoPath = result.SavedFilePath!;
                StatusMessage = successMsg;
            }
            else if (result.FailureReason == HardwareFailureReason.Cancelled)
            {
                StatusMessage = result.ErrorMessage ?? cancelMsg;
            }
            else
            {
                logger.LogWarning("Photo operation failed with reason {Reason}: {Message}", result.FailureReason, result.ErrorMessage);
                ErrorMessage = result.ErrorMessage ?? failMsg;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error while acquiring quality report photo");
            ErrorMessage = errorMsg;
        }
        finally { IsBusy = false; RefreshCommandStates(); }
    }

    public async Task GetLocationAsync()
    {
        if (IsBusy) return;
        ErrorMessage = string.Empty;
        StatusMessage = "Визначаємо координати...";
        IsBusy = true;
        RefreshCommandStates();
        try
        {
            if (!await permissionService.EnsureGrantedAsync(
                    AppPermission.LocationWhenInUse,
                    "Дозвіл на геолокацію",
                    "Геолокація потрібна для фіксації координат у зверненні щодо якості."))
            {
                logger.LogWarning("Location permission denied while creating quality report");
                StatusMessage = string.Empty;
                ErrorMessage = "Дозвіл на геолокацію не надано.";
                return;
            }

            var result = await locationService.GetCurrentLocationAsync(TimeSpan.FromSeconds(10));
            if (result.IsSuccess && result.Location is not null)
            {
                Latitude = result.Location.Latitude;
                Longitude = result.Location.Longitude;
                StatusMessage = "Координати успішно визначено.";
                return;
            }

            StatusMessage = string.Empty;
            logger.LogWarning("Location lookup failed with reason {Reason}: {Message}", result.FailureReason, result.ErrorMessage);
            ErrorMessage = result.ErrorMessage ?? "Не вдалося визначити координати.";
        }
        catch (Exception ex)
        {
            StatusMessage = string.Empty;
            logger.LogError(ex, "Unexpected error in GetLocationAsync");
            ErrorMessage = "Не вдалося отримати координати GPS.";
        }
        finally { IsBusy = false; RefreshCommandStates(); }
    }

    private bool CanSaveReport() => !IsBusy && !string.IsNullOrWhiteSpace(Description) && Description.Trim().Length >= 3;

    public async Task SaveReportAsync()
    {
        if (!CanSaveReport()) return;
        var trimmedDescription = Description.Trim();
        if (trimmedDescription.Length < 3) { ErrorMessage = "Опишіть проблему детальніше (щонайменше 3 символи)."; return; }

        IsBusy = true;
        ErrorMessage = StatusMessage = string.Empty;
        RefreshCommandStates();
        try
        {
            var report = new QualityReport
            {
                OrderId = OrderId,
                OrderNumber = OrderNumber.Trim(),
                Description = trimmedDescription,
                PhotoPath = PhotoPath,
                Latitude = Latitude,
                Longitude = Longitude,
                CreatedAtUtc = DateTime.UtcNow
            };

            await qualityReportRepository.SaveAsync(report);
            await syncService.EnqueueAsync("QualityReport", "Insert", new
            {
                report.Id, report.OrderId, report.OrderNumber, report.Description,
                report.PhotoPath, report.Latitude, report.Longitude, UpdatedAt = report.CreatedAtUtc
            });

            crashReporting?.TrackEvent(CrashEventNames.QualityReportCreated, new Dictionary<string, string>
            {
                ["order_number"] = string.IsNullOrWhiteSpace(report.OrderNumber) ? "none" : report.OrderNumber,
                ["has_photo"] = report.HasPhoto.ToString(),
                ["has_coordinates"] = report.HasCoordinates.ToString()
            });

            SavedReports.Insert(0, report);
            Description = PhotoPath = string.Empty;
            Latitude = Longitude = null;
            StatusMessage = "Звернення щодо якості збережено.";
            logger.LogInformation("Created quality report Id={ReportId} (HasPhoto={HasPhoto}, HasCoords={HasCoords})",
                report.Id, report.HasPhoto, report.HasCoordinates);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save quality report");
            crashReporting?.CaptureException(ex, "QualityReportViewModel.SaveReportAsync");
            ErrorMessage = "Не вдалося зберегти звернення. Спробуйте ще раз.";
        }
        finally { IsBusy = false; RefreshCommandStates(); }
    }

    private async Task DeleteReportAsync(QualityReport? report)
    {
        if (report is null || IsBusy || !await dialogService.ConfirmAsync(
                "Видалення звернення", "Видалити це звернення щодо якості?", "Видалити", "Скасувати"))
            return;

        try
        {
            await qualityReportRepository.DeleteAsync(report);
            await syncService.EnqueueAsync("QualityReport", "Delete", new { report.Id, UpdatedAt = DateTime.UtcNow });
            SavedReports.Remove(report);
            StatusMessage = "Звернення видалено.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete quality report Id={ReportId}", report.Id);
            ErrorMessage = "Не вдалося видалити звернення.";
        }
    }

    private void RefreshCommandStates()
    {
        TakePhotoCommand.NotifyCanExecuteChanged();
        PickPhotoCommand.NotifyCanExecuteChanged();
        GetLocationCommand.NotifyCanExecuteChanged();
        SaveReportCommand.NotifyCanExecuteChanged();
    }
}
