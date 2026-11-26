using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Repositories;
using PizzaTake.Services;

namespace PizzaTake.ViewModels;

/// <summary>
/// Platform-independent ViewModel for displaying pizzerias and quality reports on OpenStreetMap
/// and launching coordinates in an external maps app.
/// </summary>
public sealed class PizzeriasMapViewModel : BaseViewModel
{
    private readonly IPizzeriaRepository pizzeriaRepository;
    private readonly IQualityReportRepository qualityReportRepository;
    private readonly IAppStateService appState;
    private readonly IMapLauncherService mapLauncherService;
    private readonly ILocationService? locationService;
    private readonly IPermissionService? permissionService;
    private readonly ILogger<PizzeriasMapViewModel> logger;

    private MapPinItem? selectedPin;
    private GeoPoint? userLocation;
    private string headerSubtitle = "Піцерії та звернення на карті OpenStreetMap";
    private string statusMessage = string.Empty;

    public PizzeriasMapViewModel(
        IPizzeriaRepository pizzeriaRepository,
        IQualityReportRepository qualityReportRepository,
        IAppStateService appState,
        IMapLauncherService mapLauncherService,
        ILocationService? locationService = null,
        IPermissionService? permissionService = null,
        ILogger<PizzeriasMapViewModel>? logger = null)
    {
        this.pizzeriaRepository = pizzeriaRepository;
        this.qualityReportRepository = qualityReportRepository;
        this.appState = appState;
        this.mapLauncherService = mapLauncherService;
        this.locationService = locationService;
        this.permissionService = permissionService;
        this.logger = logger ?? NullLogger<PizzeriasMapViewModel>.Instance;

        Title = "Карта піцерій";

        LoadPinsCommand = new Command(async () => await LoadPinsAsync());
        SelectPinCommand = new Command<MapPinItem?>(SelectPin);
        OpenExternalMapCommand = new Command(async () => await OpenExternalMapAsync(), () => SelectedPin is not null);
        CenterOnMyLocationCommand = new Command(async () => await CenterOnMyLocationAsync(), () => !IsBusy);
        GoBackCommand = new Command(async () => await Shell.Current.GoToAsync(".."));
    }

    public event EventHandler? PinsUpdated;
    public event EventHandler<GeoPoint>? FocusRequested;

    public ObservableCollection<MapPinItem> Pins { get; } = new();

    public MapPinItem? SelectedPin
    {
        get => selectedPin;
        set
        {
            if (SetProperty(ref selectedPin, value))
            {
                OnPropertyChanged(nameof(HasSelectedPin));
                OnPropertyChanged(nameof(SelectedPinCoordinatesText));
                ((Command)OpenExternalMapCommand).ChangeCanExecute();
                if (value is not null)
                    FocusRequested?.Invoke(this, new GeoPoint(value.Latitude, value.Longitude));
            }
        }
    }

    public bool HasSelectedPin => SelectedPin is not null;

    public string SelectedPinCoordinatesText => SelectedPin is null
        ? string.Empty
        : string.Format(CultureInfo.InvariantCulture, "{0:F5}, {1:F5}", SelectedPin.Latitude, SelectedPin.Longitude);

    public GeoPoint? UserLocation
    {
        get => userLocation;
        private set => SetProperty(ref userLocation, value);
    }

    public string HeaderSubtitle
    {
        get => headerSubtitle;
        private set => SetProperty(ref headerSubtitle, value);
    }

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

    public ICommand LoadPinsCommand { get; }
    public ICommand SelectPinCommand { get; }
    public ICommand OpenExternalMapCommand { get; }
    public ICommand CenterOnMyLocationCommand { get; }
    public ICommand GoBackCommand { get; }

