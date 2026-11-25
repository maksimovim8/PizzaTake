using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Maui.Storage;
using PizzaTake.Models;

namespace PizzaTake.Services;

public interface IAppStateService
{
    event EventHandler? StateChanged;
    City? SelectedCity { get; set; }
    Pizzeria? SelectedPizzeria { get; set; }
    DeliveryType SelectedDeliveryType { get; set; }
    bool IsDarkTheme { get; }
    bool IsSystemTheme { get; }
    Dictionary<string, string> DraftFormState { get; set; }
    void SaveState();
    void RestoreState();
    void ApplyTheme(bool isDark);
    void ApplySystemTheme();
}

public sealed class AppStateService : IAppStateService
{
    private const string CityKey = "app.selected-city";
    private const string PizzeriaKey = "app.selected-pizzeria";
    private const string DeliveryTypeKey = "app.delivery-type";
    private const string ThemeKey = "app.dark-theme";
    private const string ThemeModeKey = "app.theme-mode";
    private const string DraftFormKey = "app.draft-form";

    private readonly ILogger<AppStateService> logger;

    public AppStateService(ILogger<AppStateService>? logger = null)
    {
        this.logger = logger ?? NullLogger<AppStateService>.Instance;
    }

    public event EventHandler? StateChanged;

    public City? SelectedCity { get; set; }
    public Pizzeria? SelectedPizzeria { get; set; }
    public DeliveryType SelectedDeliveryType { get; set; } = DeliveryType.Delivery;
    public bool IsDarkTheme { get; private set; }
    public bool IsSystemTheme { get; private set; }
    public Dictionary<string, string> DraftFormState { get; set; } = new(StringComparer.Ordinal);

    public void SaveState()
    {
        SaveJson(CityKey, SelectedCity);
        SaveJson(PizzeriaKey, SelectedPizzeria);
        Preferences.Default.Set(DeliveryTypeKey, (int)SelectedDeliveryType);
        Preferences.Default.Set(ThemeKey, IsDarkTheme);
        Preferences.Default.Set(ThemeModeKey, IsSystemTheme ? "System" : IsDarkTheme ? "Dark" : "Light");
        SaveJson(DraftFormKey, DraftFormState);
        logger.LogDebug("Application state saved (City={City}, Pizzeria={Pizzeria})", SelectedCity?.Name, SelectedPizzeria?.StoreNumber);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RestoreState()
    {
        SelectedCity = ReadJson<City>(CityKey);
        SelectedPizzeria = ReadJson<Pizzeria>(PizzeriaKey);
        SelectedDeliveryType = (DeliveryType)Preferences.Default.Get(DeliveryTypeKey, (int)DeliveryType.Delivery);
        var savedTheme = Preferences.Default.Get(ThemeModeKey, string.Empty);
        if (string.IsNullOrWhiteSpace(savedTheme))
            savedTheme = Preferences.Default.Get(ThemeKey, false) ? "Dark" : "Light";
        DraftFormState = ReadJson<Dictionary<string, string>>(DraftFormKey) ?? new(StringComparer.Ordinal);
        ApplyThemeMode(savedTheme);
        logger.LogDebug("Application state restored (City={City}, Theme={Theme})", SelectedCity?.Name, savedTheme);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ApplyTheme(bool isDark)
    {
        IsDarkTheme = isDark;
        IsSystemTheme = false;
        if (Application.Current is not null)
            Application.Current.UserAppTheme = isDark ? AppTheme.Dark : AppTheme.Light;
        Preferences.Default.Set(ThemeKey, isDark);
        Preferences.Default.Set(ThemeModeKey, isDark ? "Dark" : "Light");
    }

    public void ApplySystemTheme()
    {
        IsDarkTheme = false;
        IsSystemTheme = true;
        if (Application.Current is not null)
            Application.Current.UserAppTheme = AppTheme.Unspecified;
        Preferences.Default.Set(ThemeKey, false);
        Preferences.Default.Set(ThemeModeKey, "System");
    }

    private void ApplyThemeMode(string mode)
    {
        if (string.Equals(mode, "System", StringComparison.OrdinalIgnoreCase))
            ApplySystemTheme();
        else
            ApplyTheme(string.Equals(mode, "Dark", StringComparison.OrdinalIgnoreCase));
    }

    private static void SaveJson<T>(string key, T value) =>
        Preferences.Default.Set(key, JsonSerializer.Serialize(value));

    private T? ReadJson<T>(string key)
    {
        var json = Preferences.Default.Get(key, string.Empty);
        if (string.IsNullOrWhiteSpace(json))
            return default;

        try
        {
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Corrupt JSON detected in preference key {Key}; resetting value", key);
            Preferences.Default.Remove(key);
            return default;
        }
    }
}
