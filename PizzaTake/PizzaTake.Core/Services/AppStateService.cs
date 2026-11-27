using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PizzaTake.Core.Models;

namespace PizzaTake.Core.Services;

public sealed class DraftFormState : Dictionary<string, string>
{
    public DraftFormState() : base(StringComparer.Ordinal) { }
    public DraftFormState(IEqualityComparer<string>? comparer) : base(comparer) { }

    public double? AddressLatitude { get; set; }
    public double? AddressLongitude { get; set; }
}

public interface IAppStateService
{
    event EventHandler? StateChanged;
    City? SelectedCity { get; set; }
    Pizzeria? SelectedPizzeria { get; set; }
    DeliveryType SelectedDeliveryType { get; set; }
    bool IsDarkTheme { get; }
    bool IsSystemTheme { get; }
    DraftFormState DraftFormState { get; set; }
    string? PendingDeepLinkRoute { get; set; }
    void SetDraftValue(string key, string value);
    string? GetDraftValue(string key);
    void ClearDraftPrefix(string prefix);
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
    private const string DraftAddressLatKey = "app.draft-address-lat";
    private const string DraftAddressLngKey = "app.draft-address-lng";

    private readonly ISettingsService settings;
    private readonly IThemeService themeService;
    private readonly ILogger<AppStateService> logger;

    public AppStateService(
        ISettingsService settings,
        IThemeService themeService,
        ILogger<AppStateService>? logger = null)
    {
        this.settings = settings;
        this.themeService = themeService;
        this.logger = logger ?? NullLogger<AppStateService>.Instance;
    }

    public event EventHandler? StateChanged;

    public City? SelectedCity { get; set; }
    public Pizzeria? SelectedPizzeria { get; set; }
    public DeliveryType SelectedDeliveryType { get; set; } = DeliveryType.Delivery;
    public bool IsDarkTheme { get; private set; }
    public bool IsSystemTheme { get; private set; }
    public DraftFormState DraftFormState { get; set; } = new(StringComparer.Ordinal);
    public string? PendingDeepLinkRoute { get; set; }

    public void SetDraftValue(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;

        DraftFormState[key] = value;
    }

    public string? GetDraftValue(string key) =>
        DraftFormState.TryGetValue(key, out var value) ? value : null;

    public void ClearDraftPrefix(string prefix)
    {
        var keysToRemove = DraftFormState.Keys
            .Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
            .ToList();

        foreach (var key in keysToRemove)
            DraftFormState.Remove(key);

        if (prefix.StartsWith("addressEdit", StringComparison.Ordinal))
        {
            DraftFormState.AddressLatitude = null;
            DraftFormState.AddressLongitude = null;
        }
    }

    public void SaveState()
    {
        SaveJson(CityKey, SelectedCity);
        SaveJson(PizzeriaKey, SelectedPizzeria);
        settings.SetInt(DeliveryTypeKey, (int)SelectedDeliveryType);
        settings.SetBool(ThemeKey, IsDarkTheme);
        settings.SetString(ThemeModeKey, IsSystemTheme ? "System" : IsDarkTheme ? "Dark" : "Light");
        SaveJson(DraftFormKey, new Dictionary<string, string>(DraftFormState, StringComparer.Ordinal));
        if (DraftFormState.AddressLatitude.HasValue)
            settings.SetString(DraftAddressLatKey, DraftFormState.AddressLatitude.Value.ToString("R", CultureInfo.InvariantCulture));
        else
            settings.Remove(DraftAddressLatKey);

        if (DraftFormState.AddressLongitude.HasValue)
            settings.SetString(DraftAddressLngKey, DraftFormState.AddressLongitude.Value.ToString("R", CultureInfo.InvariantCulture));
        else
            settings.Remove(DraftAddressLngKey);

        logger.LogDebug("Application state saved (City={City}, Pizzeria={Pizzeria})", SelectedCity?.Name, SelectedPizzeria?.StoreNumber);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RestoreState()
    {
        SelectedCity = ReadJson<City>(CityKey);
        SelectedPizzeria = ReadJson<Pizzeria>(PizzeriaKey);
        SelectedDeliveryType = (DeliveryType)settings.GetInt(DeliveryTypeKey, (int)DeliveryType.Delivery);
        var savedTheme = settings.GetString(ThemeModeKey, string.Empty);
        if (string.IsNullOrWhiteSpace(savedTheme))
            savedTheme = settings.GetBool(ThemeKey, false) ? "Dark" : "Light";

        var restoredEntries = ReadJson<Dictionary<string, string>>(DraftFormKey);
        var restoredDraft = new DraftFormState(StringComparer.Ordinal);
        if (restoredEntries is not null)
        {
            foreach (var entry in restoredEntries)
                restoredDraft[entry.Key] = entry.Value;
        }

        if (double.TryParse(settings.GetString(DraftAddressLatKey, string.Empty), NumberStyles.Float, CultureInfo.InvariantCulture, out var lat))
            restoredDraft.AddressLatitude = lat;
        if (double.TryParse(settings.GetString(DraftAddressLngKey, string.Empty), NumberStyles.Float, CultureInfo.InvariantCulture, out var lng))
            restoredDraft.AddressLongitude = lng;

        DraftFormState = restoredDraft;
        ApplyThemeMode(savedTheme);
        logger.LogDebug("Application state restored (City={City}, Theme={Theme})", SelectedCity?.Name, savedTheme);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ApplyTheme(bool isDark)
    {
        IsDarkTheme = isDark;
        IsSystemTheme = false;
        themeService.ApplyTheme(isDark ? AppThemePreference.Dark : AppThemePreference.Light);
        settings.SetBool(ThemeKey, isDark);
        settings.SetString(ThemeModeKey, isDark ? "Dark" : "Light");
    }

    public void ApplySystemTheme()
    {
        IsDarkTheme = false;
        IsSystemTheme = true;
        themeService.ApplyTheme(AppThemePreference.System);
        settings.SetBool(ThemeKey, false);
        settings.SetString(ThemeModeKey, "System");
    }

    private void ApplyThemeMode(string mode)
    {
        if (string.Equals(mode, "System", StringComparison.OrdinalIgnoreCase))
            ApplySystemTheme();
        else
            ApplyTheme(string.Equals(mode, "Dark", StringComparison.OrdinalIgnoreCase));
    }

    private void SaveJson<T>(string key, T value) =>
        settings.SetString(key, JsonSerializer.Serialize(value));

    private T? ReadJson<T>(string key)
    {
        var json = settings.GetString(key, string.Empty);
        if (string.IsNullOrWhiteSpace(json))
            return default;

        try
        {
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Corrupt JSON detected in preference key {Key}; resetting value", key);
            settings.Remove(key);
            return default;
        }
    }
}
