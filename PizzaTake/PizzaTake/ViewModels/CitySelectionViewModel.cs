using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices.Sensors;
using PizzaTake.Models;
using PizzaTake.Repositories;
using PizzaTake.Services;

namespace PizzaTake.ViewModels;

public sealed class CitySelectionViewModel : BaseViewModel, IQueryAttributable
{
    private readonly ICityRepository cities;
    private readonly IAppStateService appState;
    private readonly IUserProfileRepository? userProfile;
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

    public ICommand LoadCommand { get; }
    public ICommand SelectCityCommand { get; }
    public ICommand FindNearbyCommand { get; }
    public ICommand GoBackCommand { get; }

    public CitySelectionViewModel(ICityRepository cities, IAppStateService appState, IUserProfileRepository? userProfile = null)
    {
        this.cities = cities;
        this.appState = appState;
        this.userProfile = userProfile;
        LoadCommand = new Command(async () => await LoadAsync());
        SelectCityCommand = new Command<City>(async city => await SelectCityAsync(city));
        FindNearbyCommand = new Command(async () => await FindNearbyAsync());
        GoBackCommand = new Command(async () => await Shell.Current.GoToAsync(".."));
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        returnTo = query.TryGetValue("returnTo", out var value) ? NormalizeReturnRoute(value?.ToString()) : null;
    }

    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            await ReplaceCitiesAsync(await cities.GetPopularAsync());
            StatusMessage = Cities.Count == 0 ? "Поки немає доступних міст." : string.Empty;
        }
        finally { IsBusy = false; }
    }

    private async Task SearchAsync()
    {
        await ReplaceCitiesAsync(await cities.SearchAsync(SearchText));
    }

    private Task ReplaceCitiesAsync(IEnumerable<City> values)
    {
        return MainThread.InvokeOnMainThreadAsync(() =>
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
        if (userProfile is not null)
        {
            var profile = await userProfile.GetCurrentAsync() ?? new UserProfile();
            profile.CityName = city.Name;
            profile.UpdatedAt = DateTime.UtcNow;
            await userProfile.SaveAsync(profile);
        }
        if (appState.SelectedPizzeria is null || appState.SelectedDeliveryType == DeliveryType.Pickup)
        {
            var pizzeriaReturnTo = returnTo ?? "menu";
            await Shell.Current.GoToAsync($"pizzerias?returnTo={pizzeriaReturnTo}");
        }
        else if (returnTo is not null)
        {
            await Shell.Current.GoToAsync($"///{returnTo}");
        }
        else
        {
            await Shell.Current.GoToAsync("///menu");
        }
    }

    private async Task FindNearbyAsync()
    {
        try
        {
            var location = await Geolocation.Default.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(10)));
            if (location is null)
            {
                StatusMessage = "Не вдалося визначити місце розташування.";
                return;
            }

            var allCities = await cities.GetAllAsync();
            var nearest = allCities
                .Where(city => city.Latitude != 0 || city.Longitude != 0)
                .OrderBy(city => DistanceKm(location.Latitude, location.Longitude, city.Latitude, city.Longitude))
                .FirstOrDefault();

            if (nearest is null)
                StatusMessage = "Немає міст із координатами для пошуку поруч.";
            else
                await SelectCityAsync(nearest);
        }
        catch (FeatureNotEnabledException)
        {
            StatusMessage = "Увімкніть геолокацію на пристрої.";
        }
        catch (PermissionException)
        {
            StatusMessage = "Надайте застосунку дозвіл на геолокацію.";
        }
        catch (Exception)
        {
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
        _ => null
    };
}
