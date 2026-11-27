using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Core.Models;
using PizzaTake.Core.Repositories;
using PizzaTake.Core.Services;

namespace PizzaTake.Core.ViewModels;

public sealed class PizzeriaSelectionViewModel : BaseViewModel, IQueryParameterReceiver
{
    private readonly IPizzeriaRepository pizzerias;
    private readonly IAppStateService appState;
    private readonly INavigationService navigation;
    private readonly ILogger<PizzeriaSelectionViewModel> logger;
    private readonly IMapLauncherService? mapLauncherService;
    private readonly ILocationService? locationService;
    private readonly IPermissionService? permissionService;

    private Pizzeria? selectedPizzeria;
    private GeoPoint? userLocation;
    private string mapStatusMessage = string.Empty;
    private string? returnTo;

    public event EventHandler? MapDataUpdated;
    public event EventHandler<GeoPoint>? FocusRequested;

    public ObservableCollection<Pizzeria> Pizzerias { get; } = [];

    public Pizzeria? SelectedPizzeria
    {
        get => selectedPizzeria;
        set
        {
            if (SetProperty(ref selectedPizzeria, value))
            {
                foreach (var pizzeria in Pizzerias)
                    pizzeria.IsSelected = selectedPizzeria is not null && pizzeria.Id == selectedPizzeria.Id;
                OnPropertyChanged(nameof(HasSelectedPizzeria));
                ConfirmPizzeriaCommand.NotifyCanExecuteChanged();
                OpenExternalMapCommand.NotifyCanExecuteChanged();
                MapDataUpdated?.Invoke(this, EventArgs.Empty);
                if (value is not null && (Math.Abs(value.Latitude) > 0.00001 || Math.Abs(value.Longitude) > 0.00001))
                    FocusRequested?.Invoke(this, new GeoPoint(value.Latitude, value.Longitude));
            }
        }
    }

    public bool HasSelectedPizzeria => SelectedPizzeria is not null;

    public GeoPoint? UserLocation
    {
        get => userLocation;
        private set => SetProperty(ref userLocation, value);
    }

    public string MapStatusMessage
    {
        get => mapStatusMessage;
        private set
        {
            if (SetProperty(ref mapStatusMessage, value))
                OnPropertyChanged(nameof(HasMapStatusMessage));
        }
    }

    public bool HasMapStatusMessage => !string.IsNullOrWhiteSpace(MapStatusMessage);

    public IAsyncRelayCommand LoadCommand { get; }
    public IRelayCommand<Pizzeria> SelectPizzeriaCommand { get; }
    public IAsyncRelayCommand ConfirmPizzeriaCommand { get; }
    public IAsyncRelayCommand OpenMapCommand { get; }
    public IAsyncRelayCommand CenterOnMyLocationCommand { get; }
    public IAsyncRelayCommand OpenExternalMapCommand { get; }
    public IAsyncRelayCommand GoBackCommand { get; }

    public PizzeriaSelectionViewModel(
        IPizzeriaRepository pizzerias,
        IAppStateService appState,
        INavigationService navigation,
        ILogger<PizzeriaSelectionViewModel>? logger = null,
        IMapLauncherService? mapLauncherService = null,
        ILocationService? locationService = null,
        IPermissionService? permissionService = null)
    {
        this.pizzerias = pizzerias;
        this.appState = appState;
        this.navigation = navigation;
        this.logger = logger ?? NullLogger<PizzeriaSelectionViewModel>.Instance;
        this.mapLauncherService = mapLauncherService;
        this.locationService = locationService;
        this.permissionService = permissionService;

        LoadCommand = new AsyncRelayCommand(LoadAsync);
        SelectPizzeriaCommand = new RelayCommand<Pizzeria>(pizzeria => SelectedPizzeria = pizzeria);
        ConfirmPizzeriaCommand = new AsyncRelayCommand(ConfirmAsync, () => SelectedPizzeria is not null);
        OpenMapCommand = new AsyncRelayCommand(() => navigation.GoToAsync("pizzeriasmap"));
        CenterOnMyLocationCommand = new AsyncRelayCommand(CenterOnMyLocationAsync, () => !IsBusy);
        OpenExternalMapCommand = new AsyncRelayCommand(OpenExternalMapAsync, () => SelectedPizzeria is not null);
        GoBackCommand = new AsyncRelayCommand(() => navigation.GoBackAsync());
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        returnTo = query.TryGetValue("returnTo", out var value) && value?.ToString() is "settings" or "menu" or "cart"
            ? value.ToString()
            : null;
    }

