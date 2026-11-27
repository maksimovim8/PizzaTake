namespace PizzaTake.Core.Services;

public interface INavigationService
{
    Task GoToAsync(string route, IDictionary<string, object>? parameters = null);
    Task GoBackAsync(IDictionary<string, object>? parameters = null);
    Task PopToRootAsync(bool animated = false);
}

public interface IDialogService
{
    Task AlertAsync(string title, string message, string cancel = "OK");
    Task<bool> ConfirmAsync(string title, string message, string accept = "Так", string cancel = "Ні");
    Task<string?> PromptAsync(
        string title,
        string message,
        string accept = "Надіслати",
        string cancel = "Скасувати",
        string? placeholder = null,
        int maxLength = -1);
}

public interface ISettingsService
{
    string GetString(string key, string defaultValue = "");
    void SetString(string key, string value);
    bool GetBool(string key, bool defaultValue = false);
    void SetBool(string key, bool value);
    int GetInt(string key, int defaultValue = 0);
    void SetInt(string key, int value);
    void Remove(string key);
    void Clear();
}

public interface ISecureStoreService
{
    Task<string?> GetAsync(string key);
    Task SetAsync(string key, string value);
    bool Remove(string key);
}

public interface IMainThreadDispatcher
{
    void BeginInvokeOnMainThread(Action action);
    Task InvokeOnMainThreadAsync(Action action);
    Task InvokeOnMainThreadAsync(Func<Task> func);
}

public enum AppThemePreference
{
    System,
    Light,
    Dark
}

public interface IThemeService
{
    AppThemePreference CurrentTheme { get; }
    void ApplyTheme(AppThemePreference theme);
}

public interface IAppPathsService
{
    string AppDataDirectory { get; }
    string DatabasePath { get; }
    string LogsDirectory { get; }
    string PhotosDirectory { get; }
}

/// <summary>
/// Platform-independent counterpart of MAUI IQueryAttributable for ViewModels in PizzaTake.Core.
/// </summary>
public interface IQueryParameterReceiver
{
    void ApplyQueryAttributes(IDictionary<string, object> query);
}
