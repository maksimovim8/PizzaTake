using Microsoft.Maui.Storage;
using PizzaTake.Core.Services;

namespace PizzaTake.Services.Platform;

public sealed class MauiSettingsService : ISettingsService
{
    public string GetString(string key, string defaultValue = "") =>
        Preferences.Default.Get(key, defaultValue);

    public void SetString(string key, string value) =>
        Preferences.Default.Set(key, value);

    public bool GetBool(string key, bool defaultValue = false) =>
        Preferences.Default.Get(key, defaultValue);

    public void SetBool(string key, bool value) =>
        Preferences.Default.Set(key, value);

    public int GetInt(string key, int defaultValue = 0) =>
        Preferences.Default.Get(key, defaultValue);

    public void SetInt(string key, int value) =>
        Preferences.Default.Set(key, value);

    public void Remove(string key) =>
        Preferences.Default.Remove(key);

    public void Clear() =>
        Preferences.Default.Clear();
}