    public async Task LoadAsync()
    {
        if (appState.SelectedCity is null) return;
        IsBusy = true;
        MapStatusMessage = string.Empty;
        CenterOnMyLocationCommand.NotifyCanExecuteChanged();
        try
        {
            Pizzerias.Clear();
            var list = await pizzerias.GetByCityIdAsync(appState.SelectedCity.Id);
            foreach (var pizzeria in list)
                Pizzerias.Add(pizzeria);

            var currentId = appState.SelectedPizzeria?.Id;
            var preselected = currentId.HasValue
                ? Pizzerias.FirstOrDefault(p => p.Id == currentId.Value)
                : Pizzerias.FirstOrDefault();
            SelectedPizzeria = preselected;
            if (preselected is not null)
                preselected.IsSelected = true;

            MapDataUpdated?.Invoke(this, EventArgs.Empty);
            logger.LogInformation("Loaded {PizzeriaCount} pizzeria(s) for city {CityName}", Pizzerias.Count, appState.SelectedCity.Name);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load pizzerias for city {CityId}", appState.SelectedCity.Id);
            MapStatusMessage = "Не вдалося завантажити список піцерій.";
        }
        finally
        {
            IsBusy = false;
            CenterOnMyLocationCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task CenterOnMyLocationAsync()
    {
        if (IsBusy || locationService is null)
            return;

        IsBusy = true;
        MapStatusMessage = "Визначаємо вашу локацію...";
        CenterOnMyLocationCommand.NotifyCanExecuteChanged();
        try
        {
            if (permissionService is not null)
            {
                var granted = await permissionService.EnsureGrantedAsync(
                    AppPermission.LocationWhenInUse,
                    "Дозвіл на геолокацію",
                    "Геолокація потрібна, щоб показати ваше поточне місце та найближчу піцерію на карті.");
                if (!granted)
                {
                    MapStatusMessage = "Дозвіл на геолокацію не надано.";
                    return;
                }
            }

            var result = await locationService.GetCurrentLocationAsync(TimeSpan.FromSeconds(10));
            if (result.IsSuccess && result.Location is not null)
            {
                UserLocation = result.Location;
                MapStatusMessage = string.Format(
                    CultureInfo.InvariantCulture,
                    "Ваші координати: {0:F5}, {1:F5}",
                    result.Location.Latitude,
                    result.Location.Longitude);

                var nearest = Pizzerias
                    .Where(p => Math.Abs(p.Latitude) > 0.00001 || Math.Abs(p.Longitude) > 0.00001)
                    .OrderBy(p => DistanceKm(result.Location.Latitude, result.Location.Longitude, p.Latitude, p.Longitude))
                    .FirstOrDefault();
                if (nearest is not null)
                    SelectedPizzeria = nearest;

                MapDataUpdated?.Invoke(this, EventArgs.Empty);
                FocusRequested?.Invoke(this, result.Location);
            }
            else
            {
                MapStatusMessage = result.ErrorMessage ?? "Не вдалося визначити координати.";
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to center pizzeria selection map on user location");
            MapStatusMessage = "Не вдалося визначити поточну локацію.";
        }
        finally
        {
            IsBusy = false;
            CenterOnMyLocationCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task OpenExternalMapAsync()
    {
        if (SelectedPizzeria is null || mapLauncherService is null)
            return;

        var opened = await mapLauncherService.OpenExternalMapAsync(
            SelectedPizzeria.Latitude,
            SelectedPizzeria.Longitude,
            $"Піцерія {SelectedPizzeria.StoreNumber} — {SelectedPizzeria.Address}");

        if (!opened)
            MapStatusMessage = "Не вдалося відкрити зовнішню карту на цьому пристрої.";
    }

    private async Task ConfirmAsync()
    {
        if (SelectedPizzeria is null) return;
        appState.SelectedPizzeria = SelectedPizzeria;
        appState.SaveState();
        logger.LogInformation("Confirmed pizzeria {StoreNumber} ({Address})", SelectedPizzeria.StoreNumber, SelectedPizzeria.Address);
        await navigation.GoToAsync(returnTo switch
        {
            "settings" => "///settings",
            "cart" => "///cart",
            _ => "///menu"
        });
    }

    private static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double earthRadiusKm = 6371;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLon = (lon2 - lon1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
            + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180)
            * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return earthRadiusKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
}
