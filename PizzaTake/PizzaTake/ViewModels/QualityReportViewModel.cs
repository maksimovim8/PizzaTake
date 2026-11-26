using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Models;
using PizzaTake.Repositories;
using PizzaTake.Services;

namespace PizzaTake.ViewModels;

/// <summary>
/// ViewModel for creating and reviewing order quality reports with camera/gallery photos and GPS coordinates.
/// </summary>
public sealed class QualityReportViewModel : BaseViewModel, IQueryAttributable
{
    private readonly IQualityReportRepository qualityReportRepository;
    private readonly IPermissionService permissionService;
    private readonly IMediaService mediaService;
    private readonly ILocationService locationService;
    private readonly ISyncService syncService;
    private readonly ICrashReportingService? crashReporting;
    private readonly ILogger<QualityReportViewModel> logger;

    private int? orderId;
    private string orderNumber = string.Empty;
    private string description = string.Empty;
    private string photoPath = string.Empty;
    private double? latitude;
    private double? longitude;
    private string statusMessage = string.Empty;
    private string errorMessage = string.Empty;

    public QualityReportViewModel(
        IQualityReportRepository qualityReportRepository,
        IPermissionService permissionService,
        IMediaService mediaService,
        ILocationService locationService,
        ISyncService syncService,
        ICrashReportingService? crashReporting = null,
        ILogger<QualityReportViewModel>? logger = null)
    {
        this.qualityReportRepository = qualityReportRepository;
        this.permissionService = permissionService;
        this.mediaService = mediaService;
        this.locationService = locationService;
        this.syncService = syncService;
        this.crashReporting = crashReporting;
        this.logger = logger ?? NullLogger<QualityReportViewModel>.Instance;

        Title = "Контроль якості";

        LoadReportsCommand = new Command(async () => await LoadReportsAsync());
        TakePhotoCommand = new Command(async () => await TakePhotoAsync(), () => !IsBusy);
        PickPhotoCommand = new Command(async () => await PickPhotoAsync(), () => !IsBusy);
        GetLocationCommand = new Command(async () => await GetLocationAsync(), () => !IsBusy);
        SaveReportCommand = new Command(async () => await SaveReportAsync(), CanSaveReport);
        DeleteReportCommand = new Command<QualityReport?>(async report => await DeleteReportAsync(report));
        OpenMapCommand = new Command(async () => await Shell.Current.GoToAsync("pizzeriasmap"));
        GoBackCommand = new Command(async () => await Shell.Current.GoToAsync(".."));

        SavedReports.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasSavedReports));
            OnPropertyChanged(nameof(HasNoSavedReports));
        };
    }

    public ObservableCollection<QualityReport> SavedReports { get; } = new();

    public int? OrderId
    {
        get => orderId;
        private set
        {
            if (SetProperty(ref orderId, value))
                OnPropertyChanged(nameof(HasOrderContext));
        }
    }

    public string OrderNumber
    {
        get => orderNumber;
        set
        {
            if (SetProperty(ref orderNumber, value))
            {
                OnPropertyChanged(nameof(HasOrderContext));
                OnPropertyChanged(nameof(OrderContextText));
            }
        }
    }

    public bool HasOrderContext => !string.IsNullOrWhiteSpace(OrderNumber);
    public string OrderContextText => HasOrderContext
        ? $"Прив'язано до замовлення №{OrderNumber}"
        : "Загальне звернення щодо якості";

    public string Description
    {
        get => description;
        set
        {
            if (SetProperty(ref description, value))
            {
                ErrorMessage = string.Empty;
                ((Command)SaveReportCommand).ChangeCanExecute();
            }
        }
    }

    public string PhotoPath
    {
        get => photoPath;
        private set
        {
            if (SetProperty(ref photoPath, value))
                OnPropertyChanged(nameof(HasPhoto));
        }
    }

    public bool HasPhoto => !string.IsNullOrWhiteSpace(PhotoPath);

    public double? Latitude
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

    public double? Longitude
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

    public bool HasCoordinates =>
        Latitude.HasValue && Longitude.HasValue &&
        (Math.Abs(Latitude.Value) > 0.00001 || Math.Abs(Longitude.Value) > 0.00001);

    public string CoordinatesText => HasCoordinates
        ? string.Format(CultureInfo.InvariantCulture, "GPS: {0:F5}, {1:F5}", Latitude!.Value, Longitude!.Value)
        : "Координати ще не визначено";

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
                OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasSavedReports => SavedReports.Count > 0;
    public bool HasNoSavedReports => SavedReports.Count == 0;

    public ICommand LoadReportsCommand { get; }
    public ICommand TakePhotoCommand { get; }
    public ICommand PickPhotoCommand { get; }
    public ICommand GetLocationCommand { get; }
    public ICommand SaveReportCommand { get; }
    public ICommand DeleteReportCommand { get; }
    public ICommand OpenMapCommand { get; }
    public ICommand GoBackCommand { get; }

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
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                SavedReports.Clear();
                foreach (var report in reports)
                    SavedReports.Add(report);
            });
            logger.LogInformation("Loaded {ReportCount} saved quality report(s)", reports.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load saved quality reports");
            ErrorMessage = "Не вдалося завантажити список звернень.";
        }
    }

    public async Task TakePhotoAsync()
    {
        if (IsBusy)
            return;

        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        IsBusy = true;
        RefreshCommandStates();
        try
        {
            var granted = await permissionService.EnsureGrantedAsync(
                AppPermission.Camera,
                "Дозвіл на камеру",
                "Камера потрібна, щоб сфотографувати піцу або стан доставки для акту якості.");

            if (!granted)
            {
                logger.LogWarning("Camera permission denied while creating quality report");
                ErrorMessage = "Дозвіл на камеру не надано. Ви можете обрати фото з галереї.";
                return;
            }

            var result = await mediaService.CapturePhotoAsync("report");
            if (result.IsSuccess)
            {
                PhotoPath = result.SavedFilePath!;
                StatusMessage = "Фото успішно додано.";
                return;
            }

            if (result.FailureReason == HardwareFailureReason.Cancelled)
            {
                StatusMessage = result.ErrorMessage ?? "Зйомку фото скасовано.";
            }
            else
            {
                logger.LogWarning("Photo capture failed with reason {Reason}: {Message}", result.FailureReason, result.ErrorMessage);
                ErrorMessage = result.ErrorMessage ?? "Не вдалося зробити фото.";
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error in TakePhotoAsync");
            ErrorMessage = "Сталася помилка під час роботи з камерою.";
        }
        finally
        {
            IsBusy = false;
            RefreshCommandStates();
        }
    }

    public async Task PickPhotoAsync()
    {
        if (IsBusy)
            return;

        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        IsBusy = true;
        RefreshCommandStates();
        try
        {
            var result = await mediaService.PickPhotoAsync("report");
            if (result.IsSuccess)
            {
                PhotoPath = result.SavedFilePath!;
                StatusMessage = "Фото обрано з галереї.";
                return;
            }

            if (result.FailureReason == HardwareFailureReason.Cancelled)
            {
                StatusMessage = result.ErrorMessage ?? "Вибір фото скасовано.";
            }
            else
            {
                logger.LogWarning("Photo pick failed with reason {Reason}: {Message}", result.FailureReason, result.ErrorMessage);
                ErrorMessage = result.ErrorMessage ?? "Не вдалося обрати фото.";
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error in PickPhotoAsync");
            ErrorMessage = "Сталася помилка під час вибору фото.";
        }
        finally
        {
            IsBusy = false;
            RefreshCommandStates();
        }
    }

    public async Task GetLocationAsync()
    {
        if (IsBusy)
            return;

        ErrorMessage = string.Empty;
        StatusMessage = "Визначаємо координати...";
        IsBusy = true;
        RefreshCommandStates();
        try
        {
            var granted = await permissionService.EnsureGrantedAsync(
                AppPermission.LocationWhenInUse,
                "Дозвіл на геолокацію",
                "Геолокація потрібна для фіксації координат у зверненні щодо якості.");

            if (!granted)
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
        finally
        {
            IsBusy = false;
            RefreshCommandStates();
        }
    }

    private bool CanSaveReport() => !IsBusy && !string.IsNullOrWhiteSpace(Description);

    public async Task SaveReportAsync()
    {
        if (!CanSaveReport())
            return;

        var trimmedDescription = Description.Trim();
        if (trimmedDescription.Length < 3)
        {
            ErrorMessage = "Опишіть проблему детальніше (щонайменше 3 символи).";
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
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
                report.Id,
                report.OrderId,
                report.OrderNumber,
                report.Description,
                report.PhotoPath,
                report.Latitude,
                report.Longitude,
                UpdatedAt = report.CreatedAtUtc
            });

            crashReporting?.TrackEvent(CrashEventNames.QualityReportCreated, new Dictionary<string, string>
            {
                ["order_number"] = string.IsNullOrWhiteSpace(report.OrderNumber) ? "none" : report.OrderNumber,
                ["has_photo"] = report.HasPhoto.ToString(),
                ["has_coordinates"] = report.HasCoordinates.ToString()
            });

            SavedReports.Insert(0, report);
            Description = string.Empty;
            PhotoPath = string.Empty;
            Latitude = null;
            Longitude = null;
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
        finally
        {
            IsBusy = false;
            RefreshCommandStates();
        }
    }

    private async Task DeleteReportAsync(QualityReport? report)
    {
        if (report is null || IsBusy)
            return;

        try
        {
            await qualityReportRepository.DeleteAsync(report);
            await syncService.EnqueueAsync("QualityReport", "Delete", new
            {
                report.Id,
                UpdatedAt = DateTime.UtcNow
            });
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
        ((Command)TakePhotoCommand).ChangeCanExecute();
        ((Command)PickPhotoCommand).ChangeCanExecute();
        ((Command)GetLocationCommand).ChangeCanExecute();
        ((Command)SaveReportCommand).ChangeCanExecute();
    }
}
