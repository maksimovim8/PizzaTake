using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Core.Models;
using PizzaTake.Core.Repositories;
using PizzaTake.Core.Services;

namespace PizzaTake.Core.ViewModels;

public sealed class CitySelectionViewModel : BaseViewModel, IQueryParameterReceiver
{
    private readonly ICityRepository cities;
    private readonly IAppStateService appState;
    private readonly INavigationService navigation;
    private readonly ILocationService locationService;
    private readonly IMainThreadDispatcher dispatcher;
    private readonly IPizzeriaRepository? pizzerias;
    private readonly IUserProfileRepository? userProfile;
    private readonly ISyncService? syncService;
    private readonly IPermissionService? permissionService;
    private readonly ILogger<CitySelectionViewModel> logger;

    public ObservableCollection<City> Cities { get; } = [];
    private string searchText = string.Empty;
    private string statusMessage = string.Empty;
    private string? returnTo;

    public string SearchText
    {
        get => searchText;
        set
        {
            if (SetProperty(ref searchText, value))
                _ = SearchAsync();
        }
    }

    public string StatusMessage
    {
        get => statusMessage;
        set => SetProperty(ref statusMessage, value);
    }

    public IAsyncRelayCommand LoadCommand { get; }
    public IAsyncRelayCommand<City> SelectCityCommand { get; }
    public IAsyncRelayCommand FindNearbyCommand { get; }
    public IAsyncRelayCommand GoBackCommand { get; }

    public CitySelectionViewModel(
        ICityRepository cities,
        IAppStateService appState,
        INavigationService navigation,
        ILocationService locationService,
        IMainThreadDispatcher dispatcher,
        IPizzeriaRepository? pizzerias = null,
        IUserProfileRepository? userProfile = null,
        ISyncService? syncService = null,
        IPermissionService? permissionService = null,
        ILogger<CitySelectionViewModel>? logger = null)
    {
        this.cities = cities;
        this.appState = appState;
        this.navigation = navigation;
        this.locationService = locationService;
        this.dispatcher = dispatcher;
        this.pizzerias = pizzerias;
        this.userProfile = userProfile;
        this.syncService = syncService;
        this.permissionService = permissionService;
        this.logger = logger ?? NullLogger<CitySelectionViewModel>.Instance;

        LoadCommand = new AsyncRelayCommand(LoadAsync);
        SelectCityCommand = new AsyncRelayCommand<City>(SelectCityAsync);
        FindNearbyCommand = new AsyncRelayCommand(FindNearbyAsync);
        GoBackCommand = new AsyncRelayCommand(() => navigation.GoBackAsync());
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        returnTo = query.TryGetValue("returnTo", out var value) ? NormalizeReturnRoute(value?.ToString()) : null;
    }

    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            await ReplaceCitiesAsync(await cities.GetPopularAsync());
            StatusMessage = Cities.Count == 0 ? "Поки немає доступних міст." : string.Empty;
            logger.LogInformation("Loaded {CityCount} popular city(ies)", Cities.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load cities");
            StatusMessage = "Не вдалося завантажити список міст.";
        }
        finally { IsBusy = false; }
    }

    public async Task SearchAsync()
    {
        try
        {
            var results = await cities.SearchAsync(SearchText);
            await ReplaceCitiesAsync(results);
            StatusMessage = results.Count == 0 ? "Нічого не знайдено." : string.Empty;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to search cities for query {SearchText}", SearchText);
            StatusMessage = "Не вдалося виконати пошук міст.";
        }
    }

    private Task ReplaceCitiesAsync(IEnumerable<City> values) =>
        dispatcher.InvokeOnMainThreadAsync(() => ReplaceCollection(Cities, values));

    private async Task SelectCityAsync(City? city)
    {
        if (city is null) return;
        if (appState.SelectedCity?.Id != city.Id)
            appState.SelectedPizzeria = null;
        appState.SelectedCity = city;
        appState.SaveState();
        logger.LogInformation("Selected delivery city {CityName} (Id={CityId})", city.Name, city.Id);
        await SaveProfileCityAsync(city);

        if (appState.SelectedPizzeria is null || appState.SelectedDeliveryType == DeliveryType.Pickup)
            await navigation.GoToAsync($"pizzerias?returnTo={returnTo ?? "menu"}");
        else
            await navigation.GoToAsync($"///{returnTo ?? "menu"}");
    }

    private async Task SaveProfileCityAsync(City city)
    {
        if (userProfile is null) return;
        var profile = await userProfile.GetCurrentAsync() ?? new UserProfile();
        profile.CityName = city.Name;
        profile.UpdatedAt = DateTime.UtcNow;
        await userProfile.SaveAsync(profile);
        if (syncService is not null)
            await syncService.EnqueueAsync("UserProfile", "Update", new { profile.Id, CityId = city.Id, UpdatedAt = profile.UpdatedAt });
    }

    private async Task FindNearbyAsync()
    {
        try
        {
            if (permissionService is not null && !await permissionService.EnsureGrantedAsync(
                    AppPermission.LocationWhenInUse,
                    "Дозвіл на геолокацію",
                    "Геолокація потрібна для автоматичного визначення найближчого міста з піцеріями PizzaTake."))
            {
                logger.LogWarning("Location permission denied while searching for nearby city");
                StatusMessage = "Надайте застосунку дозвіл на геолокацію.";
                return;
            }

            var result = await locationService.GetCurrentLocationAsync(TimeSpan.FromSeconds(10));
            if (!result.IsSuccess || result.Location is null)
            {
                logger.LogWarning("Geolocation failed while searching for nearby city: {Reason}", result.FailureReason);
                StatusMessage = result.ErrorMessage ?? "Не вдалося визначити місце розташування.";
                return;
            }

            var nearest = (await cities.GetAllAsync())
                .Where(c => GeoMath.HasValidCoordinates(c.Latitude, c.Longitude))
                .OrderBy(c => GeoMath.DistanceKm(result.Location.Latitude, result.Location.Longitude, c.Latitude, c.Longitude))
                .FirstOrDefault();

            if (nearest is null)
            {
                StatusMessage = "Немає міст із координатами для пошуку поруч.";
                return;
            }

            if (pizzerias is not null)
            {
                var cityPizzerias = await pizzerias.GetByCityIdAsync(nearest.Id);
                var nearestPizzeria = cityPizzerias
                    .Where(p => GeoMath.HasValidCoordinates(p.Latitude, p.Longitude))
                    .OrderBy(p => GeoMath.DistanceKm(result.Location.Latitude, result.Location.Longitude, p.Latitude, p.Longitude))
                    .FirstOrDefault() ?? cityPizzerias.FirstOrDefault();

                if (nearestPizzeria is not null)
                {
                    appState.SelectedCity = nearest;
                    appState.SelectedPizzeria = nearestPizzeria;
                    appState.SaveState();
                    logger.LogInformation("Auto-selected nearest city {CityName} and pizzeria {StoreNumber} via GPS", nearest.Name, nearestPizzeria.StoreNumber);
                    await SaveProfileCityAsync(nearest);
                    await navigation.GoToAsync($"///{returnTo ?? "menu"}");
                    return;
                }
            }

            await SelectCityAsync(nearest);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error while detecting nearest city");
            StatusMessage = "Не вдалося визначити найближче місто.";
        }
    }

    private static string? NormalizeReturnRoute(string? route) => route is "menu" or "settings" or "cart" ? route : null;
}
