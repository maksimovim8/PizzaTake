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
    private readonly IUserProfileRepository? userProfile;
    private readonly ISyncService? syncService;
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
        IUserProfileRepository? userProfile = null,
        ISyncService? syncService = null,
        ILogger<CitySelectionViewModel>? logger = null)
    {
        this.cities = cities;
        this.appState = appState;
        this.navigation = navigation;
        this.locationService = locationService;
        this.dispatcher = dispatcher;
        this.userProfile = userProfile;
        this.syncService = syncService;
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

    private Task ReplaceCitiesAsync(IEnumerable<City> values)
    {
        return dispatcher.InvokeOnMainThreadAsync(() =>
        {
            Cities.Clear();
            foreach (var city in values)
                Cities.Add(city);
        });
    }

    private async Task SelectCityAsync(City? city)
    {
        if (city is null) return;
        if (appState.SelectedCity?.Id != city.Id)
            appState.SelectedPizzeria = null;
        appState.SelectedCity = city;
        appState.SaveState();
        logger.LogInformation("Selected delivery city {CityName} (Id={CityId})", city.Name, city.Id);
        if (userProfile is not null)
        {
            var profile = await userProfile.GetCurrentAsync() ?? new UserProfile();
            profile.CityName = city.Name;
            profile.UpdatedAt = DateTime.UtcNow;
            await userProfile.SaveAsync(profile);
            if (syncService is not null)
            {
                await syncService.EnqueueAsync("UserProfile", "Update", new
                {
                    profile.Id,
                    profile.CityName,
                    UpdatedAt = profile.UpdatedAt
                });
            }
        }
        if (appState.SelectedPizzeria is null || appState.SelectedDeliveryType == DeliveryType.Pickup)
        {
            var pizzeriaReturnTo = returnTo ?? "menu";
            await navigation.GoToAsync($"pizzerias?returnTo={pizzeriaReturnTo}");
        }
        else if (returnTo is not null)
        {
            await navigation.GoToAsync($"///{returnTo}");
        }
        else
        {
            await navigation.GoToAsync("///menu");
        }
    }

    private async Task FindNearbyAsync()
    {
        try
        {
            var result = await locationService.GetCurrentLocationAsync(TimeSpan.FromSeconds(10));
            if (!result.IsSuccess || result.Location is null)
            {
                logger.LogWarning("Location lookup failed ({Reason}): {Message}", result.FailureReason, result.ErrorMessage);
                StatusMessage = result.ErrorMessage ?? "Не вдалося визначити місце розташування.";
                return;
            }

            var allCities = await cities.GetAllAsync();
            var nearest = allCities
                .Where(city => city.Latitude != 0 || city.Longitude != 0)
                .OrderBy(city => DistanceKm(result.Location.Latitude, result.Location.Longitude, city.Latitude, city.Longitude))
                .FirstOrDefault();

            if (nearest is null)
                StatusMessage = "Немає міст із координатами для пошуку поруч.";
            else
                await SelectCityAsync(nearest);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error while detecting nearest city");
            StatusMessage = "Не вдалося визначити найближче місто.";
        }
    }

    private static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double earthRadiusKm = 6371;
        var dLat = DegreesToRadians(lat2 - lat1);
        var dLon = DegreesToRadians(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
            + Math.Cos(DegreesToRadians(lat1)) * Math.Cos(DegreesToRadians(lat2))
            * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return earthRadiusKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180;

    private static string? NormalizeReturnRoute(string? route) => route switch
    {
        "menu" => "menu",
        "settings" => "settings",
        "cart" => "cart",
        _ => null
    };
}