    public async Task LoadPinsAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        StatusMessage = string.Empty;
        ((Command)CenterOnMyLocationCommand).ChangeCanExecute();
        try
        {
            var selectedCity = appState.SelectedCity;
            var pizzerias = selectedCity is not null
                ? await pizzeriaRepository.GetByCityIdAsync(selectedCity.Id)
                : await pizzeriaRepository.GetAllAsync();

            if (pizzerias.Count == 0)
                pizzerias = await pizzeriaRepository.GetAllAsync();

            var reports = await qualityReportRepository.GetAllAsync();
            var newPins = new List<MapPinItem>();

            foreach (var pizzeria in pizzerias)
            {
                if (Math.Abs(pizzeria.Latitude) < 0.00001 && Math.Abs(pizzeria.Longitude) < 0.00001)
                    continue;

                newPins.Add(new MapPinItem(
                    pizzeria.Id,
                    $"Піцерія {pizzeria.StoreNumber}",
                    $"{pizzeria.Address} ({pizzeria.WorkingHoursText})",
                    pizzeria.Latitude,
                    pizzeria.Longitude,
                    "Pizzeria"));
            }

            foreach (var report in reports.Where(r => r.HasCoordinates))
            {
                newPins.Add(new MapPinItem(
                    report.Id,
                    string.IsNullOrWhiteSpace(report.OrderNumber)
                        ? $"Акт якості #{report.Id}"
                        : $"Акт якості · №{report.OrderNumber}",
                    report.Description,
                    report.Latitude!.Value,
                    report.Longitude!.Value,
                    "QualityReport"));
            }

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                Pins.Clear();
                foreach (var pin in newPins)
                    Pins.Add(pin);

                HeaderSubtitle = selectedCity is not null
                    ? $"Місто {selectedCity.Name}: {Pins.Count} маркер(ів)"
                    : $"Усі локації: {Pins.Count} маркер(ів)";

                SelectedPin = Pins.FirstOrDefault(p =>
                    appState.SelectedPizzeria is not null &&
                    p.Category == "Pizzeria" &&
                    p.EntityId == appState.SelectedPizzeria.Id)
                    ?? Pins.FirstOrDefault();

                PinsUpdated?.Invoke(this, EventArgs.Empty);
            });

            logger.LogInformation("Loaded {PinCount} map pin(s) for OpenStreetMap view", newPins.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load map pins");
            StatusMessage = "Не вдалося завантажити маркери на карті.";
        }
        finally
        {
            IsBusy = false;
            ((Command)CenterOnMyLocationCommand).ChangeCanExecute();
        }
    }

    private void SelectPin(MapPinItem? pin)
    {
        if (pin is null)
            return;

        SelectedPin = pin;
    }

    private async Task OpenExternalMapAsync()
    {
        if (SelectedPin is null)
            return;

        var opened = await mapLauncherService.OpenExternalMapAsync(
            SelectedPin.Latitude,
            SelectedPin.Longitude,
            SelectedPin.Title);

        if (!opened)
            StatusMessage = "Не вдалося відкрити зовнішню карту на цьому пристрої.";
    }

    private async Task CenterOnMyLocationAsync()
    {
        if (IsBusy || locationService is null)
            return;

        IsBusy = true;
        StatusMessage = "Визначаємо вашу локацію...";
        ((Command)CenterOnMyLocationCommand).ChangeCanExecute();
        try
        {
            if (permissionService is not null)
            {
                var granted = await permissionService.EnsureGrantedAsync(
                    AppPermission.LocationWhenInUse,
                    "Дозвіл на геолокацію",
                    "Геолокація потрібна, щоб показати ваше поточне місце на карті піцерій.");
                if (!granted)
                {
                    StatusMessage = "Дозвіл на геолокацію не надано.";
                    return;
                }
            }

            var result = await locationService.GetCurrentLocationAsync(TimeSpan.FromSeconds(10));
            if (result.IsSuccess && result.Location is not null)
            {
                UserLocation = result.Location;
                StatusMessage = string.Format(
                    CultureInfo.InvariantCulture,
                    "Ваші координати: {0:F5}, {1:F5}",
                    result.Location.Latitude,
                    result.Location.Longitude);
                PinsUpdated?.Invoke(this, EventArgs.Empty);
                FocusRequested?.Invoke(this, result.Location);
            }
            else
            {
                StatusMessage = result.ErrorMessage ?? "Не вдалося визначити координати.";
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to center map on user location");
            StatusMessage = "Не вдалося визначити поточну локацію.";
        }
        finally
        {
            IsBusy = false;
            ((Command)CenterOnMyLocationCommand).ChangeCanExecute();
        }
    }
}
