using System.Text.Json;
using Microsoft.Maui.Storage;
using PizzaTake.Models;

namespace PizzaTake.Services;

public interface IAppStateService
{
    City? SelectedCity { get; set; }
    Pizzeria? SelectedPizzeria { get; set; }
    DeliveryType SelectedDeliveryType { get; set; }
    bool IsDarkTheme { get; }
    Dictionary<string, string> DraftFormState { get; set; }
    void SaveState();
    void RestoreState();
    void ApplyTheme(bool isDark);
}

public sealed class AppStateService : IAppStateService
{
    private const string CityKey = "app.selected-city";
    private const string PizzeriaKey = "app.selected-pizzeria";
    private const string DeliveryTypeKey = "app.delivery-type";
    private const string ThemeKey = "app.dark-theme";
    private const string DraftFormKey = "app.draft-form";

    public City? SelectedCity { get; set; }
    public Pizzeria? SelectedPizzeria { get; set; }
    public DeliveryType SelectedDeliveryType { get; set; } = DeliveryType.Delivery;
    public bool IsDarkTheme { get; private set; }
    public Dictionary<string, string> DraftFormState { get; set; } = new(StringComparer.Ordinal);

    public void SaveState()
    {
        SaveJson(CityKey, SelectedCity);
        SaveJson(PizzeriaKey, SelectedPizzeria);
        Preferences.Default.Set(DeliveryTypeKey, (int)SelectedDeliveryType);
        Preferences.Default.Set(ThemeKey, IsDarkTheme);
        SaveJson(DraftFormKey, DraftFormState);
    }

    public void RestoreState()
    {
        SelectedCity = ReadJson<City>(CityKey);
        SelectedPizzeria = ReadJson<Pizzeria>(PizzeriaKey);
        SelectedDeliveryType = (DeliveryType)Preferences.Default.Get(DeliveryTypeKey, (int)DeliveryType.Delivery);
        IsDarkTheme = Preferences.Default.Get(ThemeKey, false);
        DraftFormState = ReadJson<Dictionary<string, string>>(DraftFormKey) ?? new(StringComparer.Ordinal);
        ApplyTheme(IsDarkTheme);
    }

    public void ApplyTheme(bool isDark)
    {
        IsDarkTheme = isDark;
        if (Application.Current is not null)
            Application.Current.UserAppTheme = isDark ? AppTheme.Dark : AppTheme.Light;
        Preferences.Default.Set(ThemeKey, isDark);
    }

    private static void SaveJson<T>(string key, T value) =>
        Preferences.Default.Set(key, JsonSerializer.Serialize(value));

    private static T? ReadJson<T>(string key)
    {
        var json = Preferences.Default.Get(key, string.Empty);
        if (string.IsNullOrWhiteSpace(json))
            return default;

        try
        {
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (JsonException)
        {
            Preferences.Default.Remove(key);
            return default;
        }
    }
}
